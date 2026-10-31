using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Shelly.Discovery;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>The state of the connection test in step 2.</summary>
public enum ShellyTestState
{
    /// <summary>Not tested (or the host/password changed since).</summary>
    Idle,

    /// <summary>A test is running.</summary>
    Testing,

    /// <summary>The last test received a reading.</summary>
    Succeeded,

    /// <summary>The last test failed; see <see cref="ShellyStepModel.ErrorText"/>.</summary>
    Failed,
}

/// <summary>Which view step 2 shows: the opening question, the discovery and connection cards, or the setup guide.</summary>
public enum ShellySetupMode
{
    /// <summary>"Is your Shelly plug already on your Wi-Fi network?" has not been answered yet.</summary>
    Unanswered,

    /// <summary>The plug is on the network: show discovery and connection.</summary>
    Ready,

    /// <summary>The plug still needs setting up: show the setup guide.</summary>
    Guide,
}

/// <summary>
/// Wizard step 2: discover plugs through mDNS, pick one or type a host, and test the connection (3 s timeout). Only
/// reads (mDNS, HTTP GET). Next requires a valid host; the session runs the test on Next when the last successful test
/// does not match the host and password, and a failed test can be overridden with <see cref="SkipTest"/>.
/// The step opens with a question (<see cref="SetupMode"/>); the answer is kept for the wizard session only, and a
/// re-run with a configured host skips it.
/// </summary>
public sealed class ShellyStepModel
{
    /// <summary>The connection test timeout.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The opening question.</summary>
    public const string SetupQuestion = "Is your Shelly plug already on your Wi-Fi network?";

    /// <summary>The "Yes" answer (shows discovery and connection).</summary>
    public const string ReadyChoiceText = "Yes, find it";

    /// <summary>The "No" answer (shows the setup guide).</summary>
    public const string GuideChoiceText = "No, show me how to set it up";

    /// <summary>The setup guide's title.</summary>
    public const string GuideTitle = "Set up your Shelly plug";

    /// <summary>The button under the setup guide (switches to discovery).</summary>
    public const string GuideDoneText = "Done, search now";

    /// <summary>
    /// The setup guide steps, in order (<see cref="InlineMarkup"/>: names of settings and buttons are bold).
    /// </summary>
    public static readonly IReadOnlyList<string> GuideSteps =
    [
        "Plug it in. A new plug broadcasts its own open Wi-Fi network named like **ShellyPlugSG3-xxxxxxxxxxxx**. Ignore any phone prompt to set it up through an app.",
        "Connect a phone or laptop to that network and open **http://192.168.33.1** in a browser.",
        "**Settings › Wi-Fi**: enable the Wi-Fi client, enter your network name and password, save. The plug reboots and joins your network.",
        "Find its IP in your router's client list, open **http://<ip>** and finish the setup: **Settings › Authentication** set a password (the app supports it), **Settings › Bluetooth** off, **Settings › Wi-Fi › Access Point** off, **Settings › Shelly Cloud** off unless you want it.",
        "Give the plug a fixed IP or a DHCP reservation in your router. If the plug sits on another network or VLAN, allow **TCP port 80** from this PC to the plug.",
        "Plug the PC's power cable into the Shelly.",
    ];

    /// <summary>The tip about a fixed IP and the firewall (<see cref="InlineMarkup"/>: bold in <c>**</c>).</summary>
    public const string FixedIpTip = "Give the plug a fixed IP or a DHCP reservation in your router.\nIf the plug is on another network or VLAN, allow **TCP port 80** from this PC to the plug.";

