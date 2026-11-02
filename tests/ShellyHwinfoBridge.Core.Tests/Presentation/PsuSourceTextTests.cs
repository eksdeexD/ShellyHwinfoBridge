using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Presentation;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class PsuSourceTextTests
{
    private static PsuEntry Entry(Dictionary<string, string>? eta, bool with115 = true)
    {
        var curves = new Dictionary<string, List<double[]>> { ["230"] = [[100, 90], [200, 185]] };
        if (with115)
        {
            curves["115"] = [[100, 88], [200, 182]];
        }

        return new PsuEntry
        {
            Id = "a",
            Brand = "B",
            Series = "S",
            Model = "M",
            Wattage = 650,
            Rating80Plus = "None",
            RatingsCybenetics = eta,
            Source = new PsuSource { Name = "Cybenetics", Url = "https://www.cybenetics.com/evaluations/psus/1/", ReportDate = "2024-09-03" },
            Curves = curves,
        };
    }

    [Fact]
    public void EtaIsWrittenExactlyWithItsVoltage()
    {
        var e = Entry(new() { ["230"] = "Gold", ["115"] = "platinum" });
        Assert.Equal("Cybenetics ETA Gold (230 V), ETA Platinum (115 V)", PsuSourceText.Eta(e, null));
        Assert.Equal("Cybenetics ETA Gold (230 V)", PsuSourceText.Eta(e, InputVoltage.V230));
        Assert.Equal("Cybenetics ETA Platinum (115 V)", PsuSourceText.Eta(e, InputVoltage.V115));
    }

    [Fact]
    public void EtaIsShownOnlyWhereTheReportGivesAGrade()
    {
        Assert.Null(PsuSourceText.Eta(Entry(null), null));
        Assert.Null(PsuSourceText.Eta(Entry([]), InputVoltage.V230));

        var only115 = Entry(new() { ["115"] = "Gold" });
        Assert.Equal("Cybenetics ETA Gold (115 V)", PsuSourceText.Eta(only115, null));
        Assert.Null(PsuSourceText.Eta(only115, InputVoltage.V230));

        // A 230 V-only unit uses its 230 V curve at 115 V too, so the grade shown is the 230 V one, labelled 230 V.
        var euOnly = Entry(new() { ["230"] = "Platinum" }, with115: false);
        Assert.Equal("Cybenetics ETA Platinum (230 V)", PsuSourceText.Eta(euOnly, InputVoltage.V115));
    }

    [Fact]
    public void ReportDateReadsReportAndTheDate()
    {
        Assert.Equal("report 2024-09-03", PsuSourceText.ReportDate(Entry(null).Source));
        Assert.Null(PsuSourceText.ReportDate(new PsuSource { Name = "Cybenetics", Url = "https://x/" }));
        Assert.Null(PsuSourceText.ReportDate(null));
    }
}
