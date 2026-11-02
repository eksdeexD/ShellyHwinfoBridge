using System.Text.Json;
using System.Text.RegularExpressions;
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

/// <summary>How <see cref="BackfillDatesCommand.BackfillEta"/> treated one entry.</summary>
public enum EtaBackfillStatus
{
    /// <summary>The entry's <c>ratingsCybenetics</c> was set to the grades the matching pages show.</summary>
    Filled,

    /// <summary>The pages' ETA badge table shows no grade at any voltage: the entry has no <c>ratingsCybenetics</c>.</summary>
    NoRating,

    /// <summary>No saved page matched the entry; it was left as it was.</summary>
    NoPage,

    /// <summary>The matching pages have no ETA badge table; the entry was left as it was.</summary>
    NoEtaTable,

    /// <summary>A matching page has a badge the parser does not know; the entry was left as it was.</summary>
    Unreadable,

    /// <summary>The matching pages show different grades, or one links another report URL; the entry was left as it was.</summary>
    Conflict,
}

/// <summary>The ETA outcome for one entry.</summary>
/// <param name="Entry">The entry (updated in place when Filled or NoRating).</param>
/// <param name="Status">What happened.</param>
/// <param name="Pages">The file names of the matching pages.</param>
/// <param name="Before">The grades the entry had before (an older database's single grade reads as 230 V), or <c>null</c>.</param>
/// <param name="Detail">The grades ("230 Gold, 115 Platinum"), the conflicting grades or the problem.</param>
public sealed record EtaBackfillResult(PsuEntry Entry, EtaBackfillStatus Status, IReadOnlyList<string> Pages, IReadOnlyDictionary<string, string>? Before, string Detail)
{
    /// <summary>
    /// Gets whether the entry had a 230 V grade before that the pages contradict (another 230 V grade, or none).
    /// Only meaningful for <see cref="EtaBackfillStatus.Filled"/> and <see cref="EtaBackfillStatus.NoRating"/>.
    /// </summary>
    public bool Contradicts230 =>
        Before?.GetValueOrDefault("230") is { } old
        && Status is EtaBackfillStatus.Filled or EtaBackfillStatus.NoRating
        && !old.Equals(Entry.RatingsCybenetics?.GetValueOrDefault("230"), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// <c>backfill --pages &lt;dir&gt; --into &lt;psu-database.json&gt; [--dry-run]</c>: fills <c>source.reportDate</c> and
/// <c>ratingsCybenetics</c> of existing entries from saved evaluation pages; <c>backfill-dates</c> and
/// <c>backfill-eta</c> fill only one of them. Nothing else in the database changes (not even <c>updated</c>).
/// </summary>
/// <remarks>
/// A page matches an entry when it is named <c>&lt;reportId&gt;.htm</c> or <c>&lt;reportId&gt;.html</c>, or when the
/// "Report ID" printed on it equals the entry's <c>source.reportId</c> (saved pages are usually named after the model).
/// Entries without a report id match pages whose report URL equals <c>source.url</c>. Dates: entries that already have
/// a date, have no matching page, or whose pages give no date, different dates or another report URL are left
/// untouched. ETA grades: always taken from the pages (they replace what the entry had, including the single
/// <c>ratingCybenetics</c> of older databases); a page whose ETA badge table shows no grade removes them; entries with
/// no matching page, no badge table, an unknown badge or disagreeing pages are left untouched and listed.
/// </remarks>
public static partial class BackfillDatesCommand
{
    /// <summary>Runs the backfill.</summary>
    /// <param name="options">The parsed options (<c>--pages</c>, <c>--into</c>, flag <c>--dry-run</c>).</param>
    /// <param name="log">The summary and the entries left untouched.</param>
    /// <param name="dates">Whether to fill <c>source.reportDate</c>.</param>
    /// <param name="eta">Whether to fill <c>ratingsCybenetics</c>.</param>
    internal static int Run(OptionSet options, TextWriter log, bool dates = true, bool eta = true)
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
            original = File.ReadAllText(databasePath).Replace("\r\n", "\n", StringComparison.Ordinal);
            database = PsuDatabaseJson.Deserialize(original);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.WriteLine($"error: cannot read {databasePath}: {ex.Message}");
            return ImportCommand.Failed;
        }

        // The database is rewritten with the merge formatter; refuse when that would change more than the backfilled
        // fields. An older file's "ratingCybenetics" line becomes "ratingsCybenetics" (or goes), so it may differ only
        // there, and only when the ETA grades are backfilled too.
        var formatted = MergeCommand.Format(database);
        if (formatted != original && (!eta || WithoutEtaLines(formatted) != WithoutEtaLines(original)))
        {
            log.WriteLine(formatted != original && WithoutEtaLines(formatted) == WithoutEtaLines(original)
                ? $"error: {databasePath} still has the single \"ratingCybenetics\"; run \"backfill\" or \"backfill-eta\" first. Nothing written."
                : $"error: {databasePath} is not in the layout the merge command writes; rewriting it would change more than the backfilled fields. Nothing written.");
            return ImportCommand.Failed;
        }

        var pages = Directory.EnumerateFiles(pagesDir)
            .Where(f => Path.GetExtension(f).Equals(".htm", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".html", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: Path.GetFileName(f), Metadata: HtmlReportParser.ReadMetadata(File.ReadAllText(f))))
            .ToList();

        var dateResults = dates ? Backfill(database, pages) : [];
        var etaResults = eta ? BackfillEta(database, pages) : [];
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

        log.WriteLine($"{pages.Count} pages read, {database.Psus.Count} entries.");
        if (dates)
        {
            LogDates(dateResults, log);
        }

        if (eta)
        {
            LogEta(etaResults, log);
        }

        var result = MergeCommand.Format(database);
        if (options.Flag("dry-run"))
        {
            log.WriteLine($"Dry run: {databasePath} is unchanged.");
        }
        else if (result != original)
        {
            File.WriteAllText(databasePath, result);
            log.WriteLine($"Wrote {databasePath}.");
        }
        else
        {
            log.WriteLine($"Nothing to fill; {databasePath} is unchanged.");
        }

        return ImportCommand.Ok;
    }

