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
        var database = Path.Combine(_dir, "psu-database.json");
        File.WriteAllText(database, MergeCommand.Format(PsuDatabaseLoader.LoadEmbedded()));
        var entries = Sub("entries");

        var seed = PsuDatabaseLoader.LoadEmbedded().Psus.Single();
        seed.RatingCybenetics = "Diamond";
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
        Assert.Equal("Diamond", merged.Psus[2].RatingCybenetics);
        Assert.Equal(MergeCommand.Format(merged), text);
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
    public async Task DownloadIsPoliteAndSkipsExistingFiles()
    {
        var reports = Sub("reports");
        File.WriteAllText(Path.Combine(reports, "EXISTING.pdf"), "%PDF-1.7 already here");
        var rows = Catalog.Parse(string.Join('\n',
        [
            Header,
            Row("existing", "EXISTING", "https://reports.example/existing.pdf"),
            Row("nourl", "NOURL", string.Empty),
            Row("plain-http", "PLAIN", "http://reports.example/plain.pdf"),
            Row("ok-one", "OK1", "https://reports.example/ok1.pdf"),
            Row("not-pdf", "HTML", "https://reports.example/page.html"),
            Row("ok-two", "OK2", "https://reports.example/ok2.pdf"),
            Row("limited", "LIMITED", "https://reports.example/limited.pdf"),
            Row("never", "NEVER", "https://reports.example/never.pdf"),
        ]));
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/ok1.pdf" or "/ok2.pdf" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7 fake"u8.ToArray()) },
            "/page.html" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") },
            "/limited.pdf" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var delays = new List<TimeSpan>();
        using var downloader = new ReportDownloader(handler, (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        });
        using var log = new StringWriter();

        var summary = await downloader.DownloadAsync(rows, reports, log);

        Assert.Equal(new DownloadSummary(Downloaded: 2, Skipped: 2, Failed: 3, Requests: 4, Stopped: true), summary);
        Assert.Equal(["/ok1.pdf", "/page.html", "/ok2.pdf", "/limited.pdf"], handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.All(handler.Requests, r => Assert.Equal(ReportDownloader.UserAgent, r.Headers.UserAgent.ToString()));
        Assert.Equal([ReportDownloader.MinInterval, ReportDownloader.MinInterval, ReportDownloader.MinInterval], delays);
        Assert.True(ReportDownloader.MinInterval >= TimeSpan.FromSeconds(5));
        Assert.Equal("%PDF-1.7 fake", File.ReadAllText(Path.Combine(reports, "OK1.pdf")));
        Assert.Equal("%PDF-1.7 already here", File.ReadAllText(Path.Combine(reports, "EXISTING.pdf")));
        Assert.False(File.Exists(Path.Combine(reports, "HTML.pdf")));
        Assert.Empty(Directory.GetFiles(reports, "*.part"));
        Assert.Contains("stopping", log.ToString(), StringComparison.Ordinal);
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

    private static string Row(string id, string reportId, string pdfUrl) =>
        $"{id},B,S,M,,650,Gold,,{reportId},https://e.com/{id}/,{pdfUrl},1,";

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
