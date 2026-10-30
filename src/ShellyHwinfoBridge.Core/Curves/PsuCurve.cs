namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// An immutable, validated PSU efficiency curve: (AC W, DC W) points sorted by strictly increasing AC.
/// </summary>
public sealed class PsuCurve
{
    private readonly CurvePoint[] _points;

    /// <summary>Creates a curve from the given points.</summary>
    /// <param name="points">The curve rows, in order of increasing AC.</param>
    /// <exception cref="CurveValidationException">The points do not form a valid curve.</exception>
    public PsuCurve(IEnumerable<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var array = points.ToArray();
        var errors = CurveValidator.Validate(array);
        if (errors.Count > 0)
        {
            throw new CurveValidationException(errors);
        }

        _points = array;
    }

    /// <summary>Gets the curve points, sorted by increasing AC.</summary>
    public IReadOnlyList<CurvePoint> Points => _points;

    /// <summary>Tries to create a curve without throwing.</summary>
    /// <param name="points">The curve rows.</param>
    /// <param name="curve">The created curve, or <c>null</c> when invalid.</param>
    /// <param name="errors">The validation errors (empty on success).</param>
    /// <returns><c>true</c> when the curve is valid.</returns>
    public static bool TryCreate(IEnumerable<CurvePoint> points, out PsuCurve? curve, out IReadOnlyList<CurveValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(points);
        var array = points.ToArray();
        errors = CurveValidator.Validate(array);
        curve = errors.Count == 0 ? new PsuCurve(array) : null;
        return curve is not null;
    }
}
