using System.Globalization;
using System.Text;
using System.Text.Json;
using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport;

/// <summary>
/// <c>merge --entries &lt;dir&gt; --into &lt;psu-database.json&gt;</c>: merges entry JSON files into the database, replacing
/// entries with the same id, sorting by brand and model, setting <c>updated</c> to today and validating the result
/// before it is written.
/// </summary>
public static class MergeCommand
{
    /// <summary>Runs the merge.</summary>
    /// <param name="options">The parsed options (<c>--entries</c>, <c>--into</c>, optional <c>--date</c>).</param>
    /// <param name="log">Progress and errors.</param>
    /// <param name="today">The date written to <c>updated</c> unless <c>--date</c> is given.</param>
    internal static int Run(OptionSet options, TextWriter log, DateTime today)
    {
        var entriesDir = options.Require("entries");
        var databasePath = options.Require("into");
        var date = options.Get("date") is { } d
            ? DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new ArgumentException("--date must be yyyy-MM-dd.")
            : today;

        if (!Directory.Exists(entriesDir))
        {
            throw new ArgumentException($"--entries directory {entriesDir} does not exist.");
        }

        var errors = new List<string>();
        PsuDatabase database;
        try
        {
            database = PsuDatabaseJson.Deserialize(File.ReadAllText(databasePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.WriteLine($"error: cannot read {databasePath}: {ex.Message}");
            return ImportCommand.Failed;
        }
        var entries = new List<(string File, PsuEntry Entry)>();
        foreach (var file in Directory.GetFiles(entriesDir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<PsuEntry>(File.ReadAllText(file), PsuDatabaseJson.Options)
                    ?? throw new JsonException("The entry is null.");
                entries.Add((file, entry));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var duplicates = entries.GroupBy(e => e.Entry.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
        errors.AddRange(duplicates.Select(g => $"id {g.Key} appears in {string.Join(", ", g.Select(e => Path.GetFileName(e.File)))}."));

        var (merged, added, replaced) = Merge(database, entries.Select(e => e.Entry), date);
        errors.AddRange(PsuDatabaseValidator.Validate(merged).Select(e => e.ToString()));
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                log.WriteLine("error: " + error);
            }

            log.WriteLine($"Nothing written: fix the errors above ({databasePath} is unchanged).");
            return ImportCommand.Failed;
        }

        File.WriteAllText(databasePath, Format(merged));
        log.WriteLine($"Merged {entries.Count} entries ({added} added, {replaced} replaced) into {databasePath}; {merged.Psus.Count} entries in total, updated {merged.Updated}.");
        return ImportCommand.Ok;
    }

    /// <summary>Merges entries into a copy of the database: same id (case-insensitive) replaces, sorted by brand, model.</summary>
    /// <param name="database">The current database.</param>
    /// <param name="entries">The entries to add or replace.</param>
    /// <param name="date">The new <c>updated</c> date.</param>
    public static (PsuDatabase Database, int Added, int Replaced) Merge(PsuDatabase database, IEnumerable<PsuEntry> entries, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(entries);
        var byId = new Dictionary<string, PsuEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in database.Psus ?? [])
        {
            byId[existing.Id] = existing;
        }

        int added = 0, replaced = 0;
        foreach (var entry in entries)
        {
            if (byId.ContainsKey(entry.Id))
            {
                replaced++;
            }
            else
            {
                added++;
            }

            byId[entry.Id] = entry;
        }

        var merged = new PsuDatabase
        {
            SchemaVersion = database.SchemaVersion,
            Updated = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Psus = [.. byId.Values
                .OrderBy(e => e.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Model, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Revision ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)],
        };
        return (merged, added, replaced);
    }

    /// <summary>Formats the database like the shipped <c>psu-database.json</c> (one [AC, DC] pair per line).</summary>
    /// <param name="database">The database.</param>
    public static string Format(PsuDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"schemaVersion\": ").Append(database.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("  \"updated\": ").Append(database.Updated is null ? "null" : JsonSerializer.Serialize(database.Updated)).Append(",\n");
        sb.Append("  \"psus\": [");
        var psus = database.Psus ?? [];
        sb.Append(psus.Count == 0 ? "]\n" : "\n");
        for (var i = 0; i < psus.Count; i++)
        {
            var lines = EntryWriter.Format(psus[i]).TrimEnd('\n').Split('\n');
            for (var j = 0; j < lines.Length; j++)
            {
                sb.Append("    ").Append(lines[j]);
                sb.Append(j < lines.Length - 1 ? "\n" : i < psus.Count - 1 ? ",\n" : "\n");
            }
        }

        if (psus.Count > 0)
        {
            sb.Append("  ]\n");
        }

        sb.Append("}\n");
        return sb.ToString();
    }
}
