using FanControl.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FanControl.Service.Hardware;

/// <summary>按配置选择风扇转速数据源（策略工厂）。</summary>
public sealed class FanSpeedProviderFactory
{
    private readonly IServiceProvider _services;

    public FanSpeedProviderFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IFanSpeedProvider Create(FanSpeedSource source) =>
        source switch
        {
            FanSpeedSource.AtkAcpi => Get<AtkAcpiFanSpeedProvider>(),
            FanSpeedSource.Wmi => Get<WmiFanSpeedProvider>(),
            FanSpeedSource.LibreHardwareMonitor => Get<LibreHardwareMonitorFanSpeedProvider>(),
            FanSpeedSource.Aida64 => Get<Aida64FanSpeedProvider>(),
            FanSpeedSource.Simulated => Get<SimulatedFanSpeedProvider>(),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "未知的风扇转速数据源。"),
        };

    private IFanSpeedProvider Get<T>()
        where T : IFanSpeedProvider
        => _services.GetRequiredService<T>();
}
