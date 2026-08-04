using FanControl.Shared.Models;

namespace FanControl.Service.Fan;

/// <summary>
/// 温度-PWM 曲线线性插值。
/// 规则：温度低于首点取首点 PWM；高于末点取末点 PWM；输出始终钳制在 0-100。
/// </summary>
public static class CurveInterpolator
{
    public static double Calculate(double temperatureCelsius, IReadOnlyList<CurvePoint> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (curve.Count == 0)
        {
            throw new ArgumentException("曲线不能为空。", nameof(curve));
        }

        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i].TemperatureCelsius <= curve[i - 1].TemperatureCelsius)
            {
                throw new ArgumentException("曲线温度点必须严格递增。", nameof(curve));
            }
        }

        // 无有效温度（NaN）时返回 NaN，由调用方保持上次 PWM，避免插值落到曲线末点（100%）
        if (double.IsNaN(temperatureCelsius))
        {
            return double.NaN;
        }

        if (temperatureCelsius <= curve[0].TemperatureCelsius)
        {
            return ClampPwm(curve[0].PwmPercent);
        }

        if (temperatureCelsius >= curve[^1].TemperatureCelsius)
        {
            return ClampPwm(curve[^1].PwmPercent);
        }

        for (var i = 1; i < curve.Count; i++)
        {
            var lower = curve[i - 1];
            var upper = curve[i];

            if (temperatureCelsius <= upper.TemperatureCelsius)
            {
                var ratio =
                    (temperatureCelsius - lower.TemperatureCelsius)
                    / (upper.TemperatureCelsius - lower.TemperatureCelsius);

                return ClampPwm(lower.PwmPercent + ratio * (upper.PwmPercent - lower.PwmPercent));
            }
        }

        return ClampPwm(curve[^1].PwmPercent);
    }

    /// <summary>
    /// 转速-PWM 曲线线性插值：目标 RPM 低于首点取首点 PWM；高于末点取末点 PWM；输出钳制在 0-100。
    /// </summary>
    public static double CalculateRpm(double rpm, IReadOnlyList<RpmCurvePoint> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (curve.Count == 0)
        {
            throw new ArgumentException("转速曲线不能为空。", nameof(curve));
        }

        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i].Rpm <= curve[i - 1].Rpm)
            {
                throw new ArgumentException("转速曲线点必须严格递增。", nameof(curve));
            }
        }

        if (rpm <= curve[0].Rpm)
        {
            return ClampPwm(curve[0].PwmPercent);
        }

        if (rpm >= curve[^1].Rpm)
        {
            return ClampPwm(curve[^1].PwmPercent);
        }

        for (var i = 1; i < curve.Count; i++)
        {
            var lower = curve[i - 1];
            var upper = curve[i];

            if (rpm <= upper.Rpm)
            {
                var ratio = (rpm - lower.Rpm) / (upper.Rpm - lower.Rpm);
                return ClampPwm(lower.PwmPercent + ratio * (upper.PwmPercent - lower.PwmPercent));
            }
        }

        return ClampPwm(curve[^1].PwmPercent);
    }

    private static double ClampPwm(double pwm) => Math.Clamp(pwm, 0, 100);
}
