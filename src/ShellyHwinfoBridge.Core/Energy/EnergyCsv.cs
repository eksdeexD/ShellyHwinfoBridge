using System.Globalization;

namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>
/// The hourly energy CSV format: <c>hour_start_utc,ac_wh,dc_est_wh,samples,ac_w_min,ac_w_max,ac_w_avg</c>,
/// with ISO UTC hours and invariant numbers (three decimals, dot separator, empty for "no value").
/// </summary>
public static class EnergyCsv
{
    /// <summary>The header line.</summary>
    public const string Header = "hour_start_utc,ac_wh,dc_est_wh,samples,ac_w_min,ac_w_max,ac_w_avg";

    private const string HourFormat = "yyyy-MM-dd'T'HH':00:00Z'";
    private const string ParseFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>Formats one row (without a line break).</summary>
    /// <param name="record">The record.</param>
    public static string FormatRow(EnergyHourRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Join(
            ',',
            record.HourStartUtc.UtcDateTime.ToString(HourFormat, CultureInfo.InvariantCulture),
            Number(record.AcWh),
            Number(record.DcEstWh),
            record.Samples.ToString(CultureInfo.InvariantCulture),
            Number(record.AcWMin),
            Number(record.AcWMax),
            Number(record.AcWAvg));
    }

    /// <summary>Parses one row; returns <c>false</c> for the header, blank lines and malformed rows.</summary>
    /// <param name="line">The line.</param>
    /// <param name="record">The parsed record.</param>
    public static bool TryParseRow(string? line, out EnergyHourRecord record)
    {
        record = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Trim().Split(',');
        if (parts.Length != 7 ||
            !DateTime.TryParseExact(parts[0], ParseFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var hour) ||
            !TryNumber(parts[1], out var ac) || ac is null ||
            !TryNumber(parts[2], out var dc) || dc is null ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var samples) || samples < 0 ||
            !TryNumber(parts[4], out var min) ||
            !TryNumber(parts[5], out var max) ||
            !TryNumber(parts[6], out var avg))
        {
            return false;
        }

        record = new EnergyHourRecord(EnergyHourRecord.HourStart(new DateTimeOffset(hour, TimeSpan.Zero)), ac.Value, dc.Value, samples, min, max, avg);
        return true;
    }

    private static string Number(double? value) =>
        value is double v && double.IsFinite(v) ? v.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty;

    private static bool TryNumber(string text, out double? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
        {
            value = v;
            return true;
        }

        return false;
    }
}
