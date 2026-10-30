using System.Globalization;
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

    /// <summary>Maps a restart result to advice.</summary>
    /// <param name="result">The result.</param>
    /// <param name="canStart">Whether HWiNFO's path is known (offer "Start HWiNFO").</param>
    public static RestartAdvice From(HwinfoRestartResult result, bool canStart) => result switch
    {
        HwinfoRestartResult.Restarted => new(Tone.Success, "HWiNFO restarted. Checking for the Shelly Power sensors…", []),
        HwinfoRestartResult.CouldNotClose => new(Tone.Warning, CouldNotCloseText, [RestartAction.Retry, RestartAction.Verify]),
        HwinfoRestartResult.RelaunchFailed => new(
            Tone.Warning,
            "HWiNFO was closed but could not be started again. Start it manually, then click Verify.",
            canStart ? [RestartAction.StartHwinfo, RestartAction.Verify] : [RestartAction.Verify]),
        HwinfoRestartResult.ExecutableNotFound => new(Tone.Warning, "Restart HWiNFO manually, then click Verify.", [RestartAction.Verify]),
        _ => new(
            Tone.Warning,
            "HWiNFO is not running. Start it, then click Verify.",
            canStart ? [RestartAction.StartHwinfo, RestartAction.Verify] : [RestartAction.Verify]),
    };
}

/// <summary>A sensor in the verify checklist.</summary>
/// <param name="Label">The sensor name ("AC Power (Wall)").</param>
/// <param name="Found">Whether HWiNFO shows it under "Shelly Power".</param>
public sealed record VerifyItem(string Label, bool Found);

/// <summary>One verification result.</summary>
/// <param name="SharedMemoryAvailable">Whether shared memory could be read.</param>
/// <param name="Items">The four sensors.</param>
/// <param name="Tiles">The live AC/DC/Loss/Other values.</param>
/// <param name="Implausible">Whether Other Components is negative or more than half of DC.</param>
/// <param name="Message">A warning (implausible, shared memory off), or <c>null</c>.</param>
public sealed record VerifyResult(bool SharedMemoryAvailable, IReadOnlyList<VerifyItem> Items, IReadOnlyList<WizardTile> Tiles, bool Implausible, string? Message)
{
    /// <summary>Gets whether all four sensors are visible.</summary>
    public bool AllFound => Items.Count > 0 && Items.All(i => i.Found);
}

/// <summary>Checks HWiNFO's shared memory for the four Shelly Power sensors and judges the Other value.</summary>
public static class VerifyEvaluator
{
    /// <summary>The message when shared memory is off.</summary>
    public const string SharedMemoryOffText =
        "Shared memory is off, so the app can't confirm. Look for 'Shelly Power' in HWiNFO's sensor window.";

    /// <summary>The four labels to look for.</summary>
    public static IReadOnlyList<string> Labels { get; } =
        [HwinfoSensorNames.AcPower, HwinfoSensorNames.DcPower, HwinfoSensorNames.PsuLoss, HwinfoSensorNames.OtherComponents];

    /// <summary>Returns whether an Other value is implausible: negative or more than half of DC.</summary>
    /// <param name="other">Other Components in W.</param>
    /// <param name="dc">DC in W.</param>
    public static bool IsImplausible(double other, double dc) => other < 0 || (dc > 0 && other > 0.5 * dc);

    /// <summary>Evaluates a snapshot.</summary>
    /// <param name="snapshot">HWiNFO's shared memory, or <c>null</c> when it is off.</param>
    /// <param name="poller">The bridge's latest reading (fallback for AC/DC/Loss), or <c>null</c>.</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    public static VerifyResult Evaluate(HwinfoSnapshot? snapshot, PowerReading? poller, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var readings = Labels.Select(l => snapshot?.FindReading(l, WizardText.BridgeDevice)).ToList();
        var items = Labels.Select((l, i) => new VerifyItem(l, readings[i] is not null)).ToList();

        double? ac = readings[0]?.Value ?? poller?.AcWatts;
        double? dc = readings[1]?.Value ?? poller?.DcWatts;
        double? loss = readings[2]?.Value ?? poller?.LossWatts;
        double? other = readings[3]?.Value;

        string Watts(double? w) => w is double v ? WizardText.SignedWatts(v, culture) : UiFormat.Dash;
        var tiles = new List<WizardTile>
        {
            new("AC", Watts(ac)),
            new("DC", Watts(dc)),
            new("Loss", Watts(loss)),
            new("Other", Watts(other)),
        };

        if (snapshot is null)
        {
            return new VerifyResult(false, items, tiles, false, SharedMemoryOffText);
        }

        var implausible = other is double o && dc is double d && IsImplausible(o, d);
        var message = implausible
            ? $"Other Components looks implausible ({Watts(other)}). Check the sensor selection; a sensor may be counted twice or missing."
            : null;
        return new VerifyResult(true, items, tiles, implausible, message);
    }
}
