namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// Builds a curve from the simplified manual entry form: efficiency % at 10/20/50/100 % load plus rated wattage.
/// </summary>
/// <remarks>Each point is DC = load · rated watts, AC = DC / efficiency.</remarks>
public static class SimplifiedCurveBuilder
{
    /// <summary>The load fractions the form asks for.</summary>
    public static IReadOnlyList<double> LoadFractions { get; } = [0.10, 0.20, 0.50, 1.00];

    /// <summary>Builds the curve rows (not yet validated as a curve).</summary>
    /// <param name="ratedWatts">Rated DC output in watts (&gt; 0).</param>
    /// <param name="eff10">Efficiency % at 10 % load (0–100 exclusive).</param>
    /// <param name="eff20">Efficiency % at 20 % load.</param>
    /// <param name="eff50">Efficiency % at 50 % load.</param>
    /// <param name="eff100">Efficiency % at 100 % load.</param>
    public static IReadOnlyList<CurvePoint> BuildPoints(int ratedWatts, double eff10, double eff20, double eff50, double eff100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ratedWatts, 0);
        double[] efficiencies = [eff10, eff20, eff50, eff100];
        var points = new CurvePoint[efficiencies.Length];
        for (var i = 0; i < efficiencies.Length; i++)
        {
            var eff = efficiencies[i];
            if (!double.IsFinite(eff) || eff <= 0 || eff >= 100)
            {
                throw new ArgumentOutOfRangeException(nameof(efficiencies), eff, "Efficiency must be between 0 and 100 % (exclusive).");
            }

            var dc = LoadFractions[i] * ratedWatts;
            points[i] = new CurvePoint(dc / (eff / 100.0), dc);
        }

        return points;
    }

    /// <summary>Builds and validates the curve.</summary>
    /// <param name="ratedWatts">Rated DC output in watts (&gt; 0).</param>
    /// <param name="eff10">Efficiency % at 10 % load.</param>
    /// <param name="eff20">Efficiency % at 20 % load.</param>
    /// <param name="eff50">Efficiency % at 50 % load.</param>
    /// <param name="eff100">Efficiency % at 100 % load.</param>
    /// <exception cref="CurveValidationException">The efficiencies do not produce a valid curve.</exception>
    public static PsuCurve Build(int ratedWatts, double eff10, double eff20, double eff50, double eff100) =>
        new(BuildPoints(ratedWatts, eff10, eff20, eff50, eff100));
}
