using System.Globalization;
using System.Text.Json;
using ShellyHwinfoBridge.Core.Curves;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>A database validation problem.</summary>
/// <param name="Path">Location in the document, e.g. <c>psus[2].curves.230</c>; empty for the root.</param>
/// <param name="Message">Description of the problem.</param>
public sealed record DatabaseValidationError(string Path, string Message)
{
    /// <inheritdoc />
    public override string ToString() => Path.Length == 0 ? Message : $"{Path}: {Message}";
}

/// <summary>
/// Validates a PSU database before it is used or replaces the local copy: schema version 1, required fields,
/// unique ids, attribution present (with an optional yyyy-MM-dd report date), and every curve passing <see cref="CurveValidator"/>.
/// </summary>
public static class PsuDatabaseValidator
{
    /// <summary>The only supported schema version.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>Accepted values for <see cref="PsuEntry.Rating80Plus"/> (case-insensitive).</summary>
    public static IReadOnlyList<string> AllowedRatings { get; } =
        [.. Enum.GetNames<EightyPlusRating>(), "None"];

    /// <summary>
    /// Accepted values of <see cref="PsuEntry.RatingsCybenetics"/> (case-insensitive), the Cybenetics ETA grades from
    /// best to lowest.
    /// </summary>
    public static IReadOnlyList<string> AllowedEtaRatings { get; } =
        ["Diamond", "Titanium", "Platinum", "Gold", "Silver", "Bronze", "Standard"];

    /// <summary>Parses and validates JSON text.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="database">The parsed database when valid; otherwise <c>null</c>.</param>
    /// <returns>The problems found; empty when valid.</returns>
    public static IReadOnlyList<DatabaseValidationError> ValidateJson(string? json, out PsuDatabase? database)
    {
        database = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return [new DatabaseValidationError(string.Empty, "The file is empty.")];
        }

        PsuDatabase parsed;
        try
        {
            parsed = PsuDatabaseJson.Deserialize(json);
        }
        catch (JsonException ex)
        {
            return [new DatabaseValidationError(string.Empty, $"Invalid JSON: {ex.Message}")];
        }

        var errors = Validate(parsed);
        if (errors.Count == 0)
        {
            database = parsed;
        }

        return errors;
    }

    /// <summary>Validates an already-parsed database.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The problems found; empty when valid.</returns>
    public static IReadOnlyList<DatabaseValidationError> Validate(PsuDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var errors = new List<DatabaseValidationError>();

        if (database.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(new("schemaVersion", $"Expected {SupportedSchemaVersion} but found {database.SchemaVersion}."));
        }

        if (database.Psus is null)
        {
            errors.Add(new("psus", "Required."));
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < database.Psus.Count; i++)
        {
            var path = $"psus[{i}]";
            var entry = database.Psus[i];
            if (entry is null)
            {
                errors.Add(new(path, "Entry is null."));
                continue;
            }

            ValidateEntry(entry, path, errors);
            if (!string.IsNullOrWhiteSpace(entry.Id) && !ids.Add(entry.Id))
            {
                errors.Add(new($"{path}.id", $"Duplicate id '{entry.Id}'."));
            }
        }

        return errors;
    }

    private static void ValidateEntry(PsuEntry entry, string path, List<DatabaseValidationError> errors)
    {
        Require(entry.Id, $"{path}.id", errors);
        Require(entry.Brand, $"{path}.brand", errors);
        Require(entry.Series, $"{path}.series", errors);
        Require(entry.Model, $"{path}.model", errors);

        if (entry.Wattage <= 0)
        {
            errors.Add(new($"{path}.wattage", "Must be greater than 0."));
        }

        if (!AllowedRatings.Contains(entry.Rating80Plus ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new($"{path}.rating80Plus", $"Must be one of {string.Join(", ", AllowedRatings)}."));
        }

        // ratingsCybenetics is optional; when present it maps "115"/"230" to an ETA grade.
        foreach (var (key, grade) in entry.RatingsCybenetics ?? [])
        {
            var etaPath = $"{path}.ratingsCybenetics.{key}";
            if (key != "115" && key != "230")
            {
                errors.Add(new(etaPath, "ETA keys must be \"115\" or \"230\"."));
            }
            else if (!AllowedEtaRatings.Contains(grade ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new(etaPath, $"Must be one of {string.Join(", ", AllowedEtaRatings)}."));
            }
        }

        if (entry.Source is null)
        {
            errors.Add(new($"{path}.source", "Required (attribution and report link)."));
        }
        else
        {
            Require(entry.Source.Name, $"{path}.source.name", errors);

            // source.reportId is optional (older reports have none); the URL is the required link to the report.
            if (!Uri.TryCreate(entry.Source.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                errors.Add(new($"{path}.source.url", "Must be an absolute http(s) URL."));
            }

            // source.reportDate is optional; when present it must be a real calendar date written as yyyy-MM-dd.
            if (entry.Source.ReportDate is not null && !IsIsoDate(entry.Source.ReportDate))
            {
                errors.Add(new($"{path}.source.reportDate", "Must be a date written as yyyy-MM-dd."));
            }
        }

        ValidateCurves(entry, path, errors);
    }

    private static void ValidateCurves(PsuEntry entry, string path, List<DatabaseValidationError> errors)
    {
        if (entry.Curves is null || entry.Curves.Count == 0)
        {
            errors.Add(new($"{path}.curves", "At least one curve (\"115\" or \"230\") is required."));
            return;
        }

        foreach (var (key, pairs) in entry.Curves)
        {
            var curvePath = $"{path}.curves.{key}";
            if (key != "115" && key != "230")
            {
                errors.Add(new(curvePath, "Curve keys must be \"115\" or \"230\"."));
                continue;
            }

            if (pairs is null)
            {
                errors.Add(new(curvePath, "Required."));
                continue;
            }

            var badPair = pairs.FindIndex(p => p is null || p.Length != 2);
            if (badPair >= 0)
            {
                errors.Add(new($"{curvePath}[{badPair}]", "Each point must be an [AC, DC] pair."));
                continue;
            }

            foreach (var error in CurveValidator.Validate(pairs.Select(p => new CurvePoint(p[0], p[1])).ToArray()))
            {
                errors.Add(new(curvePath, error.Message));
            }
        }
    }

    /// <summary>Returns whether a value is a calendar date written exactly as yyyy-MM-dd.</summary>
    /// <param name="value">The value.</param>
    public static bool IsIsoDate(string? value) =>
        value is { Length: 10 } &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static void Require(string? value, string path, List<DatabaseValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(path, "Required."));
        }
    }
}
