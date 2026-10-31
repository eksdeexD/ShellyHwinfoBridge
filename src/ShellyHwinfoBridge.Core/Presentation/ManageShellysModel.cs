using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>One row of the Manage Shellys page.</summary>
/// <param name="Id">The device id (<see cref="AppSettings.PrimaryDeviceId"/> for the PC).</param>
/// <param name="Label">"PC" for the primary, else the device's label.</param>
/// <param name="Host">The IP address or host name.</param>
/// <param name="Model">The model (or device name) for display, or empty.</param>
/// <param name="State">The online state.</param>
/// <param name="StatusText">"Online · 31.2 W", "Offline", "Paused", "Connecting", "Not polled".</param>
/// <param name="StatusTone">The status colour.</param>
/// <param name="IncludeInTotals">Whether the device counts in the totals (always true for the PC).</param>
/// <param name="CanToggleInclude">Whether "Include in totals" can be changed (not for the PC).</param>
/// <param name="Colour">The device's colour (<c>#RRGGBB</c>) as the chart draws it.</param>
/// <param name="CanRemove">Whether Remove is offered (not for the PC).</param>
/// <param name="IsPrimary">Whether this is the PC's plug.</param>
public sealed record DeviceRow(
    string Id,
    string Label,
    string Host,
    string Model,
    OnlineState State,
    string StatusText,
    Tone StatusTone,
    bool IncludeInTotals,
    bool CanToggleInclude,
    string Colour,
    bool CanRemove,
    bool IsPrimary);

/// <summary>
/// The Manage Shellys page (opened from Settings): every device with label, host, model, online status, "Include in
/// totals" and colour swatch; Add / Edit / Remove (the PC can be edited but not removed); the colour mode (Automatic
/// scheme with base colour, or Custom). Every change is applied through the runtime immediately.
/// </summary>
/// <remarks>
/// Add/Edit open <see cref="Form"/>; call <see cref="SaveForm"/> (after showing <see cref="DeviceFormModel.RenameWarningText"/>
/// when <see cref="DeviceFormModel.ShowRenameWarning"/>) or <see cref="CancelForm"/>. Call <see cref="Refresh"/> when the
/// runtime's state changes so online statuses update. Not thread-safe: call from the UI thread.
/// </remarks>
public sealed class ManageShellysModel
{
    private readonly IBridgeRuntime _runtime;
    private readonly Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> _discover;
    private readonly CultureInfo _culture;

    /// <summary>Creates the model.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="discover">Browses the network for plugs (device form).</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    public ManageShellysModel(IBridgeRuntime runtime, Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> discover, CultureInfo? culture = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the rows: the PC first, then the additional devices in list order.</summary>
    public IReadOnlyList<DeviceRow> Rows => BuildRows();

    /// <summary>Gets whether Add is enabled (below <see cref="AppSettings.MaxDevices"/> and no form open).</summary>
    public bool CanAdd => Settings.DeviceCount < AppSettings.MaxDevices && Form is null;

    /// <summary>Gets the hint next to a disabled Add button at the cap, or <c>null</c>.</summary>
    public string? AddHint => Settings.DeviceCount >= AppSettings.MaxDevices ? Wizard.ShellyStepModel.DeviceCapHint : null;

    /// <summary>Gets the colour mode.</summary>
    public ColourMode ColourMode => Settings.ColourMode;

    /// <summary>Gets the base colour of the automatic scheme (<c>#RRGGBB</c>).</summary>
    public string BaseColour => Settings.BaseColour;

    /// <summary>Gets whether each device's colour can be picked (Custom mode); otherwise only the base colour.</summary>
    public bool CanPickDeviceColours => Settings.ColourMode == ColourMode.Custom;

    /// <summary>Gets the last colour input error, or <c>null</c>.</summary>
    public string? ColourError { get; private set; }

    /// <summary>Gets the last apply error, or <c>null</c>.</summary>
    public string? ApplyError { get; private set; }

    /// <summary>Gets the open Add/Edit form, or <c>null</c>.</summary>
    public DeviceFormModel? Form { get; private set; }

    private AppSettings Settings => _runtime.Settings ?? new AppSettings();

