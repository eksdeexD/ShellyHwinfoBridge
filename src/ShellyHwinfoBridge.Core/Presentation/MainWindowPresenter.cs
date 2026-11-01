using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>The connection status pill.</summary>
/// <param name="Dot">The dot colour.</param>
/// <param name="Text">The text, e.g. "Online · PlugMGen3-2 · 226.9 V".</param>
public sealed record StatusPillState(Tone Dot, string Text);

/// <summary>The states of the Other Components card (SPEC "Main window").</summary>
public enum OtherCardKind
{
    /// <summary>HWiNFO runs and a component source works: the card shows the value.</summary>
    Value,

    /// <summary>HWiNFO is not running: value hidden, hint "Only available if HWiNFO is running".</summary>
    HwinfoNotRunning,

    /// <summary>
    /// HWiNFO runs but no component source works: hint "Needs HWiNFO's Gadget export." and a "How to enable?"
    /// button that opens the gadget how-to (<see cref="OtherCardState.HowToText"/>).
    /// </summary>
    NeedsGadgetExport,
}

/// <summary>The Other Components card's state, for binding the hint and the "How to enable?" button.</summary>
/// <param name="Kind">The state.</param>
/// <param name="Hint">The hint shown instead of the value, or <c>null</c> for <see cref="OtherCardKind.Value"/>.</param>
/// <param name="MissingSensorNames">The selected sensors missing from the gadget export (<see cref="OtherCardKind.NeedsGadgetExport"/>).</param>
/// <param name="ShowHowToEnable">Whether to show the "How to enable?" button.</param>
/// <param name="HowToText">The gadget how-to with the user's selected sensor names (<see cref="InlineMarkup"/>).</param>
public sealed record OtherCardState(OtherCardKind Kind, string? Hint, IReadOnlyList<string> MissingSensorNames, bool ShowHowToEnable, string HowToText);

/// <summary>One live-value card.</summary>
/// <param name="Caption">The caption (the UI draws it uppercase), e.g. "PC Power (AC)".</param>
/// <param name="Value">The value with unit ("312.4 W"), or the hint text when <paramref name="Placeholder"/>.</param>
/// <param name="Sub">The secondary line ("from Shelly plug").</param>
/// <param name="ValueTone">The value colour.</param>
/// <param name="Placeholder">Whether <paramref name="Value"/> is a hint instead of a number.</param>
/// <param name="OtherState">The Other card's state (only set on the Other card).</param>
public sealed record StatCardState(string Caption, string Value, string Sub, Tone ValueTone, bool Placeholder, OtherCardState? OtherState = null);

/// <summary>The two totals under the energy chart.</summary>
/// <param name="TodayValue">"2.41 kWh".</param>
/// <param name="TodayCaption">"TODAY", or "TODAY · 0.77 DKK" when a price is set.</param>
/// <param name="MonthValue">"48.7 kWh".</param>
/// <param name="MonthCaption">"THIS MONTH", or "THIS MONTH · 15.58 DKK".</param>
public sealed record EnergyTotalsState(string TodayValue, string TodayCaption, string MonthValue, string MonthCaption);

/// <summary>Everything the main window shows.</summary>
/// <param name="IsConfigured">Whether setup was completed; otherwise the window shows the "Not set up yet" card.</param>
/// <param name="Status">The status pill.</param>
/// <param name="Ac">PC Power (AC) card.</param>
/// <param name="Dc">PC Power (DC) card.</param>
/// <param name="Loss">PC PSU Loss card.</param>
/// <param name="Other">PC Other Components card (see <see cref="StatCardState.OtherState"/>).</param>
/// <param name="Energy">The energy totals (PC plus included devices).</param>
/// <param name="PsuLine">Configuration line "PSU".</param>
/// <param name="SensorsLine">Configuration line "Sensors".</param>
/// <param name="HwinfoLine">Configuration line "HWiNFO".</param>
/// <param name="PauseButtonText">"Pause polling" or "Resume polling".</param>
/// <param name="PauseEnabled">Whether the pause button is enabled.</param>
/// <param name="Total">The Total Power (AC) card, or <c>null</c> when there are no additional devices.</param>
/// <param name="ShowChartModeToggle">Whether the Combined / Per device toggle is shown (more than one device).</param>
/// <param name="ChartMode">The chart mode stored in settings.</param>
public sealed record MainViewState(
    bool IsConfigured,
    StatusPillState Status,
    StatCardState Ac,
    StatCardState Dc,
    StatCardState Loss,
    StatCardState Other,
    EnergyTotalsState Energy,
    string PsuLine,
    string SensorsLine,
    string HwinfoLine,
    string PauseButtonText,
    bool PauseEnabled,
    StatCardState? Total = null,
    bool ShowChartModeToggle = false,
    EnergyChartMode ChartMode = EnergyChartMode.Combined)
{
    /// <summary>Gets the cards in display order: Total first when present, then AC, DC, Loss, Other.</summary>
    public IReadOnlyList<StatCardState> Cards => Total is null ? [Ac, Dc, Loss, Other] : [Total, Ac, Dc, Loss, Other];
}

