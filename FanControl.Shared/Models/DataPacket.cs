namespace FanControl.Shared.Models;

/// <summary>IPC 推送数据包骨架：温度/转速/PWM/状态快照。</summary>
public sealed record DataPacket(
    double CpuTemperatureCelsius,
    double GpuTemperatureCelsius,
    double FanRpm,
    /// <summary>平滑后的目标 PWM（实际发送给外部控制器）。</summary>
    double PwmPercent,
    string FanControlMode,
    DateTimeOffset Timestamp,
    /// <summary>平滑前的瞬时目标 PWM（曲线/手动直接计算结果）。</summary>
    double RawTargetPwmPercent = 0);