    private readonly Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> _discover;
    private readonly Func<string, string?, CancellationToken, Task<ConnectionTestResult>> _test;
    private readonly CultureInfo _culture;
    private string _host = string.Empty;
    private string? _password;
    private string? _name;
    private string? _model;
    private (string Host, string Password)? _testedWith;
    private readonly List<AdditionalDeviceSettings> _additional = [];
    private readonly Dictionary<string, string> _additionalPasswords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the model.</summary>
    /// <param name="discover">Browses the network.</param>
    /// <param name="test">Tests a host and password (e.g. <see cref="IBridgeRuntime.TestConnectionAsync"/>).</param>
    /// <param name="culture">The culture for the value tiles; <c>null</c> for the current culture.</param>
    public ShellyStepModel(
        Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> discover,
        Func<string, string?, CancellationToken, Task<ConnectionTestResult>> test,
        CultureInfo? culture = null)
    {
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _test = test ?? throw new ArgumentNullException(nameof(test));
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets which view the step shows (the answer to <see cref="SetupQuestion"/>; session only).</summary>
    public ShellySetupMode SetupMode { get; private set; }

    /// <summary>Gets whether the discovery and connection cards are shown (Next is only possible then).</summary>
    public bool IsReady => SetupMode == ShellySetupMode.Ready;

    /// <summary>Gets whether a discovery should start when the step is shown (ready, and none ran or runs yet).</summary>
    public bool ShouldDiscoverOnShow => IsReady && !HasDiscovered && !IsDiscovering;

    /// <summary>Gets the plugs found by the last discovery.</summary>
    public IReadOnlyList<DiscoveredShelly> Devices { get; private set; } = [];

    /// <summary>Gets whether a discovery is running.</summary>
    public bool IsDiscovering { get; private set; }

    /// <summary>Gets whether at least one discovery finished.</summary>
    public bool HasDiscovered { get; private set; }

    /// <summary>Gets the text for an empty device list ("Searching…" or "No plugs found…").</summary>
    public string DeviceListEmptyText => IsDiscovering
        ? "Searching…"
        : HasDiscovered ? "No plugs found. Type the IP address on the right." : string.Empty;

    /// <summary>Gets the line under the device list.</summary>
    public string DiscoveryStatusText => IsDiscovering
        ? "Searching the network…"
        : Devices.Count switch
        {
            0 => HasDiscovered ? "Nothing found. Check that the plug is on the same network." : string.Empty,
            1 => "1 plug found.",
            _ => $"{Devices.Count.ToString(_culture)} plugs found.",
        };

    /// <summary>Gets the index in <see cref="Devices"/> whose host equals <see cref="Host"/>, or -1.</summary>
    public int SelectedDeviceIndex
    {
        get
        {
            var host = Host.Trim();
            for (var i = 0; i < Devices.Count; i++)
            {
                if (string.Equals(Devices[i].Host, host, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Devices[i].Address.ToString(), host, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>Gets or sets the host (IP address or host name).</summary>
    public string Host
    {
        get => _host;
        set
        {
            value ??= string.Empty;
            if (_host == value)
            {
                return;
            }

            if (!string.Equals(_host.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Another device: the remembered name and model no longer apply.
                _name = null;
                _model = null;
            }

            _host = value;
            OnInputChanged();
        }
    }

    /// <summary>Gets or sets the password (<c>null</c> or empty for none). Never log it.</summary>
    public string? Password
    {
        get => _password;
        set
        {
            if (_password == value)
            {
                return;
            }

            _password = value;
            OnInputChanged();
        }
    }

    /// <summary>Gets whether <see cref="Host"/> is a valid host.</summary>
    public bool IsHostValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                return false;
            }

            try
            {
                ShellyClient.BuildBaseUri(Host);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>Gets the error for the host field, or <c>null</c>.</summary>
    public string? HostError => string.IsNullOrWhiteSpace(Host) || IsHostValid ? null : "Enter a valid IP address or host name.";

    /// <summary>Gets the test state.</summary>
    public ShellyTestState TestState { get; private set; }

    /// <summary>Gets the reading of the last successful test.</summary>
    public ShellyReading? LastReading { get; private set; }

    /// <summary>Gets the device info of the last successful test, when the device reported it.</summary>
    public ShellyDeviceInfo? DeviceInfo { get; private set; }

    /// <summary>Gets the error of the last failed test.</summary>
    public string? ErrorText { get; private set; }

    /// <summary>Gets or sets whether the user chose "Continue anyway" after a failed test.</summary>
    public bool SkipTest { get; set; }

    /// <summary>Gets whether the last successful test was made with the current host and password.</summary>
    public bool IsTestCurrent =>
        TestState == ShellyTestState.Succeeded && _testedWith == (Host.Trim(), Password ?? string.Empty);

    /// <summary>Gets the measured voltage of the last successful test, if any.</summary>
    public double? MeasuredVolts => IsTestCurrent ? LastReading?.Volts : null;

    /// <summary>Gets the power tile text ("312.4 W" or "—").</summary>
    public string PowerText => LastReading is { } r && TestState == ShellyTestState.Succeeded ? UiFormat.Watts(r.ActivePowerWatts, _culture) : UiFormat.Dash;

    /// <summary>Gets the voltage tile text.</summary>
    public string VoltageText => LastReading is { Volts: double v } && TestState == ShellyTestState.Succeeded ? UiFormat.Volts(v, _culture) : UiFormat.Dash;

    /// <summary>Gets the current tile text.</summary>
    public string CurrentText => LastReading is { Amps: double a } && TestState == ShellyTestState.Succeeded ? a.ToString("0.00", _culture) + " A" : UiFormat.Dash;

    /// <summary>Gets the result line: (tone, text), or <c>null</c> when there is nothing to say.</summary>
    public WizardStatusLine? ResultLine => TestState switch
    {
        ShellyTestState.Testing => new WizardStatusLine(Tone.Secondary, "Testing the connection…"),
        ShellyTestState.Succeeded => new WizardStatusLine(Tone.Success, "Connected to " + DeviceLabel() + "."),
        ShellyTestState.Failed => new WizardStatusLine(Tone.Error, ErrorText ?? "The connection failed."),
        _ => null,
    };

    /// <summary>Gets whether to offer "Continue anyway" (the test failed and was not skipped yet).</summary>
    public bool OfferContinueAnyway => TestState == ShellyTestState.Failed && !SkipTest && IsHostValid;

    /// <summary>Gets the device name to store (from the test, the discovered device or the existing settings).</summary>
    public string? DeviceName => DeviceInfo?.Name ?? _name;

    /// <summary>Gets the device model to store.</summary>
    public string? DeviceModel => DeviceInfo?.Model ?? _model;

    /// <summary>
    /// Initialises the fields from existing settings (re-run). A configured host skips the opening question.
    /// </summary>
    /// <param name="device">The current device settings.</param>
    /// <param name="password">The current password.</param>
    public void LoadFrom(ShellyDeviceSettings device, string? password)
    {
        ArgumentNullException.ThrowIfNull(device);
        _host = device.Host ?? string.Empty;
        _password = password;
        _name = device.Name;
        _model = device.Model;
        if (!string.IsNullOrWhiteSpace(_host))
        {
            SetupMode = ShellySetupMode.Ready;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// "Yes, find it" or "Done, search now": shows discovery and connection and searches the network (unless a search
    /// is already running).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task FindAsync(CancellationToken cancellationToken = default)
    {
        SetupMode = ShellySetupMode.Ready;
        Changed?.Invoke(this, EventArgs.Empty);
        return DiscoverAsync(cancellationToken);
    }

    /// <summary>The hint next to a disabled Add button at the device cap.</summary>
    public static readonly string DeviceCapHint =
        $"At most {AppSettings.MaxDevices} Shellys in total (the PC's plug and {AppSettings.MaxDevices - 1} more). Remove one to add another.";

    /// <summary>Gets the additional devices added in this step (or loaded from the settings on a re-run), in list order.</summary>
    public IReadOnlyList<AdditionalDeviceSettings> AdditionalDevices => _additional;

    /// <summary>
    /// Gets the password changes of the additional devices by id (see <see cref="DeviceFormModel.PasswordChange"/>), for
    /// <see cref="IBridgeRuntime.ApplyFromWizard"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalPasswordChanges => _additionalPasswords;

    /// <summary>
    /// Gets whether to show the "Add a Shelly for your monitor or another device" card: after the PC plug's connection
    /// test succeeded, or when devices were already added. Only in wizard step 2 (not in a device form).
    /// </summary>
    public bool ShowAddDeviceCard => IsReady && (IsTestCurrent || _additional.Count > 0);

    /// <summary>Gets whether "Add device" is enabled: below the cap and no form open.</summary>
    public bool CanAddDevice => _additional.Count < AppSettings.MaxDevices - 1 && DeviceForm is null;

    /// <summary>Gets the hint next to a disabled Add button (<see cref="DeviceCapHint"/>), or <c>null</c>.</summary>
    public string? AddDeviceHint => _additional.Count >= AppSettings.MaxDevices - 1 ? DeviceCapHint : null;

    /// <summary>Gets the open add-device form, or <c>null</c>.</summary>
    public DeviceFormModel? DeviceForm { get; private set; }

    /// <summary>"Answers" the opening question (used by device forms, which never ask it).</summary>
    public void SkipQuestion()
    {
        if (SetupMode != ShellySetupMode.Ready)
        {
            SetupMode = ShellySetupMode.Ready;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Loads existing additional devices (wizard re-run).</summary>
    /// <param name="devices">The devices.</param>
    public void LoadAdditional(IEnumerable<AdditionalDeviceSettings> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        _additional.Clear();
        _additional.AddRange(devices.Take(AppSettings.MaxDevices - 1));
        _additionalPasswords.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Add device": opens <see cref="DeviceForm"/> (label, discovery or manual host, password, test, include).</summary>
    /// <returns>The form, or <c>null</c> at the cap or while a form is open.</returns>
    public DeviceFormModel? BeginAddDevice()
    {
        if (!CanAddDevice)
        {
            return null;
        }

        DeviceForm = new DeviceFormModel(_discover, _test, _additional.Select(d => d.Label).ToList(), culture: _culture);
        DeviceForm.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        return DeviceForm;
    }

    /// <summary>Adds the device of the open form to the list when the form is valid, and closes the form.</summary>
    /// <returns><c>true</c> when the device was added.</returns>
    public bool CommitDeviceForm()
    {
        if (DeviceForm is not { IsValid: true } form || _additional.Count >= AppSettings.MaxDevices - 1)
        {
            return false;
        }

        _additional.Add(form.ToAdditional());
        if (form.PasswordChange is { Length: > 0 } password)
        {
            _additionalPasswords[form.DeviceId] = password;
        }

        DeviceForm = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Closes the open form without adding.</summary>
    public void CancelDeviceForm()
    {
        if (DeviceForm is not null)
        {
            DeviceForm = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes an additional device from the list ("Remove" link).</summary>
    /// <param name="deviceId">The device id.</param>
    public void RemoveAdditionalDevice(string deviceId)
    {
        if (_additional.RemoveAll(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            _additionalPasswords.Remove(deviceId);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Writes the additional devices into the draft.</summary>
    /// <param name="settings">The draft settings.</param>
    public void ApplyAdditionalTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.AdditionalDevices = [.. _additional];
    }

    /// <summary>"No, show me how to set it up": shows the setup guide.</summary>
    public void ShowGuide()
    {
        if (SetupMode == ShellySetupMode.Guide)
        {
            return;
        }

        SetupMode = ShellySetupMode.Guide;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fills <see cref="Host"/> from a discovered plug and remembers its name and model.</summary>
    /// <param name="device">The plug.</param>
    public void SelectDevice(DiscoveredShelly device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Host = device.Host;
        _name = device.Name ?? device.DisplayName;
        _model = device.DisplayModel;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Browses the network; failures leave an empty list.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (IsDiscovering)
        {
            return;
        }

        IsDiscovering = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            Devices = await _discover(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Devices = [];
        }
        finally
        {
            IsDiscovering = false;
            HasDiscovered = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Tests the current host and password: one device-info and one reading request within 3 s.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a reading was received.</returns>
    public async Task<bool> TestAsync(CancellationToken cancellationToken = default)
    {
        if (!IsHostValid)
        {
            TestState = ShellyTestState.Failed;
            ErrorText = "Enter a valid IP address or host name.";
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        var host = Host.Trim();
        var password = Password ?? string.Empty;
        TestState = ShellyTestState.Testing;
        ErrorText = null;
        SkipTest = false;
        Changed?.Invoke(this, EventArgs.Empty);

        ConnectionTestResult result;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TestTimeout + TimeSpan.FromSeconds(1));
        try
        {
            result = await _test(host, password.Length == 0 ? null : password, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new ConnectionTestResult(false, null, null, $"No answer within {TestTimeout.TotalSeconds:0} seconds.");
        }

        // The user may have edited the fields meanwhile; only a matching result counts.
        if (host != Host.Trim() || password != (Password ?? string.Empty))
        {
            TestState = ShellyTestState.Idle;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (result.Success && result.Reading is not null)
        {
            TestState = ShellyTestState.Succeeded;
            LastReading = result.Reading;
            DeviceInfo = result.DeviceInfo;
            _testedWith = (host, password);
            if (result.DeviceInfo is { } info)
            {
                _name = info.Name ?? _name;
                _model = info.Model ?? _model;
            }
        }
        else
        {
            TestState = ShellyTestState.Failed;
            ErrorText = result.Error ?? "The connection failed.";
            _testedWith = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return TestState == ShellyTestState.Succeeded;
    }

    /// <summary>Writes host, name and model into the draft's device settings (not the password).</summary>
    /// <param name="device">The draft device settings.</param>
    public void ApplyTo(ShellyDeviceSettings device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Host = Host.Trim();
        device.Name = DeviceName;
        device.Model = DeviceModel;
    }

    private string DeviceLabel()
    {
        var name = DeviceName ?? Host.Trim();
        return DeviceModel is { } model && !string.Equals(model, name, StringComparison.OrdinalIgnoreCase) ? $"{name} ({model})" : name;
    }

    private void OnInputChanged()
    {
        SkipTest = false;
        if (TestState is ShellyTestState.Failed or ShellyTestState.Succeeded && !IsTestCurrent)
        {
            TestState = ShellyTestState.Idle;
            ErrorText = null;
            DeviceInfo = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
