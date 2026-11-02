using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

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
        CultureInfo? culture = null,
        OtherComponentsState? other = null,
        double? total = null) =>
        MainWindowPresenter.Build(BriefExample.Reading(), state, online, hw ?? BriefExample.Hwinfo(), Totals, settings ?? BriefExample.Settings(), BriefExample.Database(), culture ?? EnUs, other ?? BriefExample.Other(), total);

    [Fact]
    public void BriefExampleRecordsGiveTheImageStrings()
    {
        var view = Build();

        Assert.True(view.IsConfigured);
        Assert.Equal(new StatusPillState(Tone.Success, "Shelly online"), view.Status);
        Assert.Equal(new StatCardState("PC Power (AC)", "312.4 W", "from Shelly plug", Tone.Ink, false), view.Ac);
        Assert.Equal(new StatCardState("PC Power (DC)", "294.8 W", "via measured curve", Tone.Accent, false), view.Dc);
        Assert.Equal(new StatCardState("PC PSU Loss", "17.6 W", "94.4 % efficiency", Tone.Ink, false), view.Loss);
        Assert.Equal(("PC Other Components", "48.1 W", "DC minus CPU, GPU, RAM", false), (view.Other.Caption, view.Other.Value, view.Other.Sub, view.Other.Placeholder));
        Assert.Equal(OtherCardKind.Value, view.Other.OtherState!.Kind);
        Assert.Equal("Seasonic Prime Fanless TX-700 · 230 V curve · Cybenetics · report 2021-07-16", view.PsuLine);
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
        Assert.Equal("Shelly online", view.Status.Text);
        Assert.Equal("2,41 kWh", view.Energy.TodayValue);
    }

    [Fact]
    public void OtherCardHintWhileHwinfoIsNotRunning()
    {
        var view = Build(hw: new HwinfoMonitorSnapshot(true, false, false, false, null));

        Assert.True(view.Other.Placeholder);
        Assert.Equal("Only available if HWiNFO is running", view.Other.Value);
        Assert.Equal(OtherCardKind.HwinfoNotRunning, view.Other.OtherState!.Kind);
        Assert.False(view.Other.OtherState.ShowHowToEnable);
        Assert.Equal("not running", view.HwinfoLine);
    }

    [Fact]
    public void OtherCardShowsTheValueWhenASourceWorks()
    {
        var gadget = Build(
            hw: BriefExample.Hwinfo() with { SharedMemoryAvailable = false, ComponentSource = OtherSource.Gadget },
            other: new OtherComponentsState(47.5, OtherSource.Gadget, true, [], true));
        Assert.Equal("47.5 W", gadget.Other.Value);
        Assert.False(gadget.Other.Placeholder);
        Assert.Equal(OtherCardKind.Value, gadget.Other.OtherState!.Kind);
        Assert.Equal("running · shared memory off · gadget export on", gadget.HwinfoLine);
    }

    [Fact]
    public void OtherCardNamesTheSensorsMissingFromTheGadgetExport()
    {
        var view = Build(
            hw: BriefExample.Hwinfo() with { SharedMemoryAvailable = false, SensorsVisible = false, ComponentSource = OtherSource.Gadget },
            other: new OtherComponentsState(0, OtherSource.None, true, ["GPU Power", "CPU Package Power"], false));

        Assert.True(view.Other.Placeholder);
        Assert.Equal("Needs HWiNFO's Gadget export.", view.Other.Value);
        var state = view.Other.OtherState!;
        Assert.Equal(OtherCardKind.NeedsGadgetExport, state.Kind);
        Assert.True(state.ShowHowToEnable);
        Assert.Equal(["GPU Power", "CPU Package Power"], state.MissingSensorNames);
        Assert.Contains("**DIMM3 Power**", state.HowToText, StringComparison.Ordinal);
        Assert.Contains("Report value in Gadget", state.HowToText, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared Memory enabled", view.Other.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherCardWithoutAnyExportListsEverySelectedSensor()
    {
        var view = Build(
            hw: BriefExample.Hwinfo() with { SharedMemoryAvailable = false, ComponentSource = OtherSource.None },
            other: new OtherComponentsState(0, OtherSource.None, true, [], false));
        Assert.Equal(MainWindowPresenter.GadgetExportHint, view.Other.Value);
        Assert.Equal(["CPU Package Power", "GPU Power", "DIMM1 Power", "DIMM3 Power"], view.Other.OtherState!.MissingSensorNames);
        Assert.Equal("running · shared memory off", view.HwinfoLine);
    }

    [Fact]
    public void GadgetHowToInsertsTheSelectedSensorNames()
    {
        Assert.Equal(HwinfoTips.GadgetExport, HwinfoTips.GadgetExportFor([]));
        var text = HwinfoTips.GadgetExportFor(["GPU Power", " gpu power ", "CPU Package Power"]);
        Assert.Contains("**GPU Power**, **CPU Package Power**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Components** in the list", text, StringComparison.Ordinal);
        Assert.Contains("**Enable reporting to Gadget**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TotalCardIsTheFirstCardOnlyWithAdditionalDevices()
    {
        var single = Build();
        Assert.Null(single.Total);
        Assert.Equal(4, single.Cards.Count);
        Assert.False(single.ShowChartModeToggle);

        var settings = BriefExample.Settings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = "Monitor", Host = "192.168.1.50" });
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = "Lamp", Host = "192.168.1.51", IncludeInTotals = false });
        var multi = Build(settings, total: 343.6);

        Assert.Equal(new StatCardState("Total Power (AC)", "343.6 W", "PC + Monitor", Tone.Ink, false), multi.Total);
        Assert.Same(multi.Total, multi.Cards[0]);
        Assert.Equal(5, multi.Cards.Count);
        Assert.True(multi.ShowChartModeToggle);

        var offline = Build(settings, online: OnlineState.Offline, total: 343.6);
        Assert.Equal("—", offline.Total!.Value);
    }

    [Fact]
    public void HwinfoLineVariants()
    {
        Assert.Equal("not running", MainWindowPresenter.HwinfoLine(new HwinfoMonitorSnapshot(true, false, false, false, null)));
        Assert.Equal("not installed", MainWindowPresenter.HwinfoLine(HwinfoMonitorSnapshot.Unknown));
        Assert.Equal("running · sensors not yet visible: restart HWiNFO", MainWindowPresenter.HwinfoLine(BriefExample.Hwinfo() with { SensorsVisible = false }));
    }

    [Fact]
    public void OfflinePausedAndUnconfigured()
    {
        var offline = Build(online: OnlineState.Offline);
        Assert.Equal(new StatusPillState(Tone.Error, "Shelly offline"), offline.Status);
        Assert.Equal("—", offline.Ac.Value);
        Assert.Equal("—", offline.Other.Value);
        Assert.Equal("— % efficiency", offline.Loss.Sub);

        var paused = Build(state: RuntimeState.Paused, online: OnlineState.Unknown);
        Assert.Equal(new StatusPillState(Tone.Secondary, "Paused"), paused.Status);
        Assert.Equal("Resume polling", paused.PauseButtonText);

        var connecting = Build(online: OnlineState.Unknown);
        Assert.Equal("Connecting…", connecting.Status.Text);

        var unconfigured = MainWindowPresenter.Build(null, RuntimeState.Unconfigured, OnlineState.Unknown, HwinfoMonitorSnapshot.Unknown, EnergyTotals.Zero, null, BriefExample.Database(), EnUs);
        Assert.False(unconfigured.IsConfigured);
        Assert.Equal("Not set up", unconfigured.Status.Text);
        Assert.False(unconfigured.PauseEnabled);
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
    }

    [Fact]
    public void SingleVoltagePsuLineShowsTheCurveNote()
    {
        var settings = BriefExample.SingleVoltageSettings();
        var db = BriefExample.SingleVoltageDatabase();
        PowerReadingEventArgs At(double volts) =>
            new(new ShellyReading(312.4, volts, 2.6, 123456), new PowerReading(312.4, 312.4, 294.8, 17.6, InputVoltage.V230));
        MainViewState View(double volts) =>
            MainWindowPresenter.Build(At(volts), RuntimeState.Running, OnlineState.Online, BriefExample.Hwinfo(), Totals, settings, db, EnUs);

        Assert.Equal("Testbrand EU-700 · 230 V curve · Cybenetics", View(226.9).PsuLine);
        Assert.Equal("Testbrand EU-700 · 230 V curve (no 115 V data) · Cybenetics", View(118.2).PsuLine);

        settings.VoltageMode = InputVoltageMode.V115;
        Assert.Equal("Testbrand EU-700 · 230 V curve (no 115 V data) · Cybenetics", View(226.9).PsuLine);
    }

    [Fact]
    public void PsuLineNamesTheRevision()
    {
        var settings = BriefExample.Settings();
        settings.Psu = new PsuSettings { Mode = PsuSelectionMode.Database, DatabaseId = "cooler-master-mwe-gold-1050-v2-atx-v2-52-2021" };
        var entry = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == settings.Psu.DatabaseId);
        Assert.StartsWith("Cooler Master MWE Gold 1050 V2 (ATX v2.52, 2021) · 230 V curve · Cybenetics · report ", MainWindowPresenter.PsuLine(settings, entry, 226.9, EnUs), StringComparison.Ordinal);
    }

    [Fact]
    public void LongestPsuLineIsTheShipped230VOnlyUnitAt115V()
    {
        var settings = BriefExample.Settings();
        settings.Psu = new PsuSettings { Mode = PsuSelectionMode.Database, DatabaseId = "cooler-master-x-silent-edge-platinum-1100-230v" };
        settings.VoltageMode = InputVoltageMode.V115;
        var entry = PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == settings.Psu.DatabaseId);
        Assert.Equal(
            "Cooler Master X Silent Edge Platinum 1100 230V · 230 V curve (no 115 V data) · Cybenetics · report 2024-06-21",
            MainWindowPresenter.PsuLine(settings, entry, 118.2, EnUs));
    }

    [Fact]
    public void ShortCurveLabelNamesTheMissingVoltage()
    {
        var curve = TestCurves.Tx700At230;
        Assert.Equal("115 V curve", PsuCurveSet.ShortCurveLabel(new PsuCurveSet(curve, curve), InputVoltage.V115));
        Assert.Equal("230 V curve (no 115 V data)", PsuCurveSet.ShortCurveLabel(new PsuCurveSet(null, curve), InputVoltage.V115));
        Assert.Equal("115 V curve (no 230 V data)", PsuCurveSet.ShortCurveLabel(new PsuCurveSet(curve, null), InputVoltage.V230));
        Assert.Equal("115 V curve", PsuCurveSet.ShortCurveLabel(null, InputVoltage.V115));
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

    [Fact]
    public void StatusPillSummarisesEveryDevice()
    {
        static DeviceStatus Device(string id, OnlineState state) => new(id, id, "host-" + id, null, null, state, null, true);
        StatusPillState Status(params OnlineState[] states) =>
            MainWindowPresenter.Status(RuntimeState.Running, states.Select((s, i) => Device(i == 0 ? "pc" : "d" + i, s)).ToList());

        Assert.Equal(new StatusPillState(Tone.Success, "Shellys online"), Status(OnlineState.Online, OnlineState.Online));
        Assert.Equal(new StatusPillState(Tone.Warning, "Some Shellys offline"), Status(OnlineState.Online, OnlineState.Offline, OnlineState.Online));
        Assert.Equal(new StatusPillState(Tone.Warning, "Some Shellys offline"), Status(OnlineState.Offline, OnlineState.Online));
        Assert.Equal(new StatusPillState(Tone.Error, "Shellys offline"), Status(OnlineState.Offline, OnlineState.Offline));
        Assert.Equal(new StatusPillState(Tone.Success, "Shelly online"), Status(OnlineState.Online));
        Assert.Equal(new StatusPillState(Tone.Error, "Shelly offline"), Status(OnlineState.Offline));

        // Devices not known yet are left out; with none known the pill says it is connecting.
        Assert.Equal(new StatusPillState(Tone.Success, "Shellys online"), Status(OnlineState.Online, OnlineState.Unknown));
        Assert.Equal(new StatusPillState(Tone.Secondary, "Connecting…"), Status(OnlineState.Unknown, OnlineState.Unknown));

        // Runtime states replace the summary; no plug name or voltage anywhere.
        var two = new[] { Device("pc", OnlineState.Online), Device("d1", OnlineState.Offline) };
        Assert.Equal(new StatusPillState(Tone.Secondary, "Paused"), MainWindowPresenter.Status(RuntimeState.Paused, two));
        Assert.Equal(new StatusPillState(Tone.Secondary, "Not set up"), MainWindowPresenter.Status(RuntimeState.Unconfigured, two));
        Assert.Equal(new StatusPillState(Tone.Warning, "No PSU curve"), MainWindowPresenter.Status(RuntimeState.CurveUnavailable, two));
    }

    [Fact]
    public void BuildUsesTheRuntimeDevicesForThePill()
    {
        var settings = BriefExample.Settings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = "11111111-1111-1111-1111-111111111111", Label = "Monitor", Host = "192.168.1.43" });
        var devices = new[]
        {
            new DeviceStatus("pc", "PC", "192.168.1.42", null, null, OnlineState.Online, 312.4, true),
            new DeviceStatus("11111111-1111-1111-1111-111111111111", "Monitor", "192.168.1.43", null, null, OnlineState.Offline, null, true),
        };

        var view = MainWindowPresenter.Build(BriefExample.Reading(), RuntimeState.Running, OnlineState.Online, BriefExample.Hwinfo(), Totals, settings, BriefExample.Database(), EnUs, BriefExample.Other(), 312.4, devices);
        Assert.Equal(new StatusPillState(Tone.Warning, "Some Shellys offline"), view.Status);

        // Without the runtime's devices, additional devices count as not known yet.
        var derived = MainWindowPresenter.Build(BriefExample.Reading(), RuntimeState.Running, OnlineState.Online, BriefExample.Hwinfo(), Totals, settings, BriefExample.Database(), EnUs);
        Assert.Equal(new StatusPillState(Tone.Success, "Shellys online"), derived.Status);
    }
}
