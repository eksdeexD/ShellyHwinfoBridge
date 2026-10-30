namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// One point of a PSU efficiency curve: AC input power at the wall and the resulting DC output power, in watts.
/// </summary>
/// <param name="AcWatts">AC input power in watts.</param>
/// <param name="DcWatts">DC output power in watts.</param>
public readonly record struct CurvePoint(double AcWatts, double DcWatts)
{
    /// <summary>Gets the efficiency (DC / AC) as a fraction, or 0 when <see cref="AcWatts"/> is 0.</summary>
    public double Efficiency => AcWatts == 0 ? 0 : DcWatts / AcWatts;
}
