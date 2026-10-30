using System.Text.Json;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport.Tests;

public class ReportParserTests
{
    // SPEC seed data for the Seasonic Prime Fanless TX-700 (Cybenetics report 21PS1872A), (AC, DC) sorted by AC.
    private static readonly (double Ac, double Dc)[] Expected230 =
    [
        (25.948, 20.298), (47.421, 40.442), (67.789, 60.907), (78.781, 70.914), (88.435, 81.049),
        (151.862, 141.691), (225.442, 212.262), (299.387, 282.765), (374.101, 353.196), (449.589, 423.603),
        (525.524, 493.819), (597.872, 559.700), (674.794, 630.023), (752.735, 700.385), (831.082, 769.816),
    ];

    private static readonly (double Ac, double Dc)[] Expected115 =
    [
        (25.678, 20.005), (46.634, 40.004), (67.580, 60.003), (78.495, 70.006), (88.228, 79.964),
        (151.718, 139.962), (226.233, 210.010), (301.226, 280.060), (376.703, 350.020), (453.959, 419.752),
        (532.715, 490.204), (610.997, 559.822), (691.011, 630.065), (772.786, 700.400), (855.467, 769.828),
    ];

    private static IReadOnlyList<string> Fixture => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tx700-stacked.txt"));

    [Fact]
    public void ParsesBothVoltagesFromStackedLayout()
    {
        var result = new ReportParser().Parse(Fixture, 700);

        Assert.Empty(result.Warnings);
        Assert.Equal([230, 115], result.Curves.Keys);
        Assert.Equal(Expected230, result.Curves[230]);
        Assert.Equal(Expected115, result.Curves[115]);
    }

    [Fact]
    public void ParsesSideBySideLayoutWithForcedVoltage()
    {
        string[] lines =
        [
            "Efficiency & Load Regulation",
            "Load 12V 5V 3.3V 5VSB DC Watts AC Watts Efficiency",
            "10% 5.450 A 1.982 A 1.979 A 0.985 A 70.914 W 78.781 W 90.014 %",
            "50% 27.146 A 1.982 A 1.979 A 0.985 A 353.196 W 374.101 W 94.412 %",
            "100% 53.831 A 1.982 A 1.979 A 0.985 A 700.385 W 752.735 W 93.045 %",
            "Light Load Efficiency",
            "20W 1.510 A 0.497 A 0.496 A 0.197 A 20.298 25.948 78.226 %",
            "80W 6.013 A 0.497 A 0.496 A 0.197 A 81.049 88.435 91.648 %",
        ];

        var result = new ReportParser(new ParserOptions { ForcedVoltage = 230 }).Parse(lines, 700);

        Assert.Equal(
            [(25.948, 20.298), (78.781, 70.914), (88.435, 81.049), (374.101, 353.196), (752.735, 700.385)],
            result.Curves[230]);
    }

    [Fact]
    public void RowsBeforeAnyVoltageMarkerNeedADefault()
    {
        string[] lines = ["Load Regulation", "50% 27.146A 353.196 94.412%", "374.101 230.14V"];

        var noDefault = new ReportParser().Parse(lines, 700);
        Assert.Empty(noDefault.Points);
        Assert.Contains(noDefault.Warnings, w => w.Contains("--voltage", StringComparison.Ordinal));

        var withDefault = new ReportParser(new ParserOptions { DefaultVoltage = 115 }).Parse(lines, 700);
        Assert.Equal(new MeasuredPoint(115, "50%", 374.101, 353.196, 2), Assert.Single(withDefault.Points));
    }

