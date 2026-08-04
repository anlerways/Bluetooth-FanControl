using FanControl.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FanControl.Service.Hardware;

/// <summary>按配置选择温度数据源（策略工厂）。</summary>
public sealed class TemperatureProviderFactory
{
    private readonly IServiceProvider _services;

    public TemperatureProviderFactory(IServiceProvider services)
    {
        _services = services;
    }

    public ITemperatureProvider Create(TemperatureSource source) =>
        source switch
        {
            TemperatureSource.AtkAcpi => Get<AtkAcpiTemperatureProvider>(),
            TemperatureSource.Wmi => Get<WmiTemperatureProvider>(),
            TemperatureSource.LibreHardwareMonitor => Get<LibreHardwareMonitorTemperatureProvider>(),
            TemperatureSource.Aida64 => Get<Aida64TemperatureProvider>(),
            TemperatureSource.Simulated => Get<SimulatedTemperatureProvider>(),
            TemperatureSource.NvidiaSmiAdl => throw new InvalidOperationException(
                "NVIDIA-SMI / ADL 仅用作 GPU 温度源，不能作为 CPU 温度数据源。"),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "未知的温度数据源。"),
        };

    private ITemperatureProvider Get<T>()
        where T : ITemperatureProvider
        => _services.GetRequiredService<T>();
}
