namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// Estimates DC output power from measured AC input power using a <see cref="PsuCurve"/>.
/// </summary>
/// <remarks>
/// Between curve points DC is linearly interpolated from AC. Below the first point the first point's
/// DC/AC ratio is applied; above the last point the last point's ratio is applied.
/// Zero, negative or non-finite AC yields 0.
/// </remarks>
public sealed class DcEstimator
{
    /// <summary>Creates an estimator for the given curve.</summary>
    /// <param name="curve">The active PSU curve.</param>
    public DcEstimator(PsuCurve curve)
    {
        Curve = curve ?? throw new ArgumentNullException(nameof(curve));
    }

    /// <summary>Gets the curve used for estimation.</summary>
    public PsuCurve Curve { get; }

    /// <summary>Estimates DC output power for an AC input power.</summary>
    /// <param name="acWatts">AC input power in watts.</param>
    /// <returns>Estimated DC output power in watts.</returns>
    public double EstimateDc(double acWatts)
    {
        if (!double.IsFinite(acWatts) || acWatts <= 0)
        {
            return 0;
        }

        var points = Curve.Points;
        var first = points[0];
        if (acWatts <= first.AcWatts)
        {
            return acWatts * first.Efficiency;
        }

        var last = points[^1];
        if (acWatts >= last.AcWatts)
        {
            return acWatts * last.Efficiency;
        }

        var lo = 0;
        var hi = points.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (points[mid].AcWatts <= acWatts)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var a = points[lo];
        var b = points[hi];
        var t = (acWatts - a.AcWatts) / (b.AcWatts - a.AcWatts);
        return a.DcWatts + (t * (b.DcWatts - a.DcWatts));
    }
}