    [Fact]
    public void SkipsRowsWithoutPlausibleValuesAndReportsDuplicates()
    {
        string[] lines =
        [
            "230V",
            "Load Regulation",
            "50% 27.146A 999.000 94.412%",       // DC far from 350 W
            "20% 10.890A 141.691 93.302%",
            "12.107V 5.041V 141.000 230.11V",      // AC below DC
            "Load Regulation (repeated on next page)",
            "50% 27.146A 353.196 94.412%",
            "12.098V 374.101 230.14V",
            "50% 27.146A 353.196 94.412%",
            "12.098V 374.101 230.14V",
        ];

        var result = new ReportParser().Parse(lines, 700);

        Assert.Equal(["50%"], result.Points.Select(p => p.Label));
        Assert.Equal(3, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("no DC value near 350", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("no AC value", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void LocatorsAreConfigurable()
    {
        string[] lines = ["Netzspannung 230 Volt", "Wirkungsgrad-Tabelle", "50 Prozent 353.196 374.101"];
        var options = new ParserOptions
        {
            VoltageMarker = @"(?<v>115|230)\s*Volt",
            LoadTableHeader = "Wirkungsgrad",
            LoadRow = @"^(?<p>\d+)\s*Prozent",
        };

        var point = Assert.Single(new ReportParser(options).Parse(lines, 700).Points);

        Assert.Equal((230, 374.101, 353.196), (point.Voltage, point.Ac, point.Dc));
    }

    [Fact]
    public void CommandWritesAValidDatabaseEntry()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(
            [
                "report.txt", "--id", "seasonic-prime-fanless-tx-700", "--brand", "Seasonic", "--series", "Prime Fanless TX",
                "--model", "Prime Fanless TX-700", "--wattage", "700", "--rating80plus", "titanium", "--ratingCybenetics", "Titanium",
                "--reportId", "21PS1872A", "--url", "https://www.cybenetics.com/evaluations/psus/1872/",
            ],
            stdout,
            stderr,
            _ => Fixture);

        Assert.True(exit == ImportCommand.Ok, stderr.ToString());
        var entry = JsonSerializer.Deserialize<PsuEntry>(stdout.ToString(), PsuDatabaseJson.Options)!;
        Assert.Equal("Titanium", entry.Rating80Plus);
        Assert.Equal("21PS1872A", entry.Source!.ReportId);
        Assert.Equal(Expected230.Select(p => new[] { p.Ac, p.Dc }), entry.Curves!["230"]);
        Assert.Equal(15, entry.GetCurve(InputVoltage.V115)!.Points.Count);

        // The output matches the shipped seed entry curve for curve.
        var seed = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == entry.Id);
        Assert.Equal(seed.Curves!["230"], entry.Curves["230"]);
        Assert.Equal(seed.Curves["115"], entry.Curves["115"]);
        Assert.Contains("230 V: 15 points", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandUsesPerInputVoltageAndWarnsAboutMissingRows()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        string[] only115 = ["Load Regulation", "50% 27.146A 350.020 92.9%", "12.0V 376.703 115.10V", "100% 53.8A 700.400 90.6%", "12.0V 772.786 115.2V"];

        var exit = ImportCommand.Run(
            [
                "--input115", "a.txt", "--id", "x", "--brand", "B", "--series", "S", "--model", "M", "--wattage", "700",
                "--rating80plus", "Gold", "--reportId", "R", "--url", "https://example.com/r",
            ],
            stdout,
            stderr,
            _ => only115);

        Assert.Equal(ImportCommand.Ok, exit);
        Assert.Contains("\"115\": [", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"230\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("115 V is missing 20W", stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "r.txt", "--brand", "B" }, "--id is required")]
    [InlineData(new[] { "--id", "x" }, "No input file")]
    [InlineData(new[] { "r.txt", "--id", "x", "--brand", "B", "--series", "S", "--model", "M", "--wattage", "abc", "--rating80plus", "Gold", "--reportId", "R", "--url", "u" }, "--wattage")]
    [InlineData(new[] { "r.txt", "--id", "x", "--brand", "B", "--series", "S", "--model", "M", "--wattage", "700", "--rating80plus", "Gold", "--reportId", "R", "--url", "u", "--load-row", "(" }, "error")]
    public void CommandRejectsBadArguments(string[] args, string message)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Assert.Equal(ImportCommand.Usage, ImportCommand.Run(args, stdout, stderr, _ => []));
        Assert.Contains(message, stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandFailsWhenNothingIsFound()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = ImportCommand.Run(
            ["r.txt", "--id", "x", "--brand", "B", "--series", "S", "--model", "M", "--wattage", "700", "--rating80plus", "Gold", "--reportId", "R", "--url", "https://e.com/"],
            stdout,
            stderr,
            _ => ["nothing to see here"]);

        Assert.Equal(ImportCommand.Failed, exit);
        Assert.Contains("--dump-text", stderr.ToString(), StringComparison.Ordinal);
    }
}
