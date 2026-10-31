using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>One selected sensor of the gadget how-to with its live marker.</summary>
/// <param name="Label">The sensor name (with its device when the name is not unique).</param>
/// <param name="Reported">Whether HWiNFO's gadget export reports it (green tick), else a red cross.</param>
public sealed record GadgetSensorItem(string Label, bool Reported);

/// <summary>
/// The gadget how-to of wizard step 5 and of the "How to enable?" help dialog (Other card, Settings › Sensors): the
/// numbered steps, the selected component sensors under step 3 ("Enable this for:"), each with a live marker read from
/// HWiNFO's gadget export (<see cref="Refresh"/>, about once per second while shown), the note about the free version's
/// 12-hour Shared Memory limit, and the confirmation shown by Next while a sensor is not reported yet.
/// </summary>
public sealed class GadgetStepModel
{
    /// <summary>The heading (wizard page title, help dialog title).</summary>
    public const string Heading = "Report the sensors to the HWiNFO Gadget";

    /// <summary>The wizard page subtitle.</summary>
    public const string Subtitle = "Keeps Other Components working when HWiNFO's Shared Memory is off.";

    /// <summary>Step 3's lead-in before the sensor list.</summary>
    public const string EnableForText = "Enable this for:";

    /// <summary>The note under the steps.</summary>
    public const string FreeVersionNote =
        "The free HWiNFO version turns Shared Memory off after 12 hours. Reporting the sensors to the gadget keeps Other Components working without it.";

    /// <summary>Shown instead of the sensor list when no component sensor is selected.</summary>
    public const string NoSensorsText = "No component sensors are selected, so there is nothing to report.";

    /// <summary>Shown while HWiNFO does not run (the markers cannot be read).</summary>
    public const string NotRunningText = "HWiNFO is not running, so the markers can't be checked. Start HWiNFO to see them change.";

    /// <summary>Shown while HWiNFO's Shared Memory is on: Other Components already works without the gadget export.</summary>
    public const string SharedMemoryOnText =
        "Shared Memory is on, so Other Components already works. The markers show the gadget export, which keeps it working after Shared Memory turns off.";

    /// <summary>The title of the confirmation shown by Next while a sensor is not reported.</summary>
    public const string ConfirmTitle = "Some sensors are not reported yet";

    /// <summary>The confirmation's button that moves on.</summary>
    public const string ContinueText = "Continue anyway";

    /// <summary>The confirmation's button that stays on the step.</summary>
    public const string GoBackText = "Go back";

    private readonly Func<IReadOnlyList<GadgetReading>?> _readExport;
    private readonly Func<bool> _sharedMemoryOn;
    private IReadOnlyList<SensorSelection> _sensors = [];

    /// <summary>Creates the model.</summary>
    /// <param name="readExport">Reads the gadget export, or returns <c>null</c> while HWiNFO does not run.</param>
    /// <param name="sharedMemoryOn">Tells whether HWiNFO's Shared Memory is currently available; <c>null</c> for never.</param>
    public GadgetStepModel(Func<IReadOnlyList<GadgetReading>?> readExport, Func<bool>? sharedMemoryOn = null)
    {
        _readExport = readExport ?? throw new ArgumentNullException(nameof(readExport));
        _sharedMemoryOn = sharedMemoryOn ?? (() => false);
    }

    /// <summary>Raised when the markers changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the numbered steps (<see cref="InlineMarkup"/>); the third ends with <see cref="EnableForText"/> and is
    /// followed by <see cref="Items"/> as an indented list.
    /// </summary>
    public static IReadOnlyList<string> Steps { get; } =
    [
        "In HWiNFO's sensor window, open **Settings**.",
        "Open the **HWiNFO Gadget** tab and tick **Enable reporting to Gadget**.",
        "Select each sensor below in the list and tick **Report value in Gadget**, so its Sidebar column shows Yes.\n" + EnableForText,
        "Click **OK**.",
    ];

    /// <summary>Gets the index of the step that is followed by the sensor list.</summary>
    public static int SensorListStep => 2;

    /// <summary>Gets the selected sensors with their markers (empty when none is selected).</summary>
    public IReadOnlyList<GadgetSensorItem> Items { get; private set; } = [];

    /// <summary>Gets whether HWiNFO's export could be read on the last <see cref="Refresh"/> (HWiNFO runs).</summary>
    public bool ExportAvailable { get; private set; }

    /// <summary>Gets whether HWiNFO's Shared Memory was available on the last <see cref="Refresh"/>.</summary>
    public bool SharedMemoryOn { get; private set; }

    /// <summary>Gets the shared-memory note: <see cref="SharedMemoryOnText"/> while Shared Memory is on, else <c>null</c>.</summary>
    public string? SharedMemoryText => SharedMemoryOn ? SharedMemoryOnText : null;

    /// <summary>Gets the status note: <see cref="NotRunningText"/> while HWiNFO does not run (no export, no Shared Memory), else <c>null</c>.</summary>
    public string? StatusText => ExportAvailable || SharedMemoryOn ? null : NotRunningText;

    /// <summary>Gets the sensors that are not reported yet (red cross).</summary>
    public IReadOnlyList<string> Unreported => Items.Where(i => !i.Reported).Select(i => i.Label).ToList();

    /// <summary>Gets whether every selected sensor is reported (vacuously true without sensors).</summary>
    public bool AllReported => Items.All(i => i.Reported);

    /// <summary>Gets whether Next should ask first (<see cref="ConfirmText"/>): a selected sensor is not reported yet.</summary>
    public bool ShouldConfirmNext => !AllReported;

    /// <summary>Gets the confirmation text: the sensors that are not reported yet, why all should be, and the Pro alternative.</summary>
    public string ConfirmText =>
        "These sensors are not reported to the HWiNFO Gadget yet:" + Environment.NewLine + Environment.NewLine
        + string.Join(Environment.NewLine, Unreported.Select(l => "•  " + l)) + Environment.NewLine + Environment.NewLine
        + "Report all of them, so Other Components keeps working when Shared Memory is off. With HWiNFO Pro you can simply leave Shared Memory on instead.";

    /// <summary>Sets the selected sensors (blank names are skipped) and reads the export.</summary>
    /// <param name="sensors">The selected component sensors.</param>
    public void Load(IEnumerable<SensorSelection> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        _sensors = sensors.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
        Items = [];
        Update(force: true);
    }

    /// <summary>Reads the gadget export and updates the markers; raises <see cref="Changed"/> when something changed.</summary>
    public void Refresh() => Update(force: false);

    private void Update(bool force)
    {
        IReadOnlyList<GadgetReading>? exported;
        try
        {
            exported = _readExport();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            exported = null;
        }

        var labels = SensorDisplay.Labels(_sensors);
        var items = _sensors.Select((s, i) => new GadgetSensorItem(labels[i], exported is not null && HwinfoMonitor.IsReportedToGadget(exported, s))).ToList();
        bool sharedMemory;
        try
        {
            sharedMemory = _sharedMemoryOn();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            sharedMemory = false;
        }

        var available = exported is not null;
        if (!force && available == ExportAvailable && sharedMemory == SharedMemoryOn && items.SequenceEqual(Items))
        {
            return;
        }

        Items = items;
        ExportAvailable = available;
        SharedMemoryOn = sharedMemory;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
