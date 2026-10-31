using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>A label/value line of the summary.</summary>
/// <param name="Label">The label ("Device").</param>
/// <param name="Value">The value.</param>
/// <param name="Monospace">Whether the value is drawn in the monospace font (the calculation).</param>
public sealed record SummaryLine(string Label, string Value, bool Monospace = false);

/// <summary>The phase of the verification loop.</summary>
public enum VerifyPhase
{
    /// <summary>Not started.</summary>
    Idle,

    /// <summary>Looking for the sensors (every second, up to <see cref="ApplyStepModel.VerifyTimeout"/>).</summary>
    Checking,

    /// <summary>All four sensors are visible; the live readout keeps updating.</summary>
    Confirmed,

    /// <summary>The sensors did not show up in time ("Verify again").</summary>
    TimedOut,
}

/// <summary>The HWiNFO button offered after Apply.</summary>
public enum HwinfoPrimaryAction
{
    /// <summary>No button (HWiNFO not found).</summary>
    None,

    /// <summary>"Restart HWiNFO".</summary>
    Restart,

    /// <summary>"Start HWiNFO".</summary>
    Start,
}

/// <summary>
/// Wizard step 5: summary, "Start with Windows", Apply (through <see cref="IBridgeRuntime.ApplyFromWizard"/>), the HWiNFO
/// restart with advice for every outcome, and the verification of the four Shelly Power sensors with a live readout.
/// The view drives <see cref="VerifyTick"/> once per second while <see cref="IsVerifyRunning"/>.
/// </summary>
public sealed class ApplyStepModel
{
    /// <summary>How long the verification looks for the sensors.</summary>
    public static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How often the view should call <see cref="VerifyTick"/>.</summary>
    public static readonly TimeSpan VerifyInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long after a verification starts (after Apply, a restart or a start of HWiNFO) missing shared memory is
    /// shown as <see cref="CheckingHwinfoText"/> instead of a warning: HWiNFO takes a while to come up.
    /// </summary>
    public static readonly TimeSpan VerifyGracePeriod = TimeSpan.FromSeconds(20);

    /// <summary>The pending line during <see cref="VerifyGracePeriod"/>.</summary>
    public const string CheckingHwinfoText = "Checking HWiNFO…";

    /// <summary>The success line once all sensors were found.</summary>
    public const string VerifiedText = "Verified: all Shelly Power sensors are visible in HWiNFO.";

    private readonly IBridgeRuntime _runtime;
    private readonly Func<HwinfoStatus> _detect;
    private readonly Func<CancellationToken, Task<HwinfoRestartResult>> _restart;
    private readonly Func<bool> _start;
    private readonly Func<HwinfoSnapshot?> _snapshot;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private DateTimeOffset _verifyStarted;
    private AppSettings? _draft;

