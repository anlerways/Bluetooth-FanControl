using FanControl.Shared.Enums;
using FanControl.Shared.Models;

namespace FanControl.Service.Fan;

/// <summary>
/// 风扇控制引擎：按模式计算目标 PWM。
/// Manual / CpuTemp / GpuTemp / Mixed 返回 0-100 的目标值；
/// Mixed 取 CPU/GPU 温度较高者（最高温度曲线）；MixedAvg 取平均温度。
/// TargetRpm（风扇转速映射）按实测风扇转速（0-10000）通过转速-PWM 曲线映射。
/// SystemFan 为历史遗留值：返回 null（不覆盖），设置页已移除该选项。
/// </summary>
public sealed class FanController
{
    public double? CalculateTargetPwm(
        FanControlMode mode,
        double cpuTemperatureCelsius,
        double gpuTemperatureCelsius,
        double manualPwmPercent,
        IReadOnlyList<CurvePoint> curve,
        double fanRpm = 0,
        IReadOnlyList<RpmCurvePoint>? rpmCurve = null)
    {
        switch (mode)
        {
            case FanControlMode.Manual:
                return Math.Clamp(manualPwmPercent, 0, 100);

            case FanControlMode.CpuTemp:
                return CurveInterpolator.Calculate(cpuTemperatureCelsius, curve);

            case FanControlMode.GpuTemp:
                return CurveInterpolator.Calculate(gpuTemperatureCelsius, curve);

            case FanControlMode.Mixed:
                var maxTemperature = Math.Max(cpuTemperatureCelsius, gpuTemperatureCelsius);
                return CurveInterpolator.Calculate(maxTemperature, curve);

            case FanControlMode.MixedAvg:
                var averageTemperature = (cpuTemperatureCelsius + gpuTemperatureCelsius) / 2.0;
                return CurveInterpolator.Calculate(averageTemperature, curve);

            case FanControlMode.SystemFan:
                // 历史遗留：跟随系统风扇（BIOS/EC 控制），不再提供 UI 选项
                return null;

            case FanControlMode.TargetRpm:
                ArgumentNullException.ThrowIfNull(rpmCurve);
                return CurveInterpolator.CalculateRpm(fanRpm, rpmCurve);

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的控制模式。");
        }
    }
}
