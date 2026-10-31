using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>The connection status pill.</summary>
/// <param name="Dot">The dot colour.</param>
/// <param name="Text">The text, e.g. "Online · PlugMGen3-2 · 226.9 V".</param>
public sealed record StatusPillState(Tone Dot, string Text);

/// <summary>One live-value card.</summary>
/// <param name="Caption">The caption (the UI draws it uppercase), e.g. "AC Power (Wall)".</param>
/// <param name="Value">The value with unit ("312.4 W"), or the hint text when <paramref name="Placeholder"/>.</param>
/// <param name="Sub">The secondary line ("from Shelly plug").</param>
/// <param name="ValueTone">The value colour.</param>
/// <param name="Placeholder">Whether <paramref name="Value"/> is a hint instead of a number.</param>
public sealed record StatCardState(string Caption, string Value, string Sub, Tone ValueTone, bool Placeholder);

/// <summary>The two totals under the energy chart.</summary>
/// <param name="TodayValue">"2.41 kWh".</param>
/// <param name="TodayCaption">"TODAY", or "TODAY · 0.77 DKK" when a price is set.</param>
/// <param name="MonthValue">"48.7 kWh".</param>
/// <param name="MonthCaption">"THIS MONTH", or "THIS MONTH · 15.58 DKK".</param>
public sealed record EnergyTotalsState(string TodayValue, string TodayCaption, string MonthValue, string MonthCaption);

/// <summary>Everything the main window shows.</summary>
/// <param name="IsConfigured">Whether setup was completed; otherwise the window shows the "Not set up yet" card.</param>
/// <param name="Status">The status pill.</param>
/// <param name="Ac">AC card.</param>
/// <param name="Dc">DC card.</param>
/// <param name="Loss">Loss card.</param>
/// <param name="Other">Other Components card.</param>
/// <param name="Energy">The energy totals.</param>
/// <param name="PsuLine">Configuration line "PSU".</param>
/// <param name="SensorsLine">Configuration line "Sensors".</param>
/// <param name="HwinfoLine">Configuration line "HWiNFO".</param>
/// <param name="PauseButtonText">"Pause polling" or "Resume polling".</param>
/// <param name="PauseEnabled">Whether the pause button is enabled.</param>
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
    bool PauseEnabled);

/// <summary>Builds <see cref="MainViewState"/> from the runtime, the HWiNFO monitor and the energy totals.</summary>
public sealed class MainWindowPresenter
{
    /// <summary>The Other card's hint when shared memory is unavailable (SPEC wording).</summary>
    public const string SharedMemoryHint = "Only available with Shared Memory enabled in HWiNFO";

    /// <summary>The Other card's hint when shared memory works but HWiNFO does not show the bridge's sensors yet.</summary>
    public const string RestartHwinfoHint = "Restart HWiNFO to see this";

    private readonly IBridgeRuntime _runtime;

    /// <summary>Creates a presenter over a runtime.</summary>
    /// <param name="runtime">The runtime.</param>
    public MainWindowPresenter(IBridgeRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <summary>Builds the state from the runtime's current values.</summary>
    /// <param name="hwinfo">The latest HWiNFO snapshot.</param>
    /// <param name="totals">The energy totals.</param>
    /// <param name="culture">The culture; defaults to the current culture.</param>
    public MainViewState Build(HwinfoMonitorSnapshot hwinfo, EnergyTotals totals, CultureInfo? culture = null) =>
        Build(_runtime.LastReading, _runtime.State, _runtime.OnlineState, hwinfo, totals, _runtime.Settings, _runtime.Database, culture ?? CultureInfo.CurrentCulture);

    /// <summary>Builds the state (pure).</summary>
    /// <param name="last">The last reading, or <c>null</c>.</param>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The online state.</param>
    /// <param name="hwinfo">The HWiNFO snapshot.</param>
    /// <param name="totals">The energy totals.</param>
    /// <param name="settings">The settings, or <c>null</c> on first run.</param>
    /// <param name="database">The PSU database.</param>
    /// <param name="culture">The culture for numbers.</param>
    public static MainViewState Build(
        PowerReadingEventArgs? last,
        RuntimeState state,
        OnlineState online,
        HwinfoMonitorSnapshot hwinfo,
        EnergyTotals totals,
        AppSettings? settings,
        PsuDatabase database,
        CultureInfo culture)
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

        var ac = new StatCardState("AC Power (Wall)", live is null ? UiFormat.Dash : UiFormat.Watts(live.Power.AcWatts, culture), "from Shelly plug", Tone.Ink, false);
        var dc = new StatCardState("DC Power (Est.)", live is null ? UiFormat.Dash : UiFormat.Watts(live.Power.DcWatts, culture), DcSub(settings, entry), Tone.Accent, false);
        var loss = new StatCardState("PSU Loss", live is null ? UiFormat.Dash : UiFormat.Watts(live.Power.LossWatts, culture), EfficiencySub(live, culture), Tone.Ink, false);

        return new MainViewState(
            state != RuntimeState.Unconfigured,
            Status(state, online, last, settings, culture),
            ac,
            dc,
            loss,
            OtherCard(live, hwinfo, settings, culture),
            Totals(totals, settings, culture),
            PsuLine(settings, entry, last?.Power.CurveVoltage, culture),
            SensorsLine(settings),
            HwinfoLine(hwinfo),
            state == RuntimeState.Paused ? "Resume polling" : "Pause polling",
            state is RuntimeState.Running or RuntimeState.Paused);
    }

