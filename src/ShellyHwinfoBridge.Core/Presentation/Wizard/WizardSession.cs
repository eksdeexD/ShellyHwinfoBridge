using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>
/// The setup wizard's state machine: six steps (the last one, Verify, only after Apply), Back/Next, and a draft of the settings. Nothing touches the registry,
/// the settings file or autostart before <see cref="Apply"/>: the steps only read (process list, shared memory, mDNS,
/// HTTP GET). Cancel = drop the session.
/// </summary>
public sealed class WizardSession
{
    /// <summary>The step labels for the step indicator.</summary>
    public static IReadOnlyList<string> StepLabels { get; } = ["HWiNFO", "Shelly", "PSU", "Sensors", "Apply", "Verify"];

    private readonly WizardDependencies _dependencies;
    private readonly PsuDatabase _database;
    private readonly HashSet<WizardStep> _completed = [];
    private bool _busy;

    /// <summary>Creates a session.</summary>
    /// <param name="current">The current settings, or <c>null</c> on first run.</param>
    /// <param name="currentPassword">The current device password, or <c>null</c>.</param>
    /// <param name="database">The PSU database.</param>
    /// <param name="dependencies">What the steps read from (and the runtime that applies).</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    public WizardSession(AppSettings? current, string? currentPassword, PsuDatabase database, WizardDependencies dependencies, CultureInfo? culture = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        IsFirstRun = !BridgeRuntime.IsConfigured(current);
        Draft = current?.Clone() ?? new AppSettings();
        DraftPassword = currentPassword;

        Hwinfo = new HwinfoStepModel(dependencies.DetectHwinfo, dependencies.ReadSharedMemory);
        Shelly = new ShellyStepModel(dependencies.DiscoverShellys, dependencies.Runtime.TestConnectionAsync, culture);
        Psu = new PsuStepModel(database, culture);
        Sensors = new SensorStepModel(culture);
        Apply = new ApplyStepModel(dependencies, IsFirstRun || Draft.StartWithWindows, culture);

        Shelly.LoadFrom(Draft.Device, currentPassword);
        if (!IsFirstRun)
        {
            Psu.LoadFrom(Draft);
        }

