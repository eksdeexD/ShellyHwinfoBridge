using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>A one-line result shown under a settings action ("Test connection", "Check for update").</summary>
/// <param name="Tone">The tone (Success, Error, Secondary).</param>
/// <param name="Text">The text.</param>
public sealed record ResultLine(Tone Tone, string Text);

/// <summary>What the settings page shows.</summary>
/// <param name="Host">The host field text.</param>
/// <param name="HostError">The host validation error, or <c>null</c>.</param>
/// <param name="HasPassword">Whether a password is stored.</param>
/// <param name="PasswordPlaceholder">"unchanged" when a password is stored, else "leave empty if not set".</param>
/// <param name="PsuTitle">"Seasonic Prime Fanless TX-700".</param>
/// <param name="PsuSubtitle">"700 W · Titanium · Cybenetics 21PS1872A".</param>
/// <param name="VoltageMode">The voltage override.</param>
/// <param name="FormulaText">The Other Components calculation ("PC Power (DC) − CPU Package Power − …") broken across lines before each " − ".</param>
/// <param name="IntervalMs">The poll interval.</param>
/// <param name="IntervalText">"1000 ms".</param>
/// <param name="Smoothing">The smoothing window.</param>
/// <param name="SmoothingText">"1 sample".</param>
/// <param name="EnergyEnabled">Whether energy logging is on.</param>
/// <param name="PriceText">The price field text (current culture), empty when none.</param>
/// <param name="PriceError">The price validation error, or <c>null</c>.</param>
/// <param name="Currency">The currency text.</param>
/// <param name="StartWithWindows">Autostart.</param>
/// <param name="ShowTrayIcon">Whether the tray icon is shown.</param>
/// <param name="DatabaseText">"Last updated 2026-10-29 · 1 entry · shipped" (or "downloaded", "built-in").</param>
/// <param name="TestResult">The last connection test result, or <c>null</c>.</param>
/// <param name="DatabaseResult">The last database check result, or <c>null</c>.</param>
/// <param name="ApplyError">The last apply error, or <c>null</c>.</param>
/// <param name="IsBusy">Whether a test or update check is running.</param>
/// <param name="CurveNote">
/// "230 V curve (115 V data not available for this unit)" when the selected PSU has no curve for the voltage in use;
/// otherwise <c>null</c>.
/// </param>
/// <param name="RetiredFormulaNote">
/// When the settings still hold a retired custom formula: the note that it is no longer applied; otherwise <c>null</c>.
/// </param>
/// <param name="GadgetTip">The gadget how-to with the selected sensor names (<see cref="HwinfoTips.GadgetExportFor"/>).</param>
/// <param name="SmoothComponents">Whether the component sum uses the AC smoothing window.</param>
/// <param name="DevicesText">The Manage Shellys summary ("PC · 192.168.1.42", "PC + 1 more device").</param>
/// <param name="DiagnosticTraceRunning">Whether the diagnostic trace is recording.</param>
/// <param name="DiagnosticTraceText">The Maintenance text action: "Start diagnostic trace" or "Stop diagnostic trace".</param>
/// <param name="DiagnosticTraceCaption">
/// The caption after it: "Recording to trace-YYYYMMDD-HHmmss.csv" while recording, the start error after a failed
/// start, otherwise <c>null</c>.
/// </param>
public sealed record SettingsViewState(
    string Host,
    string? HostError,
    bool HasPassword,
    string PasswordPlaceholder,
    string PsuTitle,
    string PsuSubtitle,
    InputVoltageMode VoltageMode,
    string FormulaText,
    int IntervalMs,
    string IntervalText,
    int Smoothing,
    string SmoothingText,
    bool EnergyEnabled,
    string PriceText,
    string? PriceError,
    string Currency,
    bool StartWithWindows,
    bool ShowTrayIcon,
    string DatabaseText,
    ResultLine? TestResult,
    ResultLine? DatabaseResult,
    string? ApplyError,
    bool IsBusy,
    string? CurveNote = null,
    string? RetiredFormulaNote = null,
    string GadgetTip = "",
    bool SmoothComponents = false,
    string DevicesText = "",
    bool DiagnosticTraceRunning = false,
    string DiagnosticTraceText = SettingsPresenter.StartTraceText,
    string? DiagnosticTraceCaption = null);

