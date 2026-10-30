namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>Energy used during one clock hour (UTC), as stored in the monthly CSV files.</summary>
/// <param name="HourStartUtc">Start of the hour in UTC (minutes and seconds are zero).</param>
/// <param name="AcWh">Wall (AC) energy in watt-hours, from the plug's counter or integrated from samples.</param>
/// <param name="DcEstWh">Estimated DC energy in watt-hours, integrated from samples while the app was running.</param>
/// <param name="Samples">Number of readings that contributed; 0 for rows back-filled from a counter gap.</param>
/// <param name="AcWMin">Lowest AC power sample in watts, or <c>null</c> without samples.</param>
/// <param name="AcWMax">Highest AC power sample in watts, or <c>null</c> without samples.</param>
/// <param name="AcWAvg">Average AC power sample in watts, or <c>null</c> without samples.</param>
public sealed record EnergyHourRecord(
    DateTimeOffset HourStartUtc,
    double AcWh,
    double DcEstWh,
    int Samples,
    double? AcWMin,
    double? AcWMax,
    double? AcWAvg)
{
    /// <summary>
    /// Combines two rows for the same hour (for example a partial hour written at exit and the rest of that hour
    /// after a restart): energies and samples are summed, min/max are combined, the average is sample-weighted.
    /// </summary>
    /// <param name="other">The other row; its hour is not checked.</param>
    /// <returns>The merged row, keeping this row's hour.</returns>
    public EnergyHourRecord Merge(EnergyHourRecord other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var samples = Samples + other.Samples;
        double? avg = (AcWAvg, other.AcWAvg) switch
        {
            (double a, double b) when samples > 0 => ((a * Samples) + (b * other.Samples)) / samples,
            (double a, null) => a,
            (null, double b) => b,
            _ => null,
        };

        return new EnergyHourRecord(
            HourStartUtc,
            AcWh + other.AcWh,
            DcEstWh + other.DcEstWh,
            samples,
            MinOf(AcWMin, other.AcWMin),
            MaxOf(AcWMax, other.AcWMax),
            avg);
    }

    /// <summary>Returns the start of the UTC hour containing <paramref name="utc"/>.</summary>
    /// <param name="utc">Any instant.</param>
    public static DateTimeOffset HourStart(DateTimeOffset utc)
    {
        var u = utc.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, 0, 0, TimeSpan.Zero);
    }

    private static double? MinOf(double? a, double? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static double? MaxOf(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}

/// <summary>The persisted state of the plug's energy counter, used to derive consumption across restarts.</summary>
/// <param name="DeviceKey">Identifies the plug the counter belongs to (host and device id); a different key means a different counter.</param>
/// <param name="LastCounterWh">The last <c>aenergy.total</c> seen, or <c>null</c> when the device has no counter.</param>
/// <param name="LastSampleUtc">When that value was read.</param>
public sealed record EnergyCounterState(string DeviceKey, double? LastCounterWh, DateTimeOffset LastSampleUtc);
