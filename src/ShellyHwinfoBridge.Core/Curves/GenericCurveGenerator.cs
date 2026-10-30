namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>80 Plus certification levels.</summary>
public enum EightyPlusRating
{
    /// <summary>80 Plus (White / Standard).</summary>
    White,

    /// <summary>80 Plus Bronze.</summary>
    Bronze,

    /// <summary>80 Plus Silver.</summary>
    Silver,

    /// <summary>80 Plus Gold.</summary>
    Gold,

    /// <summary>80 Plus Platinum.</summary>
    Platinum,

    /// <summary>80 Plus Titanium.</summary>
    Titanium,
}

/// <summary>Minimum 80 Plus efficiencies (percent) at the defined load levels.</summary>
/// <param name="At10Percent">Efficiency at 10 % load (Titanium only), otherwise <c>null</c>.</param>
/// <param name="At20Percent">Efficiency at 20 % load.</param>
/// <param name="At50Percent">Efficiency at 50 % load.</param>
/// <param name="At100Percent">Efficiency at 100 % load.</param>
public sealed record EfficiencyRequirements(double? At10Percent, double At20Percent, double At50Percent, double At100Percent);

/// <summary>
/// Generates approximate PSU curves from the 80 Plus minimum efficiency requirements.
/// </summary>
/// <remarks>
/// <para>
/// Model: for rated wattage W, each defined load fraction f with minimum efficiency e gives a point with
/// DC = f·W and AC = DC / e. The defined points are 20/50/100 % (115 V and 230 V EU internal tables),
/// plus 10 % for Titanium (90 % at both voltages).
/// </para>
/// <para>
/// Low-load falloff: below the lowest defined point (10 % for Titanium, 20 % otherwise) one extra point is
/// added at 5 % load with an efficiency <see cref="LowLoadEfficiencyDropPoints"/> percentage points below
/// the 20 % value. Linear AC/DC interpolation between that point and the lowest defined point is exactly a
/// fixed-plus-proportional loss model (AC = k·DC + L₀), so efficiency falls off hyperbolically towards
/// light load, which is how real PSUs behave. Below 5 % load the estimator applies the 5 % point's ratio.
/// </para>
/// <para>This is a rough model and is least accurate at low load; the UI must label it as approximate.</para>
/// </remarks>
public static class GenericCurveGenerator
{
    /// <summary>The load fraction of the extrapolated low-load point.</summary>
    public const double LowLoadFraction = 0.05;

    /// <summary>How many percentage points below the 20 % efficiency the 5 % load point is.</summary>
    public const double LowLoadEfficiencyDropPoints = 10;

    /// <summary>Returns the 80 Plus minimum requirements for a rating and input voltage.</summary>
    /// <param name="rating">The 80 Plus rating.</param>
    /// <param name="voltage">115 V or 230 V EU internal.</param>
    public static EfficiencyRequirements GetRequirements(EightyPlusRating rating, InputVoltage voltage)
    {
        var is230 = voltage == InputVoltage.V230;
        return rating switch
        {
            EightyPlusRating.White => is230 ? new(null, 82, 85, 82) : new(null, 80, 80, 80),
            EightyPlusRating.Bronze => is230 ? new(null, 85, 88, 85) : new(null, 82, 85, 82),
            EightyPlusRating.Silver => is230 ? new(null, 87, 90, 87) : new(null, 85, 88, 85),
            EightyPlusRating.Gold => is230 ? new(null, 90, 92, 89) : new(null, 87, 90, 87),
            EightyPlusRating.Platinum => is230 ? new(null, 92, 94, 90) : new(null, 90, 92, 89),
            EightyPlusRating.Titanium => is230 ? new(90, 94, 96, 94) : new(90, 92, 94, 90),
            _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, "Unknown rating."),
        };
    }

    /// <summary>Generates the (load fraction, efficiency %) anchor points used for a curve.</summary>
    /// <param name="rating">The 80 Plus rating.</param>
    /// <param name="voltage">The input voltage.</param>
    public static IReadOnlyList<(double LoadFraction, double EfficiencyPercent)> GetAnchors(EightyPlusRating rating, InputVoltage voltage)
    {
        var req = GetRequirements(rating, voltage);
        var anchors = new List<(double, double)>
        {
            (LowLoadFraction, req.At20Percent - LowLoadEfficiencyDropPoints),
        };
        if (req.At10Percent is double at10)
        {
            anchors.Add((0.10, at10));
        }

        anchors.Add((0.20, req.At20Percent));
        anchors.Add((0.50, req.At50Percent));
        anchors.Add((1.00, req.At100Percent));
        return anchors;
    }

    /// <summary>Generates a generic curve for one input voltage.</summary>
    /// <param name="ratedWatts">The PSU's rated DC output in watts.</param>
    /// <param name="rating">The 80 Plus rating.</param>
    /// <param name="voltage">The input voltage.</param>
    public static PsuCurve Generate(int ratedWatts, EightyPlusRating rating, InputVoltage voltage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ratedWatts, 0);
        return new PsuCurve(GetAnchors(rating, voltage).Select(a =>
        {
            var dc = a.LoadFraction * ratedWatts;
            return new CurvePoint(dc / (a.EfficiencyPercent / 100.0), dc);
        }));
    }

    /// <summary>Generates generic curves for both input voltages.</summary>
    /// <param name="ratedWatts">The PSU's rated DC output in watts.</param>
    /// <param name="rating">The 80 Plus rating.</param>
    public static PsuCurveSet GenerateSet(int ratedWatts, EightyPlusRating rating) =>
        new(Generate(ratedWatts, rating, InputVoltage.V115), Generate(ratedWatts, rating, InputVoltage.V230));
}
