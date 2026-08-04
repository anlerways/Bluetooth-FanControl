namespace FanControl.Shared.Enums;

/// <summary>
/// 风扇转速数据源（独立于温度数据源，用户可自行选择）。
/// ATKACPI 使用 G-Helper 同款 DSTS 端点（CPU_Fan/GPU_Fan，值 ×100 = RPM）。
/// </summary>
public enum FanSpeedSource
{
    AtkAcpi = 0,
    Wmi = 1,
    LibreHardwareMonitor = 2,
    Aida64 = 3,
    Simulated = 99,
}
