namespace FanControl.Shared.Models;

/// <summary>温度-PWM 曲线点（温度 ℃，PWM 0-100）。</summary>
public sealed record CurvePoint(double TemperatureCelsius, double PwmPercent);
