using FanControl.Shared.Enums;

namespace FanControl.Service.Hardware;

/// <summary>温度数据源策略接口（策略模式）。</summary>
public interface ITemperatureProvider
{
    TemperatureSource Source { get; }

    /// <param name="gpuSelection">GPU 选择：Auto=自动（多卡取最高温），或厂商/名称关键词。</param>
    Task<TemperatureSnapshot> ReadAsync(
        CancellationToken cancellationToken = default,
        string? gpuSelection = null);
}
