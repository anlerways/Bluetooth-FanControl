using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>模拟温度数据源：正弦波动，用于开发/演示/无硬件调试。</summary>
public sealed class SimulatedTemperatureProvider : ITemperatureProvider
{
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    public TemperatureSource Source => TemperatureSource.Simulated;

    public Task<TemperatureSnapshot> ReadAsync(
        CancellationToken cancellationToken = default,
        string? gpuSelection = null)
    {
        var elapsedSeconds = (DateTimeOffset.UtcNow - _started).TotalSeconds;
        var cpu = 45 + 15 * Math.Sin(elapsedSeconds / 20);
        var gpu = cpu + 8;
        var rpm = 2200 + 800 * Math.Sin(elapsedSeconds / 12) + 400 * Math.Sin(elapsedSeconds / 37);

        return Task.FromResult(new TemperatureSnapshot(cpu, gpu, FanRpm: rpm));
    }
}
