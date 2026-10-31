using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>An action offered after an HWiNFO restart attempt.</summary>
public enum RestartAction
{
    /// <summary>Try the restart again.</summary>
    Retry,

    /// <summary>Start HWiNFO from its detected path.</summary>
    StartHwinfo,

    /// <summary>Check (again) that the sensors are visible.</summary>
    Verify,
}

/// <summary>What to tell the user after an HWiNFO restart attempt.</summary>
/// <param name="Tone">Success or Warning.</param>
/// <param name="Text">The advice.</param>
/// <param name="Actions">The buttons to offer.</param>
public sealed record RestartAdvice(Tone Tone, string Text, IReadOnlyList<RestartAction> Actions)
{
    /// <summary>The CouldNotClose advice (HWiNFO usually runs elevated).</summary>
    public const string CouldNotCloseText =
        "HWiNFO is running as administrator, so this app can't close it. Exit HWiNFO from its tray icon, start it again, then click Verify.";

    /// <summary>The advice while HWiNFO does not run.</summary>
    public const string NotRunningText = "HWiNFO is not running. Start it, then click Verify.";

    /// <summary>The second sentence of a pending advice, while the verification looks for the sensors.</summary>
    public const string CheckingSensorsText = "Checking for the Shelly Power sensors…";

    /// <summary>The second sentence of a pending advice once the sensors were found.</summary>
    public const string SensorsFoundText = "Shelly Power sensors found.";

    /// <summary>The second sentence of a pending advice when the sensors did not show up within the grace period.</summary>
    public const string SensorsNotFoundText = "The Shelly Power sensors were not found yet. Check HWiNFO's sensor window, then click Verify.";

    /// <summary>
    /// Gets the first sentence ("HWiNFO restarted.") of an advice that waits for the verification, or <c>null</c> for
    /// a final advice. <see cref="VerifyEvaluator.ResolveAdvice"/> completes it with the verification's outcome.
    /// </summary>
    public string? PendingLead { get; init; }

    /// <summary>Creates an advice that waits for the verification ("HWiNFO restarted. Checking for the Shelly Power sensors…").</summary>
    /// <param name="lead">The first sentence ("HWiNFO restarted.").</param>
    public static RestartAdvice Checking(string lead) => new(Tone.Success, lead + " " + CheckingSensorsText, []) { PendingLead = lead };

    /// <summary>Maps a restart result to advice.</summary>
    /// <param name="result">The result.</param>
    /// <param name="canStart">Whether HWiNFO's path is known (offer "Start HWiNFO").</param>
    public static RestartAdvice From(HwinfoRestartResult result, bool canStart) => result switch
    {
        HwinfoRestartResult.Restarted => Checking("HWiNFO restarted."),
        HwinfoRestartResult.CouldNotClose => new(Tone.Warning, CouldNotCloseText, [RestartAction.Retry, RestartAction.Verify]),
        HwinfoRestartResult.RelaunchFailed => new(
            Tone.Warning,
            "HWiNFO was closed but could not be started again. Start it manually, then click Verify.",
            canStart ? [RestartAction.StartHwinfo, RestartAction.Verify] : [RestartAction.Verify]),
        HwinfoRestartResult.ExecutableNotFound => new(Tone.Warning, "Restart HWiNFO manually, then click Verify.", [RestartAction.Verify]),
        _ => new(
            Tone.Warning,
            NotRunningText,
            canStart ? [RestartAction.StartHwinfo, RestartAction.Verify] : [RestartAction.Verify]),
    };
}

/// <summary>A sensor in the verify checklist.</summary>
/// <param name="Label">The sensor name ("PC Power (AC)", or the user's custom name).</param>
/// <param name="Found">Whether HWiNFO shows it under "Shelly Power".</param>
public sealed record VerifyItem(string Label, bool Found);

/// <summary>One verification result.</summary>
/// <param name="SharedMemoryAvailable">Whether shared memory could be read.</param>
/// <param name="Items">The power sensors of the layout (PC AC/DC/Loss/Other, each device's AC, Total).</param>
/// <param name="Tiles">The live AC/DC/Loss/Other values.</param>
/// <param name="Implausible">Whether Other Components is negative or more than half of DC.</param>
/// <param name="Message">A warning (implausible, shared memory off), or <c>null</c>.</param>
public sealed record VerifyResult(bool SharedMemoryAvailable, IReadOnlyList<VerifyItem> Items, IReadOnlyList<WizardTile> Tiles, bool Implausible, string? Message)
{
    /// <summary>Gets whether every sensor is visible.</summary>
    public bool AllFound => Items.Count > 0 && Items.All(i => i.Found);
}

/// <summary>Checks HWiNFO's shared memory for the Shelly Power sensors (by their current names) and judges the Other value.</summary>
public static class VerifyEvaluator
{
    /// <summary>The message when shared memory is off.</summary>
    public const string SharedMemoryOffText =
        "Shared memory is off, so the app can't confirm. Look for 'Shelly Power' in HWiNFO's sensor window.";

