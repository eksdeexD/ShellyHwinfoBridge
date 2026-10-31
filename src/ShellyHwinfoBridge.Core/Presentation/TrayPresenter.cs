using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Polling;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>The tray icon's look.</summary>
public enum TrayIconState
{
    /// <summary>Orange glyph: polling and the Shelly answers.</summary>
    Online,

    /// <summary>Grey glyph: offline, connecting, not set up or no curve.</summary>
    Offline,

    /// <summary>Grey glyph with a pause mark.</summary>
    Paused,
}

/// <summary>Builds the tray tooltip and icon state (both follow the PC's plug).</summary>
public static class TrayPresenter
{
    /// <summary>The longest tooltip a NotifyIcon accepts (.NET 8).</summary>
    public const int MaxTooltipLength = 127;

    /// <summary>
    /// Returns the tooltip: "AC 312.4 W · DC 294.8 W · Other 48.1 W · Total 380 W" (Other only when a component source
    /// works, Total only with additional devices), "Shelly offline", "Polling paused", "Setup not completed", …; never
    /// longer than <see cref="MaxTooltipLength"/>.
    /// </summary>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The PC's online state.</param>
    /// <param name="last">The PC's last reading.</param>
    /// <param name="hwinfo">The HWiNFO snapshot.</param>
    /// <param name="culture">The culture; defaults to the current culture.</param>
    /// <param name="other">The app-computed Other Components (<see cref="IBridgeRuntime.Other"/>), or <c>null</c> to leave it out.</param>
    /// <param name="totalWatts">Total Power (AC) (<see cref="IBridgeRuntime.TotalWatts"/>), or <c>null</c> to leave it out.</param>
    public static string Tooltip(
        RuntimeState state,
        OnlineState online,
        PowerReadingEventArgs? last,
        HwinfoMonitorSnapshot hwinfo,
        CultureInfo? culture = null,
        OtherComponentsState? other = null,
        double? totalWatts = null)
    {
        ArgumentNullException.ThrowIfNull(hwinfo);
        culture ??= CultureInfo.CurrentCulture;
        var text = state switch
        {
            RuntimeState.Unconfigured => "Setup not completed",
            RuntimeState.CurveUnavailable => "No PSU curve: open Settings",
            RuntimeState.Paused => "Polling paused",
            _ => online switch
            {
                OnlineState.Offline => "Shelly offline",
                OnlineState.Online when last is not null => Live(last, hwinfo, other, totalWatts, culture),
                _ => "ShellyHwinfoBridge · connecting…",
            },
        };
        return text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
    }

    /// <summary>Returns the tooltip for a runtime (see the other overload).</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="hwinfo">The HWiNFO snapshot.</param>
    /// <param name="culture">The culture; defaults to the current culture.</param>
    public static string Tooltip(IBridgeRuntime runtime, HwinfoMonitorSnapshot hwinfo, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return Tooltip(runtime.State, runtime.OnlineState, runtime.LastReading, hwinfo, culture, runtime.Other, runtime.TotalWatts);
    }

    /// <summary>Returns the icon state.</summary>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The PC's online state.</param>
    public static TrayIconState IconState(RuntimeState state, OnlineState online) => state switch
    {
        RuntimeState.Paused => TrayIconState.Paused,
        RuntimeState.Running when online == OnlineState.Online => TrayIconState.Online,
        _ => TrayIconState.Offline,
    };

    private static string Live(PowerReadingEventArgs last, HwinfoMonitorSnapshot hwinfo, OtherComponentsState? other, double? totalWatts, CultureInfo culture)
    {
        var text = $"AC {UiFormat.Watts(last.Power.AcWatts, culture)} · DC {UiFormat.Watts(last.Power.DcWatts, culture)}";
        if (hwinfo.IsRunning && other is { Source: not OtherSource.None } o)
        {
            text += $" · Other {UiFormat.Watts(o.Watts, culture)}";
        }

        if (totalWatts is double total)
        {
            text += " · Total " + total.ToString("0", culture) + " W";
        }

        return text;
    }
}
