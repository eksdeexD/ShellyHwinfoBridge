using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Curves;

public class DcEstimatorTests
{
    [Fact]
    public void Tx700_230V_At151_9W_Ac_Gives141_7W_Dc()
    {
        // SPEC acceptance test 4.
        var estimator = new DcEstimator(TestCurves.Tx700At230);
        Assert.Equal(141.7, estimator.EstimateDc(151.9), 0.05);
    }

    [Fact]
    public void ExactCurvePointsAreReproduced()
    {
        var curve = TestCurves.Tx700At230;
        var estimator = new DcEstimator(curve);
        foreach (var p in curve.Points)
        {
            Assert.Equal(p.DcWatts, estimator.EstimateDc(p.AcWatts), 9);
        }
    }

    [Theory]
    [InlineData(150, 130)]
    [InlineData(125, 105)]
    [InlineData(175, 155)]
    public void InterpolatesLinearlyBetweenPoints(double ac, double expectedDc)
    {
        Assert.Equal(expectedDc, new DcEstimator(TestCurves.Simple).EstimateDc(ac), 9);
    }

    [Fact]
    public void BelowFirstPointUsesFirstPointRatio()
    {
        var estimator = new DcEstimator(TestCurves.Simple);
        Assert.Equal(40, estimator.EstimateDc(50), 9);
        Assert.Equal(0.8, estimator.EstimateDc(10) / 10, 9);
    }

    [Fact]
    public void AboveLastPointUsesLastPointRatio()
    {
        var estimator = new DcEstimator(TestCurves.Simple);
        Assert.Equal(360, estimator.EstimateDc(400), 9);
    }

    [Fact]
    public void Tx700EdgesUseEndRatios()
    {
        var estimator = new DcEstimator(TestCurves.Tx700At230);
        Assert.Equal(10 * 20.298 / 25.948, estimator.EstimateDc(10), 9);
        Assert.Equal(1000 * 769.816 / 831.082, estimator.EstimateDc(1000), 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonPositiveOrInvalidAcGivesZero(double ac)
    {
        Assert.Equal(0, new DcEstimator(TestCurves.Simple).EstimateDc(ac));
    }
}