/// <summary>
/// Drives the settings page: every setter clones the current settings, changes one thing and applies it through
/// the runtime immediately. Invalid input is reported in the state and not applied.
/// </summary>
/// <remarks>
/// Password semantics: the field starts empty with the placeholder "unchanged" when a password is stored. Call
/// <see cref="SetPassword"/> only when the user typed something (empty text is ignored); <see cref="ClearPassword"/>
/// removes the stored password. Not thread-safe: call from the UI thread.
/// </remarks>
public sealed class SettingsPresenter
{
    /// <summary>The password placeholder when a password is stored.</summary>
    public const string PasswordUnchangedPlaceholder = "unchanged";

    /// <summary>The password placeholder when none is stored.</summary>
    public const string PasswordEmptyPlaceholder = "leave empty if not set";

    /// <summary>The note shown when a retired custom formula is still stored.</summary>
    public const string RetiredFormulaText = "A custom formula from an earlier version is stored but no longer applied: Other Components is now computed by the app from the selected sensors.";

    /// <summary>The Maintenance text action while the diagnostic trace is idle.</summary>
    public const string StartTraceText = "Start diagnostic trace";

    /// <summary>The Maintenance text action while the diagnostic trace is recording.</summary>
    public const string StopTraceText = "Stop diagnostic trace";

    /// <summary>The caption after a failed start of the diagnostic trace.</summary>
    public const string TraceStartFailedText = "Could not create the trace file. See the log.";

    private readonly IBridgeRuntime _runtime;
    private readonly CultureInfo _culture;
    private string? _hostText;
    private string? _hostError;
    private string? _priceText;
    private string? _priceError;
    private ResultLine? _testResult;
    private ResultLine? _databaseResult;
    private string? _applyError;
    private bool _busy;
    private bool _traceFailed;

    /// <summary>Creates a presenter.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="culture">The culture for numbers; defaults to the current culture.</param>
    public SettingsPresenter(IBridgeRuntime runtime, CultureInfo? culture = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    /// <summary>Raised after <see cref="State"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the current view state.</summary>
    public SettingsViewState State => BuildState();

    /// <summary>Sets the host (validated; applied when valid).</summary>
    /// <param name="text">The host text.</param>
    public void SetHost(string text)
    {
        text = (text ?? string.Empty).Trim();
        _hostText = text;
        try
        {
            ShellyClient.BuildBaseUri(text);
        }
        catch (ArgumentException)
        {
            _hostError = "Enter a valid IP address or host name.";
            OnChanged();
            return;
        }

        _hostError = null;
        _hostText = null;
        Update(s => s.Device.Host = text);
    }

    /// <summary>Replaces the stored password with typed text; empty text is ignored (the password stays unchanged).</summary>
    /// <param name="typed">The typed password.</param>
    public void SetPassword(string? typed)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return;
        }

