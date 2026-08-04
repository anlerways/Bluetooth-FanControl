namespace FanControl.Shared.Models;

/// <summary>转速-PWM 曲线点（目标转速 RPM 0-10000，PWM 0-100%）。</summary>
public sealed record RpmCurvePoint(double Rpm, double PwmPercent);
