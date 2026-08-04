namespace FanControl.Shared.Enums;

/// <summary>温度数据源（策略模式，M2 实现 Provider）。</summary>
public enum TemperatureSource
{
    AtkAcpi = 0,
    Wmi = 1,
    LibreHardwareMonitor = 2,
    Aida64 = 3,
    // GPU 专用数据源：NVIDIA nvidia-smi → AMD ADL（不能作为 CPU 数据源）
    NvidiaSmiAdl = 4,
    Simulated = 99,
}
