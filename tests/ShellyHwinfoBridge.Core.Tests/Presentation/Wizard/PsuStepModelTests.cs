using System.Globalization;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

public class PsuStepModelTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static PsuStepModel Model() => new(TestDatabase.Create(), EnUs);

    [Fact]
    public void FiltersCascadeWithAnyFirst()
    {
        var m = Model();
        Assert.Equal(["Any", "Corsair", "Seasonic"], m.BrandOptions.Select(o => o.Text));
        Assert.Equal(["Any", "700 W", "750 W", "850 W"], m.WattageOptions.Select(o => o.Text));
        Assert.Equal(6, m.Rows.Count);

        m.SetBrand("Seasonic");
        Assert.Equal(["Any", "700 W", "750 W"], m.WattageOptions.Select(o => o.Text));
        m.SetWattage(750);
        Assert.Equal(["Any", "Platinum", "Titanium"], m.RatingOptions.Select(o => o.Text));
        m.SetRating("Titanium");
        Assert.Equal(["Prime TX-750"], m.Rows.Select(r => r.Title));
    }

    [Fact]
    public void ParentChangeResetsChildrenThatAreNoLongerAvailable()
    {
        var m = Model();
        m.SetBrand("Seasonic");
        m.SetWattage(700);
        m.SetRating("Titanium");

        m.SetBrand("Corsair");
        Assert.Null(m.Wattage);
        Assert.Null(m.Rating);

        m.SetWattage(750);
        m.SetRating("Platinum");
        m.SetBrand(null);
        Assert.Equal(750, m.Wattage);
        Assert.Equal("Platinum", m.Rating);
        Assert.Equal(["Corsair SF750", "Seasonic Prime PX-750"], m.Rows.Select(r => r.Title));
    }

    [Fact]
    public void SearchIntersectsTheFilters()
    {
        var m = Model();
        m.SetSearchText("prime 750");
        Assert.Equal(["Seasonic Prime PX-750", "Seasonic Prime TX-750"], m.Rows.Select(r => r.Title));

        m.SetRating("Titanium");
        Assert.Equal(["Seasonic Prime TX-750"], m.Rows.Select(r => r.Title));

        m.SetBrand("Corsair");
        Assert.Empty(m.Rows);
        Assert.StartsWith("No PSU matches", m.EmptyListText, StringComparison.Ordinal);
    }

    [Fact]
    public void RowsShowRevisionRatingsAndTheReportLink()
    {
        var m = Model();
        m.SetBrand("Corsair");
        Assert.Equal(["RM850x (2018)", "RM850x (2021)", "SF750"], m.Rows.Select(r => r.Title));

        var old = m.Rows[0];
        Assert.Equal("80 Plus Gold", old.EightyPlus);
        Assert.Empty(old.EtaLines);
        var tx = Model().Rows.Single(r => r.Id == "tx700");
        Assert.Equal("Seasonic Prime Fanless TX-700", tx.Title);
        // The voltage is not known yet (automatic, nothing measured): every ETA grade, each with its voltage.
        Assert.Equal("80 Plus Titanium", tx.EightyPlus);
        Assert.Equal(["Cybenetics ETA Titanium (230 V)", "Cybenetics ETA Titanium (115 V)"], tx.EtaLines);
        Assert.Equal("report 21PS1872A · 2021-07-16 ↗", tx.LinkText);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/tx700/", tx.Url);

        // Older reports have no report id: the link degrades to "report ↗" and still opens the URL.
        var sf = Model().Rows.Single(r => r.Id == "sf750");
        Assert.Equal("report ↗", sf.LinkText);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/sf750/", sf.Url);
    }

    [Fact]
    public void SearchMatchesTheRevisionShownInTheTitle()
    {
        var m = Model();
        m.SetSearchText("rm850x (2021)");
        Assert.Equal(["Corsair RM850x (2021)"], m.Rows.Select(r => r.Title));

        m.SetSearchText("2018");
        Assert.Equal(["Corsair RM850x (2018)"], m.Rows.Select(r => r.Title));
    }

    [Fact]
    public void RowsShowTheEtaGradeOfTheVoltageInUse()
    {
        var m = Model();
        IReadOnlyList<string> Rm850() => m.Rows.Single(r => r.Id == "rm850").EtaLines;
        Assert.Equal(["Cybenetics ETA Gold (230 V)", "Cybenetics ETA Platinum (115 V)"], Rm850());

        m.MeasuredVolts = 118.2;
        Assert.Equal(["Cybenetics ETA Platinum (115 V)"], Rm850());
        m.MeasuredVolts = 226.9;
        Assert.Equal(["Cybenetics ETA Gold (230 V)"], Rm850());
        m.VoltageMode = InputVoltageMode.V115;
        Assert.Equal(["Cybenetics ETA Platinum (115 V)"], Rm850());
        Assert.Equal("80 Plus Gold", m.Rows.Single(r => r.Id == "rm850").EightyPlus);

        // A unit without a published ETA rating shows none; never a grade borrowed from 80 Plus.
        Assert.Empty(m.Rows.Single(r => r.Id == "rm850old").EtaLines);

        // A unit graded only at 230 V shows nothing at 115 V when it has a 115 V curve.
        Assert.Empty(m.Rows.Single(r => r.Id == "px750").EtaLines);
    }

    /// <summary>Entries for the three-column tests: every combination of 80 Plus, ETA grades and report source.</summary>
    private static PsuStepModel ColumnModel()
    {
        var curve = Tests.TestSupport.TestCurves.Tx700.Curves;
        PsuEntry Entry(string id, string rating, Dictionary<string, string>? eta, string? report = "21PS1872A", string? date = "2021-07-16", string url = "https://www.cybenetics.com/evaluations/psus/1872/") => new()
        {
            Id = id,
            Brand = "Testbrand",
            Series = "Series",
            Model = "Model " + id,
            Wattage = 750,
            Rating80Plus = rating,
            RatingsCybenetics = eta,
            Source = new PsuSource { Name = "Cybenetics", ReportId = report, Url = url, ReportDate = date },
            Curves = curve,
        };

        return new PsuStepModel(
            new PsuDatabase
            {
                SchemaVersion = 1,
                Psus =
                [
                    Entry("both", "Gold", new() { ["230"] = "Gold", ["115"] = "Platinum" }),
                    Entry("noplus", "None", new() { ["230"] = "Silver" }),
                    Entry("noeta", "Bronze", null),
                    Entry("noid", "Platinum", new() { ["230"] = "Platinum" }, report: null),
                    Entry("nosource", "Gold", new() { ["230"] = "Gold" }, report: null, date: null, url: ""),
                ],
            },
            EnUs);
    }

    [Fact]
    public void ColumnsShowThe80PlusRatingAndTheEtaGradeAtAKnownVoltage()
    {
        var m = ColumnModel();
        m.MeasuredVolts = 226.9;
        var row = m.Rows.Single(r => r.Id == "both");
        Assert.Equal("Testbrand Model both", row.Title);
        Assert.Equal("80 Plus Gold", row.EightyPlus);
        Assert.Equal(["Cybenetics ETA Gold (230 V)"], row.EtaLines);
        Assert.Equal("report 21PS1872A · 2021-07-16 ↗", row.LinkText);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/1872/", row.Url);

        m.VoltageMode = InputVoltageMode.V115;
        Assert.Equal(["Cybenetics ETA Platinum (115 V)"], m.Rows.Single(r => r.Id == "both").EtaLines);
    }

    [Fact]
    public void ColumnsShowBothEtaGradesWhileTheVoltageIsUnknown()
    {
        var row = ColumnModel().Rows.Single(r => r.Id == "both");
        Assert.Equal(["Cybenetics ETA Gold (230 V)", "Cybenetics ETA Platinum (115 V)"], row.EtaLines);
        Assert.Equal("report 21PS1872A · 2021-07-16 ↗", row.LinkText);
    }

    [Fact]
    public void ColumnsLeaveThe80PlusColumnEmptyWithoutARating()
    {
        var row = ColumnModel().Rows.Single(r => r.Id == "noplus");
        Assert.Null(row.EightyPlus);
        Assert.Equal(["Cybenetics ETA Silver (230 V)"], row.EtaLines);
    }

    [Fact]
    public void ColumnsShowOnlyTheReportLinkWithoutAnEtaGrade()
    {
        var row = ColumnModel().Rows.Single(r => r.Id == "noeta");
        Assert.Equal("80 Plus Bronze", row.EightyPlus);
        Assert.Empty(row.EtaLines);
        Assert.Equal("report 21PS1872A · 2021-07-16 ↗", row.LinkText);
    }

    [Fact]
    public void ColumnsShowTheReportDateWithoutAReportId()
    {
        var m = ColumnModel();
        var row = m.Rows.Single(r => r.Id == "noid");
        Assert.Equal("report 2021-07-16 ↗", row.LinkText);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/1872/", row.Url);

        // Without a URL there is nothing to open: no link at all.
        var none = m.Rows.Single(r => r.Id == "nosource");
        Assert.Null(none.LinkText);
        Assert.Null(none.Url);
        Assert.Equal(["Cybenetics ETA Gold (230 V)"], none.EtaLines);
    }

    [Fact]
    public void ListTextsAreTheHeadersAndTheCredit()
    {
        Assert.Equal(["Model", "80 Plus", "Cybenetics ETA"], PsuStepModel.ColumnHeaders);
        Assert.Equal("Test results provided by Cybenetics.", PsuStepModel.AttributionLine);
    }

    [Fact]
    public void DatabaseModeIsValidOnlyWithASelection()
    {
        var m = Model();
        Assert.False(m.IsValid);
        Assert.Equal("Select your PSU in the list.", m.ValidationMessage);

        m.Select("tx750");
        Assert.True(m.IsValid);
        Assert.Equal(5, m.SelectedRowIndex);

        m.SetBrand("Seasonic");
        Assert.Equal(2, m.SelectedRowIndex);
        m.SetBrand("Corsair");
        Assert.Equal(-1, m.SelectedRowIndex);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void GenericPreviewShowsThe80PlusRequirementsForTheVoltage()
    {
        var m = Model();
        m.Mode = PsuSelectionMode.Generic;
        Assert.False(m.IsValid);
        Assert.Equal(["—", "—", "—"], m.GenericPreview.Select(t => t.Value));

        m.SetGenericWattage("850");
        m.SetGenericRating(EightyPlusRating.Gold);
        Assert.True(m.IsValid);
        Assert.Equal(["20 % load", "50 % load", "100 % load"], m.GenericPreview.Select(t => t.Caption));
        Assert.Equal(["90 %", "92 %", "89 %"], m.GenericPreview.Select(t => t.Value));

        m.MeasuredVolts = 118.5;
        Assert.Equal(["87 %", "90 %", "87 %"], m.GenericPreview.Select(t => t.Value));

        var s = new AppSettings();
        m.ApplyTo(s);
        Assert.Equal(PsuSelectionMode.Generic, s.Psu.Mode);
        Assert.Equal(850, s.Psu.GenericWattage);
        Assert.Equal(EightyPlusRating.Gold, s.Psu.GenericRating);
    }

    [Fact]
    public void GenericWattageMustBeANumber()
    {
        var m = Model();
        m.Mode = PsuSelectionMode.Generic;
        m.SetGenericRating(EightyPlusRating.Bronze);
        m.SetGenericWattage("lots");
        Assert.Equal("Enter the rated wattage in W, e.g. 850.", m.GenericWattageError);
        Assert.False(m.IsValid);
        m.SetGenericWattage("650 W");
        Assert.Null(m.GenericWattageError);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void ManualGridRowsAreParsedAndValidated()
    {
        var m = Model();
        m.Mode = PsuSelectionMode.Manual;
        m.SetManualRows([("100", "80"), ("", ""), ("abc", "1"), ("90", "95")]);

        Assert.Equal(2, m.ManualPoints.Count);
        Assert.Equal(
            [
                "Grid row 3: enter numbers for AC W and DC W.",
                "Row 2: DC must be less than AC.",
                "Row 2: AC must be greater than the previous row's AC.",
            ],
            m.ManualErrors);
        Assert.False(m.IsValid);

        m.SetManualRows([("100", "80"), ("200.5", "180")]);
        Assert.Empty(m.ManualErrors);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void ManualGridAcceptsTheCurrentCultureDecimalComma()
    {
        var m = new PsuStepModel(TestDatabase.Create(), CultureInfo.GetCultureInfo("da-DK"));
        m.SetManualRows([("100,5", "80,25"), ("200", "180")]);
        Assert.Equal(new CurvePoint(100.5, 80.25), m.ManualPoints[0]);
        Assert.Equal("100,5", m.FormatGridValue(100.5));
    }

    [Fact]
    public void CsvImportExportRoundTripsAndReportsErrors()
    {
        var m = Model();
        m.Mode = PsuSelectionMode.Manual;
        Assert.True(m.ImportCsv("ac_w,dc_w\r\n100,80\r\n200,180\r\n"));
        Assert.Equal(ManualCurveEntry.PointGrid, m.ManualEntry);
        Assert.Equal("ac_w,dc_w\r\n100,80\r\n200,180\r\n", m.ExportCsv());

        Assert.False(m.ImportCsv("ac_w,dc_w\n100;80\n"));
        Assert.Equal(["Line 2: expected 2 fields but found 1."], m.ManualErrors);
        Assert.Equal(2, m.ManualPoints.Count);
    }

    [Fact]
    public void EfficiencyFormGeneratesTheGrid()
    {
        var m = Model();
        m.Mode = PsuSelectionMode.Manual;
        m.ManualEntry = ManualCurveEntry.EfficiencyForm;

        Assert.False(m.GenerateFromEfficiencies("750", "80", "90", "120", "88"));
        Assert.Equal("Enter the efficiency at 50 % load as a percentage between 0 and 100.", m.FormError);
        Assert.False(m.GenerateFromEfficiencies("", "80", "90", "92", "88"));
        Assert.Equal("Enter the rated wattage in W, e.g. 750.", m.FormError);

        Assert.True(m.GenerateFromEfficiencies("750", "80", "90", "92", "88"));
        Assert.Null(m.FormError);
        Assert.Equal(ManualCurveEntry.PointGrid, m.ManualEntry);
        Assert.Equal(4, m.ManualPoints.Count);
        Assert.Equal(75, m.ManualPoints[0].DcWatts, 6);
        Assert.Equal(93.75, m.ManualPoints[0].AcWatts, 6);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void VoltageTextShowsTheCurveNoteForASingleVoltageUnit()
    {
        var m = new PsuStepModel(Tests.Presentation.BriefExample.SingleVoltageDatabase(), EnUs);
        m.Select(Tests.Presentation.BriefExample.SingleVoltageId);
        Assert.True(m.IsValid);

        m.MeasuredVolts = 226.9;
        Assert.Equal("Input voltage: 230 V curve (plug measured 226.9 V)", m.VoltageText);
        m.MeasuredVolts = 118.2;
        Assert.Equal("Input voltage: 230 V curve (115 V data not available for this unit) · plug measured 118.2 V", m.VoltageText);
        m.VoltageMode = InputVoltageMode.V115;
        Assert.Equal("Input voltage: 230 V curve (115 V data not available for this unit) · set manually; plug measured 118.2 V", m.VoltageText);
        m.MeasuredVolts = null;
        Assert.Equal("Input voltage: 230 V curve (115 V data not available for this unit) · set manually", m.VoltageText);
        m.VoltageMode = InputVoltageMode.Auto;
        Assert.Equal("Input voltage: 230 V curve (picked automatically from the plug's voltage)", m.VoltageText);

        // A unit with both curves never shows the note.
        m.Select("seasonic-prime-fanless-tx-700");
        m.MeasuredVolts = 118.2;
        Assert.Equal("Input voltage: 115 V curve (plug measured 118.2 V)", m.VoltageText);
    }

    [Fact]
    public void VoltageTextFollowsModeAndMeasurement()
    {
        var m = Model();
        Assert.Equal("Input voltage: 230 V curve (picked automatically from the plug's voltage)", m.VoltageText);
        m.MeasuredVolts = 226.9;
        Assert.Equal("Input voltage: 230 V curve (plug measured 226.9 V)", m.VoltageText);
        m.MeasuredVolts = 119.2;
        Assert.Equal("Input voltage: 115 V curve (plug measured 119.2 V)", m.VoltageText);
        m.VoltageMode = InputVoltageMode.V230;
        Assert.Equal("Input voltage: 230 V curve (set manually; plug measured 119.2 V)", m.VoltageText);
        m.MeasuredVolts = null;
        Assert.Equal("Input voltage: 230 V curve (set manually)", m.VoltageText);

        var s = new AppSettings();
        m.ApplyTo(s);
        Assert.Equal(InputVoltageMode.V230, s.VoltageMode);
    }

    [Fact]
    public void LoadFromKeepsEveryModesData()
    {
        var settings = new AppSettings
        {
            VoltageMode = InputVoltageMode.V115,
            Psu = new PsuSettings
            {
                Mode = PsuSelectionMode.Manual,
                DatabaseId = "rm850",
                GenericWattage = 650,
                GenericRating = EightyPlusRating.Silver,
                ManualCurve = [.. Points.Valid],
            },
        };
        var m = Model();
        m.LoadFrom(settings);

        Assert.Equal(PsuSelectionMode.Manual, m.Mode);
        Assert.Equal("Corsair", m.Brand);
        Assert.Equal("rm850", m.SelectedId);
        Assert.Equal("650", m.GenericWattageText);
        Assert.Equal(3, m.ManualPoints.Count);
        Assert.Equal(InputVoltageMode.V115, m.VoltageMode);

        var copy = new AppSettings();
        m.ApplyTo(copy);
        Assert.Equal("rm850", copy.Psu.DatabaseId);
        Assert.Equal(650, copy.Psu.GenericWattage);
        Assert.Equal(3, copy.Psu.ManualCurve!.Count);
    }
}
