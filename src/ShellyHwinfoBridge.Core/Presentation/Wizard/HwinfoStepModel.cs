using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>
/// Wizard step 1: is HWiNFO installed and running, and is Shared Memory Support on. Only reads (process list,
/// registry, shared memory). Next is always allowed; a missing HWiNFO is a warning.
/// </summary>
public sealed class HwinfoStepModel
{
    /// <summary>
    /// The how-to steps for turning on Shared Memory Support, in order (<see cref="InlineMarkup"/>: names of tabs,
    /// buttons and options are bold).
    /// </summary>
    public static readonly IReadOnlyList<string> HowToSteps =
    [
        "In HWiNFO's sensor window, open **Settings** (the gear icon), then click **Main Settings**.",
        "On the **General** tab, tick **Shared Memory Support** and click **OK**.",
        "Restart HWiNFO.",
        "Click **Check again** below.",
    ];

    /// <summary>The note about the free version's 12-hour shared memory limit (the only note under the status card).</summary>
    public const string FreeVersionNote =
        "The free HWiNFO version turns Shared Memory Support off after 12 hours. That is fine: the app only needs it during setup, and you can turn it off again afterwards.";

    private readonly Func<HwinfoStatus> _detect;
    private readonly Func<HwinfoSnapshot?> _readSharedMemory;

    /// <summary>Creates the model.</summary>
    /// <param name="detect">Detects the installation and process.</param>
    /// <param name="readSharedMemory">Reads the shared memory (<c>null</c> when off).</param>
    public HwinfoStepModel(Func<HwinfoStatus> detect, Func<HwinfoSnapshot?> readSharedMemory)
    {
        _detect = detect ?? throw new ArgumentNullException(nameof(detect));
        _readSharedMemory = readSharedMemory ?? throw new ArgumentNullException(nameof(readSharedMemory));
    }

    /// <summary>Raised after <see cref="Refresh"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the last detection result (<c>null</c> before the first <see cref="Refresh"/>).</summary>
    public HwinfoStatus? Status { get; private set; }

    /// <summary>Gets whether HWiNFO's shared memory could be read at the last refresh.</summary>
    public bool SharedMemoryOn { get; private set; }

    /// <summary>Gets the three status lines: installed, running, Shared Memory Support.</summary>
    public IReadOnlyList<WizardStatusLine> Lines { get; private set; } = [];

    /// <summary>Gets a warning when HWiNFO is missing, else <c>null</c>.</summary>
    public string? Warning { get; private set; }

    /// <summary>Detects HWiNFO and reads the shared memory again.</summary>
    public void Refresh()
    {
        var status = _detect();
        var sharedMemory = _readSharedMemory() is not null;
        Status = status;
        SharedMemoryOn = sharedMemory;

        var lines = new List<WizardStatusLine>
        {
            status.IsInstalled
                ? new WizardStatusLine(Tone.Success, status.ExecutablePath is { } path ? "Installed · " + path : "Installed")
                : new WizardStatusLine(Tone.Warning, "Not installed"),
            status.IsRunning
                ? new WizardStatusLine(Tone.Success, "Running")
                : new WizardStatusLine(Tone.Warning, "Not running"),
        };

        if (sharedMemory)
        {
            lines.Add(new WizardStatusLine(Tone.Success, "Shared Memory Support is on"));
        }
        else if (status.IsRunning)
        {
            lines.Add(new WizardStatusLine(Tone.Warning, "Shared Memory Support is off"));
        }
        else
        {
            lines.Add(new WizardStatusLine(Tone.Secondary, "Shared Memory Support: start HWiNFO to check"));
        }

        Lines = lines;
        Warning = status.IsInstalled
            ? null
            : "HWiNFO64 was not found. Install it from hwinfo.com and start it, then click Check again. You can also continue and install it later.";
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