    private static void LogDates(IReadOnlyList<BackfillResult> results, TextWriter log)
    {
        var count = (BackfillStatus s) => results.Count(r => r.Status == s);
        log.WriteLine($"Dates: {count(BackfillStatus.Filled)} filled, "
            + $"{count(BackfillStatus.AlreadySet)} already set, {count(BackfillStatus.NoPage)} no page, "
            + $"{count(BackfillStatus.NoDate)} no date found, {count(BackfillStatus.Conflict)} conflicting dates.");
        foreach (var result in results.Where(r => r.Status is not (BackfillStatus.Filled or BackfillStatus.AlreadySet)))
        {
            log.WriteLine($"  {result.Status}: {Name(result.Entry)}{PageList(result.Pages)}{(result.Detail.Length > 0 ? " " + result.Detail : string.Empty)}");
        }

        foreach (var result in results.Where(r => r.Status == BackfillStatus.Filled && r.Pages.Count > 1))
        {
            log.WriteLine($"  note: {Name(result.Entry)}: {result.Pages.Count} pages agree on {result.Detail}{PageList(result.Pages)}");
        }
    }

    private static void LogEta(IReadOnlyList<EtaBackfillResult> results, TextWriter log)
    {
        var count = (EtaBackfillStatus s) => results.Count(r => r.Status == s);
        var differing = results.Count(r => r.Entry.RatingsCybenetics is { Count: 2 } g && !g["115"].Equals(g["230"], StringComparison.OrdinalIgnoreCase));
        log.WriteLine($"ETA: {count(EtaBackfillStatus.Filled)} with grades ({differing} with different grades at 115 V and 230 V), "
            + $"{count(EtaBackfillStatus.NoRating)} without an ETA rating, {count(EtaBackfillStatus.NoPage)} no page, "
            + $"{count(EtaBackfillStatus.NoEtaTable)} no ETA table, {count(EtaBackfillStatus.Unreadable)} unknown badge, "
            + $"{count(EtaBackfillStatus.Conflict)} conflicting.");
        foreach (var result in results.Where(r => r.Status is not (EtaBackfillStatus.Filled or EtaBackfillStatus.NoRating)))
        {
            log.WriteLine($"  {result.Status}: {Name(result.Entry)}{PageList(result.Pages)}{(result.Detail.Length > 0 ? " " + result.Detail : string.Empty)}");
        }

        foreach (var result in results.Where(r => r.Status == EtaBackfillStatus.NoRating))
        {
            log.WriteLine($"  no ETA rating: {Name(result.Entry)}{PageList(result.Pages)}");
        }

        foreach (var result in results.Where(r => r.Contradicts230))
        {
            log.WriteLine($"  changed: {Name(result.Entry)}: had {Grades(result.Before)}, the page shows {(result.Detail.Length > 0 ? result.Detail : "no grade")}");
        }
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

            var (matches, otherUrl) = Match(entry, pages);
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

            if (otherUrl is not null)
            {
                results.Add(new(entry, BackfillStatus.Conflict, files, otherUrl));
                continue;
            }

            var found = matches.Select(m => m.Metadata.ReportDate).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            if (found.Count == 0)
            {
                var printed = matches.Select(m => m.Metadata.TestingDate).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
                results.Add(new(entry, BackfillStatus.NoDate, files, printed is null ? "no Testing Date row" : $"unreadable Testing Date \"{printed}\""));
                continue;
            }

            if (found.Count > 1)
            {
                results.Add(new(entry, BackfillStatus.Conflict, files, string.Join(" / ", found)));
                continue;
            }

            source.ReportDate = found[0];
            results.Add(new(entry, BackfillStatus.Filled, files, found[0]));
        }

