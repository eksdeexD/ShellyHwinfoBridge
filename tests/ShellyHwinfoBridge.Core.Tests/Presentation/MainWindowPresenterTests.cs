using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class MainWindowPresenterTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly EnergyTotals Totals = new(2.41, 48.7, 2.2, 45.1);

    private static MainViewState Build(
        AppSettings? settings = null,
        RuntimeState state = RuntimeState.Running,
        OnlineState online = OnlineState.Online,
        HwinfoMonitorSnapshot? hw = null,
        CultureInfo? culture = null) =>
        MainWindowPresenter.Build(BriefExample.Reading(), state, online, hw ?? BriefExample.Hwinfo(), Totals, settings ?? BriefExample.Settings(), BriefExample.Database(), culture ?? EnUs);

    [Fact]
    public void BriefExampleRecordsGiveTheImageStrings()
    {
        var view = Build();

        Assert.True(view.IsConfigured);
        Assert.Equal(new StatusPillState(Tone.Success, "Online · PlugMGen3-2 · 226.9 V"), view.Status);
        Assert.Equal(new StatCardState("AC Power (Wall)", "312.4 W", "from Shelly plug", Tone.Ink, false), view.Ac);
        Assert.Equal(new StatCardState("DC Power (Est.)", "294.8 W", "via TX-700 curve", Tone.Accent, false), view.Dc);
        Assert.Equal(new StatCardState("PSU Loss", "17.6 W", "94.4 % efficiency", Tone.Ink, false), view.Loss);
        Assert.Equal(new StatCardState("Other Components", "48.1 W", "DC minus CPU, GPU, RAM", Tone.Ink, false), view.Other);
        Assert.Equal("Seasonic Prime Fanless TX-700 · 230 V curve · Cybenetics", view.PsuLine);
        Assert.Equal("CPU Package Power, GPU Power, DIMM1 Power, DIMM3 Power", view.SensorsLine);
        Assert.Equal("running · sensors visible", view.HwinfoLine);
        Assert.Equal(new EnergyTotalsState("2.41 kWh", "TODAY", "48.7 kWh", "THIS MONTH"), view.Energy);
        Assert.Equal("Pause polling", view.PauseButtonText);
        Assert.True(view.PauseEnabled);
    }

    [Fact]
    public void PriceAddsCostToTheCaptions()
    {
        var settings = BriefExample.Settings();
        settings.EnergyPricePerKwh = 0.32m;
        settings.EnergyCurrency = "DKK";

        Assert.Equal(new EnergyTotalsState("2.41 kWh", "TODAY · 0.77 DKK", "48.7 kWh", "THIS MONTH · 15.58 DKK"), Build(settings).Energy);

        settings.EnergyCurrency = null;
        Assert.Equal("TODAY · 0.77", Build(settings).Energy.TodayCaption);
    }

    [Fact]
    public void UsesCurrentCultureForNumbers()
    {
        var view = Build(culture: CultureInfo.GetCultureInfo("da-DK"));

        Assert.Equal("312,4 W", view.Ac.Value);
        Assert.Equal("94,4 % efficiency", view.Loss.Sub);
        Assert.Equal("Online · PlugMGen3-2 · 226,9 V", view.Status.Text);
        Assert.Equal("2,41 kWh", view.Energy.TodayValue);
    }

    [Fact]
    public void OtherCardPlaceholders()
    {
        var off = Build(hw: BriefExample.Hwinfo() with { SharedMemoryAvailable = false, SensorsVisible = false, OtherWatts = null });
        Assert.Equal(new StatCardState("Other Components", "Only available with Shared Memory enabled in HWiNFO", string.Empty, Tone.Ink, true), off.Other);
        Assert.Equal("running · shared memory off", off.HwinfoLine);

        var notVisible = Build(hw: BriefExample.Hwinfo() with { SensorsVisible = false, OtherWatts = null });
        Assert.Equal("Restart HWiNFO to see this", notVisible.Other.Value);
        Assert.True(notVisible.Other.Placeholder);
        Assert.Equal("running · sensors not yet visible: restart HWiNFO", notVisible.HwinfoLine);
    }

    [Fact]
    public void HwinfoLineVariants()
    {
        Assert.Equal("not running", MainWindowPresenter.HwinfoLine(new HwinfoMonitorSnapshot(true, false, false, false, null, null)));
        Assert.Equal("not installed", MainWindowPresenter.HwinfoLine(HwinfoMonitorSnapshot.Unknown));
    }

    [Fact]
    public void OfflinePausedAndUnconfigured()
    {
        var offline = Build(online: OnlineState.Offline);
        Assert.Equal(new StatusPillState(Tone.Error, "Offline · PlugMGen3-2"), offline.Status);
        Assert.Equal("—", offline.Ac.Value);
        Assert.Equal("—", offline.Other.Value);
        Assert.Equal("— % efficiency", offline.Loss.Sub);

        var paused = Build(state: RuntimeState.Paused, online: OnlineState.Unknown);
        Assert.Equal(new StatusPillState(Tone.Secondary, "Paused · PlugMGen3-2"), paused.Status);
        Assert.Equal("Resume polling", paused.PauseButtonText);

        var connecting = Build(online: OnlineState.Unknown);
        Assert.Equal("Connecting · PlugMGen3-2", connecting.Status.Text);

        var unconfigured = MainWindowPresenter.Build(null, RuntimeState.Unconfigured, OnlineState.Unknown, HwinfoMonitorSnapshot.Unknown, EnergyTotals.Zero, null, BriefExample.Database(), EnUs);
        Assert.False(unconfigured.IsConfigured);
        Assert.Equal("Not set up", unconfigured.Status.Text);
        Assert.False(unconfigured.PauseEnabled);

        var noName = BriefExample.Settings();
        noName.Device.Name = null;
        Assert.Equal("Offline · 192.168.1.42", Build(noName, online: OnlineState.Offline).Status.Text);
    }

    [Fact]
    public void SensorsLineShortensLongLists()
    {
        var settings = BriefExample.Settings();
        settings.Sensors.Add(new SensorSelection { Name = "Capture Card Power", Category = SensorCategory.OtherMeasured });
        Assert.Equal("CPU Package Power, GPU Power, DIMM1 Power and 2 more", MainWindowPresenter.SensorsLine(settings));
        Assert.Equal("DC minus CPU, GPU, RAM, other", Build(settings).Other.Sub);

        settings.Sensors.Clear();
        Assert.Equal("none selected", MainWindowPresenter.SensorsLine(settings));

        settings.CustomFormula = "\"DC Power (Est.)\" - 50";
        Assert.Equal("custom formula", MainWindowPresenter.SensorsLine(settings));
        Assert.Equal("custom formula", Build(settings).Other.Sub);
    }

    [Fact]
    public void GenericAndManualPsuLines()
    {
        var settings = BriefExample.Settings();
        settings.Psu = new PsuSettings { Mode = PsuSelectionMode.Generic, GenericWattage = 850, GenericRating = EightyPlusRating.Gold };
        Assert.Equal("Generic Gold 850 W (approximate) · 230 V curve", MainWindowPresenter.PsuLine(settings, null, null, EnUs));
        Assert.Equal("via generic curve", Build(settings).Dc.Sub);

        settings.Psu = new PsuSettings { Mode = PsuSelectionMode.Manual, ManualCurve = Enumerable.Range(1, 12).Select(i => new CurvePoint(i * 60, i * 55)).ToList() };
        settings.VoltageMode = InputVoltageMode.V115;
        Assert.Equal("Manual curve (12 points) · 115 V curve", MainWindowPresenter.PsuLine(settings, null, null, EnUs));
    }
}