        Shelly.Changed += (_, _) => RaiseChanged();
        Psu.Changed += (_, _) => RaiseChanged();
        Sensors.Changed += (_, _) => RaiseChanged();
        Apply.Changed += (_, _) => RaiseChanged();
        Hwinfo.Refresh();
    }

    /// <summary>Raised when the step, validity or a step model changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets whether this is the first run (no configured device before).</summary>
    public bool IsFirstRun { get; }

    /// <summary>Gets the draft settings (a clone of the current settings; the runtime takes a copy on Apply).</summary>
    public AppSettings Draft { get; }

    /// <summary>Gets the device password the draft will be applied with.</summary>
    public string? DraftPassword { get; private set; }

    /// <summary>Gets the current step.</summary>
    public WizardStep Current { get; private set; } = WizardStep.Hwinfo;

    /// <summary>Gets the completed steps.</summary>
    public IReadOnlySet<WizardStep> Completed => _completed;

    /// <summary>Gets step 1.</summary>
    public HwinfoStepModel Hwinfo { get; }

    /// <summary>Gets step 2.</summary>
    public ShellyStepModel Shelly { get; }

    /// <summary>Gets step 3.</summary>
    public PsuStepModel Psu { get; }

    /// <summary>Gets step 4.</summary>
    public SensorStepModel Sensors { get; }

    /// <summary>Gets steps 5 (summary and Apply) and 6 (HWiNFO restart and verification).</summary>
    public ApplyStepModel Apply { get; }

    /// <summary>Gets whether the settings were applied (the wizard is on step 6: Back and Cancel are gone).</summary>
    public bool IsApplied => Apply.IsApplied;

    /// <summary>Gets whether a Next transition (connection test) is running.</summary>
    public bool IsBusy => _busy;

    /// <summary>Gets whether Back is allowed: before Apply and not on step 1.</summary>
    public bool CanGoBack => !IsApplied && !_busy && Current > WizardStep.Hwinfo;

    /// <summary>Gets whether Next (or Apply / Finish) is allowed.</summary>
    public bool CanGoNext => !_busy && Current switch
    {
        WizardStep.Hwinfo => true,
        WizardStep.Shelly => Shelly.IsReady && Shelly.IsHostValid && Shelly.TestState != ShellyTestState.Testing,
        WizardStep.Psu => Psu.IsValid,
        WizardStep.Sensors => Sensors.IsValid,
        WizardStep.Apply => !IsApplied,
        _ => !Apply.IsRestarting,
    };

    /// <summary>Gets the Next button text: "Next", "Apply" on step 5, "Finish" on step 6.</summary>
    public string NextText => Current switch
    {
        WizardStep.Apply => "Apply",
        WizardStep.Verify => "Finish",
        _ => "Next",
    };

    /// <summary>Gets the page title of the current step.</summary>
    public string PageTitle => Current switch
    {
        WizardStep.Hwinfo => "Check HWiNFO",
        WizardStep.Shelly => "Connect your Shelly plug",
        WizardStep.Psu => "Choose your power supply",
        WizardStep.Sensors => "Choose the component sensors",
        WizardStep.Apply => "Review and apply",
        _ => "Verify in HWiNFO",
    };

    /// <summary>Gets the page subtitle of the current step.</summary>
    public string PageSubtitle => Current switch
    {
        WizardStep.Hwinfo => "HWiNFO shows the Shelly readings as sensors. The sensor picker in step 4 reads its shared memory.",
        WizardStep.Shelly => Shelly.SetupMode switch
        {
            ShellySetupMode.Unanswered => "The bridge reads the PC's power from a Shelly plug on your Wi-Fi network.",
            ShellySetupMode.Guide => "Set the plug up once in its own web page, then search for it.",
            _ => "Pick your plug or type its IP address. The password is only needed if you set one in the Shelly app.",
        },
        WizardStep.Psu => "Pick it from the database for the most accurate curve. If it is missing, use a generic curve by rating or enter your own.",
        WizardStep.Sensors => "These HWiNFO power sensors are subtracted from DC power to compute Other Components. Likely candidates are already ticked.",
        WizardStep.Apply => "Nothing has been changed yet. Check the summary, then click Apply.",
        _ => "The sensors are written and polling runs. Restart HWiNFO so it shows them, then verify.",
    };

    /// <summary>
    /// Goes to the next step. On step 2 it runs the connection test first unless the last successful test matches or the
    /// user chose "Continue anyway"; a failed test keeps the step (see <see cref="ShellyStepModel.OfferContinueAnyway"/>).
    /// On step 5 it applies and, when that worked, moves on to step 6 (which starts the verification). On step 6 it
    /// returns <c>false</c> ("Finish" closes the wizard).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the step changed or Apply succeeded.</returns>
    public async Task<bool> GoNextAsync(CancellationToken cancellationToken = default)
    {
        if (!CanGoNext)
        {
            return false;
        }

        switch (Current)
        {
            case WizardStep.Shelly when !Shelly.IsTestCurrent && !Shelly.SkipTest:
                _busy = true;
                RaiseChanged();
                bool ok;
                try
                {
                    ok = await Shelly.TestAsync(cancellationToken);
                }
                finally
                {
                    _busy = false;
                }

                if (!ok)
                {
                    RaiseChanged();
                    return false;
                }

                break;

            case WizardStep.Apply:
                if (!Apply.Apply(Draft, DraftPassword))
                {
                    RaiseChanged();
                    return false;
                }

                break;

            case WizardStep.Verify:
                return false;
        }

        Leave(Current);
        _completed.Add(Current);
        Current++;
        Enter(Current);
        RaiseChanged();
        return true;
    }

    /// <summary>Continues past a failed connection test ("Continue anyway").</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> ContinueAnywayAsync(CancellationToken cancellationToken = default)
    {
        Shelly.SkipTest = true;
        return GoNextAsync(cancellationToken);
    }

    /// <summary>Goes back one step (allowed before Apply).</summary>
    /// <returns><c>true</c> when the step changed.</returns>
    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        Leave(Current);
        Current--;
        Enter(Current);
        RaiseChanged();
        return true;
    }

    /// <summary>Reads HWiNFO's sensors again for step 4, keeping the current selection.</summary>
    public void ReloadSensors()
    {
        Sensors.ApplyTo(Draft);
        Sensors.Load(_dependencies.ReadSharedMemory()?.GetPowerSensors(), Draft.Sensors, Draft.CustomFormula);
    }

    private void Leave(WizardStep step)
    {
        switch (step)
        {
            case WizardStep.Shelly:
                Shelly.ApplyTo(Draft.Device);
                DraftPassword = string.IsNullOrEmpty(Shelly.Password) ? null : Shelly.Password;
                break;
            case WizardStep.Psu:
                Psu.ApplyTo(Draft);
                break;
            case WizardStep.Sensors:
                Sensors.ApplyTo(Draft);
                break;
        }
    }

    private void Enter(WizardStep step)
    {
        switch (step)
        {
            case WizardStep.Hwinfo:
                Hwinfo.Refresh();
                break;
            case WizardStep.Psu:
                Psu.MeasuredVolts = Shelly.MeasuredVolts;
                break;
            case WizardStep.Sensors when !Sensors.IsLoaded:
                Sensors.Load(_dependencies.ReadSharedMemory()?.GetPowerSensors(), Draft.Sensors, Draft.CustomFormula);
                break;
            case WizardStep.Apply:
                Apply.Prepare(Draft, _database, Shelly.MeasuredVolts);
                break;
            case WizardStep.Verify:
                // Right after Apply: HWiNFO may already show the sensors (a re-run); the grace period covers a slow start.
                Apply.StartVerify();
                break;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
