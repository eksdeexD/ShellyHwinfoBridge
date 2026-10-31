using System.Text;
using System.Text.RegularExpressions;

namespace CybeneticsImport;

/// <summary>One row of the maintainer-reviewed catalogue.</summary>
/// <param name="LineNumber">The 1-based line number in the CSV file, for messages.</param>
/// <param name="Values">The row's values by column name (case-insensitive).</param>
public sealed record CatalogRow(int LineNumber, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Gets a value, or an empty string when the column is missing.</summary>
    /// <param name="column">The column name.</param>
    public string this[string column] => Values.TryGetValue(column, out var v) ? v.Trim() : string.Empty;

    /// <summary>Gets the entry id.</summary>
    public string Id => this["id"];

    /// <summary>Gets the Cybenetics report id (also the PDF file name).</summary>
    public string ReportId => this["reportId"];

    /// <summary>Builds the entry metadata from the row.</summary>
    /// <exception cref="ArgumentException">A value is missing or malformed.</exception>
    public EntryMetadata ToMetadata() => EntryMetadata.FromValues(new Dictionary<string, string?>
    {
        ["id"] = this["id"],
        ["brand"] = this["brand"],
        ["series"] = this["series"],
        ["model"] = this["model"],
        ["revision"] = this["revision"],
        ["wattage"] = this["wattage"],
        ["rating80plus"] = this["rating80Plus"],
        ["ratingcybenetics"] = this["ratingCybenetics"],
        ["reportid"] = this["reportId"],
        ["url"] = this["reportUrl"],
    });
}

/// <summary>
/// Reads the catalogue CSV (RFC 4180: comma separated, optional double quotes, a header row with the columns
/// <c>id,brand,series,model,revision,wattage,rating80Plus,ratingCybenetics,reportId,reportUrl,pdfUrl,tier,notes</c>).
/// </summary>
public static partial class Catalog
{
    /// <summary>The columns every catalogue must have.</summary>
    public static IReadOnlyList<string> Columns { get; } =
        ["id", "brand", "series", "model", "revision", "wattage", "rating80Plus", "ratingCybenetics", "reportId", "reportUrl", "pdfUrl", "tier", "notes"];

    /// <summary>Reads a catalogue file.</summary>
    /// <param name="path">The CSV file.</param>
    public static IReadOnlyList<CatalogRow> Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses catalogue text. Blank lines are skipped.</summary>
    /// <param name="text">The CSV text.</param>
    /// <exception cref="ArgumentException">The header lacks a column, or a row has the wrong number of fields.</exception>
    public static IReadOnlyList<CatalogRow> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = ReadRecords(text.TrimStart('﻿')).Where(r => r.Fields.Any(f => f.Length > 0)).ToList();
        if (records.Count == 0)
        {
            throw new ArgumentException("The catalogue is empty.");
        }

        var header = records[0].Fields.Select(f => f.Trim()).ToList();
        var missing = Columns.Where(c => !header.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"The catalogue header lacks {string.Join(", ", missing)}.");
        }

        var rows = new List<CatalogRow>();
        foreach (var (line, fields) in records.Skip(1))
        {
            if (fields.Count != header.Count)
            {
                throw new ArgumentException($"Catalogue line {line} has {fields.Count} fields; the header has {header.Count}.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                values[header[i]] = fields[i];
            }

            rows.Add(new CatalogRow(line, values));
        }

        return rows;
    }

    /// <summary>Returns whether a value is safe to use as a file name (letters, digits, '.', '_', '-').</summary>
    /// <param name="value">The value.</param>
    public static bool IsSafeFileName(string value) => SafeNameRegex().IsMatch(value) && !value.StartsWith('.');

    /// <summary>Quotes a value for a CSV field when needed.</summary>
    /// <param name="value">The value.</param>
    public static string Quote(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static IEnumerable<(int Line, List<string> Fields)> ReadRecords(string text)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    line += c == '\n' ? 1 : 0;
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                yield return (recordLine, fields);
                fields = [];
                line++;
                recordLine = line;
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (recordLine, fields);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeNameRegex();
}
