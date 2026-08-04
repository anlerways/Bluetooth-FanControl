using System.IO.Ports;
using FanControl.Service.Config;
using FanControl.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace FanControl.Service.Communication;

/// <summary>
/// 串口通信通道：以一行 ASCII 发送 PWM 指令（PWM:&lt;0-100&gt;\r\n）。
/// 连接参数（端口/波特率）来自 AppConfig。
/// </summary>
public sealed class ComChannel : ICommunicationChannel
{
    private readonly IConfigManager _configManager;
    private readonly ILogger<ComChannel> _logger;
    private readonly object _sync = new();
    private SerialPort? _serialPort;

    public ComChannel(IConfigManager configManager, ILogger<ComChannel> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    public CommunicationType Type => CommunicationType.Com;

    public bool IsConnected => _serialPort?.IsOpen == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configManager.LoadAppConfigAsync(cancellationToken);

        SerialPort port;
        lock (_sync)
        {
            if (_serialPort?.IsOpen == true)
            {
                return;
            }

            _serialPort?.Dispose();

            port = new SerialPort(config.ComPort, config.ComBaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                // 蓝牙虚拟串口必须启用 DTR/RTS 才会真正建立 RFCOMM 链路
                DtrEnable = true,
                RtsEnable = true,
            };
        }

        // Open 在蓝牙虚拟串口/设备未就绪时可能无限阻塞（且会拖死通信循环），
        // 这里放到后台任务并加 3 秒超时，且不持有锁。
        var openTask = Task.Run(() => port.Open(), cancellationToken);
        var completed = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken));
        if (completed != openTask)
        {
            try
            {
                port.Dispose();
            }
            catch
            {
                // 释放失败继续
            }

            throw new InvalidOperationException($"串口打开超时（{config.ComPort}），设备可能未就绪。");
        }

        await openTask; // 若 Open 抛异常在此抛出

        lock (_sync)
        {
            _serialPort = port;
        }

        // 握手：发送 STATUS? 并读取应答，确认蓝牙 SPP 链路已建立（而非仅端口可打开）
        try
        {
            _serialPort.WriteLine("STATUS?");
            var reply = _serialPort.ReadLine();
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "串口握手无应答（{Port}）：蓝牙链路未就绪，关闭端口稍后重试。",
                config.ComPort);
            ClosePort();
            throw new InvalidOperationException("蓝牙链路未就绪（握手无应答）。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "串口握手失败（{Port}）。", config.ComPort);
            ClosePort();
            throw;
        }
    }

    private void ClosePort()
    {
        lock (_sync)
        {
            try
            {
                _serialPort?.Close();
            }
            catch
            {
                // 关闭失败继续释放
            }

            _serialPort?.Dispose();
            _serialPort = null;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Close();
            }

            _serialPort?.Dispose();
            _serialPort = null;
        }

        return Task.CompletedTask;
    }

    public Task SendAsync(PwmCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_sync)
        {
            if (_serialPort?.IsOpen != true)
            {
                throw new InvalidOperationException("串口未连接。");
            }

            var line = $"PWM:{command.PwmPercent:0.0}\r\n";
            _serialPort.Write(line);
            _serialPort.BaseStream.Flush();
        }

        return Task.CompletedTask;
    }

    public Task SendTimeAsync(DateTime time, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_serialPort?.IsOpen != true)
            {
                throw new InvalidOperationException("串口未连接。");
            }

            var line = $"TIME:{time:yyyy/MM/dd HH:mm:ss}\r\n";
            _serialPort.Write(line);
            _serialPort.BaseStream.Flush();
        }

        return Task.CompletedTask;
    }

    public Task SendTemperaturesAsync(double cpuCelsius, double? gpuCelsius, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_serialPort?.IsOpen != true)
            {
                throw new InvalidOperationException("串口未连接。");
            }

            var line = $"CPU{cpuCelsius:0.0}\r\n";
            if (gpuCelsius is double gpu)
            {
                line += $"GPU{gpu:0.0}\r\n";
            }

            _serialPort.Write(line);
            _serialPort.BaseStream.Flush();
        }

        return Task.CompletedTask;
    }
}
