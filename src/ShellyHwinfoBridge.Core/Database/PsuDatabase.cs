using System.Text.Json.Serialization;
using ShellyHwinfoBridge.Core.Curves;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>The PSU database (<c>psu-database.json</c>), schema version 1.</summary>
public sealed class PsuDatabase
{
    /// <summary>Gets or sets the schema version; must be 1.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets or sets the date the database was last updated (ISO yyyy-MM-dd).</summary>
    public string? Updated { get; set; }

    /// <summary>Gets or sets the PSU entries.</summary>
    public List<PsuEntry> Psus { get; set; } = [];
}

/// <summary>One PSU model in the database.</summary>
public sealed class PsuEntry
{
    /// <summary>Gets or sets the unique, stable identifier (e.g. <c>seasonic-prime-fanless-tx-700</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the brand.</summary>
    public string Brand { get; set; } = string.Empty;

    /// <summary>Gets or sets the series.</summary>
    public string Series { get; set; } = string.Empty;

    /// <summary>Gets or sets the model name.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the platform revision, for models that share a name across platforms.</summary>
    public string? Revision { get; set; }

    /// <summary>Gets or sets the rated DC wattage.</summary>
    public int Wattage { get; set; }

    /// <summary>Gets or sets the 80 Plus rating name (White, Bronze, Silver, Gold, Platinum, Titanium or None).</summary>
    public string Rating80Plus { get; set; } = string.Empty;

    /// <summary>Gets or sets the Cybenetics ETA rating, if known.</summary>
    public string? RatingCybenetics { get; set; }

    /// <summary>Gets or sets the data source attribution.</summary>
    public PsuSource? Source { get; set; }

    /// <summary>Gets or sets the curves keyed by voltage ("115", "230"); each is a list of [AC, DC] pairs.</summary>
    public Dictionary<string, List<double[]>>? Curves { get; set; }

    /// <summary>Gets a display name: model plus revision, if any.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Revision) ? Model : $"{Model} ({Revision})";

    /// <summary>Returns the curve for a voltage, or <c>null</c> if the entry has none or it is invalid.</summary>
    /// <param name="voltage">The input voltage.</param>
    public PsuCurve? GetCurve(InputVoltage voltage)
    {
        if (Curves is null || !Curves.TryGetValue(VoltageKey(voltage), out var pairs) || pairs is null)
        {
            return null;
        }

        if (pairs.Any(p => p is null || p.Length != 2))
        {
            return null;
        }

        return PsuCurve.TryCreate(pairs.Select(p => new CurvePoint(p[0], p[1])), out var curve, out _) ? curve : null;
    }

    /// <summary>Returns the entry's curves as a set, or <c>null</c> when it has no valid curve.</summary>
    public PsuCurveSet? GetCurveSet()
    {
        var c115 = GetCurve(InputVoltage.V115);
        var c230 = GetCurve(InputVoltage.V230);
        return c115 is null && c230 is null ? null : new PsuCurveSet(c115, c230);
    }

    /// <summary>Returns the JSON key used for a voltage ("115" or "230").</summary>
    /// <param name="voltage">The input voltage.</param>
    public static string VoltageKey(InputVoltage voltage) => voltage == InputVoltage.V115 ? "115" : "230";
}

/// <summary>Attribution for a PSU entry's curve data.</summary>
public sealed class PsuSource
{
    /// <summary>Gets or sets the source name (e.g. Cybenetics).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report identifier; optional (<c>null</c> or empty), since some older reports have none.
    /// The URL is then the only reference to the report.
    /// </summary>
    public string? ReportId { get; set; }

    /// <summary>Gets or sets the URL of the original report (required).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report date: the "Testing Date" printed in the Cybenetics report, as ISO yyyy-MM-dd.
    /// Optional (<c>null</c> when unknown) and then left out of the JSON. Databases without it still load, and
    /// 1.0.0 clients, which do not know the field, skip it like any unknown field.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReportDate { get; set; }
}
