using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>
/// The texts that credit a database PSU's Cybenetics report: the ETA grade per input voltage and the report date.
/// Cybenetics' conditions: the grade is written exactly "Cybenetics ETA &lt;grade&gt;", always with its voltage, and only
/// for units with a published ETA rating; it is never presented as an 80 Plus tier.
/// </summary>
public static class PsuSourceText
{
    /// <summary>
    /// Returns the ETA text. With a voltage: the grade at the voltage of the curve the app uses for it (the other curve
    /// when the unit has none at <paramref name="voltage"/>), "Cybenetics ETA Gold (230 V)". Without one: every grade,
    /// 230 V first, "Cybenetics ETA Gold (230 V), ETA Platinum (115 V)". <c>null</c> when there is no grade to show.
    /// </summary>
    /// <param name="entry">The database entry.</param>
    /// <param name="voltage">The input voltage in use, or <c>null</c> when it is not known yet.</param>
    public static string? Eta(PsuEntry entry, InputVoltage? voltage)
    {
        ArgumentNullException.ThrowIfNull(entry);
        IEnumerable<InputVoltage> voltages = voltage is { } requested
            ? [entry.GetCurveSet()?.AvailableVoltage(requested) ?? requested]
            : [InputVoltage.V230, InputVoltage.V115];
        var parts = voltages
            .Select(v => (Voltage: v, Grade: entry.GetEtaRating(v)))
            .Where(p => p.Grade is not null)
            .Select((p, i) => $"{(i == 0 ? "Cybenetics ETA" : "ETA")} {p.Grade} ({(int)p.Voltage} V)")
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// Returns the ETA grades as separate lines, each written in full: with a voltage the one grade
    /// <see cref="Eta"/> shows ("Cybenetics ETA Gold (230 V)"); without one every grade, 230 V first
    /// ("Cybenetics ETA Gold (230 V)", "Cybenetics ETA Platinum (115 V)"). Empty when there is no grade to show.
    /// </summary>
    /// <param name="entry">The database entry.</param>
    /// <param name="voltage">The input voltage in use, or <c>null</c> when it is not known yet.</param>
    public static IReadOnlyList<string> EtaLines(PsuEntry entry, InputVoltage? voltage)
    {
        ArgumentNullException.ThrowIfNull(entry);
        IEnumerable<InputVoltage> voltages = voltage is { } requested
            ? [entry.GetCurveSet()?.AvailableVoltage(requested) ?? requested]
            : [InputVoltage.V230, InputVoltage.V115];
        return voltages
            .Select(v => (Voltage: v, Grade: entry.GetEtaRating(v)))
            .Where(p => p.Grade is not null)
            .Select(p => $"Cybenetics ETA {p.Grade} ({(int)p.Voltage} V)")
            .ToList();
    }

    /// <summary>
    /// Returns the report link text "report 21PS1872A · 2021-07-16 ↗" (without a report id "report 2021-07-16 ↗",
    /// with neither "report ↗"), or <c>null</c> when the source has no URL to open.
    /// </summary>
    /// <param name="source">The source, or <c>null</c>.</param>
    public static string? ReportLink(PsuSource? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Url))
        {
            return null;
        }

        var id = string.IsNullOrWhiteSpace(source.ReportId) ? null : source.ReportId.Trim();
        var date = string.IsNullOrWhiteSpace(source.ReportDate) ? null : source.ReportDate.Trim();
        var parts = string.Join(" · ", new[] { id, date }.OfType<string>());
        return parts.Length == 0 ? "report ↗" : "report " + parts + " ↗";
    }

    /// <summary>Returns the 80 Plus text "80 Plus Gold", or <c>null</c> when the entry has none ("None" or empty).</summary>
    /// <param name="entry">The database entry.</param>
    public static string? EightyPlus(PsuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrWhiteSpace(entry.Rating80Plus) || entry.Rating80Plus.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)
            ? null
            : "80 Plus " + entry.Rating80Plus.Trim();
    }

    /// <summary>Returns "report 2021-07-16", or <c>null</c> when the source has no report date.</summary>
    /// <param name="source">The source, or <c>null</c>.</param>
    public static string? ReportDate(PsuSource? source) =>
        string.IsNullOrWhiteSpace(source?.ReportDate) ? null : "report " + source.ReportDate.Trim();
}
