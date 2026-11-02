using System.Text.Json;
using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport;

/// <summary>How <see cref="BackfillDatesCommand.Backfill"/> treated one entry.</summary>
public enum BackfillStatus
{
    /// <summary>The entry got its <c>source.reportDate</c> from a saved page.</summary>
    Filled,

    /// <summary>The entry already had a <c>source.reportDate</c>; it was left as it was.</summary>
    AlreadySet,

    /// <summary>No saved page matched the entry.</summary>
    NoPage,

    /// <summary>A page matched, but it has no readable "Testing Date".</summary>
    NoDate,

    /// <summary>
    /// Several matching pages give different testing dates, or a matching page links another report URL; the entry
    /// was left as it was.
    /// </summary>
    Conflict,
}

/// <summary>The outcome for one entry.</summary>
/// <param name="Entry">The entry (updated in place when <paramref name="Status"/> is <see cref="BackfillStatus.Filled"/>).</param>
/// <param name="Status">What happened.</param>
/// <param name="Pages">The file names of the matching pages.</param>
/// <param name="Detail">Extra information (the date, or the conflicting dates).</param>
public sealed record BackfillResult(PsuEntry Entry, BackfillStatus Status, IReadOnlyList<string> Pages, string Detail);

/// <summary>
/// <c>backfill-dates --pages &lt;dir&gt; --into &lt;psu-database.json&gt; [--dry-run]</c>: fills <c>source.reportDate</c> of
/// existing entries from saved evaluation pages. Nothing else in the database changes (not even <c>updated</c>).
/// </summary>
/// <remarks>
/// A page matches an entry when it is named <c>&lt;reportId&gt;.htm</c> or <c>&lt;reportId&gt;.html</c>, or when the
/// "Report ID" printed on it equals the entry's <c>source.reportId</c> (saved pages are usually named after the model).
/// Entries without a report id match pages whose report URL equals <c>source.url</c>. Entries that already have a
/// date, have no matching page, or whose pages give no date, different dates or another report URL are left untouched.
/// </remarks>
public static class BackfillDatesCommand
{
    /// <summary>Runs the backfill.</summary>
    /// <param name="options">The parsed options (<c>--pages</c>, <c>--into</c>, flag <c>--dry-run</c>).</param>
    /// <param name="log">The summary and the entries left without a date.</param>
    internal static int Run(OptionSet options, TextWriter log)
    {
        var pagesDir = options.Require("pages");
        var databasePath = options.Require("into");
        if (!Directory.Exists(pagesDir))
        {
            throw new ArgumentException($"--pages directory {pagesDir} does not exist.");
        }

        string original;
        PsuDatabase database;
        try
        {
            original = File.ReadAllText(databasePath);
            database = PsuDatabaseJson.Deserialize(original);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.WriteLine($"error: cannot read {databasePath}: {ex.Message}");
            return ImportCommand.Failed;
        }

        // The database is rewritten with the merge formatter; refuse when that would change more than the dates.
        if (MergeCommand.Format(database) != original.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            log.WriteLine($"error: {databasePath} is not in the layout the merge command writes; rewriting it would change more than the dates. Nothing written.");
            return ImportCommand.Failed;
        }

        var pages = Directory.EnumerateFiles(pagesDir)
            .Where(f => Path.GetExtension(f).Equals(".htm", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".html", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: Path.GetFileName(f), Metadata: HtmlReportParser.ReadMetadata(File.ReadAllText(f))))
            .ToList();

        var results = Backfill(database, pages);
        var errors = PsuDatabaseValidator.Validate(database);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                log.WriteLine("error: " + error);
            }

