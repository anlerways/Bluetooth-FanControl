using FanControl.Service.Fan;
using FanControl.Shared.Models;

namespace FanControl.Tests;

public class CurveInterpolatorTests
{
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

    [Theory]
    [InlineData(20, 20)]
    [InlineData(30, 20)]
    [InlineData(50, 35)]
    [InlineData(60, 47.5)]
    [InlineData(90, 100)]
    [InlineData(100, 100)]
    public void Calculate_ClampsAndInterpolates(double temperature, double expected)
    {
        var actual = CurveInterpolator.Calculate(temperature, Curve);

        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void Calculate_SinglePointCurve_ReturnsConstant()
    {
        var single = new List<CurvePoint> { new(40, 55) };

        Assert.Equal(55, CurveInterpolator.Calculate(10, single));
        Assert.Equal(55, CurveInterpolator.Calculate(40, single));
        Assert.Equal(55, CurveInterpolator.Calculate(90, single));
    }

    [Fact]
    public void Calculate_ClampsOutOfRangePwmToZeroToHundred()
    {
        var curve = new List<CurvePoint> { new(40, -20), new(80, 150) };

        Assert.Equal(0, CurveInterpolator.Calculate(10, curve));
        Assert.Equal(100, CurveInterpolator.Calculate(90, curve));
    }

    [Fact]
    public void Calculate_EmptyCurve_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CurveInterpolator.Calculate(50, Array.Empty<CurvePoint>()));
    }

    [Fact]
    public void Calculate_UnsortedOrDuplicateTemperatures_Throws()
    {
        var unsorted = new List<CurvePoint> { new(70, 60), new(30, 20) };
        var duplicate = new List<CurvePoint> { new(30, 20), new(30, 30) };

        Assert.Throws<ArgumentException>(() => CurveInterpolator.Calculate(50, unsorted));
        Assert.Throws<ArgumentException>(() => CurveInterpolator.Calculate(50, duplicate));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2000, 25)]
    [InlineData(3000, 35)]
    [InlineData(4000, 45)]
    [InlineData(10000, 100)]
    [InlineData(12000, 100)]
    public void CalculateRpm_ClampsAndInterpolates(double rpm, double expected)
    {
        var actual = CurveInterpolator.CalculateRpm(rpm, RpmCurve);

        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void CalculateRpm_EmptyCurve_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CurveInterpolator.CalculateRpm(3000, Array.Empty<RpmCurvePoint>()));
    }

    [Fact]
    public void CalculateRpm_UnsortedOrDuplicateRpm_Throws()
    {
        var unsorted = new List<RpmCurvePoint> { new(4000, 45), new(0, 0) };
        var duplicate = new List<RpmCurvePoint> { new(2000, 20), new(2000, 30) };

        Assert.Throws<ArgumentException>(() => CurveInterpolator.CalculateRpm(2000, unsorted));
        Assert.Throws<ArgumentException>(() => CurveInterpolator.CalculateRpm(2000, duplicate));
    }
}
