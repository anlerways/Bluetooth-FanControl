using FanControl.Service.Config;
using FanControl.Shared.Enums;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace FanControl.Service.Communication;

/// <summary>
/// BLE 通信通道（Nordic UART Service，与固件约定）：
/// - RX 特征（6e400002）写入文本行：PWM:x / TIME:... / STATUS? / CPUx / GPUx
/// - TX 特征（6e400003）订阅通知，接收设备状态
/// </summary>
public sealed class BleChannel : ICommunicationChannel
{
    private const string NusServiceUuid = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    private const string NusRxUuid = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
    private const string NusTxUuid = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";

    private readonly IConfigManager _configManager;
    private readonly ILogger<BleChannel> _logger;
    private readonly object _sync = new();
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _rxCharacteristic;
    private GattCharacteristic? _txCharacteristic;

    public BleChannel(IConfigManager configManager, ILogger<BleChannel> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    public CommunicationType Type => CommunicationType.Ble;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configManager.LoadAppConfigAsync(cancellationToken);
        var deviceName = config.BleDeviceName;
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new InvalidOperationException("未配置 BLE 设备名，请在设置中填写。");
        }

        var devices = await WithTimeoutAsync(
            DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector()).AsTask(cancellationToken),
            TimeSpan.FromSeconds(10),
            "设备枚举");
        var deviceInfo = devices.FirstOrDefault(d =>
            string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));

        if (deviceInfo is null)
        {
            throw new InvalidOperationException($"未找到已配对的蓝牙设备：{deviceName}");
        }

        var device = await WithTimeoutAsync(
            BluetoothLEDevice.FromIdAsync(deviceInfo.Id).AsTask(cancellationToken),
            TimeSpan.FromSeconds(10),
            "打开设备")
            ?? throw new InvalidOperationException($"无法打开蓝牙设备：{deviceName}");

        GattDeviceServicesResult serviceResult;
        GattCharacteristicsResult rxResult;
        GattCharacteristicsResult txResult;

        try
        {
            serviceResult = await WithTimeoutAsync(
                device.GetGattServicesForUuidAsync(new Guid(NusServiceUuid)).AsTask(cancellationToken),
                TimeSpan.FromSeconds(10),
                "服务发现");

            if (serviceResult.Status != GattCommunicationStatus.Success
                || serviceResult.Services.Count == 0)
            {
                throw new InvalidOperationException($"{deviceName} 未提供 NUS 服务，请确认固件已启用 BLE 模式。");
            }

            var service = serviceResult.Services[0];
            rxResult = await WithTimeoutAsync(
                service.GetCharacteristicsForUuidAsync(new Guid(NusRxUuid)).AsTask(cancellationToken),
                TimeSpan.FromSeconds(10),
                "RX 特征");
            txResult = await WithTimeoutAsync(
                service.GetCharacteristicsForUuidAsync(new Guid(NusTxUuid)).AsTask(cancellationToken),
                TimeSpan.FromSeconds(10),
                "TX 特征");

            if (rxResult.Status != GattCommunicationStatus.Success || rxResult.Characteristics.Count == 0
                || txResult.Status != GattCommunicationStatus.Success || txResult.Characteristics.Count == 0)
            {
                throw new InvalidOperationException("设备 NUS 特征不完整，请确认固件版本。");
            }

            var tx = txResult.Characteristics[0];
            var status = await WithTimeoutAsync(
                tx.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken),
                TimeSpan.FromSeconds(10),
                "订阅通知");

            if (status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException("订阅设备通知失败。");
            }
        }
        catch
        {
            try
            {
                device.Dispose();
            }
            catch
            {
                // 释放失败不影响抛出原因
            }

            throw;
        }

        lock (_sync)
        {
            _device?.Dispose();
            _device = device;
            _rxCharacteristic = rxResult.Characteristics[0];
            _txCharacteristic = txResult.Characteristics[0];
            IsConnected = true;
        }

    }

    public async Task SendAsync(PwmCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await SendLineAsync($"PWM:{command.PwmPercent:0.0}\r\n", cancellationToken);
    }

    public async Task SendTimeAsync(DateTime time, CancellationToken cancellationToken = default)
    {
        await SendLineAsync($"TIME:{time:yyyy/MM/dd HH:mm:ss}\r\n", cancellationToken);
    }

    public async Task SendTemperaturesAsync(double cpuCelsius, double? gpuCelsius, CancellationToken cancellationToken = default)
    {
        await SendLineAsync($"CPU{cpuCelsius:0.0}\r\n", cancellationToken);
        if (gpuCelsius is double gpu)
        {
            await SendLineAsync($"GPU{gpu:0.0}\r\n", cancellationToken);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        BluetoothLEDevice? device;
        lock (_sync)
        {
            device = _device;
            _device = null;
            _rxCharacteristic = null;
            _txCharacteristic = null;
            IsConnected = false;
        }

        if (device is not null)
        {
            try
            {
                device.Dispose();
            }
            catch
            {
                // 释放失败不影响退出
            }
        }

        await Task.CompletedTask;
    }

    private async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        GattCharacteristic? rx;
        lock (_sync)
        {
            rx = _rxCharacteristic;
            if (!IsConnected || rx is null)
            {
                throw new InvalidOperationException("BLE 未连接。");
            }
        }

        using var writer = new DataWriter();
        writer.WriteString(line);

        var writeTask = rx
            .WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse)
            .AsTask(cancellationToken);

        var completed = await Task.WhenAny(
            writeTask,
            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        if (completed != writeTask)
        {
            throw new InvalidOperationException("BLE 写入超时。");
        }

        var writeStatus = await writeTask;

        if (writeStatus != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"BLE 写入失败：{writeStatus}");
        }
    }

    private static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, string operation)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new InvalidOperationException($"BLE {operation}超时（{(int)timeout.TotalSeconds}s）。");
        }

        return await task;
    }
}
