using System.Text.Json;
using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport.Tests;

public sealed class HtmlReportParserTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cyb-html-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "evaluation-page-tx700.html");

    [Fact]
    public void ReproducesTheSeedCurvesFromAnEvaluationPage()
    {
        var report = HtmlReportParser.Parse(File.ReadAllText(FixturePath));

        Assert.Empty(report.Result.Warnings);
        Assert.Equal([230, 115], report.Result.Curves.Keys);
        Assert.Equal(ReportParserTests.Expected230, report.Result.Curves[230]);
        Assert.Equal(ReportParserTests.Expected115, report.Result.Curves[115]);
        Assert.DoesNotContain(report.Result.Points, p => p.Label.StartsWith("CL", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsTheMetadataOfTheSavedPage()
    {
        var metadata = HtmlReportParser.Parse(File.ReadAllText(FixturePath)).Metadata;

        Assert.Equal("Examplebrand", metadata.Brand);
        Assert.Equal("Fictional FX-700", metadata.Model);
        Assert.Equal("ATX v3.1", metadata.Revision);
        Assert.Equal(700, metadata.Wattage);
        Assert.Equal("99PS9872A", metadata.ReportId);
        Assert.Equal("https://reports.example/evaluations/psus/9872/", metadata.Url);
        Assert.Equal("Jan 1, 2026", metadata.TestingDate);
        Assert.Equal("examplebrand-fictional-fx-700-atx-v3-1", metadata.ToValues()["id"]);
    }

    [Fact]
    public void ImportCommandBuildsAValidEntryFromThePage()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(["import", "--html", FixturePath, "--series", "Fictional FX", "--rating80plus", "titanium"], stdout, stderr);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        var entry = JsonSerializer.Deserialize<PsuEntry>(stdout.ToString(), PsuDatabaseJson.Options)!;
        Assert.Equal("examplebrand-fictional-fx-700-atx-v3-1", entry.Id);
        Assert.Equal(("Examplebrand", "Fictional FX-700", "ATX v3.1", 700), (entry.Brand, entry.Model, entry.Revision, entry.Wattage));
        Assert.Equal(("Cybenetics", "99PS9872A", "https://reports.example/evaluations/psus/9872/"), (entry.Source!.Name, entry.Source.ReportId, entry.Source.Url));
        var seed = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == "seasonic-prime-fanless-tx-700");
        Assert.Equal(seed.Curves!["230"], entry.Curves!["230"]);
        Assert.Equal(seed.Curves["115"], entry.Curves["115"]);
    }

    [Fact]
    public void AnExplicitEmptyRevisionIsNotFilledFromThePage()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(
            ["import", "--html", FixturePath, "--id", "examplebrand-fictional-fx-700", "--revision", "", "--series", "Fictional FX", "--rating80plus", "Titanium"],
            stdout, stderr);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        var entry = JsonSerializer.Deserialize<PsuEntry>(stdout.ToString(), PsuDatabaseJson.Options)!;
        Assert.Equal(("examplebrand-fictional-fx-700", "Fictional FX-700"), (entry.Id, entry.Model));
        Assert.Null(entry.Revision);
    }

    [Fact]
    public void ImportOfASingleVoltagePageSucceedsWithAWarning()
    {
        var page = Path.Combine(AppContext.BaseDirectory, "Fixtures", "evaluation-page-230-only.html");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(["import", "--html", page, "--series", "Fictional EU", "--rating80plus", "Titanium"], stdout, stderr);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        Assert.Contains("warning: single-voltage entry: only 230 V was found", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("error", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"115\"", stdout.ToString(), StringComparison.Ordinal);
        var entry = JsonSerializer.Deserialize<PsuEntry>(stdout.ToString(), PsuDatabaseJson.Options)!;
        Assert.Equal(("examplebrand-fictional-eu-700", "99PS9873A"), (entry.Id, entry.Source!.ReportId));
        Assert.Equal(["230"], entry.Curves!.Keys);
        var seed = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == "seasonic-prime-fanless-tx-700");
        Assert.Equal(seed.Curves!["230"], entry.Curves["230"]);
        Assert.Empty(PsuDatabaseValidator.Validate(new PsuDatabase { SchemaVersion = 1, Psus = [entry] }));
    }

    [Fact]
    public void BatchAcceptsASingleVoltagePageAsAWarning()
    {
        var reports = Directory.CreateDirectory(Path.Combine(_dir, "reports-230")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "evaluation-page-230-only.html"), Path.Combine(reports, "99PS9873A.html"));
        var catalog = Path.Combine(_dir, "catalog-230.csv");
        File.WriteAllLines(catalog,
        [
            "id,brand,series,model,revision,wattage,rating80Plus,ratingCybenetics,reportId,reportUrl,pdfUrl,tier,notes",
            "examplebrand-eu-700,,Fictional EU,,,,Titanium,,99PS9873A,,,1,",
        ]);
        var output = Path.Combine(_dir, "out-230");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(["batch", "--catalog", catalog, "--reports", reports, "--out", output], stdout, stderr);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        var entry = JsonSerializer.Deserialize<PsuEntry>(File.ReadAllText(Path.Combine(output, "examplebrand-eu-700.json")), PsuDatabaseJson.Options)!;
        Assert.Equal(["230"], entry.Curves!.Keys);
        var line = File.ReadAllLines(Path.Combine(output, BatchCommand.SummaryFileName))[1];
        Assert.StartsWith("examplebrand-eu-700,99PS9873A,ok,15,0,1,", line, StringComparison.Ordinal);
        Assert.Contains("single-voltage entry", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandLineValuesOverrideThePage()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(
            [FixturePath, "--id", "custom-id", "--model", "Other", "--series", "S", "--rating80plus", "Gold", "--url", "https://e.com/r/"],
            stdout,
            stderr);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        var entry = JsonSerializer.Deserialize<PsuEntry>(stdout.ToString(), PsuDatabaseJson.Options)!;
        Assert.Equal(("custom-id", "Other", "https://e.com/r/"), (entry.Id, entry.Model, entry.Source!.Url));
    }

    [Fact]
    public void ReadsNumberedTestsOfOlderPages()
    {
        // 2020 pages label tests "Test #" 1..11 and 1..4; the pane id tells load from light load.
        const string Html = """
            <div class="tab-pane" id="nav-abc-10-110-230"><table><tr><td>Test #</td><td>12V</td><td>5V</td><td>3.3V</td><td>5VSB</td><td>DC/AC (Watts)</td><td>Efficiency</td><td>Fan Speed (RPM)</td><td>PSU Noise (dB[A])</td><td>Temps (In/Out)</td><td>PF/AC Volts</td></tr>
            <tr><td rowspan="2">5</td><td>62.4A</td><td>5A</td><td>5A</td><td>1.8A</td><td>799.905</td><td rowspan="2">95%</td><td rowspan="2">801</td><td rowspan="2">9.7</td><td>42.4°C</td><td>0.998</td></tr>
            <tr><td>12.01V</td><td>4.97V</td><td>3.3V</td><td>4.96V</td><td>842.005</td><td>48.1°C</td><td>230.1V</td></tr></table></div>
            <div class="tab-pane" id="nav-def-20-80-230"><table><tr><td>Test #</td><td>12V</td><td>5V</td><td>3.3V</td><td>5VSB</td><td>DC/AC (Watts)</td><td>Efficiency</td><td>Temps (In/Out)</td><td>PF/AC Volts</td></tr>
            <tr><td rowspan="2">1</td><td>1.2A</td><td>0.5A</td><td>0.5A</td><td>0.2A</td><td>20.1</td><td rowspan="2">67%</td><td>39°C</td><td>0.45</td></tr>
            <tr><td>12.1V</td><td>5.1V</td><td>3.3V</td><td>5V</td><td>30</td><td>36°C</td><td>230.4V</td></tr></table></div>
            """;

        var result = HtmlReportParser.Parse(Html, ratedWattage: 1600).Result;

        Assert.Empty(result.Warnings);
        Assert.Equal(["50%", "20W"], result.Points.Select(p => p.Label));
        Assert.Equal([(30.0, 20.1), (842.005, 799.905)], result.Curves[230]);
    }

    [Fact]
    public void FallsBackToTheAcVoltageColumnAndAcceptsPairCells()
    {
        // No tab panes: the AC voltage cell decides (>= 180 V is 230 V); a "DC / AC" cell holds both values.
        const string Html = """
            <table><tr><td>Test</td><td>12V</td><td>DC/AC (Watts)</td><td>Efficiency</td><td>PF/AC Volts</td></tr>
            <tr><td>10%</td><td>5.4A 12.1V</td><td>70.006 / 78.495</td><td>89.185%</td><td>0.957 115.2V</td></tr>
            <tr><td>10%</td><td>5.4A 12.1V</td><td>70.914 / 78.781</td><td>90.014%</td><td>0.756 230.41V</td></tr>
            <tr><td>CL1</td><td>0.1A 12.1V</td><td>101.3 / 116.1</td><td>87.3%</td><td>0.97 230.4V</td></tr></table>
            """;

        var result = HtmlReportParser.Parse(Html, ratedWattage: 700).Result;

        Assert.Empty(result.Warnings);
        Assert.Equal((78.495, 70.006), Assert.Single(result.Curves[115]));
        Assert.Equal((78.781, 70.914), Assert.Single(result.Curves[230]));
    }

    [Fact]
    public void WarnsWhenThePrintedEfficiencyDisagrees()
    {
        const string Html = """
            <div id="nav-x-20-80-115"><table><tr><td>Test</td><td>12V</td><td>5V</td><td>3.3V</td><td>5VSB</td><td>DC/AC (Watts)</td><td>Efficiency</td><td>PF/AC Volts</td></tr>
            <tr><td rowspan="2">20W</td><td>1.2A</td><td>0.5A</td><td>0.5A</td><td>0.2A</td><td>20.008</td><td rowspan="2">75.53%</td><td>0.8</td></tr>
            <tr><td>12.1V</td><td>5.1V</td><td>3.3V</td><td>5V</td><td>26.422</td><td>115.1V</td></tr></table></div>
            """;

        var result = HtmlReportParser.Parse(Html).Result;

        Assert.Equal((26.422, 20.008), Assert.Single(result.Curves[115]));
        Assert.Contains("does not match the printed efficiency", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectsHtmlByExtensionOrContent()
    {
        var page = Path.Combine(_dir, "page.txt");
        File.WriteAllText(page, "\n  <!DOCTYPE html><html></html>");
        var text = Path.Combine(_dir, "lines.txt");
        File.WriteAllText(text, "230V\nLoad Tests");

        Assert.True(HtmlReportParser.IsHtml(page));
        Assert.False(HtmlReportParser.IsHtml(text));
        Assert.True(HtmlReportParser.IsHtml("saved page.htm"));
        Assert.False(HtmlReportParser.IsHtml("report.pdf"));
    }

    [Fact]
    public void BatchPrefersTheSavedPageAndFillsEmptyCatalogueCells()
    {
        var reports = Directory.CreateDirectory(Path.Combine(_dir, "reports")).FullName;
        File.Copy(FixturePath, Path.Combine(reports, "99PS9872A.html"));
        File.WriteAllText(Path.Combine(reports, "99PS9872A.pdf"), "not read when the page exists");
        var catalog = Path.Combine(_dir, "catalog.csv");
        File.WriteAllLines(catalog,
        [
            "id,brand,series,model,revision,wattage,rating80Plus,ratingCybenetics,reportId,reportUrl,pdfUrl,tier,notes",
            "examplebrand-fx-700,,Fictional FX,,,,Titanium,Titanium,99PS9872A,,,1,",
        ]);
        var output = Path.Combine(_dir, "out");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(["batch", "--catalog", catalog, "--reports", reports, "--out", output], stdout, stderr);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        var entry = JsonSerializer.Deserialize<PsuEntry>(File.ReadAllText(Path.Combine(output, "examplebrand-fx-700.json")), PsuDatabaseJson.Options)!;
        Assert.Equal(("Examplebrand", "Fictional FX-700", "ATX v3.1", 700), (entry.Brand, entry.Model, entry.Revision, entry.Wattage));
        Assert.Equal("https://reports.example/evaluations/psus/9872/", entry.Source!.Url);
        Assert.Equal(15, entry.Curves!["115"].Count);
        Assert.StartsWith("examplebrand-fx-700,99PS9872A,ok,15,15,0,", File.ReadAllLines(Path.Combine(output, BatchCommand.SummaryFileName))[1], StringComparison.Ordinal);
    }
}