    /// <summary>Raises <see cref="Changed"/> (call when the runtime's state changed).</summary>
    public void Refresh() => OnChanged();

    /// <summary>Opens the form to add a device.</summary>
    /// <returns>The form, or <c>null</c> at the cap or while a form is open.</returns>
    public DeviceFormModel? BeginAdd()
    {
        if (!CanAdd)
        {
            return null;
        }

        return Open(new DeviceFormModel(_discover, _runtime.TestConnectionAsync, Settings.AdditionalDevices.Select(d => d.Label).ToList(), culture: _culture));
    }

    /// <summary>Opens the form to edit a device (the PC too).</summary>
    /// <param name="deviceId">The device id.</param>
    /// <returns>The form, or <c>null</c> when the device is unknown or a form is open.</returns>
    public DeviceFormModel? BeginEdit(string deviceId)
    {
        if (Form is not null)
        {
            return null;
        }

        var settings = Settings;
        if (AppSettings.IsPrimary(deviceId))
        {
            return Open(new DeviceFormModel(_discover, _runtime.TestConnectionAsync, settings.AdditionalDevices.Select(d => d.Label).ToList(), settings.Device, _runtime.Password, culture: _culture));
        }

        var device = settings.FindAdditionalDevice(deviceId);
        if (device is null)
        {
            return null;
        }

        var others = settings.AdditionalDevices.Where(d => !ReferenceEquals(d, device)).Select(d => d.Label).ToList();
        return Open(new DeviceFormModel(_discover, _runtime.TestConnectionAsync, others, device, _runtime.GetDevicePassword(device.Id), culture: _culture, sensorNames: settings.SensorNames));
    }

    /// <summary>Closes the form without saving.</summary>
    public void CancelForm()
    {
        Form = null;
        OnChanged();
    }

    /// <summary>
    /// Saves the open form (adds or updates the device) and applies the settings. Show
    /// <see cref="DeviceFormModel.RenameWarningText"/> first when <see cref="DeviceFormModel.ShowRenameWarning"/>.
    /// </summary>
    /// <returns>The outcome, or <c>null</c> when no valid form is open (the form stays open).</returns>
    public ApplyOutcome? SaveForm()
    {
        if (Form is not { IsValid: true } form)
        {
            return null;
        }

        var clone = Settings.Clone();
        string? primaryPassword = null;
        Dictionary<string, string>? devicePasswords = null;
        if (form.IsPrimary)
        {
            form.Connection.ApplyTo(clone.Device);
            primaryPassword = form.PasswordChange;
        }
        else
        {
            var index = clone.AdditionalDevices.FindIndex(d => string.Equals(d.Id, form.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && clone.DeviceCount >= AppSettings.MaxDevices)
            {
                ApplyError = ShellyCapError;
                OnChanged();
                return ApplyOutcome.Failed(ShellyCapError);
            }

            var device = form.ToAdditional(index >= 0 ? clone.AdditionalDevices[index] : null);
            if (index >= 0)
            {
                clone.AdditionalDevices[index] = device;
            }
            else
            {
                clone.AdditionalDevices.Add(device);
            }

            if (form.PasswordChange is { } change)
            {
                devicePasswords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [device.Id] = change };
            }
        }

        var outcome = _runtime.Apply(clone, primaryPassword, devicePasswords);
        ApplyError = outcome.Success ? null : outcome.Error;
        if (outcome.Success)
        {
            Form = null;
        }

        OnChanged();
        return outcome;
    }

