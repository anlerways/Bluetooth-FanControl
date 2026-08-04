using FanControl.Shared.Enums;

namespace FanControl.Service.Communication;

/// <summary>与 ESP32 的通信通道（策略模式）。</summary>
public interface ICommunicationChannel
{
    CommunicationType Type { get; }

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task SendAsync(PwmCommand command, CancellationToken cancellationToken = default);

    /// <summary>下发当前时间（设备 OLED 时钟显示，协议 TIME:yyyy/MM/dd HH:mm:ss）。</summary>
    Task SendTimeAsync(DateTime time, CancellationToken cancellationToken = default);

    /// <summary>下发 CPU/GPU 温度（协议 CPU<值>/GPU<值>，供设备 OLED 显示）。</summary>
    Task SendTemperaturesAsync(double cpuCelsius, double? gpuCelsius, CancellationToken cancellationToken = default);
}