    /// <summary>The default names of the four PC power sensors.</summary>
    public static IReadOnlyList<string> Labels { get; } =
        [SensorNaming.PcAcName, SensorNaming.PcDcName, SensorNaming.PcLossName, SensorNaming.PcOtherName];

    /// <summary>Returns whether an Other value is implausible: negative or more than half of DC.</summary>
    /// <param name="other">Other Components in W.</param>
    /// <param name="dc">DC in W.</param>
    public static bool IsImplausible(double other, double dc) => other < 0 || (dc > 0 && other > 0.5 * dc);

    /// <summary>
    /// Completes an advice that waits for the verification (<see cref="RestartAdvice.PendingLead"/>): "… Shelly Power
    /// sensors found." (success) once they are visible, "… were not found yet." (warning, Verify) after the grace period,
    /// else unchanged ("… Checking for the Shelly Power sensors…"). A final advice is returned as it is.
    /// </summary>
    /// <param name="advice">The advice, or <c>null</c>.</param>
    /// <param name="sensorsFound">Whether every Shelly Power sensor was found.</param>
    /// <param name="gracePeriodOver">Whether the verification has looked for longer than the grace period (or timed out).</param>
    public static RestartAdvice? ResolveAdvice(RestartAdvice? advice, bool sensorsFound, bool gracePeriodOver)
    {
        if (advice?.PendingLead is not { } lead)
        {
            return advice;
        }

        if (sensorsFound)
        {
            return advice with { Tone = Tone.Success, Text = lead + " " + RestartAdvice.SensorsFoundText, Actions = [] };
        }

        return gracePeriodOver
            ? advice with { Tone = Tone.Warning, Text = lead + " " + RestartAdvice.SensorsNotFoundText, Actions = [RestartAction.Verify] }
            : advice;
    }

    /// <summary>
    /// Re-evaluates the HWiNFO card when the user clicks Verify: a closed HWiNFO turns the card into the "not running"
    /// warning (dropping a green "restarted" check); a running HWiNFO after a warning waits for the verification again
    /// ("HWiNFO is running. Checking…"); without an earlier advice the card keeps showing the status text.
    /// </summary>
    /// <param name="advice">The current advice, or <c>null</c>.</param>
    /// <param name="status">HWiNFO's detected status, or <c>null</c> when it could not be detected (no change).</param>
    public static RestartAdvice? ReviewHwinfo(RestartAdvice? advice, HwinfoStatus? status) => status switch
    {
        null => advice,
        { IsRunning: false } => RestartAdvice.From(HwinfoRestartResult.NotRunning, status.ExecutablePath is not null),
        _ when advice is null || advice.PendingLead is not null => advice,
        _ => RestartAdvice.Checking("HWiNFO is running."),
    };

    /// <summary>Evaluates a snapshot.</summary>
    /// <param name="snapshot">HWiNFO's shared memory, or <c>null</c> when it is off.</param>
    /// <param name="poller">The bridge's latest PC reading (fallback for AC/DC/Loss), or <c>null</c>.</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    /// <param name="layout">The published layout (names); <c>null</c> for <see cref="SensorLayout.Default"/>.</param>
    /// <param name="other">The app-computed Other Components (fallback for the Other tile), or <c>null</c>.</param>
    public static VerifyResult Evaluate(HwinfoSnapshot? snapshot, PowerReading? poller, CultureInfo? culture = null, SensorLayout? layout = null, OtherComponentsState? other = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        layout ??= SensorLayout.Default;
        var slots = layout.Slots.Where(s => s.Key.ValueKind == SensorValueKind.Watts).ToList();
        var items = slots.Select(s => new VerifyItem(s.Name, snapshot?.FindReading(s.Name, WizardText.BridgeDevice) is not null)).ToList();

        double? Read(SensorKey key) => layout[key] is { } slot ? snapshot?.FindReading(slot.Name, WizardText.BridgeDevice)?.Value : null;
        double? ac = Read(SensorKey.PcAc) ?? poller?.AcWatts;
        double? dc = Read(SensorKey.PcDc) ?? poller?.DcWatts;
        double? loss = Read(SensorKey.PcLoss) ?? poller?.LossWatts;
        double? otherWatts = Read(SensorKey.PcOther) ?? (other is { Source: not OtherSource.None } o ? o.Watts : null);

        string Watts(double? w) => w is double v ? WizardText.SignedWatts(v, culture) : UiFormat.Dash;
        var tiles = new List<WizardTile>
        {
            new("AC", Watts(ac)),
            new("DC", Watts(dc)),
            new("Loss", Watts(loss)),
            new("Other", Watts(otherWatts)),
        };

        if (snapshot is null)
        {
            return new VerifyResult(false, items, tiles, false, SharedMemoryOffText);
        }

        var implausible = otherWatts is double ow && dc is double d && IsImplausible(ow, d);
        var message = implausible
            ? $"Other Components looks implausible ({Watts(otherWatts)}). Check the sensor selection; a sensor may be counted twice or missing."
            : null;
        return new VerifyResult(true, items, tiles, implausible, message);
    }
}