    /// <summary>Removes an additional device (the PC cannot be removed). Its energy history stays on disk.</summary>
    /// <param name="deviceId">The device id.</param>
    /// <returns><c>true</c> when the device was removed.</returns>
    public bool Remove(string deviceId)
    {
        if (AppSettings.IsPrimary(deviceId) || Settings.FindAdditionalDevice(deviceId) is null)
        {
            return false;
        }

        return Update(s => s.AdditionalDevices.RemoveAll(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Sets "Include in totals" of an additional device.</summary>
    /// <param name="deviceId">The device id.</param>
    /// <param name="include">Whether to include it.</param>
    public void SetIncludeInTotals(string deviceId, bool include)
    {
        if (Settings.FindAdditionalDevice(deviceId) is { } d && d.IncludeInTotals != include)
        {
            Update(s => s.FindAdditionalDevice(deviceId)!.IncludeInTotals = include);
        }
    }

    /// <summary>Sets the colour mode.</summary>
    /// <param name="mode">The mode.</param>
    public void SetColourMode(ColourMode mode)
    {
        if (Settings.ColourMode != mode)
        {
            Update(s => s.ColourMode = mode);
        }
    }

    /// <summary>Sets the base colour from <c>#RRGGBB</c> text; invalid text sets <see cref="ColourError"/>.</summary>
    /// <param name="text">The colour text.</param>
    /// <returns><c>true</c> when applied.</returns>
    public bool SetBaseColour(string? text)
    {
        if (DeviceColours.Normalize(text) is not { } colour)
        {
            ColourError = ColourFormatError;
            OnChanged();
            return false;
        }

        ColourError = null;
        return Update(s => s.BaseColour = colour);
    }

    /// <summary>Sets one device's colour (Custom mode) from <c>#RRGGBB</c> text; <c>null</c> or empty resets it to automatic.</summary>
    /// <param name="deviceId">The device id (the PC too).</param>
    /// <param name="text">The colour text.</param>
    /// <returns><c>true</c> when applied.</returns>
    public bool SetDeviceColour(string deviceId, string? text)
    {
        string? colour = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            colour = DeviceColours.Normalize(text);
            if (colour is null)
            {
                ColourError = ColourFormatError;
                OnChanged();
                return false;
            }
        }

        ColourError = null;
        if (AppSettings.IsPrimary(deviceId))
        {
            return Update(s => s.Device.Colour = colour);
        }

        return Settings.FindAdditionalDevice(deviceId) is not null && Update(s => s.FindAdditionalDevice(deviceId)!.Colour = colour);
    }

    /// <summary>The error for an unparsable colour.</summary>
    public const string ColourFormatError = "Enter a colour as #RRGGBB, for example #FF6600.";

    private static readonly string ShellyCapError = $"At most {AppSettings.MaxDevices} Shellys are supported.";

    private DeviceFormModel Open(DeviceFormModel form)
    {
        Form = form;
        form.Changed += (_, _) => OnChanged();
        OnChanged();
        return form;
    }

    private bool Update(Action<AppSettings> mutate)
    {
        var clone = Settings.Clone();
        mutate(clone);
        var outcome = _runtime.Apply(clone);
        ApplyError = outcome.Success ? null : outcome.Error;
        OnChanged();
        return outcome.Success;
    }

    private IReadOnlyList<DeviceRow> BuildRows()
    {
        var settings = Settings;
        var colours = DeviceColours.Resolve(settings);
        var statuses = _runtime.Devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var paused = _runtime.State == RuntimeState.Paused;
        var rows = new List<DeviceRow>
        {
            Row(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, settings.Device, true, false, colours, statuses, paused),
        };
        rows.AddRange(settings.AdditionalDevices.Select(d => Row(d.Id, d.Label, d, d.IncludeInTotals, true, colours, statuses, paused)));
        return rows;
    }

    private DeviceRow Row(string id, string label, ShellyDeviceSettings device, bool include, bool additional, IReadOnlyDictionary<string, string> colours, Dictionary<string, DeviceStatus> statuses, bool paused)
    {
        var status = statuses.GetValueOrDefault(id);
        var state = status?.State ?? OnlineState.Unknown;
        var (text, tone) = paused
            ? ("Paused", Tone.Secondary)
            : state switch
            {
                OnlineState.Online => (status?.AcWatts is double w ? "Online · " + UiFormat.Watts(w, _culture) : "Online", Tone.Success),
                OnlineState.Offline => ("Offline", Tone.Error),
                _ => (_runtime.State == RuntimeState.Running ? "Connecting" : "Not polled", Tone.Secondary),
            };
        var model = !string.IsNullOrWhiteSpace(device.Model) ? device.Model.Trim() : device.Name?.Trim() ?? string.Empty;
        return new DeviceRow(id, label, device.Host.Trim(), model, state, text, tone, include, additional, colours.GetValueOrDefault(id) ?? DeviceColours.DefaultBase, additional, !additional);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
