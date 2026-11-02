using System.Globalization;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class SettingsPresenterTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private readonly FakeRuntime _runtime = new();

    [Fact]
    public void StateShowsTheBriefExample()
    {
        _runtime.Database = SyntheticDatabase("2026-10-29", 1);
        var state = new SettingsPresenter(_runtime, EnUs).State;

        Assert.Equal("192.168.1.42", state.Host);
        Assert.Equal("Seasonic Prime Fanless TX-700", state.PsuTitle);
        Assert.Equal("700 W · 80 Plus Titanium · Cybenetics ETA Titanium (230 V)", state.PsuSubtitle);
        Assert.Equal("report 21PS1872A · 2021-07-16 ↗", state.PsuLinkText);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/1872/", state.PsuReportUrl);
        Assert.Equal("PC Power (DC)\r\n − CPU Package Power\r\n − GPU Power\r\n − DIMM1 Power\r\n − DIMM3 Power", state.FormulaText);
        Assert.Null(state.RetiredFormulaNote);
        Assert.Equal("PC · 192.168.1.42", state.DevicesText);
        Assert.Equal("2 s", state.DisplayIntervalText);
        Assert.Equal(2000, state.DisplayIntervalMs);
        Assert.Equal("1 sample", state.SmoothingText);
        Assert.Equal("Last updated 2026-10-29 · 1 entry · shipped", state.DatabaseText);
        Assert.Equal("leave empty if not set", state.PasswordPlaceholder);
        Assert.True(state.ShowTrayIcon);
        Assert.True(state.EnergyEnabled);
        Assert.Equal(string.Empty, state.PriceText);
    }

    [Fact]
    public void PsuSubtitleShowsTheEtaGradeOfTheVoltageInUseAndTheReportDate()
    {
        var db = SyntheticDatabase("2026-11-02", 1);
        var entry = db.Psus[0];
        entry.RatingsCybenetics = new() { ["230"] = "Gold", ["115"] = "Platinum" };
        _runtime.Database = db;
        var presenter = new SettingsPresenter(_runtime, EnUs);
        Assert.Equal("700 W · 80 Plus Titanium · Cybenetics ETA Gold (230 V)", presenter.State.PsuSubtitle);
        Assert.Equal("report 21PS1872A · 2021-07-16 ↗", presenter.State.PsuLinkText);

        presenter.SetVoltageMode(InputVoltageMode.V115);
        Assert.Equal("700 W · 80 Plus Titanium · Cybenetics ETA Platinum (115 V)", presenter.State.PsuSubtitle);

        // No 80 Plus rating: left out, never "80 Plus None".
        entry.Rating80Plus = "None";
        Assert.Equal("700 W · Cybenetics ETA Platinum (115 V)", presenter.State.PsuSubtitle);

        // No ETA grade (at this voltage or at all): the source is named; the report stays the link on line 3.
        entry.Rating80Plus = "Gold";
        entry.RatingsCybenetics = new() { ["230"] = "Gold" };
        Assert.Equal("700 W · 80 Plus Gold · Cybenetics", presenter.State.PsuSubtitle);
        entry.RatingsCybenetics = null;
        entry.Source!.ReportDate = null;
        Assert.Equal("700 W · 80 Plus Gold · Cybenetics", presenter.State.PsuSubtitle);
        Assert.Equal("report 21PS1872A ↗", presenter.State.PsuLinkText);

        // A source without a URL has no link: its report id and date stay on line 2.
        entry.Source.Url = string.Empty;
        entry.Source.ReportDate = "2021-07-16";
        Assert.Equal("700 W · 80 Plus Gold · Cybenetics 21PS1872A · report 2021-07-16", presenter.State.PsuSubtitle);
        Assert.Null(presenter.State.PsuLinkText);
        Assert.Null(presenter.State.PsuReportUrl);
    }

    [Fact]
    public void PsuHasNoReportLinkOutsideDatabaseMode()
    {
        _runtime.Settings!.Psu = new Core.Settings.PsuSettings { Mode = Core.Settings.PsuSelectionMode.Generic, GenericWattage = 850, GenericRating = EightyPlusRating.Gold };
        var state = new SettingsPresenter(_runtime, EnUs).State;
        Assert.Equal("Approximate curve from the 80 Plus rating", state.PsuSubtitle);
        Assert.Null(state.PsuLinkText);
        Assert.Null(state.PsuReportUrl);
    }

    [Fact]
    public void PsuCardNamesTheRevision()
    {
        _runtime.Settings!.Psu = new Core.Settings.PsuSettings { Mode = Core.Settings.PsuSelectionMode.Database, DatabaseId = "cooler-master-mwe-gold-1050-v2-atx-v3-0-2023" };
        Assert.Equal("Cooler Master MWE Gold 1050 V2 (ATX v3.0, 2023)", new SettingsPresenter(_runtime, EnUs).State.PsuTitle);
    }

    [Fact]
    public void SingleVoltagePsuShowsTheCurveNoteWhenTheOtherVoltageIsSelected()
    {
        _runtime.Database = BriefExample.SingleVoltageDatabase();
        _runtime.Settings = BriefExample.SingleVoltageSettings();
        var presenter = new SettingsPresenter(_runtime, EnUs);

        // The plug measures 226.9 V: the 230 V curve exists, nothing to say.
        Assert.Null(presenter.State.CurveNote);

        presenter.SetVoltageMode(InputVoltageMode.V115);
        Assert.Equal("230 V curve (115 V data not available for this unit)", presenter.State.CurveNote);

        presenter.SetVoltageMode(InputVoltageMode.Auto);
        _runtime.LastReading = new(new Core.Shelly.ShellyReading(312.4, 118.2, 2.6, 1), new PowerReading(312.4, 312.4, 294.8, 17.6, InputVoltage.V230));
        Assert.Equal("230 V curve (115 V data not available for this unit)", presenter.State.CurveNote);

        _runtime.Settings = BriefExample.Settings();
        Assert.Null(presenter.State.CurveNote);
    }

    [Fact]
    public void DatabaseTextUsesThePluralForSeveralEntries()
    {
        _runtime.Database = SyntheticDatabase("2026-10-31", 6);
        Assert.Equal("Last updated 2026-10-31 · 6 entries · shipped", new SettingsPresenter(_runtime, EnUs).State.DatabaseText);
    }

    [Theory]
    [InlineData(DatabaseOrigin.Downloaded, "Last updated 2026-11-15 · 6 entries · downloaded")]
    [InlineData(DatabaseOrigin.Shipped, "Last updated 2026-11-15 · 6 entries · shipped")]
    [InlineData(DatabaseOrigin.Embedded, "Last updated 2026-11-15 · 6 entries · built-in")]
    public void DatabaseTextNamesTheCopyInUse(DatabaseOrigin origin, string expected)
    {
        _runtime.Database = SyntheticDatabase("2026-11-15", 6);
        _runtime.DatabaseOrigin = origin;
        Assert.Equal(expected, new SettingsPresenter(_runtime, EnUs).State.DatabaseText);
    }

    /// <summary>The TX-700 entry first, then copies with other ids, so the count is exact whatever the shipped database holds.</summary>
    private static PsuDatabase SyntheticDatabase(string updated, int count)
    {
        var db = new PsuDatabase { SchemaVersion = 1, Updated = updated };
        for (var i = 0; i < count; i++)
        {
            var entry = TestCurves.Tx700;
            if (i > 0)
            {
                entry.Id += "-copy" + i.ToString(CultureInfo.InvariantCulture);
            }

            db.Psus.Add(entry);
        }

        return db;
    }

    [Fact]
    public void SettersCloneAndApply()
    {
        var presenter = new SettingsPresenter(_runtime, EnUs);
        var original = _runtime.Settings!;
        var changes = 0;
        presenter.Changed += (_, _) => changes++;

        presenter.SetVoltageMode(InputVoltageMode.V115);
        presenter.SetShowTrayIcon(false);
        presenter.SetStartWithWindows(true);
        presenter.SetSmoothing(99);
        presenter.SetEnergyEnabled(false);
        presenter.SetCurrency(" DKK ");

        Assert.Equal(6, _runtime.Applied.Count);
        Assert.Equal(6, changes);
        Assert.Equal(InputVoltageMode.Auto, original.VoltageMode);
        var s = _runtime.Settings!;
        Assert.Equal(InputVoltageMode.V115, s.VoltageMode);
        Assert.True(s.HideTrayIcon);
        Assert.True(s.StartWithWindows);
        Assert.Equal(MovingAverage.MaxSamples, s.SmoothingSamples);
        Assert.False(s.EnergyLoggingEnabled);
        Assert.Equal("DKK", s.EnergyCurrency);
        Assert.All(_runtime.Applied, a => Assert.Null(a.Password));
    }

    [Theory]
    [InlineData(1234, 1000)]
    [InlineData(1250, 1500)]
    [InlineData(2600, 2500)]
    [InlineData(100, 1000)]
    [InlineData(9000, 5000)]
    public void DisplayIntervalSnapsAndClamps(int input, int expected)
    {
        var presenter = new SettingsPresenter(_runtime, EnUs);
        presenter.SetDisplayIntervalMs(input);
        Assert.Equal(expected, _runtime.Settings!.DisplayIntervalMs);
        Assert.Equal(expected, presenter.State.DisplayIntervalMs);
        Assert.Null(_runtime.Settings.PollIntervalMs);
    }

    [Theory]
    [InlineData(1000, "en-US", "1 s")]
    [InlineData(1500, "en-US", "1.5 s")]
    [InlineData(2000, "en-US", "2 s")]
    [InlineData(5000, "en-US", "5 s")]
    [InlineData(2500, "da-DK", "2,5 s")]
    public void DisplayIntervalTextIsInSeconds(int milliseconds, string culture, string expected) =>
        Assert.Equal(expected, SettingsPresenter.FormatDisplayInterval(milliseconds, CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void DisplayCardTexts()
    {
        Assert.Equal("Display", SettingsPresenter.DisplayCardTitle);
        Assert.Equal("Display interval", SettingsPresenter.DisplayIntervalLabel);
        Assert.Equal("Smoothing", SettingsPresenter.SmoothingLabel);
        Assert.Contains("median of the last " + Core.App.OtherComponentsCalculator.WindowSeconds + " seconds", MainWindowPresenter.OtherValueTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidHostIsReportedAndNotApplied()
    {
        var presenter = new SettingsPresenter(_runtime, EnUs);

        presenter.SetHost("http://bad host");
        Assert.Empty(_runtime.Applied);
        Assert.Equal("Enter a valid IP address or host name.", presenter.State.HostError);
        Assert.Equal("http://bad host", presenter.State.Host);

        presenter.SetHost(" 192.168.1.43 ");
        Assert.Null(presenter.State.HostError);
        Assert.Equal("192.168.1.43", _runtime.Settings!.Device.Host);
    }

    [Fact]
    public void PasswordStaysUnchangedUnlessTypedOrCleared()
    {
        _runtime.Settings!.Device.ProtectedPassword = "enc:secret";
        _runtime.Password = "secret";
        var presenter = new SettingsPresenter(_runtime, EnUs);
        Assert.Equal("unchanged", presenter.State.PasswordPlaceholder);

        presenter.SetPassword(string.Empty);
        presenter.SetPassword(null);
        Assert.Empty(_runtime.Applied);

        presenter.SetVoltageMode(InputVoltageMode.V230);
        Assert.Null(_runtime.Applied[^1].Password);
        Assert.Equal("secret", _runtime.Password);

        presenter.SetPassword("new");
        Assert.Equal("new", _runtime.Applied[^1].Password);

        presenter.ClearPassword();
        Assert.Equal(string.Empty, _runtime.Applied[^1].Password);
        Assert.Equal("leave empty if not set", presenter.State.PasswordPlaceholder);
    }

    [Theory]
    [InlineData("da-DK", "0,32", 0.32)]
    [InlineData("da-DK", "0.32", 0.32)]
    [InlineData("en-US", "0.32", 0.32)]
    [InlineData("en-US", "2", 2)]
    [InlineData("en-US", "5000", 1000)]
    public void PriceParsesCurrentCultureThenInvariantAndClamps(string culture, string text, double expected)
    {
        new SettingsPresenter(_runtime, CultureInfo.GetCultureInfo(culture)).SetPrice(text);
        Assert.Equal((decimal)expected, _runtime.Settings!.EnergyPricePerKwh);
    }

    [Fact]
    public void InvalidPriceIsReportedAndEmptyClears()
    {
        var presenter = new SettingsPresenter(_runtime, EnUs);
        presenter.SetPrice("0.32");
        Assert.Equal("0.32", presenter.State.PriceText);

        presenter.SetPrice("abc");
        Assert.Equal("Enter a price such as 0.32.", presenter.State.PriceError);
        Assert.Equal("abc", presenter.State.PriceText);
        Assert.Equal(0.32m, _runtime.Settings!.EnergyPricePerKwh);

        presenter.SetPrice("-1");
        Assert.NotNull(presenter.State.PriceError);

        presenter.SetPrice(" ");
        Assert.Null(presenter.State.PriceError);
        Assert.Null(_runtime.Settings!.EnergyPricePerKwh);
    }

    [Fact]
    public async Task TestConnectionUsesStoredPasswordWhenFieldIsEmpty()
    {
        _runtime.Password = "secret";
        var presenter = new SettingsPresenter(_runtime, EnUs);

        await presenter.TestConnectionAsync("192.168.1.42", string.Empty);
        Assert.Equal(("192.168.1.42", "secret"), _runtime.LastTest);
        Assert.Equal(new ResultLine(Tone.Success, "Connected to PlugMGen3-2: 312.4 W · 226.9 V · 1.38 A"), presenter.State.TestResult);
        Assert.False(presenter.State.IsBusy);

        _runtime.TestResult = new(false, null, null, "The device rejected the password.");
        await presenter.TestConnectionAsync("192.168.1.42", "typed");
        Assert.Equal(("192.168.1.42", "typed"), _runtime.LastTest);
        Assert.Equal(new ResultLine(Tone.Error, "The device rejected the password."), presenter.State.TestResult);
    }

    [Fact]
    public async Task DatabaseCheckAndRemoveSensors()
    {
        var presenter = new SettingsPresenter(_runtime, EnUs);

        await presenter.CheckDatabaseUpdateAsync();
        Assert.Equal(new ResultLine(Tone.Secondary, "The database is already up to date (2026-10-29)."), presenter.State.DatabaseResult);

        presenter.RemoveSensors();
        Assert.Equal(1, _runtime.RemoveCalls);
    }

    [Fact]
    public void DiagnosticTraceLinkAndCaptionFollowTheTraceState()
    {
        var presenter = new SettingsPresenter(_runtime, EnUs);
        var changes = 0;
        presenter.Changed += (_, _) => changes++;

        var idle = presenter.State;
        Assert.False(idle.DiagnosticTraceRunning);
        Assert.Equal("Start diagnostic trace", idle.DiagnosticTraceText);
        Assert.Null(idle.DiagnosticTraceCaption);

        presenter.ToggleDiagnosticTrace();
        var running = presenter.State;
        Assert.True(running.DiagnosticTraceRunning);
        Assert.Equal("Stop diagnostic trace", running.DiagnosticTraceText);
        Assert.Equal("Recording to trace-20261101-143005.csv", running.DiagnosticTraceCaption);

        presenter.ToggleDiagnosticTrace();
        Assert.Null(_runtime.DiagnosticTraceFile);
        Assert.Equal("Start diagnostic trace", presenter.State.DiagnosticTraceText);
        Assert.Null(presenter.State.DiagnosticTraceCaption);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void FailedDiagnosticTraceStartShowsTheErrorCaption()
    {
        _runtime.TraceStartFails = true;
        var presenter = new SettingsPresenter(_runtime, EnUs);

        presenter.ToggleDiagnosticTrace();

        Assert.False(presenter.State.DiagnosticTraceRunning);
        Assert.Equal("Start diagnostic trace", presenter.State.DiagnosticTraceText);
        Assert.Equal(SettingsPresenter.TraceStartFailedText, presenter.State.DiagnosticTraceCaption);
    }

    [Fact]
    public void DiagnosticTraceCaptionShowsTheFileNameOnly()
    {
        Assert.Equal("Recording to trace-20261101-090000.csv", SettingsPresenter.TraceCaption(@"C:\Users\x\AppData\Roaming\ShellyHwinfoBridge\energy\diagnostics\trace-20261101-090000.csv"));
        Assert.Equal("Stop diagnostic trace", SettingsPresenter.TraceLinkText(true));
        Assert.Equal("Start diagnostic trace", SettingsPresenter.TraceLinkText(false));
    }
}