        Update(_ => { }, typed);
    }

    /// <summary>Removes the stored password.</summary>
    public void ClearPassword() => Update(_ => { }, string.Empty);

    /// <summary>Sets the input voltage override.</summary>
    /// <param name="mode">The mode.</param>
    public void SetVoltageMode(InputVoltageMode mode) => Update(s => s.VoltageMode = mode);

    /// <summary>Sets the poll interval (snapped to 100 ms and clamped to 500–5000 ms).</summary>
    /// <param name="milliseconds">The interval.</param>
    public void SetIntervalMs(int milliseconds)
    {
        var snapped = (int)(Math.Round(milliseconds / 100.0, MidpointRounding.AwayFromZero) * 100);
        Update(s => s.PollIntervalMs = Math.Clamp(snapped, PollingOptions.MinIntervalMs, PollingOptions.MaxIntervalMs));
    }

    /// <summary>Sets the smoothing window (clamped to 1–<see cref="MovingAverage.MaxSamples"/>).</summary>
    /// <param name="samples">The number of samples.</param>
    public void SetSmoothing(int samples) => Update(s => s.SmoothingSamples = Math.Clamp(samples, 1, MovingAverage.MaxSamples));

    /// <summary>Turns energy logging on or off.</summary>
    /// <param name="enabled">Whether to log.</param>
    public void SetEnergyEnabled(bool enabled) => Update(s => s.EnergyLoggingEnabled = enabled);

    /// <summary>Sets the price per kWh from text (current culture, then invariant); empty clears it.</summary>
    /// <param name="text">The text.</param>
    public void SetPrice(string? text)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            _priceText = null;
            _priceError = null;
            Update(s => s.EnergyPricePerKwh = null);
            return;
        }

        if (!TryParsePrice(text, _culture, out var price) || price < 0)
        {
            _priceText = text;
            _priceError = "Enter a price such as " + 0.32m.ToString("0.00", _culture) + ".";
            OnChanged();
            return;
        }

        _priceText = null;
        _priceError = null;
        Update(s => s.EnergyPricePerKwh = Math.Min(price, AppSettings.MaxPricePerKwh));
    }

    /// <summary>Sets the currency text (trimmed, at most <see cref="AppSettings.MaxCurrencyLength"/> characters).</summary>
    /// <param name="text">The text.</param>
    public void SetCurrency(string? text) => Update(s => s.EnergyCurrency = text);

    /// <summary>Turns "Start with Windows" on or off.</summary>
    /// <param name="enabled">Whether to start with Windows.</param>
    public void SetStartWithWindows(bool enabled) => Update(s => s.StartWithWindows = enabled);

    /// <summary>Shows or hides the tray icon.</summary>
    /// <param name="show">Whether to show it.</param>
    public void SetShowTrayIcon(bool show) => Update(s => s.HideTrayIcon = !show);

    /// <summary>Applies an arbitrary change to a clone of the settings (used by the PSU and sensor dialogs).</summary>
    /// <param name="mutate">Changes the clone.</param>
    /// <param name="newPassword">See <see cref="IBridgeRuntime.Apply"/>.</param>
    public ApplyOutcome Update(Action<AppSettings> mutate, string? newPassword = null)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var clone = _runtime.Settings?.Clone() ?? new AppSettings();
        mutate(clone);
        var outcome = _runtime.Apply(clone, newPassword);
        _applyError = outcome.Success ? null : outcome.Error;
        OnChanged();
        return outcome;
    }

    /// <summary>Tests a host and password; an empty password falls back to the stored one.</summary>
    /// <param name="host">The host.</param>
    /// <param name="typedPassword">The typed password, or empty to use the stored one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TestConnectionAsync(string host, string? typedPassword, CancellationToken cancellationToken = default)
    {
        _busy = true;
        _testResult = new ResultLine(Tone.Secondary, "Testing…");
        OnChanged();
        try
        {
            var password = string.IsNullOrEmpty(typedPassword) ? _runtime.Password : typedPassword;
            var result = await _runtime.TestConnectionAsync(host, password, cancellationToken).ConfigureAwait(true);
            _testResult = result.Success && result.Reading is { } r
                ? new ResultLine(Tone.Success, ConnectedText(result, r))
                : new ResultLine(Tone.Error, result.Error ?? "The test failed.");
        }
        finally
        {
            _busy = false;
            OnChanged();
        }
    }

    /// <summary>Checks for a PSU database update and reports the result.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CheckDatabaseUpdateAsync(CancellationToken cancellationToken = default)
    {
        _busy = true;
        _databaseResult = new ResultLine(Tone.Secondary, "Checking…");
        OnChanged();
        try
        {
            var result = await _runtime.CheckDatabaseUpdateAsync(cancellationToken).ConfigureAwait(true);
            var tone = result.Status switch
            {
                DatabaseUpdateStatus.Updated => Tone.Success,
                DatabaseUpdateStatus.AlreadyCurrent => Tone.Secondary,
                _ => Tone.Error,
            };
            _databaseResult = new ResultLine(tone, result.Message);
        }
        finally
        {
            _busy = false;
            OnChanged();
        }
    }

    /// <summary>Starts the diagnostic trace while idle, stops it while recording.</summary>
    public void ToggleDiagnosticTrace()
    {
        if (_runtime.IsDiagnosticTraceRunning)
        {
            _runtime.StopDiagnosticTrace();
            _traceFailed = false;
        }
        else
        {
            _traceFailed = !_runtime.StartDiagnosticTrace();
        }

        OnChanged();
    }

    /// <summary>Returns the diagnostic trace text action for the trace state.</summary>
    /// <param name="running">Whether the trace is recording.</param>
    public static string TraceLinkText(bool running) => running ? StopTraceText : StartTraceText;

    /// <summary>Returns the caption while recording: "Recording to trace-YYYYMMDD-HHmmss.csv" (file name only).</summary>
    /// <param name="path">The trace file's path, or <c>null</c>.</param>
    public static string TraceCaption(string? path) =>
        "Recording to " + (string.IsNullOrEmpty(path) ? "a trace file" : Path.GetFileName(path));

    /// <summary>Removes the HWiNFO sensors (polling pauses).</summary>
    public void RemoveSensors()
    {
        _runtime.RemoveSensors();
        OnChanged();
    }

    /// <summary>Parses a price: current culture first, then the invariant culture.</summary>
    /// <param name="text">The text.</param>
    /// <param name="culture">The current culture.</param>
    /// <param name="price">The parsed price.</param>
    public static bool TryParsePrice(string? text, CultureInfo culture, out decimal price)
    {
        ArgumentNullException.ThrowIfNull(culture);
        const NumberStyles styles = NumberStyles.Number & ~NumberStyles.AllowThousands;
        text = (text ?? string.Empty).Trim();
        return decimal.TryParse(text, styles, culture, out price) ||
               decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out price);
    }

    /// <summary>Breaks a calculation across lines before each " − " (or " - ").</summary>
    /// <param name="formula">The calculation text.</param>
    public static string BreakFormula(string formula) =>
        (formula ?? string.Empty).Replace(" − ", "\r\n − ", StringComparison.Ordinal).Replace(" - ", "\r\n - ", StringComparison.Ordinal);

    /// <summary>Sets the energy chart mode (the Combined / Per device toggle).</summary>
    /// <param name="mode">The mode.</param>
    public void SetChartMode(EnergyChartMode mode) => Update(s => s.ChartMode = mode);

    /// <summary>Turns smoothing of the Other Components component sum on or off.</summary>
    /// <param name="enabled">Whether to smooth.</param>
    public void SetSmoothComponents(bool enabled) => Update(s => s.SmoothComponents = enabled);

    /// <summary>Creates the Manage Shellys page model over the same runtime.</summary>
    /// <param name="discover">Browses the network for plugs (for the device form).</param>
    public ManageShellysModel CreateManageShellys(Func<CancellationToken, Task<IReadOnlyList<Shelly.Discovery.DiscoveredShelly>>> discover) =>
        new(_runtime, discover, _culture);

    /// <summary>Creates the "Customize HWiNFO sensor names" dialog model over the same runtime.</summary>
    public SensorNamesModel CreateSensorNames() => new(_runtime);

    private string ConnectedText(ConnectionTestResult result, ShellyReading r)
    {
        var parts = new List<string> { UiFormat.Watts(r.ActivePowerWatts, _culture) };
        if (r.Volts is double v)
        {
            parts.Add(UiFormat.Volts(v, _culture));
        }

        if (r.Amps is double a)
        {
            parts.Add(a.ToString("0.00", _culture) + " A");
        }

        var name = result.DeviceInfo?.Name ?? result.DeviceInfo?.App;
        return (name is null ? "Connected" : "Connected to " + name) + ": " + string.Join(" · ", parts);
    }

    private SettingsViewState BuildState()
    {
        var s = _runtime.Settings ?? new AppSettings();
        var db = _runtime.Database;
        var (psuTitle, psuSubtitle) = PsuText(s, db);
        var hasPassword = _runtime.HasPassword;
        var formula = BreakFormula(CalculationText(s));
        var traceRunning = _runtime.IsDiagnosticTraceRunning;

        return new SettingsViewState(
            _hostText ?? s.Device.Host,
            _hostError,
            hasPassword,
            hasPassword ? PasswordUnchangedPlaceholder : PasswordEmptyPlaceholder,
            psuTitle,
            psuSubtitle,
            s.VoltageMode,
            formula,
            s.PollIntervalMs,
            s.PollIntervalMs.ToString(_culture) + " ms",
            s.SmoothingSamples,
            s.SmoothingSamples == 1 ? "1 sample" : s.SmoothingSamples.ToString(_culture) + " samples",
            s.EnergyLoggingEnabled,
            _priceText ?? s.EnergyPricePerKwh?.ToString("0.00##", _culture) ?? string.Empty,
            _priceError,
            s.EnergyCurrency ?? string.Empty,
            s.StartWithWindows,
            !s.HideTrayIcon,
            DatabaseText(db, _runtime.DatabaseOrigin),
            _testResult,
            _databaseResult,
            _applyError,
            _busy,
            CurveNote(s, db),
            string.IsNullOrWhiteSpace(s.CustomFormula) ? null : RetiredFormulaText,
            HwinfoTips.GadgetExportFor(s.Sensors.Select(x => x.Name)),
            s.SmoothComponents,
            DevicesText(s),
            traceRunning,
            TraceLinkText(traceRunning),
            traceRunning ? TraceCaption(_runtime.DiagnosticTraceFile) : _traceFailed ? TraceStartFailedText : null);
    }

    /// <summary>Returns the Other Components calculation: "PC Power (DC) − CPU Package Power − GPU Power".</summary>
    /// <param name="settings">The settings.</param>
    public static string CalculationText(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.Join(" − ", settings.Sensors.Select(x => x.Name.Trim()).Where(SensorNaming.IsValidName).Prepend(SensorNaming.PcDcName));
    }

    private string DevicesText(AppSettings s)
    {
        var host = s.Device.Host.Trim();
        var pc = host.Length == 0 ? AppSettings.PrimaryLabel : AppSettings.PrimaryLabel + " · " + host;
        return s.AdditionalDevices.Count switch
        {
            0 => pc,
            1 => pc + " + 1 more device",
            var n => pc + " + " + n.ToString(_culture) + " more devices",
        };
    }

    private string? CurveNote(AppSettings s, PsuDatabase db)
    {
        var voltage = VoltageSelector.Resolve(s.VoltageMode, _runtime.LastReading?.Raw.Volts);
        return PsuCurveResolver.Resolve(s.Psu, db)?.CurveNote(voltage);
    }

    private (string Title, string Subtitle) PsuText(AppSettings s, PsuDatabase db)
    {
        switch (s.Psu.Mode)
        {
            case PsuSelectionMode.Database:
                var entry = s.Psu.DatabaseId is { } id ? new PsuDatabaseQuery(db).FindById(id) : null;
                if (entry is null)
                {
                    return ("No PSU selected", s.Psu.DatabaseId is null ? "Choose one with Change" : "The selected PSU is not in the database");
                }

                var parts = new List<string> { entry.Wattage.ToString(_culture) + " W" };
                if (!string.IsNullOrWhiteSpace(entry.Rating80Plus))
                {
                    parts.Add(entry.Rating80Plus);
                }

                if (entry.Source is { } source && !string.IsNullOrWhiteSpace(source.Name))
                {
                    parts.Add(string.IsNullOrWhiteSpace(source.ReportId) ? source.Name + " report" : source.Name + " " + source.ReportId.Trim());
                }

                return ($"{entry.Brand} {entry.DisplayName}".Trim(), string.Join(" · ", parts));

            case PsuSelectionMode.Generic:
                return s.Psu.GenericWattage is int w && s.Psu.GenericRating is { } rating
                    ? ($"Generic {rating} {w.ToString(_culture)} W", "Approximate curve from the 80 Plus rating")
                    : ("Generic curve", "Wattage or rating missing");

            default:
                var points = s.Psu.ManualCurve?.Count ?? 0;
                return ("Manual curve", points == 1 ? "1 point" : points.ToString(_culture) + " points");
        }
    }

    /// <summary>The source word of <see cref="SettingsViewState.DatabaseText"/> ("downloaded", "shipped", "built-in").</summary>
    /// <param name="origin">Where the database was loaded from.</param>
    public static string DatabaseSourceText(DatabaseOrigin origin) => origin switch
    {
        DatabaseOrigin.Downloaded => "downloaded",
        DatabaseOrigin.Shipped => "shipped",
        _ => "built-in",
    };

    private string DatabaseText(PsuDatabase db, DatabaseOrigin origin)
    {
        var count = db.Psus.Count;
        var entries = count == 1 ? "1 entry" : count.ToString(_culture) + " entries";
        var text = string.IsNullOrWhiteSpace(db.Updated) ? entries : $"Last updated {db.Updated} · {entries}";
        return text + " · " + DatabaseSourceText(origin);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
