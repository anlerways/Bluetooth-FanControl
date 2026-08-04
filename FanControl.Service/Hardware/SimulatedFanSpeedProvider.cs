using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>模拟风扇转速数据源：正弦波动，用于开发/演示/无硬件调试。</summary>
public sealed class SimulatedFanSpeedProvider : IFanSpeedProvider
{
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    public FanSpeedSource Source => FanSpeedSource.Simulated;

    public Task<double?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var elapsedSeconds = (DateTimeOffset.UtcNow - _started).TotalSeconds;
        var rpm = 2200 + 800 * Math.Sin(elapsedSeconds / 12) + 400 * Math.Sin(elapsedSeconds / 37);
        return Task.FromResult<double?>(rpm);
    }
}
