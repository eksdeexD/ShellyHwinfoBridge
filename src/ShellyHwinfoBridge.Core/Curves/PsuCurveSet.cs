namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// The curves available for one PSU: a 115 V curve, a 230 V curve, or both. Single-voltage units (e.g. tested at
/// 230 V only) use their one curve for both voltages, and <see cref="CurveNote"/> says so.
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

    /// <summary>Returns the voltage of the curve <see cref="Select"/> uses: <paramref name="requested"/> when present, else the other one.</summary>
    /// <param name="requested">The requested input voltage.</param>
    public InputVoltage AvailableVoltage(InputVoltage requested) => requested == InputVoltage.V115
        ? (Curve115 is null ? InputVoltage.V230 : InputVoltage.V115)
        : (Curve230 is null ? InputVoltage.V115 : InputVoltage.V230);

    /// <summary>Returns the curve for <paramref name="voltage"/>, falling back to the other voltage when missing.</summary>
    /// <param name="voltage">The requested input voltage.</param>
    public PsuCurve Select(InputVoltage voltage) => AvailableVoltage(voltage) == InputVoltage.V115 ? Curve115! : Curve230!;

    /// <summary>
    /// Returns the note shown when <paramref name="requested"/> has no curve, e.g. "230 V curve (115 V data not
    /// available for this unit)"; <c>null</c> when the requested curve exists.
    /// </summary>
    /// <param name="requested">The requested input voltage.</param>
    public string? CurveNote(InputVoltage requested)
    {
        var used = AvailableVoltage(requested);
        return used == requested ? null : $"{(int)used} V curve ({(int)requested} V data not available for this unit)";
    }

    /// <summary>
    /// Returns the curve label for display: "230 V curve", or the <see cref="CurveNote"/> when the requested voltage
    /// falls back to the other curve.
    /// </summary>
    /// <param name="curves">The resolved curves, or <c>null</c> when unknown (then the requested voltage is shown).</param>
    /// <param name="requested">The requested input voltage.</param>
    public static string CurveLabel(PsuCurveSet? curves, InputVoltage requested) =>
        curves?.CurveNote(requested) ?? $"{(int)requested} V curve";
}
