using System.Net;
using System.Text.Json;
using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport.Tests;

public sealed class BatchMergeDownloadTests : IDisposable
{
    private const string Header = "id,brand,series,model,revision,wattage,rating80Plus,ratingCybenetics,reportId,reportUrl,pdfUrl,tier,notes";

    private readonly string _dir = Directory.CreateTempSubdirectory("cyb-import-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private string Sub(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

    private static int Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        return ImportCommand.Run(args, stdout, stderr, File.ReadAllLines);
    }

    private static (int Exit, string Log) RunWithLog(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = ImportCommand.Run(args, stdout, stderr, File.ReadAllLines);
        return (exit, stderr.ToString());
    }

    [Fact]
    public void BatchImportsEveryRowAndWritesASummary()
    {
        var reports = Sub("reports");
        var output = Path.Combine(_dir, "out");

        // Row 1: one document with both voltages (the PDF is replaced by extracted text through the injected reader).
        File.Copy(FixturePath("tx700-stacked.txt"), Path.Combine(reports, "21PS1872A.pdf"));

        // Row 2: separate documents; the 230 V one has no voltage marker at all, the 115 V one is "-115.pdf".
        File.WriteAllLines(Path.Combine(reports, "99PS0002A.pdf"),
            ["Load Tests", "50% 30.1A 421.953 91.461%", "12.08V 461.345 230.15V", "100% 60.2A 843.905 89.423%", "12.07V 943.723 230.2V"]);
        File.WriteAllLines(Path.Combine(reports, "99PS0002A-115.pdf"),
            ["Load Tests", "50% 30.1A 421.953 89.962%", "12.08V 469.037 115.15V", "100% 60.2A 843.905 87.723%", "12.07V 962.011 115.2V"]);

        File.WriteAllLines(Path.Combine(reports, "99PS0004A.pdf"), ["Load Tests"]);

        var catalog = Path.Combine(_dir, "catalog.csv");
        File.WriteAllLines(catalog,
        [
            Header,
            "seasonic-prime-fanless-tx-700,Seasonic,Prime Fanless TX,Prime Fanless TX-700,,700,Titanium,Titanium,21PS1872A,https://www.cybenetics.com/evaluations/psus/1872/,,1,",
            "examplebrand-fx-850,Examplebrand,FX,\"FX-850, white\",2024,850,gold,Platinum,99PS0002A,https://example.com/r/2/,,2,\"notes, with comma\"",
            "missing-report,B,S,M,,650,Gold,,99PS0003A,https://example.com/r/3/,,2,",
            "bad-rating,B,S,M,,650,Shiny,,99PS0004A,https://example.com/r/4/,,2,",
            "traversal,B,S,M,,650,Gold,,..\\evil,https://example.com/r/5/,,2,",
        ]);

        var (exit, log) = RunWithLog("batch", "--catalog", catalog, "--reports", reports, "--out", output);

        Assert.Equal(ImportCommand.Failed, exit);
        var seasonic = JsonSerializer.Deserialize<PsuEntry>(File.ReadAllText(Path.Combine(output, "seasonic-prime-fanless-tx-700.json")), PsuDatabaseJson.Options)!;
        var seed = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == seasonic.Id);
        Assert.Equal(seed.Curves!["230"], seasonic.Curves!["230"]);
        Assert.Equal(seed.Curves["115"], seasonic.Curves["115"]);

        var split = JsonSerializer.Deserialize<PsuEntry>(File.ReadAllText(Path.Combine(output, "examplebrand-fx-850.json")), PsuDatabaseJson.Options)!;
        Assert.Equal("FX-850, white", split.Model);
        Assert.Equal("Gold", split.Rating80Plus);
        Assert.Equal("2024", split.Revision);
        Assert.Null(split.RatingsCybenetics); // An old catalogue's ratingCybenetics column is ignored: grades come from report pages.
        Assert.Equal([[461.345, 421.953], [943.723, 843.905]], split.Curves!["230"]);
        Assert.Equal([[469.037, 421.953], [962.011, 843.905]], split.Curves["115"]);

