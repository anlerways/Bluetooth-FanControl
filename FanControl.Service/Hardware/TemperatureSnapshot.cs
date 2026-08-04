namespace FanControl.Service.Hardware;

/// <summary>一次温度采样结果。CPU/GPU 温度、风扇转速均可能不可用（null，表示该源无数据）。</summary>
public sealed record TemperatureSnapshot(
    double? CpuTemperatureCelsius,
    double? GpuTemperatureCelsius,
    double? FanRpm);
