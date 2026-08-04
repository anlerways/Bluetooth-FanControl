using FanControl.Service.Fan;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;

namespace FanControl.Tests;

public class FanControllerTests
{
    private readonly FanController _controller = new();

    private static readonly IReadOnlyList<CurvePoint> Curve =
        new List<CurvePoint>
        {
            new(30, 20),
            new(50, 35),
            new(70, 60),
            new(90, 100),
        };

    private static readonly IReadOnlyList<RpmCurvePoint> RpmCurve =
        new List<RpmCurvePoint>
        {
            new(0, 0),
            new(2000, 25),
            new(4000, 45),
            new(6000, 65),
            new(8000, 85),
            new(10000, 100),
        };

    [Fact]
    public void Manual_ReturnsClampedManualPwm()
    {
        Assert.Equal(55, _controller.CalculateTargetPwm(FanControlMode.Manual, 60, 70, 55, Curve));
        Assert.Equal(100, _controller.CalculateTargetPwm(FanControlMode.Manual, 60, 70, 150, Curve));
        Assert.Equal(0, _controller.CalculateTargetPwm(FanControlMode.Manual, 60, 70, -5, Curve));
    }

    [Fact]
    public void CpuTemp_UsesCpuTemperature()
    {
        // CPU 50℃ → 35%
        Assert.Equal(35, _controller.CalculateTargetPwm(FanControlMode.CpuTemp, 50, 90, 10, Curve));
    }

    [Fact]
    public void GpuTemp_UsesGpuTemperature()
    {
        // GPU 50℃ → 35%
        Assert.Equal(35, _controller.CalculateTargetPwm(FanControlMode.GpuTemp, 90, 50, 10, Curve));
    }

    [Fact]
    public void Mixed_UsesMaxTemperature()
    {
        // max(50, 80) = 80℃ → 60 + (80-70)/(90-70) * (100-60) = 80%
        Assert.Equal(80, _controller.CalculateTargetPwm(FanControlMode.Mixed, 50, 80, 10, Curve));
        Assert.Equal(80, _controller.CalculateTargetPwm(FanControlMode.Mixed, 80, 50, 10, Curve));
    }

    [Fact]
    public void MixedAvg_UsesAverageTemperature()
    {
        // avg(50, 80) = 65℃ → 35 + (65-50)/(70-50) * (60-35) = 53.75%
        Assert.Equal(53.75, _controller.CalculateTargetPwm(FanControlMode.MixedAvg, 50, 80, 10, Curve));
        Assert.Equal(53.75, _controller.CalculateTargetPwm(FanControlMode.MixedAvg, 80, 50, 10, Curve));
    }

    [Fact]
    public void SystemFan_ReturnsNoOverride()
    {
        Assert.Null(_controller.CalculateTargetPwm(FanControlMode.SystemFan, 60, 60, 50, Curve));
    }

    [Fact]
    public void TargetRpm_MapsMeasuredFanRpmThroughRpmCurve()
    {
        // 实测风扇转速 2000 RPM → 25%
        Assert.Equal(
            25,
            _controller.CalculateTargetPwm(FanControlMode.TargetRpm, 60, 60, 10, Curve, 2000, RpmCurve));
        // 实测 3000 RPM → 25 + 1000/2000 × 20 = 35%
        Assert.Equal(
            35,
            _controller.CalculateTargetPwm(FanControlMode.TargetRpm, 60, 60, 10, Curve, 3000, RpmCurve));
        // 超上限钳到末点 100%
        Assert.Equal(
            100,
            _controller.CalculateTargetPwm(FanControlMode.TargetRpm, 60, 60, 10, Curve, 15000, RpmCurve));
    }

    [Fact]
    public void TargetRpm_NullCurve_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _controller.CalculateTargetPwm(FanControlMode.TargetRpm, 60, 60, 10, Curve, 2000, null));
    }

    [Fact]
    public void UnknownMode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _controller.CalculateTargetPwm((FanControlMode)999, 60, 60, 50, Curve));
    }
}
