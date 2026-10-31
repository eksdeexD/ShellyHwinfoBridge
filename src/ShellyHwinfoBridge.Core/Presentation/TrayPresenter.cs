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

/// <summary>Builds the tray tooltip and icon state.</summary>
public static class TrayPresenter
{
    /// <summary>The longest tooltip a NotifyIcon accepts (.NET 8).</summary>
    public const int MaxTooltipLength = 127;

    /// <summary>
    /// Returns the tooltip: "AC 312.4 W · DC 294.8 W · Other 48.1 W" (Other only when HWiNFO's value is available
    /// from shared memory or its gadget export), "Shelly offline", "Polling paused", "Setup not completed", …; never longer than
    /// <see cref="MaxTooltipLength"/>.
    /// </summary>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The online state.</param>
    /// <param name="last">The last reading.</param>
    /// <param name="hwinfo">The HWiNFO snapshot.</param>
    /// <param name="culture">The culture; defaults to the current culture.</param>
    public static string Tooltip(RuntimeState state, OnlineState online, PowerReadingEventArgs? last, HwinfoMonitorSnapshot hwinfo, CultureInfo? culture = null)
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
                OnlineState.Online when last is not null => Live(last, hwinfo, culture),
                _ => "ShellyHwinfoBridge · connecting…",
            },
        };
        return text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
    }

    /// <summary>Returns the icon state.</summary>
    /// <param name="state">The runtime state.</param>
    /// <param name="online">The online state.</param>
    public static TrayIconState IconState(RuntimeState state, OnlineState online) => state switch
    {
        RuntimeState.Paused => TrayIconState.Paused,
        RuntimeState.Running when online == OnlineState.Online => TrayIconState.Online,
        _ => TrayIconState.Offline,
    };

    private static string Live(PowerReadingEventArgs last, HwinfoMonitorSnapshot hwinfo, CultureInfo culture)
    {
        var text = $"AC {UiFormat.Watts(last.Power.AcWatts, culture)} · DC {UiFormat.Watts(last.Power.DcWatts, culture)}";
        if (hwinfo.OtherSource != OtherSource.None && hwinfo.OtherWatts is double other)
        {
            text += $" · Other {UiFormat.Watts(other, culture)}";
        }

        return text;
    }
}