/// <summary>Builds <see cref="MainViewState"/> from the runtime, the HWiNFO monitor and the energy totals.</summary>
public sealed class MainWindowPresenter
{
    /// <summary>The Other card's hint while HWiNFO is not running.</summary>
    public const string HwinfoNotRunningHint = "Only available if HWiNFO is running";

    /// <summary>The Other card's hint when HWiNFO runs but no component source works (the help dialog names the sensors).</summary>
    public const string GadgetExportHint = "Needs HWiNFO's Gadget export.";

    /// <summary>The caption of the "How to enable?" button.</summary>
    public const string HowToEnableText = "How to enable?";

    /// <summary>
    /// The Other card's hover tooltip: the value is the median over <see cref="OtherComponentsCalculator.WindowSeconds"/>
    /// seconds.
    /// </summary>
    public const string OtherValueTooltip = "The median of the last 10 seconds of DC minus the selected sensors. The median removes the spikes that appear while HWiNFO's sensor values lag behind the plug's reading.";

    private readonly IBridgeRuntime _runtime;

    /// <summary>Creates a presenter over a runtime.</summary>
    /// <param name="runtime">The runtime.</param>
    public MainWindowPresenter(IBridgeRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <summary>Builds the state from the runtime's current values.</summary>
    /// <param name="hwinfo">The latest HWiNFO snapshot.</param>
    /// <param name="totals">The energy totals.</param>
    /// <param name="culture">The culture; defaults to the current culture.</param>
    public MainViewState Build(HwinfoMonitorSnapshot hwinfo, EnergyTotals totals, CultureInfo? culture = null) =>
        Build(_runtime.LastReading, _runtime.State, _runtime.OnlineState, hwinfo, totals, _runtime.Settings, _runtime.Database, culture ?? CultureInfo.CurrentCulture, _runtime.Other, _runtime.TotalWatts, _runtime.Devices);

    /// <summary>Builds the state (pure).</summary>
    /// <param name="last">The PC's last reading, or <c>null</c>.</param>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The PC's online state.</param>
    /// <param name="hwinfo">The HWiNFO snapshot.</param>
    /// <param name="totals">The energy totals.</param>
    /// <param name="settings">The settings, or <c>null</c> on first run.</param>
    /// <param name="database">The PSU database.</param>
    /// <param name="culture">The culture for numbers.</param>
    /// <param name="other">The app-computed Other Components, or <c>null</c> when unknown.</param>
    /// <param name="totalWatts">Total Power (AC), or <c>null</c> when not live.</param>
    /// <param name="devices">Every device's status (status pill); <c>null</c> derives it from <paramref name="online"/> (PC) and the settings.</param>
    public static MainViewState Build(
        PowerReadingEventArgs? last,
        RuntimeState state,
        OnlineState online,
        HwinfoMonitorSnapshot hwinfo,
        EnergyTotals totals,
        AppSettings? settings,
        PsuDatabase database,
        CultureInfo culture,
        OtherComponentsState? other = null,
        double? totalWatts = null,
        IReadOnlyList<DeviceStatus>? devices = null)
    {
        ArgumentNullException.ThrowIfNull(hwinfo);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(culture);
        settings ??= new AppSettings();
        var live = state == RuntimeState.Running && online == OnlineState.Online ? last : null;
        var entry = settings.Psu.Mode == PsuSelectionMode.Database && settings.Psu.DatabaseId is { } id
            ? new PsuDatabaseQuery(database).FindById(id)
            : null;

        var ac = new StatCardState(SensorNaming.PcAcName, live is null ? UiFormat.Dash : UiFormat.Watts(live.Power.AcWatts, culture), "from Shelly plug", Tone.Ink, false);
        var dc = new StatCardState(SensorNaming.PcDcName, live is null ? UiFormat.Dash : UiFormat.Watts(live.Power.DcWatts, culture), DcSub(settings, entry), Tone.Accent, false);
        var loss = new StatCardState(SensorNaming.PcLossName, live is null ? UiFormat.Dash : UiFormat.Watts(live.Power.LossWatts, culture), EfficiencySub(live, culture), Tone.Ink, false);
        var total = settings.HasAdditionalDevices ? TotalCard(live is null ? null : totalWatts, settings, culture) : null;

        return new MainViewState(
            state != RuntimeState.Unconfigured,
            Status(state, devices ?? DevicesFromSettings(settings, online)),
            ac,
            dc,
            loss,
            OtherCard(live, hwinfo, settings, other ?? OtherComponentsState.Unknown, culture),
            Totals(totals, settings, culture),
            PsuLine(settings, entry, last?.Raw.Volts, culture),
            SensorsLine(settings),
            HwinfoLine(hwinfo),
            state == RuntimeState.Paused ? "Resume polling" : "Pause polling",
            state is RuntimeState.Running or RuntimeState.Paused,
            total,
            settings.HasAdditionalDevices,
            settings.ChartMode);
    }

    /// <summary>
    /// Builds the Other card: the value while HWiNFO runs and a component source works; "Only available if HWiNFO is
    /// running" while it does not run; "Needs HWiNFO's Gadget export." with "How to enable?" otherwise.
    /// </summary>
    /// <param name="live">The PC's live reading, or <c>null</c>.</param>
    /// <param name="hwinfo">The HWiNFO snapshot.</param>
    /// <param name="settings">The settings (selected sensors).</param>
    /// <param name="other">The app-computed Other Components.</param>
    /// <param name="culture">The culture.</param>
    public static StatCardState OtherCard(PowerReadingEventArgs? live, HwinfoMonitorSnapshot hwinfo, AppSettings settings, OtherComponentsState other, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(hwinfo);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(culture);
        const string caption = SensorNaming.PcOtherName;
        var selected = settings.Sensors.Select(s => s.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var howTo = HwinfoTips.GadgetExportFor(selected);
        if (!hwinfo.IsRunning)
        {
            return new StatCardState(caption, HwinfoNotRunningHint, string.Empty, Tone.Ink, true, new OtherCardState(OtherCardKind.HwinfoNotRunning, HwinfoNotRunningHint, [], false, howTo));
        }

        if (other.Source != OtherSource.None)
        {
            var value = live is null ? UiFormat.Dash : UiFormat.Watts(other.Watts, culture);
            return new StatCardState(caption, value, OtherSub(settings), Tone.Ink, false, new OtherCardState(OtherCardKind.Value, null, [], false, howTo));
        }

        var missing = other.MissingGadgetSensors.Count > 0 ? other.MissingGadgetSensors : selected;
        const string hint = GadgetExportHint;
        return new StatCardState(caption, hint, string.Empty, Tone.Ink, true, new OtherCardState(OtherCardKind.NeedsGadgetExport, hint, missing, true, howTo));
    }

    /// <summary>Builds the Total Power (AC) card; the secondary line names the included devices ("PC + Monitor").</summary>
    /// <param name="totalWatts">The total, or <c>null</c> when not live.</param>
    /// <param name="settings">The settings (the included devices).</param>
    /// <param name="culture">The culture; defaults to the current culture.</param>
    public static StatCardState TotalCard(double? totalWatts, AppSettings settings, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        culture ??= CultureInfo.CurrentCulture;
        var labels = new List<string> { AppSettings.PrimaryLabel };
        labels.AddRange(settings.AdditionalDevices.Where(d => d.IncludeInTotals).Select(d => d.Label));
        return new StatCardState(SensorNaming.TotalName, totalWatts is double t ? UiFormat.Watts(t, culture) : UiFormat.Dash, string.Join(" + ", labels), Tone.Ink, false);
    }

    /// <summary>Builds the HWiNFO line ("running · sensors visible", "running · shared memory off · gadget export on", "not running", …).</summary>
    /// <param name="hwinfo">The snapshot.</param>
    public static string HwinfoLine(HwinfoMonitorSnapshot hwinfo)
    {
        ArgumentNullException.ThrowIfNull(hwinfo);
        if (!hwinfo.IsRunning)
        {
            return hwinfo.IsInstalled ? "not running" : "not installed";
        }

        if (!hwinfo.SharedMemoryAvailable)
        {
            return hwinfo.ComponentSource == OtherSource.Gadget ? "running · shared memory off · gadget export on" : "running · shared memory off";
        }

        return hwinfo.SensorsVisible ? "running · sensors visible" : "running · sensors not yet visible: restart HWiNFO";
    }

    private static string OtherSub(AppSettings settings)
    {
        var categories = settings.Sensors.Select(s => s.Category).Distinct().Order().Select(c => c switch
        {
            SensorCategory.Cpu => "CPU",
            SensorCategory.Gpu => "GPU",
            SensorCategory.Ram => "RAM",
            _ => "other",
        }).ToList();
        return categories.Count == 0 ? "no sensors selected" : "DC minus " + string.Join(", ", categories);
    }


    /// <summary>The status pill when every device answers ("Shelly online" with one device).</summary>
    public const string AllOnlineText = "Shellys online";

    /// <summary>The status pill when no device answers ("Shelly offline" with one device).</summary>
    public const string AllOfflineText = "Shellys offline";

    /// <summary>The status pill when some devices answer and some do not.</summary>
    public const string SomeOfflineText = "Some Shellys offline";

    /// <summary>
    /// Builds the status pill: a summary over every device (SPEC "Status pill"), without plug name or voltage. Green
    /// "Shellys online" when every device answers, yellow "Some Shellys offline" when some do not, red "Shellys offline"
    /// when none does (singular "Shelly online" / "Shelly offline" with one device). Devices whose state is not known
    /// yet are left out; while none is known the pill reads "Connecting…". "Not set up", "No PSU curve" and "Paused"
    /// replace the summary in those runtime states. Clicking the pill opens Manage Shellys, which shows each device.
    /// </summary>
    /// <param name="state">The runtime state.</param>
    /// <param name="devices">Every device's status (PC first).</param>
    public static StatusPillState Status(RuntimeState state, IReadOnlyList<DeviceStatus> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        switch (state)
        {
            case RuntimeState.Unconfigured:
                return new StatusPillState(Tone.Secondary, "Not set up");
            case RuntimeState.CurveUnavailable:
                return new StatusPillState(Tone.Warning, "No PSU curve");
            case RuntimeState.Paused:
                return new StatusPillState(Tone.Secondary, "Paused");
        }

        var single = devices.Count <= 1;
        var online = devices.Count(d => d.State == OnlineState.Online);
        var offline = devices.Count(d => d.State == OnlineState.Offline);
        if (online == 0 && offline == 0)
        {
            return new StatusPillState(Tone.Secondary, "Connecting…");
        }

        if (offline == 0)
        {
            return new StatusPillState(Tone.Success, single ? "Shelly online" : AllOnlineText);
        }

        return online == 0
            ? new StatusPillState(Tone.Error, single ? "Shelly offline" : AllOfflineText)
            : new StatusPillState(Tone.Warning, SomeOfflineText);
    }

    /// <summary>
    /// Derives the device states for the pill without a runtime: the PC with <paramref name="online"/>, every additional
    /// device of the settings as not known yet.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="online">The PC's online state.</param>
    public static IReadOnlyList<DeviceStatus> DevicesFromSettings(AppSettings settings, OnlineState online)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var devices = new List<DeviceStatus> { new(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, settings.Device.Host, settings.Device.Name, settings.Device.Model, online, null, true) };
        devices.AddRange(settings.AdditionalDevices.Select(d => new DeviceStatus(d.Id, d.Label, d.Host, d.Name, d.Model, OnlineState.Unknown, null, d.IncludeInTotals)));
        return devices;
    }

    /// <summary>Returns the device's display name: its name, or the host.</summary>
    /// <param name="settings">The settings.</param>
    public static string DeviceName(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !string.IsNullOrWhiteSpace(settings.Device.Name) ? settings.Device.Name.Trim() : settings.Device.Host.Trim();
    }

    /// <summary>Builds the PSU line ("Seasonic Prime Fanless TX-700 · 230 V curve · Cybenetics").</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="entry">The database entry (database mode), or <c>null</c>.</param>
    /// <param name="measuredVolts">The mains voltage of the last reading, if any (selects the curve in automatic mode).</param>
    /// <param name="culture">The culture.</param>
    /// <remarks>
    /// When the selected database entry has no curve for that voltage, the curve part reads "230 V curve (115 V data
    /// not available for this unit)".
    /// </remarks>
    public static string PsuLine(AppSettings settings, PsuEntry? entry, double? measuredVolts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var voltage = VoltageSelector.Resolve(settings.VoltageMode, measuredVolts);
        var curve = PsuCurveSet.CurveLabel(settings.Psu.Mode == PsuSelectionMode.Database ? entry?.GetCurveSet() : null, voltage);
        switch (settings.Psu.Mode)
        {
            case PsuSelectionMode.Database:
                if (entry is null)
                {
                    return settings.Psu.DatabaseId is null ? "No PSU selected" : "PSU not found in the database";
                }

                var name = $"{entry.Brand} {entry.DisplayName}".Trim();
                var source = entry.Source?.Name;
                return string.IsNullOrWhiteSpace(source) ? $"{name} · {curve}" : $"{name} · {curve} · {source}";

            case PsuSelectionMode.Generic:
                return settings.Psu.GenericWattage is int watts && settings.Psu.GenericRating is { } rating
                    ? $"Generic {rating} {watts.ToString(culture)} W (approximate) · {curve}"
                    : "Generic curve incomplete";

            default:
                var points = settings.Psu.ManualCurve?.Count ?? 0;
                return $"Manual curve ({points.ToString(culture)} points) · {curve}";
        }
    }

    /// <summary>Builds the sensors line: names, "first 3 and N more" or "none selected".</summary>
    /// <param name="settings">The settings.</param>
    public static string SensorsLine(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var names = settings.Sensors.Select(s => s.Name.Trim()).Where(n => n.Length > 0).ToList();
        return names.Count switch
        {
            0 => "none selected",
            <= 4 => string.Join(", ", names),
            _ => $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more",
        };
    }

    /// <summary>Builds the energy totals, with cost in the captions when a price is set.</summary>
    /// <param name="totals">The totals.</param>
    /// <param name="settings">The settings (price and currency).</param>
    /// <param name="culture">The culture.</param>
    public static EnergyTotalsState Totals(EnergyTotals totals, AppSettings settings, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(settings);
        var today = "TODAY";
        var month = "THIS MONTH";
        if (settings.EnergyPricePerKwh is decimal price)
        {
            today += " · " + UiFormat.Cost(price, totals.TodayAcKwh, settings.EnergyCurrency, culture);
            month += " · " + UiFormat.Cost(price, totals.MonthAcKwh, settings.EnergyCurrency, culture);
        }

        return new EnergyTotalsState(UiFormat.KwhTotal(totals.TodayAcKwh, culture), today, UiFormat.KwhTotal(totals.MonthAcKwh, culture), month);
    }

    private static string DcSub(AppSettings settings, PsuEntry? entry) => settings.Psu.Mode switch
    {
        PsuSelectionMode.Database when entry is not null => $"via {entry.Model.Trim().Split(' ')[^1]} curve",
        PsuSelectionMode.Generic => "via generic curve",
        PsuSelectionMode.Manual => "via manual curve",
        _ => "no PSU curve",
    };

    private static string EfficiencySub(PowerReadingEventArgs? live, CultureInfo culture)
    {
        if (live is null || live.Power.SmoothedAcWatts <= 0)
        {
            return UiFormat.Dash + " % efficiency";
        }

        var efficiency = 100.0 * live.Power.DcWatts / live.Power.SmoothedAcWatts;
        return efficiency.ToString("0.0", culture) + " % efficiency";
    }
}
