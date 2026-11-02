using System.Text;

namespace CybeneticsImport;

/// <summary>
/// <c>batch --catalog &lt;csv&gt; --reports &lt;dir&gt; --out &lt;dir&gt;</c>: imports every catalogue row from
/// <c>&lt;reports&gt;\&lt;reportId&gt;.htm</c> or <c>.html</c> (a saved report page), else <c>&lt;reportId&gt;.pdf</c> (plus <c>&lt;reportId&gt;-115.pdf</c> when present) and writes one entry JSON
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
        if (!Catalog.IsSafeFileName(row.Id) || !Catalog.IsSafeFileName(row.ReportId))
        {
            return ("error", $"catalogue line {row.LineNumber}: id and reportId are required and may only contain letters, digits, '.', '_' and '-'.", null);
        }

        if (!seenIds.Add(row.Id))
        {
            return ("error", $"catalogue line {row.LineNumber}: duplicate id {row.Id}.", null);
        }

        // A saved evaluation page (<reportId>.htm as the download command saves it, or .html) is preferred over the
        // PDF: its tables are structured.
        var html = new[] { ".htm", ".html" }.Select(e => Path.Combine(reports, row.ReportId + e)).FirstOrDefault(File.Exists)
            ?? Path.Combine(reports, row.ReportId + ".html");
        var main = File.Exists(html) ? html : Path.Combine(reports, row.ReportId + ".pdf");
        var us = Path.Combine(reports, row.ReportId + "-115.pdf");
        var inputs = new List<ImportInput>();
        if (main == html)
        {
            inputs.Add(new ImportInput(main, null));
        }
        else if (File.Exists(us))
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
            return ("error", $"missing {row.ReportId}.htm, {row.ReportId}.html or {main}", null);
        }

        EntryMetadata metadata;
        var notes = new List<string>();
        try
        {
            // Empty catalogue cells are completed from the saved page, when there is one.
            metadata = EntryMetadata.FromValues(ResolveValues(row, inputs, notes));
        }
        catch (ArgumentException ex)
        {
            return ("error", $"catalogue line {row.LineNumber}: {ex.Message}", null);
        }

        foreach (var note in notes)
        {
            log.WriteLine("warning: " + note);
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
        var message = string.Join(" | ", notes.Concat(result.Warnings));
        return ("ok", message, result);
    }

    /// <summary>
    /// Returns a row's metadata values completed from the saved page (see <see cref="EntryImporter.WithPageMetadata"/>).
    /// Two catalogue values give way to the page: a placeholder report id (<c>psu-2399</c>, see
    /// <see cref="Catalog.IsPlaceholderReportId"/>) is replaced by the "Report ID" the page prints, or left out with a
    /// note when the page has none; and with an empty revision, the revision tags of the model text (see
    /// <see cref="HtmlReportParser.SplitModel"/>) become the revision, from the catalogue's model or else the page's,
    /// and leave the model.
    /// </summary>
    /// <param name="row">The catalogue row.</param>
    /// <param name="inputs">The row's inputs.</param>
    /// <param name="notes">Receives warnings worth a look.</param>
    internal static Dictionary<string, string?> ResolveValues(CatalogRow row, IReadOnlyList<ImportInput> inputs, List<string> notes)
    {
        var values = row.ToValues();
        var placeholder = Catalog.IsPlaceholderReportId(row.ReportId);
        if (placeholder)
        {
            values["reportid"] = null;
        }

        if (string.IsNullOrWhiteSpace(values["revision"]) && !string.IsNullOrWhiteSpace(values["model"]))
        {
            var (model, revision) = HtmlReportParser.SplitModel(values["model"]!);
            if (revision is not null)
            {
                (values["model"], values["revision"]) = (model, revision);
            }
        }

        var resolved = EntryImporter.WithPageMetadata(values, inputs);
        if (placeholder && string.IsNullOrWhiteSpace(resolved.GetValueOrDefault("reportid")))
        {
            resolved["reportid"] = null;
            notes.Add($"{row.Id}: the catalogue's reportId {row.ReportId} is a placeholder and the page prints no report id; source.reportId is left out.");
        }

        return resolved;
    }
}
