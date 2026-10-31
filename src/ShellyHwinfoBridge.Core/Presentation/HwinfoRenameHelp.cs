using ShellyHwinfoBridge.Core.Hwinfo;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>One renamed HWiNFO sensor: the name HWiNFO still shows and the name the app now publishes.</summary>
/// <param name="OldName">The old name (what HWiNFO keeps showing).</param>
/// <param name="NewName">The new name.</param>
public sealed record SensorRename(string OldName, string NewName);

/// <summary>
/// The "Update the names in HWiNFO" help (SPEC "HWiNFO keeps old labels"): HWiNFO reads a custom sensor's name once and
/// keeps its own label per sensor slot, so a rename in the app does not change what HWiNFO shows, even after an HWiNFO
/// restart. Lists the affected sensors (old name → new name) and the two fixes. Texts use <see cref="InlineMarkup"/>.
/// </summary>
public sealed class HwinfoRenameHelp
{
    /// <summary>The dialog title.</summary>
    public const string Title = "Update the names in HWiNFO";

    /// <summary>The caption of the buttons that open the help.</summary>
    public const string ShowMeHowText = "Show me how";

    /// <summary>The text above the list of renamed sensors.</summary>
    public const string Intro =
        "HWiNFO stores its own name for each sensor the first time it sees it. When a name changes here, HWiNFO keeps showing the old name, even after you restart it. These sensors are affected:";

    /// <summary>The text shown when no sensor name changes (the list is empty).</summary>
    public const string NoRenamesText = "No sensor names change.";

    /// <summary>The heading of fix 1.</summary>
    public const string Fix1Title = "Option 1: rename the sensors in HWiNFO";

    /// <summary>Fix 1: rename each sensor in HWiNFO's sensor window (keeps every other HWiNFO customisation).</summary>
    public const string Fix1Text =
        "In HWiNFO's sensor window, right-click each sensor listed above, choose **Rename** and enter its new name. All your other HWiNFO customisations stay as they are.";

    /// <summary>The heading of fix 2.</summary>
    public const string Fix2Title = "Option 2: reset HWiNFO's preferences";

    /// <summary>Fix 2: HWiNFO Settings › Reset Preferences, then restart both apps in order (discards all customisations).</summary>
    public const string Fix2Text =
        "Only if you have no HWiNFO customisations you want to keep, because this discards all of them. In HWiNFO open **Settings** and click **Reset Preferences** (see the screenshot). Then close HWiNFO and this app, start this app, and then start HWiNFO.";

    /// <summary>The closing note about everything else that refers to sensors by name.</summary>
    public const string FollowUpText =
        "Also update HWiNFO custom formulas and OSD, tray or gadget selections that use the old names.";

    /// <summary>The screenshot shown with fix 2 (embedded in the app).</summary>
    public const string Fix2Screenshot = "hwinfo-reset-preferences.png";

    /// <summary>Creates the help for a list of renames (blank pairs, unchanged names and duplicates are skipped).</summary>
    /// <param name="renames">The renamed sensors, in display order.</param>
    public HwinfoRenameHelp(IEnumerable<SensorRename> renames)
    {
        ArgumentNullException.ThrowIfNull(renames);
        var list = new List<SensorRename>();
        foreach (var rename in renames)
        {
            var oldName = rename.OldName?.Trim() ?? string.Empty;
            var newName = rename.NewName?.Trim() ?? string.Empty;
            if (oldName.Length == 0 || newName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                continue;
            }

            var pair = new SensorRename(oldName, newName);
            if (!list.Contains(pair))
            {
                list.Add(pair);
            }
        }

        Renames = list;
    }

    /// <summary>Gets the renamed sensors (old name → new name), in display order.</summary>
    public IReadOnlyList<SensorRename> Renames { get; }

    /// <summary>Gets the list as display lines ("Monitor Power (AC) → Desk Power (AC)").</summary>
    public IReadOnlyList<string> RenameLines => Renames.Select(r => r.OldName + " → " + r.NewName).ToList();

    /// <summary>
    /// Builds the help for relabelling an additional device: its AC and Online sensors change from the old label's
    /// default names to the new label's. A sensor with a valid custom name keeps it and is not listed.
    /// </summary>
    /// <param name="deviceId">The device id.</param>
    /// <param name="oldLabel">The label before the change.</param>
    /// <param name="newLabel">The new label.</param>
    /// <param name="customNames">The custom sensor names (<see cref="Settings.AppSettings.SensorNames"/>), or <c>null</c>.</param>
    public static HwinfoRenameHelp ForDeviceRelabel(string deviceId, string oldLabel, string newLabel, IReadOnlyDictionary<string, string>? customNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var renames = new List<SensorRename>();
        if (!HasCustomName(customNames, SensorKey.DeviceAc(deviceId)))
        {
            renames.Add(new SensorRename(SensorNaming.DefaultDeviceAcName(oldLabel), SensorNaming.DefaultDeviceAcName(newLabel)));
        }

        if (!HasCustomName(customNames, SensorKey.DeviceOnline(deviceId)))
        {
            renames.Add(new SensorRename(SensorNaming.DefaultDeviceOnlineName(oldLabel), SensorNaming.DefaultDeviceOnlineName(newLabel)));
        }

        return new HwinfoRenameHelp(renames);
    }

    /// <summary>Builds the help for a "Customize HWiNFO sensor names" change: every sensor whose effective name changed.</summary>
    /// <param name="before">The effective names before the change, by sensor key id.</param>
    /// <param name="after">The effective names after the change, by sensor key id, in display order.</param>
    public static HwinfoRenameHelp ForSensorNames(IReadOnlyDictionary<string, string> before, IEnumerable<KeyValuePair<string, string>> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return new HwinfoRenameHelp(after
            .Where(p => before.TryGetValue(p.Key, out var old) && !string.Equals(old, p.Value, StringComparison.Ordinal))
            .Select(p => new SensorRename(before[p.Key], p.Value)));
    }

    private static bool HasCustomName(IReadOnlyDictionary<string, string>? customNames, SensorKey key) =>
        customNames is not null && customNames.TryGetValue(key.Id, out var name) && SensorNaming.IsValidName(name);
}
