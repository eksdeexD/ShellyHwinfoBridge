namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// The curves available for one PSU: a 115 V curve, a 230 V curve, or both.
/// </summary>
public sealed class PsuCurveSet
{
    /// <summary>Creates a curve set. At least one curve is required.</summary>
    /// <param name="curve115">The 115 V curve, if any.</param>
    /// <param name="curve230">The 230 V curve, if any.</param>
    public PsuCurveSet(PsuCurve? curve115, PsuCurve? curve230)
    {
        if (curve115 is null && curve230 is null)
        {
            throw new ArgumentException("At least one curve is required.");
        }

        Curve115 = curve115;
        Curve230 = curve230;
    }

    /// <summary>Gets the 115 V curve, if any.</summary>
    public PsuCurve? Curve115 { get; }

    /// <summary>Gets the 230 V curve, if any.</summary>
    public PsuCurve? Curve230 { get; }

    /// <summary>Creates a set that uses the same curve for both voltages (e.g. a manual curve).</summary>
    /// <param name="curve">The curve.</param>
    public static PsuCurveSet Single(PsuCurve curve) => new(curve, curve);

    /// <summary>Returns the curve for <paramref name="voltage"/>, falling back to the other voltage when missing.</summary>
    /// <param name="voltage">The requested input voltage.</param>
    public PsuCurve Select(InputVoltage voltage) => voltage == InputVoltage.V115
        ? Curve115 ?? Curve230!
        : Curve230 ?? Curve115!;
}