        return results;
    }

    /// <summary>
    /// Sets <c>ratingsCybenetics</c> of the entries in place to the ETA grades the matching pages show per voltage
    /// (removing it when the pages' ETA badge table shows none).
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pages">The saved pages: file name and metadata.</param>
    /// <returns>One result per entry, in database order.</returns>
    public static IReadOnlyList<EtaBackfillResult> BackfillEta(PsuDatabase database, IReadOnlyList<(string File, PageMetadata Metadata)> pages)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(pages);
        var results = new List<EtaBackfillResult>();
        foreach (var entry in database.Psus ?? [])
        {
            var before = entry.RatingsCybenetics is { Count: > 0 } b ? new Dictionary<string, string>(b) : null;
            if (entry.Source is null)
            {
                results.Add(new(entry, EtaBackfillStatus.NoPage, [], before, "no source"));
                continue;
            }

            var (matches, otherUrl) = Match(entry, pages);
            var files = matches.Select(m => m.File).ToList();
            if (matches.Count == 0)
            {
                results.Add(new(entry, EtaBackfillStatus.NoPage, files, before, string.Empty));
                continue;
            }

            if (otherUrl is not null)
            {
                results.Add(new(entry, EtaBackfillStatus.Conflict, files, before, otherUrl));
                continue;
            }

            var withTable = matches.Where(m => m.Metadata.HasEtaTable).ToList();
            if (withTable.Count == 0)
            {
                results.Add(new(entry, EtaBackfillStatus.NoEtaTable, files, before, string.Empty));
                continue;
            }

            var problems = withTable.SelectMany(m => m.Metadata.EtaProblems.Select(p => $"{m.File}: {p}")).ToList();
            if (problems.Count > 0)
            {
                results.Add(new(entry, EtaBackfillStatus.Unreadable, files, before, string.Join(" ", problems)));
                continue;
            }

            var found = withTable.Select(m => Grades(m.Metadata.RatingsCybenetics.ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Value)))
                .Distinct(StringComparer.Ordinal).ToList();
            if (found.Count > 1)
            {
                results.Add(new(entry, EtaBackfillStatus.Conflict, files, before, string.Join(" / ", found)));
                continue;
            }

            var grades = withTable[0].Metadata.RatingsCybenetics.ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Value);
            entry.RatingsCybenetics = grades.Count == 0 ? null : grades;
            results.Add(new(entry, grades.Count == 0 ? EtaBackfillStatus.NoRating : EtaBackfillStatus.Filled, files, before, found[0]));
        }

        return results;
    }

    // The pages that belong to an entry, and the URL of one that names another report (a different report, or a
    // mislabelled save), if any.
    private static (List<(string File, PageMetadata Metadata)> Matches, string? OtherUrl) Match(PsuEntry entry, IReadOnlyList<(string File, PageMetadata Metadata)> pages)
    {
        var source = entry.Source!;
        var reportId = string.IsNullOrWhiteSpace(source.ReportId) ? null : source.ReportId.Trim();
        var matches = pages.Where(p => reportId is not null
                ? Path.GetFileNameWithoutExtension(p.File).Equals(reportId, StringComparison.OrdinalIgnoreCase)
                    || reportId.Equals(p.Metadata.ReportId, StringComparison.OrdinalIgnoreCase)
                : SameUrl(source.Url, p.Metadata.Url))
            .ToList();
        var otherUrl = matches.Where(m => !string.IsNullOrWhiteSpace(m.Metadata.Url) && !SameUrl(source.Url, m.Metadata.Url)).Select(m => m.Metadata.Url).ToList();
        return (matches, otherUrl.Count == 0 ? null : "page URL differs: " + string.Join(", ", otherUrl));
    }

    private static string Grades(IReadOnlyDictionary<string, string>? grades) =>
        grades is null || grades.Count == 0
            ? string.Empty
            : string.Join(", ", grades.OrderByDescending(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} V {g.Value}"));

    private static string Name(PsuEntry entry) => $"{entry.Source?.ReportId ?? "(no report id)"} {entry.DisplayName} ({entry.Id})";

    private static string PageList(IReadOnlyList<string> pages) => pages.Count == 0 ? string.Empty : $" [{string.Join(", ", pages)}]";

    private static string WithoutEtaLines(string json) => EtaLineRegex().Replace(json, string.Empty);

    private static bool SameUrl(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string url) =>
        url.Trim().Replace("http://", "https://", StringComparison.OrdinalIgnoreCase).TrimEnd('/');

    [GeneratedRegex(@"^ *""ratings?Cybenetics"": .*,\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex EtaLineRegex();
}
