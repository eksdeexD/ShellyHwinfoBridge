namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// Validates PSU curves: at least two rows, AC strictly increasing, and 0 &lt; DC &lt; AC on every row.
/// </summary>
public static class CurveValidator
{
    /// <summary>The minimum number of rows a curve must have.</summary>
    public const int MinimumPoints = 2;

    /// <summary>Validates a curve and returns every problem found (empty when the curve is valid).</summary>
    /// <param name="points">The curve rows, in order.</param>
    public static IReadOnlyList<CurveValidationError> Validate(IReadOnlyList<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var errors = new List<CurveValidationError>();

        if (points.Count < MinimumPoints)
        {
            errors.Add(new CurveValidationError(
                CurveValidationErrorCode.TooFewPoints,
                null,
                $"A curve needs at least {MinimumPoints} rows but has {points.Count}."));
        }

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var row = i + 1;

            if (!double.IsFinite(p.AcWatts) || !double.IsFinite(p.DcWatts))
            {
                errors.Add(new CurveValidationError(CurveValidationErrorCode.NotFinite, i, $"Row {row}: values must be finite numbers."));
                continue;
            }

            if (p.DcWatts <= 0)
            {
                errors.Add(new CurveValidationError(CurveValidationErrorCode.DcNotPositive, i, $"Row {row}: DC must be greater than 0."));
            }
            else if (p.DcWatts >= p.AcWatts)
            {
                errors.Add(new CurveValidationError(CurveValidationErrorCode.DcNotBelowAc, i, $"Row {row}: DC must be less than AC."));
            }

            if (i > 0 && double.IsFinite(points[i - 1].AcWatts) && p.AcWatts <= points[i - 1].AcWatts)
            {
                errors.Add(new CurveValidationError(
                    CurveValidationErrorCode.AcNotStrictlyIncreasing,
                    i,
                    $"Row {row}: AC must be greater than the previous row's AC."));
            }
        }

        return errors;
    }

    /// <summary>Returns <c>true</c> when <paramref name="points"/> forms a valid curve.</summary>
    /// <param name="points">The curve rows, in order.</param>
    public static bool IsValid(IReadOnlyList<CurvePoint> points) => Validate(points).Count == 0;
}