        Assert.Equal(2, Directory.GetFiles(output, "*.json").Length);
        var summary = File.ReadAllLines(Path.Combine(output, BatchCommand.SummaryFileName));
        Assert.Equal("id,reportId,status,points230,points115,warnings,message", summary[0]);
        Assert.StartsWith("seasonic-prime-fanless-tx-700,21PS1872A,ok,15,15,0,", summary[1], StringComparison.Ordinal);
        Assert.StartsWith("examplebrand-fx-850,99PS0002A,ok,2,2,2,", summary[2], StringComparison.Ordinal);
        Assert.StartsWith("missing-report,99PS0003A,error,", summary[3], StringComparison.Ordinal);
        Assert.Contains("missing", summary[3], StringComparison.Ordinal);
        Assert.Contains("rating80plus", summary[4], StringComparison.Ordinal);
        Assert.Contains("may only contain", summary[5], StringComparison.Ordinal);
        Assert.Contains("2 of 5 rows imported", log, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchTakesTheReportIdAndRevisionFromThePageForPlaceholderRows()
    {
        var reports = Sub("reports");
        var output = Path.Combine(_dir, "out");
        var html = File.ReadAllText(FixturePath("evaluation-page-tx700.html"));
        Assert.Contains("Fictional FX-700 (ATX v3.1)", html, StringComparison.Ordinal);

        // psu-9872: the page prints "Report ID: 99PS9872A" and the model "Fictional FX-700 (ATX v3.1)".
        File.WriteAllText(Path.Combine(reports, "psu-9872.htm"), html);
        // psu-9873: no report id on the page, and a second sample of another build: "(US) (#2)".
        File.WriteAllText(Path.Combine(reports, "psu-9873.htm"), html
            .Replace("Test Result Report ID: 99PS9872A", "Test Result Report ID:", StringComparison.Ordinal)
            .Replace("Fictional FX-700 (ATX v3.1)", "Fictional FX-700 (US) (#2)", StringComparison.Ordinal));
        // A real report id in the catalogue still wins over the page, and so does a given revision.
        File.WriteAllText(Path.Combine(reports, "99PS0001A.htm"), html);
        // An empty revision with the tag in the catalogue's own model text.
        File.WriteAllText(Path.Combine(reports, "psu-9874.htm"), html);

        var catalog = Path.Combine(_dir, "catalog.csv");
        File.WriteAllLines(catalog,
        [
            Header,
            "examplebrand-fx-700-atx-v3-1,Examplebrand,Fictional FX,Fictional FX-700,,700,Titanium,,psu-9872,https://www.cybenetics.com/evaluations/psus/9872/,,2,",
            "examplebrand-fx-700-us-2,Examplebrand,Fictional FX,Fictional FX-700,,700,Titanium,,psu-9873,https://www.cybenetics.com/evaluations/psus/9873/,,2,",
            "examplebrand-fx-700-given,Examplebrand,Fictional FX,Fictional FX-700,2026,700,Titanium,,99PS0001A,https://www.cybenetics.com/evaluations/psus/1/,,2,",
            "examplebrand-fx-700-eu,Examplebrand,Fictional FX,Fictional FX-700 (EU),,700,Titanium,,psu-9874,https://www.cybenetics.com/evaluations/psus/9874/,,2,",
        ]);

        var (exit, log) = RunWithLog("batch", "--catalog", catalog, "--reports", reports, "--out", output);

        Assert.True(exit == ImportCommand.Ok, log);
        PsuEntry Read(string id) => JsonSerializer.Deserialize<PsuEntry>(File.ReadAllText(Path.Combine(output, id + ".json")), PsuDatabaseJson.Options)!;

        var fromPage = Read("examplebrand-fx-700-atx-v3-1");
        Assert.Equal(("Fictional FX-700", "ATX v3.1", "99PS9872A"), (fromPage.Model, fromPage.Revision, fromPage.Source!.ReportId));

        var noId = Read("examplebrand-fx-700-us-2");
        Assert.Equal(("Fictional FX-700", "US (#2)"), (noId.Model, noId.Revision));
        Assert.Null(noId.Source!.ReportId);
        Assert.DoesNotContain("psu-9873", File.ReadAllText(Path.Combine(output, "examplebrand-fx-700-us-2.json")), StringComparison.Ordinal);
        Assert.Contains("warning: examplebrand-fx-700-us-2: the catalogue's reportId psu-9873 is a placeholder", log, StringComparison.Ordinal);

        var given = Read("examplebrand-fx-700-given");
        Assert.Equal(("Fictional FX-700", "2026", "99PS0001A"), (given.Model, given.Revision, given.Source!.ReportId));

        var eu = Read("examplebrand-fx-700-eu");
        Assert.Equal(("Fictional FX-700", "EU", "99PS9872A"), (eu.Model, eu.Revision, eu.Source!.ReportId));
    }

    [Theory]
    [InlineData("psu-2399", true)]
    [InlineData("PSU-1", true)]
    [InlineData(" psu-17 ", true)]
    [InlineData("24PS2399A", false)]
    [InlineData("psu-", false)]
    [InlineData("psu-12a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PlaceholderReportIdsAreRecognised(string? reportId, bool expected) =>
        Assert.Equal(expected, Catalog.IsPlaceholderReportId(reportId));

    [Fact]
    public void BatchRejectsACatalogueWithoutTheRequiredColumns()
    {
        var catalog = Path.Combine(_dir, "catalog.csv");
        File.WriteAllLines(catalog, ["id,brand", "a,b"]);

        var (exit, log) = RunWithLog("batch", "--catalog", catalog, "--reports", _dir, "--out", Path.Combine(_dir, "out"));

        Assert.Equal(ImportCommand.Usage, exit);
        Assert.Contains("lacks series", log, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeReplacesByIdSortsAndSetsTheDate()
    {
        // Start from a synthetic one-entry database so the test does not depend on the shipped file's contents.
        var database = Path.Combine(_dir, "psu-database.json");
        var seed = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == "seasonic-prime-fanless-tx-700");
        File.WriteAllText(database, MergeCommand.Format(new PsuDatabase { SchemaVersion = 1, Updated = "2026-01-01", Psus = [seed] }));
        var entries = Sub("entries");

        seed.RatingsCybenetics = new Dictionary<string, string> { ["230"] = "Diamond", ["115"] = "Titanium" };
        File.WriteAllText(Path.Combine(entries, "replaced.json"), EntryWriter.Format(seed));
        File.WriteAllText(Path.Combine(entries, "a.json"), EntryWriter.Format(Entry("zz-unit", "Antec", "NeoECO 650")));
        File.WriteAllText(Path.Combine(entries, "b.json"), EntryWriter.Format(Entry("aa-unit", "Zalman", "Gigamax 750")));
        File.WriteAllText(Path.Combine(entries, "c.json"), EntryWriter.Format(Entry("mm-unit", "antec", "Earthwatts 500")));

        Assert.Equal(ImportCommand.Ok, Run("merge", "--entries", entries, "--into", database, "--date", "2026-11-02"));

        var text = File.ReadAllText(database);
        var merged = PsuDatabaseJson.Deserialize(text);
        Assert.Empty(PsuDatabaseValidator.Validate(merged));
        Assert.Equal("2026-11-02", merged.Updated);
        Assert.Equal(["mm-unit", "zz-unit", "seasonic-prime-fanless-tx-700", "aa-unit"], merged.Psus.Select(p => p.Id));
        Assert.Equal(new Dictionary<string, string> { ["230"] = "Diamond", ["115"] = "Titanium" }, merged.Psus[2].RatingsCybenetics);
        Assert.Equal(MergeCommand.Format(merged), text);
    }

    [Fact]
    public void MergeRemovesTheGivenIdsAndRejectsUnknownOrConflictingOnes()
    {
        var database = Path.Combine(_dir, "psu-database.json");
        var original = MergeCommand.Format(new PsuDatabase
        {
            SchemaVersion = 1, Updated = "2026-01-01", Psus = [Entry("old-name", "Antec", "Old 650"), Entry("keep", "Antec", "Keep 750")],
        });
        File.WriteAllText(database, original);
        var entries = Sub("entries");
        File.WriteAllText(Path.Combine(entries, "new.json"), EntryWriter.Format(Entry("new-name", "Antec", "New 650")));

        var (missingExit, missingLog) = RunWithLog("merge", "--entries", entries, "--into", database, "--remove", "nope");
        Assert.Equal(ImportCommand.Failed, missingExit);
        Assert.Contains("--remove nope", missingLog, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(database));

        var (conflictExit, conflictLog) = RunWithLog("merge", "--entries", entries, "--into", database, "--remove", "NEW-NAME");
        Assert.Equal(ImportCommand.Failed, conflictExit);
        Assert.Contains("entry file has that id", conflictLog, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(database));

        var (exit, log) = RunWithLog("merge", "--entries", entries, "--into", database, "--remove", "OLD-NAME", "--date", "2026-11-02");
        Assert.Equal(ImportCommand.Ok, exit);
        Assert.Contains("1 added, 0 replaced, 1 removed", log, StringComparison.Ordinal);
        Assert.Equal(["keep", "new-name"], PsuDatabaseJson.Deserialize(File.ReadAllText(database)).Psus.Select(p => p.Id));
    }

    [Fact]
    public void MergeWritesNothingWhenTheResultIsInvalid()
    {
        var database = Path.Combine(_dir, "psu-database.json");
        var original = MergeCommand.Format(PsuDatabaseLoader.LoadEmbedded());
        File.WriteAllText(database, original);
        var entries = Sub("entries");
        var broken = Entry("broken", "B", "M");
        broken.Curves!["230"].Reverse(); // AC no longer increasing.
        File.WriteAllText(Path.Combine(entries, "broken.json"), EntryWriter.Format(broken));
        File.WriteAllText(Path.Combine(entries, "garbage.json"), "{ not json");

        var (exit, log) = RunWithLog("merge", "--entries", entries, "--into", database, "--date", "2026-11-02");

        Assert.Equal(ImportCommand.Failed, exit);
        Assert.Contains("garbage.json", log, StringComparison.Ordinal);
        Assert.Contains("curves.230", log, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(database));
    }

    [Fact]
    public void MergeOutputOfTheShippedDatabaseIsUnchanged()
    {
        var shipped = FindRepoFile("psu-database.json");
        var database = PsuDatabaseJson.Deserialize(File.ReadAllText(shipped));

        Assert.Equal(File.ReadAllText(shipped).ReplaceLineEndings("\n"), MergeCommand.Format(database));
    }

    [Fact]
    public async Task DownloadFetchesReportPagesPolitelyAndSkipsSavedOnes()
    {
        var reports = Sub("reports");
        File.WriteAllText(Path.Combine(reports, "EXISTING.htm"), "already here");
        File.WriteAllText(Path.Combine(reports, "OLDSAVE.html"), "saved by hand");
        var rows = Catalog.Parse(string.Join('\n',
        [
            Header,
            Row("existing", "EXISTING", 1),
            Row("old-save", "OLDSAVE", 2),
            PageRow("plain-http", "PLAIN", "http://reports.example/evaluations/psus/3/"),
            PageRow("no-url", "NOURL", string.Empty),
            Row("ok-one", "OK1", 4),
            Row("ok-two", "OK2", 5),
            Row("ok-three", "OK3", 6),
        ]));
        var clock = new FakeClock();
        var handler = ReportSite(clock);
        using var downloader = clock.Downloader(handler);
        using var log = new StringWriter();

        var summary = await downloader.DownloadAsync(rows, reports, log);

        Assert.Equal(new DownloadSummary(Downloaded: 3, Skipped: 2, Failed: 2, Requests: 3, Stopped: false), summary);
        Assert.Equal(["/evaluations/psus/4/", "/evaluations/psus/5/", "/evaluations/psus/6/"], handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Equal("ShellyHwinfoBridge-import/1.0 (+https://github.com/eksdeexD/ShellyHwinfoBridge; zapador@zapador.net)", ReportDownloader.UserAgent);
        Assert.All(handler.Requests, r => Assert.Equal(ReportDownloader.UserAgent, r.Headers.UserAgent.ToString()));
        Assert.Equal(TimeSpan.FromSeconds(15), ReportDownloader.MinInterval);
        Assert.Equal([ReportDownloader.MinInterval, ReportDownloader.MinInterval], clock.Delays);
        Assert.Empty(clock.Pauses);
        Assert.Contains("Testing Date", File.ReadAllText(Path.Combine(reports, "OK1.htm")), StringComparison.Ordinal);
        Assert.Equal("already here", File.ReadAllText(Path.Combine(reports, "EXISTING.htm")));
        Assert.False(File.Exists(Path.Combine(reports, "OLDSAVE.htm")));
        Assert.Empty(Directory.GetFiles(reports, "*.part"));
        Assert.Empty(Directory.GetFiles(reports, "*.pdf"));
        Assert.Contains("\"2026-11-02\": 3", File.ReadAllText(Path.Combine(reports, ReportDownloader.StateFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARerunKeepsTheMinimumIntervalToThePreviousRun()
    {
        var reports = Sub("reports");
        var clock = new FakeClock();
        using (var first = clock.Downloader(ReportSite(clock)))
        {
            await first.DownloadAsync(Catalog.Parse(Header + "\n" + Row("a", "A", 1)), reports, TextWriter.Null);
        }

        clock.Now += TimeSpan.FromSeconds(4);
        using var second = clock.Downloader(ReportSite(clock));
        await second.DownloadAsync(Catalog.Parse(Header + "\n" + Row("b", "B", 2)), reports, TextWriter.Null);

        Assert.Equal([TimeSpan.FromSeconds(11)], clock.Delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null)]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.Found, null)]
    [InlineData(HttpStatusCode.OK, "<html><body><h1>Just a moment...</h1><p>Checking your browser.</p></body></html>")]
    [InlineData(HttpStatusCode.OK, "<html><body><table><tr><td>Brand</td><td>B</td></tr></table></body></html>")]
    public async Task AChallengeOrRefusalStopsTheRunAtOnceWithoutARetry(HttpStatusCode status, string? body)
    {
        var reports = Sub("reports");
        var rows = Catalog.Parse(string.Join('\n', [Header, Row("ok", "OK1", 1), Row("blocked", "BLOCKED", 2), Row("never", "NEVER", 3)]));
        var clock = new FakeClock();
        var site = ReportSite(clock);
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/evaluations/psus/2/"
            ? new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty) }
            : site.Respond(request));
        using var downloader = clock.Downloader(handler);
        using var log = new StringWriter();

        var summary = await downloader.DownloadAsync(rows, reports, log);

        Assert.True(summary.Stopped);
        Assert.False(summary.CapReached);
        Assert.Equal((1, 1, 2), (summary.Downloaded, summary.Failed, summary.Requests));
        Assert.Equal(["/evaluations/psus/1/", "/evaluations/psus/2/"], handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.False(File.Exists(Path.Combine(reports, "BLOCKED.htm")));
        Assert.Empty(Directory.GetFiles(reports, "*.part"));
        Assert.Contains("stopped:", log.ToString(), StringComparison.Ordinal);
        Assert.Contains("no retry", log.ToString(), StringComparison.Ordinal);
        Assert.Contains(body is null ? ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture) : "challenge page", summary.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDailyCapStopsTheRunNamesTheNextDayAndCountsAcrossRuns()
    {
        var reports = Sub("reports");
        var rows = Catalog.Parse(string.Join('\n', [Header, Row("a", "A", 1), Row("b", "B", 2), Row("c", "C", 3), Row("d", "D", 4)]));
        var clock = new FakeClock();

        using (var first = clock.Downloader(ReportSite(clock)))
        {
            using var log = new StringWriter();
            var summary = await first.DownloadAsync(rows, reports, log, maxPerDay: 2);
            Assert.Equal((2, 2, true, true), (summary.Downloaded, summary.Requests, summary.Stopped, summary.CapReached));
            Assert.Contains("the daily cap of 2 report pages is reached for 2026-11-02; the next allowed day is 2026-11-03.", log.ToString(), StringComparison.Ordinal);
        }

        // Same day, later: the state file still says 2, so nothing is fetched.
        clock.Now += TimeSpan.FromHours(5);
        using (var second = clock.Downloader(ReportSite(clock)))
        {
            var summary = await second.DownloadAsync(rows, reports, TextWriter.Null, maxPerDay: 2);
            Assert.Equal((0, 2, true), (summary.Requests, summary.Skipped, summary.CapReached));
        }

        // After local midnight the count starts again; a larger cap on the second day fetches the rest.
        clock.Now = new DateTimeOffset(2026, 11, 3, 0, 5, 0, TimeSpan.FromHours(1));
        using (var third = clock.Downloader(ReportSite(clock)))
        {
            var summary = await third.DownloadAsync(rows, reports, TextWriter.Null, maxPerDay: 300);
            Assert.Equal((2, 2, false), (summary.Downloaded, summary.Requests, summary.Stopped));
        }

        Assert.Equal(4, Directory.GetFiles(reports, "*.htm").Length);
        var state = File.ReadAllText(Path.Combine(reports, ReportDownloader.StateFileName));
        Assert.Contains("\"2026-11-02\": 2", state, StringComparison.Ordinal);
        Assert.Contains("\"2026-11-03\": 2", state, StringComparison.Ordinal);
        Assert.Equal(150, ReportDownloader.DefaultMaxPerDay);
    }

    [Fact]
    public async Task BatchesArePausedAndTheCapAndIntervalStillApply()
    {
        var reports = Sub("reports");
        var rows = Catalog.Parse(string.Join('\n', new[] { Header }.Concat(Enumerable.Range(1, 5).Select(i => Row($"u{i}", $"U{i}", i)))));
        var clock = new FakeClock();
        using var downloader = clock.Downloader(ReportSite(clock));
        using var log = new StringWriter();

        var summary = await downloader.DownloadAsync(rows, reports, log, maxPerDay: 4, batchSize: 2, batchPause: TimeSpan.FromMinutes(15));

        // Requests 1, 2 | pause | 3, 4 | cap reached before a second pause: the cap is checked first.
        Assert.Equal((4, true, true), (summary.Requests, summary.Stopped, summary.CapReached));
        Assert.Equal([TimeSpan.FromMinutes(15)], clock.Pauses);
        Assert.Equal([ReportDownloader.MinInterval, ReportDownloader.MinInterval], clock.Delays); // Within the batches only.
        Assert.Contains("batch of 2 done; pausing 15 min, resuming at 2026-11-02 10:15:15.", log.ToString(), StringComparison.Ordinal);
        Assert.Equal((50, TimeSpan.FromMinutes(15)), (ReportDownloader.DefaultBatchSize, ReportDownloader.DefaultBatchPause));
    }

    [Theory]
    [InlineData("15m", 900)]
    [InlineData("900s", 900)]
    [InlineData("900", 900)]
    [InlineData("1h", 3600)]
    [InlineData(" 20M ", 1200)]
    [InlineData("0s", 0)]
    public void BatchPauseDurationsAreRead(string text, int seconds)
    {
        Assert.True(DownloadCommand.TryParseDuration(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("m")]
    [InlineData("-5m")]
    [InlineData("1.5m")]
    [InlineData("15x")]
    [InlineData("fifteen")]
    public void BadBatchPauseDurationsAreRejected(string text) => Assert.False(DownloadCommand.TryParseDuration(text, out _));

    [Theory]
    [InlineData("--max-per-day", "0")]
    [InlineData("--max-per-day", "-1")]
    [InlineData("--max-per-day", "unlimited")]
    [InlineData("--batch-size", "0")]
    [InlineData("--batch-pause", "soon")]
    public void DownloadOptionsMustBePositiveNumbers(string option, string value)
    {
        var catalog = Path.Combine(_dir, "catalog.csv");
        File.WriteAllText(catalog, Header + "\n");
        var (exit, log) = RunWithLog("download", "--catalog", catalog, "--reports", Sub("reports"), option, value);
        Assert.Equal(ImportCommand.Usage, exit);
        Assert.Contains(option, log, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportPageHasTablesAndATestingDate()
    {
        Assert.True(ReportDownloader.IsReportPage(ReportPage(1)));
        Assert.False(ReportDownloader.IsReportPage("<html><body>Testing Date</body></html>"));
        Assert.False(ReportDownloader.IsReportPage("<html><table></table></html>"));
        Assert.False(ReportDownloader.IsReportPage(null));
    }

    private static string Row(string id, string reportId, int number) => PageRow(id, reportId, $"https://reports.example/evaluations/psus/{number}/");

    // The old catalogue layout (with ratingCybenetics and pdfUrl) still parses; the download uses reportUrl only.
    private static string PageRow(string id, string reportId, string reportUrl) =>
        $"{id},B,S,M,,650,Gold,,{reportId},{reportUrl},https://reports.example/never-fetched.pdf,1,";

    private static string ReportPage(int number) => Page($"21PS{number:0000}A", number, "Jul 16, 2021");

    private static FakeHandler ReportSite(FakeClock clock) => new(request =>
    {
        clock.Requests.Add(clock.Now);
        var number = int.Parse(request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1], System.Globalization.CultureInfo.InvariantCulture);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReportPage(number)) };
    });

    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(1));

        public List<TimeSpan> Delays { get; } = [];

        public List<TimeSpan> Pauses { get; } = [];

        public List<DateTimeOffset> Requests { get; } = [];

        public ReportDownloader Downloader(HttpMessageHandler handler) => new(
            handler,
            (d, _) =>
            {
                Delays.Add(d);
                Now += d;
                return Task.CompletedTask;
            },
            () => Now,
            (d, _) =>
            {
                Pauses.Add(d);
                Now += d;
                return Task.CompletedTask;
            });
    }

    [Fact]
    public void CatalogueParsesQuotedFieldsAndLineNumbers()
    {
        var rows = Catalog.Parse(Header + "\r\n\r\n" + "a,\"B \"\"quoted\"\"\",S,M,,650,Gold,,R1,https://e.com/,,1,\"multi\nline\"\r\nb,B,S,M,,650,Gold,,R2,https://e.com/,,1,\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("B \"quoted\"", rows[0]["brand"]);
        Assert.Equal("multi\nline", rows[0]["notes"]);
        Assert.Equal(3, rows[0].LineNumber);
        Assert.Equal(5, rows[1].LineNumber);
    }

    [Fact]
    public void BackfillFillsReportDatesFromSavedPagesAndChangesNothingElse()
    {
        PsuEntry With(string id, string? reportId, int number, string? date = null)
        {
            var entry = Entry(id, "Brand", "Model " + id);
            entry.Source = new PsuSource { Name = "Cybenetics", ReportId = reportId, Url = $"https://reports.example/evaluations/psus/{number}/", ReportDate = date };
            return entry;
        }

        var original = new PsuDatabase
        {
            SchemaVersion = 1,
            Updated = "2026-10-31",
            Psus =
            [
                With("by-file-name", "21PS0001A", 1),
                With("by-printed-id", "21PS0002A", 2),
                With("by-url", null, 3),
                With("already-set", "21PS0004A", 4, "2020-01-01"),
                With("no-page", "21PS0005A", 5),
                With("no-date", "21PS0006A", 6),
                With("conflict", "21PS0007A", 7),
                With("two-pages-agree", "21PS0008A", 8),
            ],
        };
        var database = Path.Combine(_dir, "psu-database.json");
        var before = MergeCommand.Format(original);
        File.WriteAllText(database, before);
        var pages = Sub("pages");
        File.WriteAllText(Path.Combine(pages, "21PS0001A.html"), Page("21PS0001A", 1, "Jul 16, 2021"));
        File.WriteAllText(Path.Combine(pages, "Brand Model by-printed-id.htm"), Page("21PS0002A", 2, "Sep 3, 2024"));
        File.WriteAllText(Path.Combine(pages, "Brand Model by-url.htm"), Page(null, 3, "Jan 5, 2019"));
        File.WriteAllText(Path.Combine(pages, "21PS0004A.html"), Page("21PS0004A", 4, "Mar 1, 2022"));
        File.WriteAllText(Path.Combine(pages, "21PS0006A.html"), Page("21PS0006A", 6, null));
        File.WriteAllText(Path.Combine(pages, "21PS0007A.html"), Page("21PS0007A", 7, "Feb 1, 2023"));
        File.WriteAllText(Path.Combine(pages, "conflict copy.htm"), Page("21PS0007A", 7, "Feb 2, 2023"));
        File.WriteAllText(Path.Combine(pages, "21PS0008A.html"), Page("21PS0008A", 8, "Apr 4, 2025"));
        File.WriteAllText(Path.Combine(pages, "two-pages-agree.htm"), Page("21PS0008A", 8, "Apr 4, 2025"));
        File.WriteAllText(Path.Combine(pages, "notes.txt"), "not a page");

        var (dryExit, dryLog) = RunWithLog("backfill-dates", "--pages", pages, "--into", database, "--dry-run");
        Assert.Equal(ImportCommand.Ok, dryExit);
        Assert.Equal(before, File.ReadAllText(database));
        Assert.Contains("4 filled, 1 already set, 1 no page, 1 no date found, 1 conflicting dates", dryLog, StringComparison.Ordinal);

        var (exit, log) = RunWithLog("backfill-dates", "--pages", pages, "--into", database);

        Assert.True(exit == ImportCommand.Ok, log);
        Assert.Contains("9 pages read, 8 entries.", log, StringComparison.Ordinal);
        Assert.Contains("Dates: 4 filled, 1 already set, 1 no page, 1 no date found, 1 conflicting dates.", log, StringComparison.Ordinal);
        Assert.DoesNotContain("ETA:", log, StringComparison.Ordinal);
        Assert.Contains("NoPage: 21PS0005A Model no-page", log, StringComparison.Ordinal);
        Assert.Contains("NoDate: 21PS0006A Model no-date", log, StringComparison.Ordinal);
        Assert.Contains("Conflict: 21PS0007A Model conflict", log, StringComparison.Ordinal);
        var text = File.ReadAllText(database);
        var result = PsuDatabaseJson.Deserialize(text);
        Assert.Equal(
            ["2021-07-16", "2024-09-03", "2019-01-05", "2020-01-01", null, null, null, "2025-04-04"],
            result.Psus.Select(p => p.Source!.ReportDate));
        Assert.Equal(MergeCommand.Format(result), text);

        // Apart from the dates, the file is byte for byte what it was (updated included).
        foreach (var entry in result.Psus.Zip(original.Psus))
        {
            entry.First.Source!.ReportDate = entry.Second.Source!.ReportDate;
        }

        Assert.Equal(before, MergeCommand.Format(result));
        Assert.Equal("2026-10-31", result.Updated);
    }

    [Fact]
    public void BackfillRefusesAPageWhoseUrlNamesAnotherReport()
    {
        var entry = Entry("unit", "Brand", "Model");
        entry.Source = new PsuSource { Name = "Cybenetics", ReportId = "21PS0001A", Url = "https://reports.example/evaluations/psus/1/" };
        var database = new PsuDatabase { SchemaVersion = 1, Psus = [entry] };

        var results = BackfillDatesCommand.Backfill(database, [("21PS0001A.html", HtmlReportParser.ReadMetadata(Page("21PS0001A", 99, "Jul 16, 2021")))]);

        Assert.Equal(BackfillStatus.Conflict, Assert.Single(results).Status);
        Assert.Null(entry.Source.ReportDate);
    }

    [Fact]
    public void BackfillSetsTheEtaGradesPerVoltageFromThePagesAndChangesNothingElse()
    {
        PsuEntry With(string id, string reportId, int number, string? legacy = null)
        {
            var entry = Entry(id, "Brand", "Model " + id);
            entry.Source = new PsuSource { Name = "Cybenetics", ReportId = reportId, Url = $"https://reports.example/evaluations/psus/{number}/", ReportDate = "2024-01-01" };
            return entry;
        }

        var database = Path.Combine(_dir, "psu-database.json");
        var original = MergeCommand.Format(new PsuDatabase
        {
            SchemaVersion = 1,
            Updated = "2026-10-31",
            Psus =
            [
                With("both", "21PS0001A", 1),
                With("differ", "21PS0002A", 2),
                With("eu-only", "21PS0003A", 3),
                With("no-rating", "21PS0004A", 4),
                With("legacy-agrees", "21PS0005A", 5),
                With("legacy-differs", "21PS0006A", 6),
                With("no-table", "21PS0007A", 7),
                With("unknown-badge", "21PS0008A", 8),
                With("pages-disagree", "21PS0009A", 9),
                With("no-page", "21PS0010A", 10),
            ],
        });

        // A database written before 1.0.1: every entry has the single "ratingCybenetics" (null or a grade).
        var legacy = original.Replace("      \"rating80Plus\": \"Gold\",\n", "      \"rating80Plus\": \"Gold\",\n      \"ratingCybenetics\": null,\n", StringComparison.Ordinal);
        Assert.NotEqual(original, legacy);
        legacy = ReplaceNth(legacy, "\"ratingCybenetics\": null", "\"ratingCybenetics\": \"Silver\"", 9); // pages-disagree
        legacy = ReplaceNth(legacy, "\"ratingCybenetics\": null", "\"ratingCybenetics\": \"Gold\"", 6); // legacy-differs
        legacy = ReplaceNth(legacy, "\"ratingCybenetics\": null", "\"ratingCybenetics\": \"Gold\"", 5); // legacy-agrees
        File.WriteAllText(database, legacy);

        var pages = Sub("pages");
        void Save(string file, string reportId, int number, string? eta115, string? eta230, bool table = true) =>
            File.WriteAllText(Path.Combine(pages, file), Page(reportId, number, "Jan 1, 2024", table ? EtaTable(eta115, eta230) : string.Empty));
        Save("21PS0001A.html", "21PS0001A", 1, "16", "28");
        Save("Brand Model differ.htm", "21PS0002A", 2, "16", "29");
        Save("21PS0003A.htm", "21PS0003A", 3, null, "27");
        Save("21PS0004A.html", "21PS0004A", 4, string.Empty, string.Empty);
        Save("21PS0005A.html", "21PS0005A", 5, "17", "29");
        Save("21PS0006A.html", "21PS0006A", 6, "18", "28");
        Save("21PS0007A.html", "21PS0007A", 7, null, null, table: false);
        Save("21PS0008A.html", "21PS0008A", 8, "14", "28");
        Save("21PS0009A.html", "21PS0009A", 9, "17", "29");
        Save("copy of 9.htm", "21PS0009A", 9, "17", "28");

        // Dates only: a database with the old single grade is refused (it would rewrite the ETA lines unasked).
        var (datesExit, datesLog) = RunWithLog("backfill-dates", "--pages", pages, "--into", database);
        Assert.Equal(ImportCommand.Failed, datesExit);
        Assert.Contains("still has the single \"ratingCybenetics\"", datesLog, StringComparison.Ordinal);
        Assert.Equal(legacy, File.ReadAllText(database));

        var (exit, log) = RunWithLog("backfill", "--pages", pages, "--into", database);

        Assert.True(exit == ImportCommand.Ok, log);
        Assert.Contains("ETA: 5 with grades (2 with different grades at 115 V and 230 V), 1 without an ETA rating, 1 no page, 1 no ETA table, 1 unknown badge, 1 conflicting.", log, StringComparison.Ordinal);
        Assert.Contains("changed: 21PS0006A Model legacy-differs (legacy-differs): had 230 V Gold, the page shows 230 V Platinum, 115 V Silver", log, StringComparison.Ordinal);
        Assert.DoesNotContain("changed: 21PS0005A", log, StringComparison.Ordinal);
        Assert.Contains("Unreadable: 21PS0008A", log, StringComparison.Ordinal);
        Assert.Contains("no ETA rating: 21PS0004A", log, StringComparison.Ordinal);

        var text = File.ReadAllText(database);
        Assert.DoesNotContain("\"ratingCybenetics\"", text, StringComparison.Ordinal);
        Assert.Contains("      \"rating80Plus\": \"Gold\",\n      \"ratingsCybenetics\": { \"230\": \"Gold\", \"115\": \"Platinum\" },\n      \"source\"", text, StringComparison.Ordinal);
        var result = PsuDatabaseJson.Deserialize(text);
        string Grades(PsuEntry e) => e.RatingsCybenetics is null ? "-" : string.Join(",", e.RatingsCybenetics.OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Value}"));
        Assert.Equal(
            ["115=Platinum,230=Platinum", "115=Platinum,230=Gold", "230=Titanium", "-", "115=Gold,230=Gold", "115=Silver,230=Platinum", "-", "-", "230=Silver", "-"],
            result.Psus.Select(Grades));

        // Apart from the ETA grades the file is what the merge command writes for the original, updated included.
        foreach (var entry in result.Psus)
        {
            entry.RatingsCybenetics = null;
        }

        Assert.Equal(original, MergeCommand.Format(result));
        Assert.Equal("2026-10-31", result.Updated);

        // A second run changes nothing.
        var (againExit, againLog) = RunWithLog("backfill", "--pages", pages, "--into", database);
        Assert.Equal(ImportCommand.Ok, againExit);
        Assert.Contains("Nothing to fill", againLog, StringComparison.Ordinal);
        Assert.Equal(text, File.ReadAllText(database));
    }

    private static string ReplaceNth(string text, string find, string replace, int n)
    {
        var at = -1;
        for (var i = 0; i < n; i++)
        {
            at = text.IndexOf(find, at + 1, StringComparison.Ordinal);
        }

        return text[..at] + replace + text[(at + find.Length)..];
    }

    // The ETA badge table as a saved page has it: null = not tested at that voltage, "" = no badge, else the badge number.
    private static string EtaTable(string? eta115, string? eta230)
    {
        static (string Header, string Cell) Column(string volts, string? badge) => badge switch
        {
            null => ($"<td class=\"d-none\">{volts}</td>", "<td class=\"d-none\"></td>"),
            "" => ($"<td class=\"\">{volts}</td>", "<td class=\"\"></td>"),
            _ => ($"<td class=\"\">{volts}</td>", $"<td class=\"\"><img src=\"Unit_files/{badge}_kQ_U.png\"></td>"),
        };

        var (h115, c115) = Column("115V", eta115);
        var (h230, c230) = Column("230V", eta230);
        return $"""<table class="table w-75 float-end"><tbody><tr><td></td>{h115}{h230}</tr><tr><td><span class="stdWidth">Scheme: ETA-EMI PSU</span></td>{c115}{c230}</tr></tbody></table>""";
    }

    // A minimal saved evaluation page: report id, report URL (via a PDF link) and the testing information table.
    private static string Page(string? reportId, int number, string? testingDate, string etaTable = "") => $"""
        <html><body>
        {etaTable}
        <a href="https://reports.example/evaluations/psus/{number}/pdf/">PDF</a>
        <ul class="dates"><li>Test Result Report ID: {reportId}</li><li>Certificate Issue Date: <span>Dec 24, 2026</span></li></ul>
        <table>
        <tr><td>Date of receipt</td><td>Jan 1, 2000</td></tr>
        {(testingDate is null ? string.Empty : $"<tr><td>Testing Date</td><td>{testingDate}</td></tr>")}
        <tr><td>Report Issued Date</td><td>Dec 24, 2026</td></tr>
        </table>
        </body></html>
        """;

    private static PsuEntry Entry(string id, string brand, string model) => EntryWriter.Build(
        new EntryMetadata
        {
            Id = id,
            Brand = brand,
            Series = "S",
            Model = model,
            Wattage = 650,
            Rating80Plus = "Gold",
            ReportId = "R",
            Url = "https://example.com/" + id,
        },
        new Dictionary<int, IReadOnlyList<(double Ac, double Dc)>> { [230] = [(30.0, 20.0), (100.0, 85.0), (700.0, 620.0)] });

    private static string FindRepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(name);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpResponseMessage Respond(HttpRequestMessage request) => respond(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
