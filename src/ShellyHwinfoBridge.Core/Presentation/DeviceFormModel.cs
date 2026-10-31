using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>Whether a <see cref="DeviceFormModel"/> adds a device or edits one.</summary>
public enum DeviceFormMode
{
    /// <summary>Adding a new additional device.</summary>
    Add,

    /// <summary>Editing an existing device (the PC or an additional device).</summary>
    Edit,
}

/// <summary>
/// The "Add a Shelly for your monitor or another device" form, shared by wizard step 2 and Manage Shellys (Add / Edit):
/// label, discovery list or manual host, password, Test connection and "Include in totals". The connection part is a
/// <see cref="ShellyStepModel"/> (<see cref="Connection"/>) with the opening question already answered.
/// </summary>
/// <remarks>
/// Editing the PC's plug: the label is fixed to "PC" and "Include in totals" does not apply (<see cref="IsPrimary"/>).
/// Not thread-safe: call from the UI thread.
/// </remarks>
public sealed class DeviceFormModel
{
    /// <summary>The form title when adding.</summary>
    public const string AddTitle = "Add a Shelly for your monitor or another device";

    /// <summary>The window title of the form when adding (the page heading carries <see cref="AddTitle"/>).</summary>
    public const string AddWindowTitle = "Add a Shelly";

    /// <summary>The window title of the form when editing.</summary>
    public const string EditWindowTitle = "Edit Shelly";

    /// <summary>The hover tooltip of "Include in totals".</summary>
    public const string IncludeInTotalsTooltip =
        "If enabled, this device's energy is added to the totals, cost and the chart. If disabled, it is still measured and shown in HWiNFO but left out of the totals.";

    /// <summary>
    /// The warning shown before saving a changed label of an existing device (also used by the sensor-names dialog). HWiNFO
    /// keeps its own label per sensor, so the user must rename the sensors in HWiNFO (<see cref="HwinfoRenameHelp"/>).
    /// </summary>
    public const string RenameWarningText =
        "This changes the HWiNFO sensor names, but HWiNFO keeps showing the old names until you rename the sensors in HWiNFO (right-click › Rename) or reset its preferences. HWiNFO custom formulas and OSD, tray or gadget selections that use the old names also need updating.";

    private readonly IReadOnlyList<string> _otherLabels;
    private readonly IReadOnlyDictionary<string, string>? _sensorNames;
    private readonly string? _originalLabel;
    private readonly string? _originalPassword;
    private string _label;

    /// <summary>Creates a form.</summary>
    /// <param name="discover">Browses the network.</param>
    /// <param name="test">Tests a host and password.</param>
    /// <param name="otherLabels">The labels of the other additional devices (for uniqueness).</param>
    /// <param name="existing">The device being edited (<see cref="ShellyDeviceSettings"/> for the PC, <see cref="AdditionalDeviceSettings"/> otherwise), or <c>null</c> to add one.</param>
    /// <param name="storedPassword">The device's stored password when editing, or <c>null</c>.</param>
    /// <param name="defaultLabel">The label a new device starts with; <c>null</c> for the first free "Monitor" label.</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    /// <param name="sensorNames">The custom sensor names (<see cref="AppSettings.SensorNames"/>) for <see cref="RenameHelp"/>; <c>null</c> for none.</param>
    public DeviceFormModel(
        Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> discover,
        Func<string, string?, CancellationToken, Task<ConnectionTestResult>> test,
        IReadOnlyList<string> otherLabels,
        ShellyDeviceSettings? existing = null,
        string? storedPassword = null,
        string? defaultLabel = null,
        CultureInfo? culture = null,
        IReadOnlyDictionary<string, string>? sensorNames = null)
    {
        ArgumentNullException.ThrowIfNull(otherLabels);
        _otherLabels = otherLabels;
        _sensorNames = sensorNames;
        Connection = new ShellyStepModel(discover, test, culture);
        Mode = existing is null ? DeviceFormMode.Add : DeviceFormMode.Edit;
        IsPrimary = existing is not null && existing is not AdditionalDeviceSettings;
        var additional = existing as AdditionalDeviceSettings;
        DeviceId = IsPrimary ? AppSettings.PrimaryDeviceId : additional?.Id ?? AdditionalDeviceSettings.NewId();
        _label = IsPrimary ? AppSettings.PrimaryLabel : additional?.Label ?? defaultLabel ?? DeviceLabelRules.NextDefaultLabel(otherLabels);
        _originalLabel = additional?.Label;
        IncludeInTotals = additional?.IncludeInTotals ?? true;
        Colour = existing?.Colour;
        _originalPassword = storedPassword;
        if (existing is not null)
        {
            Connection.LoadFrom(existing, storedPassword);
        }

        Connection.SkipQuestion();
        Connection.Changed += (_, _) => OnChanged();
    }

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets whether the form adds or edits.</summary>
    public DeviceFormMode Mode { get; }