    /// <summary>Creates the model.</summary>
    /// <param name="dependencies">The wizard's dependencies.</param>
    /// <param name="startWithWindows">The initial "Start with Windows" value.</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    public ApplyStepModel(WizardDependencies dependencies, bool startWithWindows, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _runtime = dependencies.Runtime;
        _detect = dependencies.DetectHwinfo;
        _restart = dependencies.RestartHwinfo;
        _start = dependencies.StartHwinfo;
        _snapshot = dependencies.ReadSharedMemory;
        _time = dependencies.Time ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        StartWithWindows = startWithWindows;
    }

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the summary lines (built by <see cref="Prepare"/>).</summary>
    public IReadOnlyList<SummaryLine> SummaryLines { get; private set; } = [];

    /// <summary>Gets the gadget how-to for step 6 with the selected component sensor names (<see cref="HwinfoTips.GadgetExportFor"/>).</summary>
    public string GadgetTip => HwinfoTips.GadgetExportFor((_runtime.Settings ?? _draft)?.Sensors.Select(s => s.Name) ?? []);

    /// <summary>Gets or sets whether to start with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Gets whether Apply succeeded.</summary>
    public bool IsApplied { get; private set; }

    /// <summary>Gets the error of the last Apply, or <c>null</c>.</summary>
    public string? ApplyError { get; private set; }

    /// <summary>Gets the HWiNFO status after Apply.</summary>
    public HwinfoStatus? HwinfoStatus { get; private set; }

    /// <summary>Gets the HWiNFO card text before any restart attempt.</summary>
    public string HwinfoText => HwinfoStatus switch
    {
        null => string.Empty,
        { IsRunning: true } => "HWiNFO is running. Restart it so it shows the Shelly Power sensors.",
        { IsInstalled: true, ExecutablePath: not null } => "HWiNFO is not running. Start it to see the Shelly Power sensors.",
        { IsInstalled: true } => "HWiNFO is not running. Start it manually, then click Verify.",
        _ => "HWiNFO was not found. Install HWiNFO64 and start it to see the Shelly Power sensors.",
    };

    /// <summary>Gets the HWiNFO button to offer.</summary>
    public HwinfoPrimaryAction PrimaryAction => HwinfoStatus switch
    {
        { IsRunning: true } => HwinfoPrimaryAction.Restart,
        { ExecutablePath: not null } => HwinfoPrimaryAction.Start,
        _ => HwinfoPrimaryAction.None,
    };

    /// <summary>Gets whether a restart (or start) is in progress.</summary>
    public bool IsRestarting { get; private set; }

    /// <summary>Gets the advice of the last restart or start attempt, or <c>null</c>.</summary>
    public RestartAdvice? Advice { get; private set; }

    /// <summary>Gets the verification phase.</summary>
    public VerifyPhase VerifyPhase { get; private set; }

    /// <summary>Gets the last verification result, or <c>null</c>.</summary>
    public VerifyResult? Verify { get; private set; }

    /// <summary>Gets whether the view should keep calling <see cref="VerifyTick"/>.</summary>
    public bool IsVerifyRunning => VerifyPhase is VerifyPhase.Checking or VerifyPhase.Confirmed;

    /// <summary>Gets whether "Verify" can be clicked: not once all four sensors are confirmed, and not while restarting.</summary>
    public bool CanVerify => VerifyPhase != VerifyPhase.Confirmed && !IsRestarting;

    /// <summary>Gets the verification status line (tone, text), or <c>null</c> before verifying.</summary>
    public WizardStatusLine? VerifyStatus
    {
        get
        {
            if (Verify is { Implausible: true, Message: { } warning })
            {
                return new WizardStatusLine(Tone.Warning, warning);
            }

            var inGrace = _time.GetUtcNow() - _verifyStarted < VerifyGracePeriod;
            return VerifyPhase switch
            {
                VerifyPhase.Checking when Verify is { SharedMemoryAvailable: false } && inGrace => new WizardStatusLine(Tone.Pending, CheckingHwinfoText),
                VerifyPhase.Checking when Verify is { SharedMemoryAvailable: false, Message: { } off } => new WizardStatusLine(Tone.Warning, off),
                VerifyPhase.Checking => new WizardStatusLine(Tone.Secondary, "Looking for the Shelly Power sensors in HWiNFO…"),
                VerifyPhase.Confirmed => new WizardStatusLine(Tone.Success, VerifiedText),
                VerifyPhase.TimedOut when Verify is { SharedMemoryAvailable: false, Message: { } off } => new WizardStatusLine(Tone.Warning, off),
                VerifyPhase.TimedOut => new WizardStatusLine(Tone.Warning, "Not visible yet. Restart HWiNFO, then click Verify again."),
                _ => null,
            };
        }
    }

    /// <summary>Builds the summary from the draft.</summary>
    /// <param name="draft">The draft settings (with the PSU and sensors applied).</param>
    /// <param name="database">The PSU database.</param>
    /// <param name="measuredVolts">The voltage measured in step 2, if any.</param>
    public void Prepare(AppSettings draft, PsuDatabase database, double? measuredVolts)
    {
        _draft = draft.Clone();
        SummaryLines = BuildSummary(draft, database, measuredVolts, _culture);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds the summary lines.</summary>
    /// <param name="draft">The draft settings.</param>
    /// <param name="database">The PSU database.</param>
    /// <param name="measuredVolts">The measured voltage, if any.</param>
    /// <param name="culture">The culture.</param>
    public static IReadOnlyList<SummaryLine> BuildSummary(AppSettings draft, PsuDatabase database, double? measuredVolts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(database);
        var device = draft.Device;
        var name = !string.IsNullOrWhiteSpace(device.Name) ? device.Name.Trim() : device.Host.Trim();
        var deviceText = name;
        if (!string.IsNullOrWhiteSpace(device.Model))
        {
            deviceText += $" ({device.Model.Trim()})";
        }

        if (!string.Equals(name, device.Host.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            deviceText += " · " + device.Host.Trim();
        }

        string psu;
        string source;
        switch (draft.Psu.Mode)
        {
            case PsuSelectionMode.Database:
                var entry = draft.Psu.DatabaseId is { } id ? new PsuDatabaseQuery(database).FindById(id) : null;
                psu = entry is null ? "No PSU selected" : $"{entry.Brand} {entry.DisplayName}".Trim();
                source = entry?.Source is { } s
                    ? (string.IsNullOrWhiteSpace(s.ReportId) ? $"{s.Name} report" : $"{s.Name} report {s.ReportId.Trim()}").Trim()
                    : "PSU database";
                break;
            case PsuSelectionMode.Generic:
                psu = draft.Psu.GenericWattage is int w && draft.Psu.GenericRating is { } r
                    ? $"Generic {(r == EightyPlusRating.White ? "80 Plus" : "80 Plus " + r)} {w.ToString(culture)} W"
                    : "Generic curve incomplete";
                source = "80 Plus minimum requirements (approximate)";
                break;
            default:
                var points = draft.Psu.ManualCurve?.Count ?? 0;
                psu = "Manual curve";
                source = $"Entered by you ({points.ToString(culture)} points)";
                break;
        }

        var voltage = VoltageSelector.Resolve(draft.VoltageMode, measuredVolts);
        var measured = measuredVolts is double v && v > 0 ? $"plug measured {UiFormat.Volts(v, culture)}" : null;
        var how = draft.VoltageMode == InputVoltageMode.Auto
            ? (measured is null ? "automatic" : $"automatic, {measured}")
            : (measured is null ? "set manually" : $"set manually, {measured}");
        var note = PsuCurveResolver.Resolve(draft.Psu, database)?.CurveNote(voltage);
        var voltageText = note is null ? $"{(int)voltage} V curve ({how})" : $"{note} · {how}";

        var sensors = draft.Sensors.Count == 0
            ? "None selected"
            : string.Join(", ", SensorDisplay.Labels(draft.Sensors).Select((label, i) => $"{label} ({CategoryText(draft.Sensors[i].Category)})"));
        var lines = new List<SummaryLine> { new("Device", deviceText) };
        if (draft.HasAdditionalDevices)
        {
            lines.Add(new("Other devices", string.Join(", ", draft.AdditionalDevices.Select(d =>
                $"{d.Label} · {d.Host.Trim()}{(d.IncludeInTotals ? string.Empty : " (not in totals)")}"))));
        }

        var calculation = string.Join(" − ", SensorDisplay.Labels(draft.Sensors.Where(s => s.Name.Trim().Length > 0).ToList()).Prepend(SensorNaming.ResolveName(SensorKey.PcDc, draft))) + " = " + SensorNaming.ResolveName(SensorKey.PcOther, draft);
        lines.AddRange(
        [
            new("PSU", psu),
            new("Curve source", source),
            new("Voltage", voltageText),
            new("Sensors", sensors),
            new("Other Components", calculation, Monospace: true),
        ]);
        return lines;
    }

    /// <summary>Applies the draft through the runtime; on failure keeps the step open with <see cref="ApplyError"/>.</summary>
    /// <param name="draft">The draft settings (a copy is handed to the runtime).</param>
    /// <param name="password">The device password, or <c>null</c>.</param>
    /// <param name="devicePasswords">New passwords of additional devices by id, or <c>null</c>.</param>
    /// <returns><c>true</c> on success.</returns>
    public bool Apply(AppSettings draft, string? password, IReadOnlyDictionary<string, string>? devicePasswords = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (IsApplied)
        {
            return true;
        }

        ApplyOutcome outcome;
        try
        {
            outcome = _runtime.ApplyFromWizard(draft.Clone(), string.IsNullOrEmpty(password) ? null : password, StartWithWindows, devicePasswords);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            outcome = ApplyOutcome.Failed("Applying the setup failed: " + ex.Message);
        }

        if (!outcome.Success)
        {
            ApplyError = outcome.Error ?? "Applying the setup failed.";
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        IsApplied = true;
        ApplyError = null;
        HwinfoStatus = SafeDetect();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Restarts HWiNFO (closes and relaunches it from its path); on success starts the verification.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RestartHwinfoAsync(CancellationToken cancellationToken = default)
    {
        if (IsRestarting)
        {
            return;
        }

        IsRestarting = true;
        Changed?.Invoke(this, EventArgs.Empty);
        HwinfoRestartResult result;
        try
        {
            result = await _restart(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = HwinfoRestartResult.CouldNotClose;
        }
        finally
        {
            IsRestarting = false;
        }

        HwinfoStatus = SafeDetect();
        Advice = RestartAdvice.From(result, HwinfoStatus?.ExecutablePath is not null);
        if (result == HwinfoRestartResult.Restarted)
        {
            StartVerify();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Starts HWiNFO from its detected path; on success starts the verification.</summary>
    public void StartHwinfo()
    {
        bool started;
        try
        {
            started = _start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            started = false;
        }

        HwinfoStatus = SafeDetect();
        if (started)
        {
            Advice = new RestartAdvice(Tone.Success, "HWiNFO started. Checking for the Shelly Power sensors…", []);
            StartVerify();
        }
        else
        {
            Advice = new RestartAdvice(Tone.Warning, "HWiNFO could not be started. Start it manually, then click Verify.", [RestartAction.Verify]);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Starts (or restarts) the verification: checks now, then on every <see cref="VerifyTick"/>.</summary>
    public void StartVerify()
    {
        _verifyStarted = _time.GetUtcNow();
        VerifyPhase = VerifyPhase.Checking;
        VerifyTick();
    }

    /// <summary>Stops the verification (the wizard closes).</summary>
    public void StopVerify()
    {
        if (IsVerifyRunning)
        {
            VerifyPhase = VerifyPhase.Idle;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Evaluates shared memory once; times out after <see cref="VerifyTimeout"/> unless all sensors were found.</summary>
    public void VerifyTick()
    {
        if (!IsVerifyRunning)
        {
            return;
        }

        HwinfoSnapshot? snapshot;
        try
        {
            snapshot = _snapshot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            snapshot = null;
        }

        var layout = SensorLayout.Build(_runtime.Settings ?? _draft ?? new AppSettings());
        Verify = VerifyEvaluator.Evaluate(snapshot, _runtime.LastReading?.Power, _culture, layout, _runtime.Other);
        if (Verify.AllFound)
        {
            VerifyPhase = VerifyPhase.Confirmed;
        }
        else if (VerifyPhase == VerifyPhase.Confirmed)
        {
            // The sensors went away (HWiNFO closed or restarted): look again, and let the user verify again.
            VerifyPhase = VerifyPhase.Checking;
            _verifyStarted = _time.GetUtcNow();
        }
        else if (VerifyPhase == VerifyPhase.Checking && _time.GetUtcNow() - _verifyStarted >= VerifyTimeout)
        {
            VerifyPhase = VerifyPhase.TimedOut;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string CategoryText(SensorCategory category) => category switch
    {
        SensorCategory.Cpu => "CPU",
        SensorCategory.Gpu => "GPU",
        SensorCategory.Ram => "RAM",
        _ => "other",
    };

    private HwinfoStatus? SafeDetect()
    {
        try
        {
            return _detect();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
