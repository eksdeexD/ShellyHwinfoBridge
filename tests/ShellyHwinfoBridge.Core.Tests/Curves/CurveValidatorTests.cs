using ShellyHwinfoBridge.Core.Curves;

namespace ShellyHwinfoBridge.Core.Tests.Curves;

public class CurveValidatorTests
{
    [Fact]
    public void ValidCurveHasNoErrors()
    {
        Assert.Empty(CurveValidator.Validate([new(100, 80), new(200, 180)]));
    }

    [Fact]
    public void FewerThanTwoRowsIsRejected()
    {
        var errors = CurveValidator.Validate([new(100, 80)]);
        Assert.Contains(errors, e => e.Code == CurveValidationErrorCode.TooFewPoints && e.RowIndex is null);
        Assert.Contains(CurveValidator.Validate([]), e => e.Code == CurveValidationErrorCode.TooFewPoints);
    }

    [Theory]
    [InlineData(100, 90)]
    [InlineData(90, 50)]
    public void AcMustStrictlyIncrease(double secondAc, double secondDc)
    {
        var errors = CurveValidator.Validate([new(100, 80), new(secondAc, secondDc)]);
        Assert.Contains(errors, e => e.Code == CurveValidationErrorCode.AcNotStrictlyIncreasing && e.RowIndex == 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DcMustBePositive(double dc)
    {
        var errors = CurveValidator.Validate([new(100, dc), new(200, 180)]);
        Assert.Contains(errors, e => e.Code == CurveValidationErrorCode.DcNotPositive && e.RowIndex == 0);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(250)]
    public void DcMustBeBelowAc(double dc)
    {
        var errors = CurveValidator.Validate([new(100, 80), new(200, dc)]);
        Assert.Contains(errors, e => e.Code == CurveValidationErrorCode.DcNotBelowAc && e.RowIndex == 1);
    }

    [Fact]
    public void NonFiniteValuesAreRejected()
    {
        var errors = CurveValidator.Validate([new(double.NaN, 80), new(200, double.PositiveInfinity)]);
        Assert.Equal(2, errors.Count(e => e.Code == CurveValidationErrorCode.NotFinite));
    }

    [Fact]
    public void PsuCurveConstructorThrowsWithStructuredErrors()
    {
        var ex = Assert.Throws<CurveValidationException>(() => new PsuCurve([new CurvePoint(100, 120)]));
        Assert.Equal(2, ex.Errors.Count);
    }

    [Fact]
    public void TryCreateReportsErrorsWithoutThrowing()
    {
        Assert.False(PsuCurve.TryCreate([new CurvePoint(200, 100), new CurvePoint(100, 50)], out var curve, out var errors));
        Assert.Null(curve);
        Assert.Single(errors);
    }
}
