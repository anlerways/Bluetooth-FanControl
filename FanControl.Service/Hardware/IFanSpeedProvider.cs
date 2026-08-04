using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>风扇转速数据源策略接口（返回 RPM，null 表示当前数据源不可用/读不到）。</summary>
public interface IFanSpeedProvider
{
    FanSpeedSource Source { get; }

    Task<double?> ReadAsync(CancellationToken cancellationToken = default);
}
