using System.Globalization;
using System.Text;
using System.Text.Json;
using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport;

/// <summary>Metadata for the database entry, from the command line.</summary>
public sealed class EntryMetadata
{
    /// <summary>Gets or sets the entry id (e.g. <c>seasonic-prime-fanless-tx-700</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the brand.</summary>
    public string Brand { get; set; } = string.Empty;

    /// <summary>Gets or sets the series.</summary>
    public string Series { get; set; } = string.Empty;

    /// <summary>Gets or sets the model.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the platform revision, if any.</summary>
    public string? Revision { get; set; }

    /// <summary>Gets or sets the rated wattage.</summary>
    public int Wattage { get; set; }

    /// <summary>Gets or sets the 80 Plus rating.</summary>
    public string Rating80Plus { get; set; } = string.Empty;

    /// <summary>Gets or sets the Cybenetics rating, if known.</summary>
    public string? RatingCybenetics { get; set; }

    /// <summary>Gets or sets the Cybenetics report id.</summary>
    public string? ReportId { get; set; }

    /// <summary>Gets or sets the report URL.</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>Builds, validates and formats one <c>psu-database.json</c> entry.</summary>
public static class EntryWriter
{
    /// <summary>Builds the entry from metadata and parsed curves.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="curves">(AC, DC) pairs per voltage, sorted by AC.</param>
    public static PsuEntry Build(EntryMetadata metadata, IReadOnlyDictionary<int, IReadOnlyList<(double Ac, double Dc)>> curves)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(curves);
        return new PsuEntry
        {
            Id = metadata.Id,
            Brand = metadata.Brand,
            Series = metadata.Series,
            Model = metadata.Model,
            Revision = string.IsNullOrWhiteSpace(metadata.Revision) ? null : metadata.Revision,
            Wattage = metadata.Wattage,
            Rating80Plus = metadata.Rating80Plus,
            RatingCybenetics = string.IsNullOrWhiteSpace(metadata.RatingCybenetics) ? null : metadata.RatingCybenetics,
            Source = new PsuSource { Name = "Cybenetics", ReportId = metadata.ReportId, Url = metadata.Url },
            Curves = curves
                .Where(c => c.Value.Count > 0)
                .ToDictionary(
                    c => c.Key.ToString(CultureInfo.InvariantCulture),
                    c => c.Value.Select(p => new[] { Math.Round(p.Ac, 3), Math.Round(p.Dc, 3) }).ToList()),
        };
    }

    /// <summary>Validates the entry with the app's own database validator.</summary>
    /// <param name="entry">The entry.</param>
    public static IReadOnlyList<DatabaseValidationError> Validate(PsuEntry entry) =>
        PsuDatabaseValidator.Validate(new PsuDatabase { SchemaVersion = PsuDatabaseValidator.SupportedSchemaVersion, Psus = [entry] });

    /// <summary>Formats the entry like <c>psu-database.json</c> (two-space indent, one [AC, DC] pair per line).</summary>
    /// <param name="entry">The entry.</param>
    public static string Format(PsuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var sb = new StringBuilder();
        sb.Append("{\n");
        Field(sb, 1, "id", Str(entry.Id));
        Field(sb, 1, "brand", Str(entry.Brand));
        Field(sb, 1, "series", Str(entry.Series));
        Field(sb, 1, "model", Str(entry.Model));
        Field(sb, 1, "revision", Str(entry.Revision));
        Field(sb, 1, "wattage", entry.Wattage.ToString(CultureInfo.InvariantCulture));
        Field(sb, 1, "rating80Plus", Str(entry.Rating80Plus));
        Field(sb, 1, "ratingCybenetics", Str(entry.RatingCybenetics));
        sb.Append("  \"source\": {\n");
        Field(sb, 2, "name", Str(entry.Source?.Name));
        Field(sb, 2, "reportId", Str(entry.Source?.ReportId));
        Field(sb, 2, "url", Str(entry.Source?.Url), last: true);
        sb.Append("  },\n");
        sb.Append("  \"curves\": {\n");
        var curves = (entry.Curves ?? []).OrderByDescending(c => c.Key, StringComparer.Ordinal).ToList();
        for (var i = 0; i < curves.Count; i++)
        {
            sb.Append("    ").Append(Str(curves[i].Key)).Append(": [\n");
            var pairs = curves[i].Value;
            for (var j = 0; j < pairs.Count; j++)
            {
                sb.Append("      [").Append(Num(pairs[j][0])).Append(", ").Append(Num(pairs[j][1])).Append(']');
                sb.Append(j < pairs.Count - 1 ? ",\n" : "\n");
            }

            sb.Append(i < curves.Count - 1 ? "    ],\n" : "    ]\n");
        }

        sb.Append("  }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void Field(StringBuilder sb, int depth, string name, string value, bool last = false) =>
        sb.Append(' ', depth * 2).Append(Str(name)).Append(": ").Append(value).Append(last ? "\n" : ",\n");

    private static string Str(string? value) => value is null ? "null" : JsonSerializer.Serialize(value);

    private static string Num(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}