    /// <summary>Gets whether the PC's plug is edited (label "PC", no include toggle, cannot be removed).</summary>
    public bool IsPrimary { get; }

    /// <summary>Gets the device id (a new GUID when adding).</summary>
    public string DeviceId { get; }

    /// <summary>Gets the title ("Add a Shelly for your monitor or another device", "Edit Monitor", "Edit PC").</summary>
    public string Title => Mode == DeviceFormMode.Add ? AddTitle : "Edit " + (IsPrimary ? AppSettings.PrimaryLabel : _originalLabel);

    /// <summary>Gets the brief window title: "Add a Shelly" or "Edit Shelly".</summary>
    public string WindowTitle => Mode == DeviceFormMode.Add ? AddWindowTitle : EditWindowTitle;

    /// <summary>Gets the host, password, discovery and test part.</summary>
    public ShellyStepModel Connection { get; }

    /// <summary>Gets or sets the label (ignored for the PC).</summary>
    public string Label
    {
        get => _label;
        set
        {
            if (IsPrimary || _label == (value ?? string.Empty))
            {
                return;
            }

            _label = value ?? string.Empty;
            OnChanged();
        }
    }

    /// <summary>Gets whether the label field is editable (not for the PC).</summary>
    public bool CanEditLabel => !IsPrimary;

    /// <summary>Gets the label validation error, or <c>null</c>.</summary>
    public string? LabelError => IsPrimary ? null : DeviceLabelRules.Validate(_label, _otherLabels);

    /// <summary>Gets or sets whether the device counts in the totals (default on; not shown for the PC).</summary>
    public bool IncludeInTotals { get; set; }

    /// <summary>Gets whether the include checkbox is shown (not for the PC).</summary>
    public bool ShowIncludeInTotals => !IsPrimary;

    /// <summary>Gets or sets the device's custom colour (<c>#RRGGBB</c>), kept as it was unless changed.</summary>
    public string? Colour { get; set; }

    /// <summary>
    /// Gets whether to show <see cref="RenameWarningText"/> before saving: an existing additional device's label changed.
    /// Not shown while adding.
    /// </summary>
    public bool ShowRenameWarning =>
        Mode == DeviceFormMode.Edit && !IsPrimary && !string.Equals(_originalLabel?.Trim(), _label.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Gets the "Update the names in HWiNFO" help for the pending relabel (the device's AC and Online sensors, old label →
    /// new label), opened by "Show me how" next to <see cref="RenameWarningText"/>. Empty while
    /// <see cref="ShowRenameWarning"/> is false.
    /// </summary>
    public HwinfoRenameHelp RenameHelp => ShowRenameWarning
        ? HwinfoRenameHelp.ForDeviceRelabel(DeviceId, _originalLabel ?? string.Empty, _label, _sensorNames)
        : new HwinfoRenameHelp([]);

    /// <summary>Gets whether the form can be saved: a valid label and host.</summary>
    public bool IsValid => LabelError is null && Connection.IsHostValid;

    /// <summary>
    /// Gets the password change to apply: <c>null</c> when unchanged, an empty string to clear the stored one, otherwise
    /// the new password.
    /// </summary>
    public string? PasswordChange
    {
        get
        {
            var typed = Connection.Password ?? string.Empty;
            return string.Equals(typed, _originalPassword ?? string.Empty, StringComparison.Ordinal) ? null : typed;
        }
    }

    /// <summary>Builds the additional device's settings from the form (keeps the stored password; see <see cref="PasswordChange"/>).</summary>
    /// <param name="existing">The device being edited, to keep its stored password and other fields; <c>null</c> when adding.</param>
    /// <exception cref="InvalidOperationException">The form edits the PC.</exception>
    public AdditionalDeviceSettings ToAdditional(AdditionalDeviceSettings? existing = null)
    {
        if (IsPrimary)
        {
            throw new InvalidOperationException("The PC's plug is not an additional device.");
        }

        var device = new AdditionalDeviceSettings
        {
            Id = DeviceId,
            Label = _label.Trim(),
            IncludeInTotals = IncludeInTotals,
            Colour = DeviceColours.Normalize(Colour),
            ProtectedPassword = existing?.ProtectedPassword,
        };
        Connection.ApplyTo(device);
        return device;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
