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
        Assert.Equal("700 W · Titanium · Cybenetics 21PS1872A", state.PsuSubtitle);
        Assert.Equal("\"DC Power (Est.)\"\r\n - \"CPU Package Power\"\r\n - \"GPU Power\"\r\n - \"DIMM1 Power\"\r\n - \"DIMM3 Power\"", state.FormulaText);
        Assert.Equal("1000 ms", state.IntervalText);
        Assert.Equal("1 sample", state.SmoothingText);
        Assert.Equal("Last updated 2026-10-29 · 1 entry", state.DatabaseText);
        Assert.Equal("leave empty if not set", state.PasswordPlaceholder);
        Assert.True(state.ShowTrayIcon);
        Assert.True(state.EnergyEnabled);
        Assert.Equal(string.Empty, state.PriceText);
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
        Assert.Equal("Last updated 2026-10-31 · 6 entries", new SettingsPresenter(_runtime, EnUs).State.DatabaseText);
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
    [InlineData(1234, 1200)]
    [InlineData(1250, 1300)]
    [InlineData(100, 500)]
    [InlineData(9000, 5000)]
    public void IntervalSnapsAndClamps(int input, int expected)
    {
        new SettingsPresenter(_runtime, EnUs).SetIntervalMs(input);
        Assert.Equal(expected, _runtime.Settings!.PollIntervalMs);
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
}
