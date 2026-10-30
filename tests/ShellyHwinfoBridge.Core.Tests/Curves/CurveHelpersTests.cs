using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Curves;

public class CurveHelpersTests
{
    [Fact]
    public void SimplifiedEntryBuildsCurveAtFourLoads()
    {
        var curve = SimplifiedCurveBuilder.Build(1000, 85, 90, 92, 89);
        Assert.Equal([100.0, 200, 500, 1000], curve.Points.Select(p => p.DcWatts));
        Assert.Equal(0.92, curve.Points[2].Efficiency, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-3)]
    public void SimplifiedEntryRejectsInvalidEfficiency(double eff)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SimplifiedCurveBuilder.Build(500, eff, 90, 92, 89));
    }

    [Fact]
    public void CsvRoundTripsUnderDanishCulture()
    {
        using var _ = new CultureScope("da-DK");
        var points = TestCurves.Tx700At230.Points;
        var csv = CurveCsv.Export(points);

        Assert.StartsWith("ac_w,dc_w\r\n25.948,20.298\r\n", csv, StringComparison.Ordinal);
        var parsed = CurveCsv.Parse(csv);
        Assert.True(parsed.Success);
        Assert.Equal(points, parsed.Points);
    }

    [Fact]
    public void CsvHeaderIsOptionalAndBlankLinesIgnored()
    {
        var parsed = CurveCsv.Parse("\n100,80\n\n200.5, 180.25\n");
        Assert.True(parsed.Success);
        Assert.Equal([new CurvePoint(100, 80), new CurvePoint(200.5, 180.25)], parsed.Points);
    }

    [Fact]
    public void CsvReportsBadLinesWithLineNumbers()
    {
        var parsed = CurveCsv.Parse("ac_w,dc_w\n100;80\n200,abc\n300,250");
        Assert.False(parsed.Success);
        Assert.Equal([2, 3], parsed.Errors.Select(e => e.LineNumber));
        Assert.Single(parsed.Points);
    }

    [Fact]
    public void MovingAverageSmoothsOverWindow()
    {
        var avg = new MovingAverage(3);
        Assert.Equal(10, avg.Add(10));
        Assert.Equal(15, avg.Add(20));
        Assert.Equal(20, avg.Add(30));
        Assert.Equal(30, avg.Add(40));
        avg.Reset();
        Assert.Equal(7, avg.Add(7));
    }

    [Fact]
    public void MovingAverageOfOneIsPassThrough()
    {
        var avg = new MovingAverage(1);
        Assert.Equal(10, avg.Add(10));
        Assert.Equal(99, avg.Add(99));
    }

    [Theory]
    [InlineData(230.4, InputVoltage.V230)]
    [InlineData(180, InputVoltage.V230)]
    [InlineData(179.9, InputVoltage.V115)]
    [InlineData(118, InputVoltage.V115)]
    public void VoltageAutoSelect(double volts, InputVoltage expected)
    {
        Assert.Equal(expected, VoltageSelector.FromMeasured(volts));
        Assert.Equal(expected, VoltageSelector.Resolve(InputVoltageMode.Auto, volts));
    }

    [Fact]
    public void VoltageOverrideWins()
    {
        Assert.Equal(InputVoltage.V115, VoltageSelector.Resolve(InputVoltageMode.V115, 230));
        Assert.Equal(InputVoltage.V230, VoltageSelector.Resolve(InputVoltageMode.V230, 115));
        Assert.Equal(InputVoltage.V230, VoltageSelector.Resolve(InputVoltageMode.Auto, null));
    }

    [Fact]
    public void PowerCalculatorSelectsCurveByMeasuredVoltage()
    {
        var calc = new PowerCalculator(TestCurves.Tx700.GetCurveSet()!);
        var high = calc.Calculate(151.862, 231);
        var low = calc.Calculate(151.718, 117);
        Assert.Equal((InputVoltage.V230, 141.691), (high.CurveVoltage, Math.Round(high.DcWatts, 3)));
        Assert.Equal((InputVoltage.V115, 139.962), (low.CurveVoltage, Math.Round(low.DcWatts, 3)));
        Assert.Equal(151.862 - 141.691, high.LossWatts, 9);
    }

    [Fact]
    public void PowerCalculatorSmoothsAcBeforeConversion()
    {
        var calc = new PowerCalculator(PsuCurveSet.Single(TestCurves.Simple), InputVoltageMode.Auto, smoothingSamples: 2);
        calc.Calculate(100, 230);
        var r = calc.Calculate(200, 230);
        Assert.Equal(200, r.AcWatts);
        Assert.Equal(150, r.SmoothedAcWatts);
        Assert.Equal(130, r.DcWatts, 9);
    }
}
