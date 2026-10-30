using ShellyHwinfoBridge.Core.Curves;

namespace ShellyHwinfoBridge.Core.Tests.Curves;

public class GenericCurveGeneratorTests
{
    [Fact]
    public void Gold850_230V_Gives92PercentAt425WDc()
    {
        // SPEC acceptance test 5.
        var curve = GenericCurveGenerator.Generate(850, EightyPlusRating.Gold, InputVoltage.V230);
        var point = Assert.Single(curve.Points, p => Math.Abs(p.DcWatts - 425) < 1e-9);
        Assert.Equal(0.92, point.Efficiency, 9);

        var estimator = new DcEstimator(curve);
        Assert.Equal(425, estimator.EstimateDc(425 / 0.92), 6);
    }

    public static TheoryData<EightyPlusRating, InputVoltage> AllCombinations()
    {
        var data = new TheoryData<EightyPlusRating, InputVoltage>();
        foreach (var rating in Enum.GetValues<EightyPlusRating>())
        {
            data.Add(rating, InputVoltage.V115);
            data.Add(rating, InputVoltage.V230);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void GeneratesValidCurveMatchingRequirements(EightyPlusRating rating, InputVoltage voltage)
    {
        const int watts = 750;
        var req = GenericCurveGenerator.GetRequirements(rating, voltage);
        var curve = GenericCurveGenerator.Generate(watts, rating, voltage);

        Assert.True(CurveValidator.IsValid(curve.Points));
        AssertEfficiencyAt(curve, 0.20 * watts, req.At20Percent);
        AssertEfficiencyAt(curve, 0.50 * watts, req.At50Percent);
        AssertEfficiencyAt(curve, 1.00 * watts, req.At100Percent);

        // Low-load falloff: 5 % load is 10 points below the 20 % value and is the first point.
        AssertEfficiencyAt(curve, 0.05 * watts, req.At20Percent - 10);
        Assert.Equal(0.05 * watts, curve.Points[0].DcWatts, 9);
    }

    [Theory]
    [InlineData(InputVoltage.V115)]
    [InlineData(InputVoltage.V230)]
    public void TitaniumIncludesTenPercentPointAt90(InputVoltage voltage)
    {
        var curve = GenericCurveGenerator.Generate(1000, EightyPlusRating.Titanium, voltage);
        Assert.Equal(5, curve.Points.Count);
        AssertEfficiencyAt(curve, 100, 90);
    }

    [Fact]
    public void NonTitaniumHasNoTenPercentPoint()
    {
        var curve = GenericCurveGenerator.Generate(1000, EightyPlusRating.Platinum, InputVoltage.V230);
        Assert.Equal(4, curve.Points.Count);
        Assert.DoesNotContain(curve.Points, p => Math.Abs(p.DcWatts - 100) < 1e-9);
    }

    [Fact]
    public void EfficiencyFallsTowardsLightLoad()
    {
        var estimator = new DcEstimator(GenericCurveGenerator.Generate(850, EightyPlusRating.Gold, InputVoltage.V230));
        Assert.True(estimator.EstimateDc(100) / 100 < estimator.EstimateDc(150) / 150);
    }

    [Theory]
    [InlineData(EightyPlusRating.White, InputVoltage.V115, 80, 80, 80)]
    [InlineData(EightyPlusRating.Bronze, InputVoltage.V230, 85, 88, 85)]
    [InlineData(EightyPlusRating.Platinum, InputVoltage.V115, 90, 92, 89)]
    [InlineData(EightyPlusRating.Titanium, InputVoltage.V230, 94, 96, 94)]
    public void RequirementsMatchSpecTable(EightyPlusRating rating, InputVoltage voltage, double e20, double e50, double e100)
    {
        var req = GenericCurveGenerator.GetRequirements(rating, voltage);
        Assert.Equal((e20, e50, e100), (req.At20Percent, req.At50Percent, req.At100Percent));
    }

    [Fact]
    public void GenerateSetHasBothVoltages()
    {
        var set = GenericCurveGenerator.GenerateSet(650, EightyPlusRating.Bronze);
        Assert.NotNull(set.Curve115);
        Assert.NotNull(set.Curve230);
        Assert.NotSame(set.Curve115, set.Curve230);
    }

    private static void AssertEfficiencyAt(PsuCurve curve, double dcWatts, double expectedPercent)
    {
        var point = Assert.Single(curve.Points, p => Math.Abs(p.DcWatts - dcWatts) < 1e-9);
        Assert.Equal(expectedPercent / 100, point.Efficiency, 9);
    }
}
