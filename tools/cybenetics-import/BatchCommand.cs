using System.Text;

namespace CybeneticsImport;

/// <summary>
/// <c>batch --catalog &lt;csv&gt; --reports &lt;dir&gt; --out &lt;dir&gt;</c>: imports every catalogue row from
/// <c>&lt;reports&gt;\&lt;reportId&gt;.pdf</c> (plus <c>&lt;reportId&gt;-115.pdf</c> when present) and writes one entry JSON
/// per row and <c>summary.csv</c>.
/// </summary>
public static class BatchCommand
{
    /// <summary>The name of the summary file written to the output directory.</summary>
    public const string SummaryFileName = "summary.csv";

    /// <summary>Runs the batch import.</summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="log">Progress, warnings and errors.</param>
    /// <param name="readLines">Reads an input's lines.</param>
    /// <returns><see cref="ImportCommand.Ok"/> when every row imported, otherwise <see cref="ImportCommand.Failed"/>.</returns>
    internal static int Run(OptionSet options, TextWriter log, Func<string, IReadOnlyList<string>> readLines)
    {
        var catalogPath = options.Require("catalog");
        var reports = options.Require("reports");
        var outDir = options.Require("out");
        _ = ImportCommand.CreateParserOptions(options);
        var rows = Catalog.Load(catalogPath);
        Directory.CreateDirectory(outDir);

        var summary = new StringBuilder("id,reportId,status,points230,points115,warnings,message\n");
        var failed = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var (status, message, result) = ImportRow(row, reports, outDir, options, log, readLines, seenIds);
            failed += status == "ok" ? 0 : 1;
            var counts = result?.PointCounts ?? new Dictionary<int, int>();
            summary.Append(Catalog.Quote(row.Id)).Append(',')
                .Append(Catalog.Quote(row.ReportId)).Append(',')
                .Append(status).Append(',')
                .Append(counts.GetValueOrDefault(230)).Append(',')
                .Append(counts.GetValueOrDefault(115)).Append(',')
                .Append(result?.Warnings.Count ?? 0).Append(',')
                .Append(Catalog.Quote(message)).Append('\n');
            log.WriteLine($"{row.Id}: {status}{(message.Length > 0 ? " - " + message : string.Empty)}");
        }

        File.WriteAllText(Path.Combine(outDir, SummaryFileName), summary.ToString());
        log.WriteLine($"{rows.Count - failed} of {rows.Count} rows imported; see {Path.Combine(outDir, SummaryFileName)}.");
        return failed == 0 ? ImportCommand.Ok : ImportCommand.Failed;
    }

    private static (string Status, string Message, ImportResult? Result) ImportRow(
        CatalogRow row,
        string reports,
        string outDir,
        OptionSet options,
        TextWriter log,
        Func<string, IReadOnlyList<string>> readLines,
        HashSet<string> seenIds)
    {
        EntryMetadata metadata;
        try
        {
            metadata = row.ToMetadata();
        }
        catch (ArgumentException ex)
        {
            return ("error", $"catalogue line {row.LineNumber}: {ex.Message}", null);
        }

        if (!Catalog.IsSafeFileName(metadata.Id) || !Catalog.IsSafeFileName(metadata.ReportId ?? string.Empty))
        {
            return ("error", $"catalogue line {row.LineNumber}: id and reportId may only contain letters, digits, '.', '_' and '-'.", null);
        }

        if (!seenIds.Add(metadata.Id))
        {
            return ("error", $"catalogue line {row.LineNumber}: duplicate id {metadata.Id}.", null);
        }

        var main = Path.Combine(reports, metadata.ReportId + ".pdf");
        var us = Path.Combine(reports, metadata.ReportId + "-115.pdf");
        var inputs = new List<ImportInput>();
        if (File.Exists(us))
        {
            // Separate documents per voltage: the main one is the 230 V report. Its own voltage markers still win,
            // so a main document that covers both voltages is not mislabelled.
            if (File.Exists(main))
            {
                inputs.Add(new ImportInput(main, null, 230));
            }

            inputs.Add(new ImportInput(us, 115));
        }
        else if (File.Exists(main))
        {
            inputs.Add(new ImportInput(main, null));
        }
        else
        {
            return ("error", $"missing {main}", null);
        }

        log.WriteLine($"--- {metadata.Id} ({string.Join(", ", inputs.Select(i => Path.GetFileName(i.Path)))})");
        ImportResult result;
        try
        {
            result = EntryImporter.Import(metadata, inputs, () => ImportCommand.CreateParserOptions(options), readLines, log);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A malformed PDF must not stop the other rows.
            return ("error", $"{ex.GetType().Name}: {ex.Message}", null);
        }

        if (result.Entry is null)
        {
            return ("error", result.Error ?? "import failed", result);
        }

        File.WriteAllText(Path.Combine(outDir, metadata.Id + ".json"), EntryWriter.Format(result.Entry));
        var message = result.Warnings.Count == 0 ? string.Empty : string.Join(" | ", result.Warnings);
        return ("ok", message, result);
    }
}
