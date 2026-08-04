namespace FanControl.Shared.Enums;

/// <summary>风扇控制模式。</summary>
public enum FanControlMode
{
    Manual = 0,
    CpuTemp = 1,
    GpuTemp = 2,
    Mixed = 3,
    SystemFan = 4,
    // 目标转速：用户设定目标 RPM（0-10000），按转速-PWM 曲线映射出 PWM%
    TargetRpm = 5,
    // 混合（平均）：CPU/GPU 温度平均后查温度曲线
    MixedAvg = 6,
}
