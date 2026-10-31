using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Shelly.Discovery;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>The seven steps of the setup wizard, in order.</summary>
public enum WizardStep
{
    /// <summary>Step 1: HWiNFO check.</summary>
    Hwinfo = 0,

    /// <summary>Step 2: Shelly connection.</summary>
    Shelly = 1,

    /// <summary>Step 3: PSU selection.</summary>
    Psu = 2,

    /// <summary>Step 4: component sensors.</summary>
    Sensors = 3,

    /// <summary>Step 5: review and apply.</summary>
    Apply = 4,

    /// <summary>Step 6: report the component sensors to the HWiNFO Gadget (after Apply).</summary>
    Gadget = 5,

    /// <summary>Step 7: restart HWiNFO and verify the sensors.</summary>
    Verify = 6,
}

/// <summary>An entry of a drop-down: a value (<c>null</c> for "Any" or "—") and its text.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="Text">The text shown.</param>
public sealed record WizardOption<T>(T Value, string Text);

/// <summary>A status line with a tone ("Running", "Shared Memory Support is off").</summary>
/// <param name="Tone">The tone (Success, Warning, Error or Secondary).</param>
/// <param name="Text">The text.</param>
public sealed record WizardStatusLine(Tone Tone, string Text);

/// <summary>
/// Everything the wizard reads from the outside world. Nothing here writes: the registry, settings and autostart are
/// only changed by <see cref="IBridgeRuntime.ApplyFromWizard"/>, and HWiNFO is only restarted or started on request.
/// </summary>
/// <param name="Runtime">The bridge runtime (connection test, apply, live readings).</param>
/// <param name="DetectHwinfo">Detects the HWiNFO installation and process.</param>
/// <param name="ReadSharedMemory">Reads HWiNFO's shared memory, or <c>null</c> when it is off.</param>
/// <param name="DiscoverShellys">Browses the network for Shelly plugs.</param>
/// <param name="RestartHwinfo">Closes and relaunches HWiNFO (runs in the background).</param>
/// <param name="StartHwinfo">Starts HWiNFO from its detected path; <c>false</c> when it could not be started.</param>
/// <param name="Time">The clock (verify timeout); <c>null</c> for the system clock.</param>
/// <param name="ReadGadgetExport">Reads HWiNFO's gadget export (step 6 markers), <c>null</c> while HWiNFO does not run; <c>null</c> for none.</param>
public sealed record WizardDependencies(
    IBridgeRuntime Runtime,
    Func<HwinfoStatus> DetectHwinfo,
    Func<HwinfoSnapshot?> ReadSharedMemory,
    Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> DiscoverShellys,
    Func<CancellationToken, Task<HwinfoRestartResult>> RestartHwinfo,
    Func<bool> StartHwinfo,
    TimeProvider? Time = null,
    Func<IReadOnlyList<GadgetReading>?>? ReadGadgetExport = null)
{
    /// <summary>Builds the dependencies from the app's services.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="locator">The HWiNFO locator.</param>
    /// <param name="reader">The shared-memory reader.</param>
    /// <param name="discovery">The mDNS discovery.</param>
    /// <param name="time">The clock, or <c>null</c> for the system clock.</param>
    /// <param name="readGadgetExport">Reads HWiNFO's gadget export (<see cref="HwinfoMonitor.ReadGadgetExport"/>), or <c>null</c>.</param>
    public static WizardDependencies Create(
        IBridgeRuntime runtime,
        HwinfoLocator locator,
        HwinfoSharedMemoryReader reader,
        ShellyDiscovery discovery,
        TimeProvider? time = null,
        Func<IReadOnlyList<GadgetReading>?>? readGadgetExport = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(discovery);
        return new WizardDependencies(
            runtime,
            locator.Detect,
            reader.TryGetSnapshot,
            ct => discovery.DiscoverAsync(cancellationToken: ct),
            ct => Task.Run(() => locator.RestartAsync(cancellationToken: ct), ct),
            locator.Start,
            time,
            readGadgetExport);
    }
}

/// <summary>Text helpers shared by the wizard models.</summary>
public static class WizardText
{
    /// <summary>The HWiNFO device name of the bridge's own sensors.</summary>
    public const string BridgeDevice = "Shelly Power";

    /// <summary>The "Any" filter entry.</summary>
    public const string Any = "Any";

    /// <summary>Parses a number typed by the user: current culture first, then the invariant culture.</summary>
    /// <param name="text">The text.</param>
    /// <param name="culture">The current culture.</param>
    /// <param name="value">The number.</param>
    public static bool TryParseNumber(string? text, CultureInfo culture, out double value)
    {
        var t = (text ?? string.Empty).Trim().TrimEnd('%', 'W', 'w').Trim();
        if (t.Length > 0 &&
            (double.TryParse(t, NumberStyles.Float, culture, out value) ||
             double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
            double.IsFinite(value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Formats a signed watt value with a typographic minus ("−12.0 W").</summary>
    /// <param name="watts">The value.</param>
    /// <param name="culture">The culture.</param>
    public static string SignedWatts(double watts, CultureInfo culture) =>
        (watts < 0 ? "−" : string.Empty) + Math.Abs(watts).ToString("0.0", culture) + " W";

    /// <summary>Returns whether an HWiNFO device name is the bridge's own "Shelly Power" device.</summary>
    /// <param name="device">The device name.</param>
    public static bool IsBridgeDevice(string? device)
    {
        var d = (device ?? string.Empty).Trim();
        return d.Equals(BridgeDevice, StringComparison.OrdinalIgnoreCase) ||
               d.EndsWith(": " + BridgeDevice, StringComparison.OrdinalIgnoreCase);
    }
}