    /// <summary>Builds the status pill.</summary>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The online state.</param>
    /// <param name="last">The last reading.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="culture">The culture.</param>
    public static StatusPillState Status(RuntimeState state, OnlineState online, PowerReadingEventArgs? last, AppSettings settings, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var name = DeviceName(settings);
        return state switch
        {
            RuntimeState.Unconfigured => new StatusPillState(Tone.Secondary, "Not set up"),
            RuntimeState.CurveUnavailable => new StatusPillState(Tone.Warning, "No PSU curve · " + name),
            RuntimeState.Paused => new StatusPillState(Tone.Secondary, "Paused · " + name),
            _ => online switch
            {
                OnlineState.Online => new StatusPillState(
                    Tone.Success,
                    last?.Raw.Volts is double v && double.IsFinite(v) ? $"Online · {name} · {UiFormat.Volts(v, culture)}" : "Online · " + name),
                OnlineState.Offline => new StatusPillState(Tone.Error, "Offline · " + name),
                _ => new StatusPillState(Tone.Secondary, "Connecting · " + name),
            },
        };
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
    /// <param name="curveVoltage">The voltage of the curve in use, if known from a reading.</param>
    /// <param name="culture">The culture.</param>
    public static string PsuLine(AppSettings settings, PsuEntry? entry, InputVoltage? curveVoltage, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var voltage = curveVoltage ?? VoltageSelector.Resolve(settings.VoltageMode, null);
        var curve = $"{(int)voltage} V curve";
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

    /// <summary>Builds the sensors line: names, "first 3 and N more", "custom formula" or "none selected".</summary>
    /// <param name="settings">The settings.</param>
    public static string SensorsLine(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(settings.CustomFormula))
        {
            return "custom formula";
        }

        var names = settings.Sensors.Select(s => s.Name.Trim()).Where(n => n.Length > 0).ToList();
        return names.Count switch
        {
            0 => "none selected",
            <= 4 => string.Join(", ", names),
            _ => $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more",
        };
    }

    /// <summary>Builds the HWiNFO line ("running · sensors visible", "not running", …).</summary>
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
            return "running · shared memory off";
        }

        return hwinfo.SensorsVisible ? "running · sensors visible" : "running · sensors not yet visible: restart HWiNFO";
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

    private static StatCardState OtherCard(PowerReadingEventArgs? live, HwinfoMonitorSnapshot hwinfo, AppSettings settings, CultureInfo culture)
    {
        const string caption = "Other Components";
        if (!hwinfo.SharedMemoryAvailable)
        {
            return new StatCardState(caption, SharedMemoryHint, string.Empty, Tone.Ink, true);
        }

        if (!hwinfo.SensorsVisible || hwinfo.OtherWatts is null)
        {
            return new StatCardState(caption, RestartHwinfoHint, string.Empty, Tone.Ink, true);
        }

        var value = live is null ? UiFormat.Dash : UiFormat.Watts(hwinfo.OtherWatts.Value, culture);
        return new StatCardState(caption, value, OtherSub(settings), Tone.Ink, false);
    }

    private static string OtherSub(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CustomFormula))
        {
            return "custom formula";
        }

        var categories = settings.Sensors.Select(s => s.Category).Distinct().Order().Select(c => c switch
        {
            SensorCategory.Cpu => "CPU",
            SensorCategory.Gpu => "GPU",
            SensorCategory.Ram => "RAM",
            _ => "other",
        }).ToList();
        return categories.Count == 0 ? "no sensors selected" : "DC minus " + string.Join(", ", categories);
    }
}