            log.WriteLine($"Nothing written ({databasePath} is unchanged).");
            return ImportCommand.Failed;
        }

        var count = (BackfillStatus s) => results.Count(r => r.Status == s);
        log.WriteLine($"{pages.Count} pages read, {results.Count} entries: {count(BackfillStatus.Filled)} filled, "
            + $"{count(BackfillStatus.AlreadySet)} already set, {count(BackfillStatus.NoPage)} no page, "
            + $"{count(BackfillStatus.NoDate)} no date found, {count(BackfillStatus.Conflict)} conflicting dates.");
        foreach (var result in results.Where(r => r.Status is not (BackfillStatus.Filled or BackfillStatus.AlreadySet)))
        {
            var pageList = result.Pages.Count == 0 ? string.Empty : $" [{string.Join(", ", result.Pages)}]";
            log.WriteLine($"  {result.Status}: {result.Entry.Source?.ReportId ?? "(no report id)"} {result.Entry.DisplayName} ({result.Entry.Id}){pageList}{(result.Detail.Length > 0 ? " " + result.Detail : string.Empty)}");
        }

        foreach (var result in results.Where(r => r.Status == BackfillStatus.Filled && r.Pages.Count > 1))
        {
            log.WriteLine($"  note: {result.Entry.Source?.ReportId ?? "(no report id)"} {result.Entry.DisplayName}: {result.Pages.Count} pages agree on {result.Detail} [{string.Join(", ", result.Pages)}]");
        }

        if (options.Flag("dry-run"))
        {
            log.WriteLine($"Dry run: {databasePath} is unchanged.");
        }
        else if (count(BackfillStatus.Filled) > 0)
        {
            File.WriteAllText(databasePath, MergeCommand.Format(database));
            log.WriteLine($"Wrote {databasePath}.");
        }
        else
        {
            log.WriteLine($"Nothing to fill; {databasePath} is unchanged.");
        }

        return ImportCommand.Ok;
    }

    /// <summary>Fills <c>source.reportDate</c> of the entries in place from the pages' testing dates.</summary>
    /// <param name="database">The database.</param>
    /// <param name="pages">The saved pages: file name and metadata.</param>
    /// <returns>One result per entry, in database order.</returns>
    public static IReadOnlyList<BackfillResult> Backfill(PsuDatabase database, IReadOnlyList<(string File, PageMetadata Metadata)> pages)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(pages);
        var results = new List<BackfillResult>();
        foreach (var entry in database.Psus ?? [])
        {
            var source = entry.Source;
            if (source is null)
            {
                results.Add(new(entry, BackfillStatus.NoPage, [], "no source"));
                continue;
            }

            var reportId = string.IsNullOrWhiteSpace(source.ReportId) ? null : source.ReportId.Trim();
            var matches = pages.Where(p => reportId is not null
                    ? Path.GetFileNameWithoutExtension(p.File).Equals(reportId, StringComparison.OrdinalIgnoreCase)
                        || reportId.Equals(p.Metadata.ReportId, StringComparison.OrdinalIgnoreCase)
                    : SameUrl(source.Url, p.Metadata.Url))
                .ToList();
            var files = matches.Select(m => m.File).ToList();

            if (!string.IsNullOrWhiteSpace(source.ReportDate))
            {
                results.Add(new(entry, BackfillStatus.AlreadySet, files, source.ReportDate));
                continue;
            }

            if (matches.Count == 0)
            {
                results.Add(new(entry, BackfillStatus.NoPage, files, string.Empty));
                continue;
            }

            // A page with the right report id but another report URL is a different report (or a mislabelled save).
            var otherUrl = matches.Where(m => !string.IsNullOrWhiteSpace(m.Metadata.Url) && !SameUrl(source.Url, m.Metadata.Url)).ToList();
            if (otherUrl.Count > 0)
            {
                results.Add(new(entry, BackfillStatus.Conflict, files, "page URL differs: " + string.Join(", ", otherUrl.Select(m => m.Metadata.Url))));
                continue;
            }

            var dates = matches.Select(m => m.Metadata.ReportDate).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            if (dates.Count == 0)
            {
                var printed = matches.Select(m => m.Metadata.TestingDate).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
                results.Add(new(entry, BackfillStatus.NoDate, files, printed is null ? "no Testing Date row" : $"unreadable Testing Date \"{printed}\""));
                continue;
            }

            if (dates.Count > 1)
            {
                results.Add(new(entry, BackfillStatus.Conflict, files, string.Join(" / ", dates)));
                continue;
            }

            source.ReportDate = dates[0];
            results.Add(new(entry, BackfillStatus.Filled, files, dates[0]));
        }

        return results;
    }

    private static bool SameUrl(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string url) =>
        url.Trim().Replace("http://", "https://", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
}
