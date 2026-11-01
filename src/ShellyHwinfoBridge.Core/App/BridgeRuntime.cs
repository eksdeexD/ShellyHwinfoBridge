using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Startup;

namespace ShellyHwinfoBridge.Core.App;

/// <summary>
/// The bridge's composition of settings, one <see cref="PollingService"/> per Shelly device, the HWiNFO registry
/// writer, autostart, the <see cref="EnergyLogger"/> and the Other Components calculation. See <see cref="IBridgeRuntime"/>.
/// </summary>
/// <remarks>
/// <para>Every device is polled on the shared interval by its own poller (own request pipeline and offline counter);
/// device i of n starts i × interval / n after the first, so requests do not fire at the same instant. One device's
/// failure never affects the others. The PC's readings drive the PSU curve, Other Components, the status and the tray.</para>
/// <para>Readings update the state on every poll, but the power values of the <see cref="SensorLayout"/> are written to
/// HWiNFO from readings at most every <see cref="HwinfoWriteInterval"/>. Other events (going offline, pause, layout and
/// settings changes, shutdown) write at once. The energy logger writes the energy totals after each reading.</para>
/// <para>Settings changes rebuild only what changed. A replaced Shelly client is disposed immediately; an in-flight
/// request on it fails and counts as one poll failure (never enough to go offline).</para>
/// <para>All members are thread-safe; events are raised outside the internal lock.</para>
/// </remarks>
public sealed class BridgeRuntime : IBridgeRuntime
{
    /// <summary>The timeout of <see cref="TestConnectionAsync"/>.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The shortest time between two reading-driven writes of the power values to HWiNFO (with 1 s polling every
    /// second poll writes). HWiNFO polls its sensors every 2 s by default, so the bridge's sensors change at the same
    /// cadence as HWiNFO's own numbers instead of half of HWiNFO's samples showing a value one poll newer. Writes caused
    /// by other events (offline, pause, layout or settings changes, shutdown) are not throttled.
    /// </summary>
    public static readonly TimeSpan HwinfoWriteInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The slack on <see cref="HwinfoWriteInterval"/>: a reading that arrives up to this much early still writes, so
    /// HTTP response-time jitter does not turn every second poll into every third.
    /// </summary>
    internal static readonly TimeSpan HwinfoWriteTolerance = TimeSpan.FromMilliseconds(250);

    private readonly SettingsStore _store;
    private readonly PsuDatabaseLoader _databaseLoader;
    private readonly DatabaseUpdater _databaseUpdater;
    private readonly IHwinfoSensorRegistry _registry;
    private readonly AutostartManager _autostart;
    private readonly EnergyLogger _energy;
    private readonly IComponentSensorSource? _components;
    private readonly Func<string, string?, IShellyClient> _clientFactory;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, DevicePoller> _pollers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _devicePasswords = new(StringComparer.OrdinalIgnoreCase);
    private readonly OtherComponentsCalculator _other;
    private readonly DiagnosticTrace _trace;

    private AppSettings? _settings;
    private string? _password;
    private PsuDatabase _database;
    private DatabaseOrigin _databaseOrigin;
    private PowerReadingEventArgs? _last;
    private OtherComponentsState _otherState = OtherComponentsState.Unknown;
    private bool _started;
    private bool _userPaused;
    private bool _sensorsRemoved;
    private DateTimeOffset? _lastReadingWrite;

    /// <summary>Creates the runtime; nothing is polled until <see cref="Start"/>.</summary>
    /// <param name="settings">The loaded settings, or <c>null</c> on first run.</param>
    /// <param name="store">Settings persistence and password protection.</param>
    /// <param name="databaseLoader">Loads the PSU database.</param>
    /// <param name="databaseUpdater">Checks for database updates.</param>
    /// <param name="registry">The HWiNFO sensor writer; its layout is set from <paramref name="settings"/> (no registry access).</param>
    /// <param name="autostart">The Run-key manager.</param>
    /// <param name="energy">The energy logger (fed with every reading).</param>
    /// <param name="clientFactory">Creates a Shelly client for a host and password; defaults to <see cref="ShellyClient"/>.</param>
    /// <param name="timeProvider">The clock; defaults to the system clock.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="components">Reads the selected component sensors (e.g. <see cref="HwinfoMonitor"/>); <c>null</c> = no source, Other stays 0.</param>
    /// <param name="diagnosticsDirectory">The folder of the diagnostic trace files; defaults to <see cref="AppPaths.DiagnosticsDirectory"/>.</param>
    public BridgeRuntime(
        AppSettings? settings,
        SettingsStore store,
        PsuDatabaseLoader databaseLoader,
        DatabaseUpdater databaseUpdater,
        IHwinfoSensorRegistry registry,
        AutostartManager autostart,
        EnergyLogger energy,
        Func<string, string?, IShellyClient>? clientFactory = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        IComponentSensorSource? components = null,
        string? diagnosticsDirectory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _databaseLoader = databaseLoader ?? throw new ArgumentNullException(nameof(databaseLoader));
        _databaseUpdater = databaseUpdater ?? throw new ArgumentNullException(nameof(databaseUpdater));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        _energy = energy ?? throw new ArgumentNullException(nameof(energy));
        _components = components;
        _clientFactory = clientFactory ?? ((host, password) => new ShellyClient(host, password));
        _time = timeProvider ?? TimeProvider.System;
        _other = new OtherComponentsCalculator(_time);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<BridgeRuntime>();
        _trace = new DiagnosticTrace(diagnosticsDirectory ?? AppPaths.DiagnosticsDirectory, _time, _loggerFactory.CreateLogger<DiagnosticTrace>());
        _trace.StateChanged += (_, _) => RaiseStateChanged();
        var loaded = _databaseLoader.Load();
        _database = loaded.Database;
        _databaseOrigin = loaded.Origin;
        if (settings is not null)
        {
            settings.Normalize();
            _settings = settings;
            _password = store.GetPassword(settings);
            LoadDevicePasswords(settings);
            _registry.SetLayout(SensorLayout.Build(settings));
        }
    }

    /// <inheritdoc />
    public event EventHandler? SettingsChanged;

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public event EventHandler<PowerReadingEventArgs>? ReadingReceived;

    /// <inheritdoc />
    public event EventHandler? SensorKeysCreated;

    /// <inheritdoc />
    public AppSettings? Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    /// <inheritdoc />
    public bool HasPassword
    {
        get
        {
            lock (_gate)
            {
                return !string.IsNullOrEmpty(_settings?.Device.ProtectedPassword);
            }
        }
    }

    /// <inheritdoc />
    public string? Password
    {
        get
        {
            lock (_gate)
            {
                return _password;
            }
        }
    }

    /// <inheritdoc />
    public PsuDatabase Database
    {
        get
        {
            lock (_gate)
            {
                return _database;
            }
        }
    }

    /// <inheritdoc />
    public DatabaseOrigin DatabaseOrigin
    {
        get
        {
            lock (_gate)
            {
                return _databaseOrigin;
            }
        }
    }

    /// <inheritdoc />
    public RuntimeState State
    {
        get
        {
            lock (_gate)
            {
                return ComputeState();
            }
        }
    }

    /// <inheritdoc />
    public OnlineState OnlineState
    {
        get
        {
            lock (_gate)
            {
                return Primary?.Poller.State ?? OnlineState.Unknown;
            }
        }
    }

    /// <inheritdoc />
    public PowerReadingEventArgs? LastReading
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DeviceStatus> Devices
    {
        get
        {
            lock (_gate)
            {
                var settings = _settings ?? new AppSettings();
                var list = new List<DeviceStatus>
                {
                    Status(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, settings.Device, true),
                };
                list.AddRange(settings.AdditionalDevices.Select(d => Status(d.Id, d.Label, d, d.IncludeInTotals)));
                return list;
            }
        }
    }

    /// <inheritdoc />
    public double? TotalWatts
    {
        get
        {
            lock (_gate)
            {
                return _settings is { HasAdditionalDevices: true } && _last is not null && ComputeState() == RuntimeState.Running
                    ? ComputeTotal()
                    : null;
            }
        }
    }

    /// <inheritdoc />
    public OtherComponentsState Other
    {
        get
        {
            lock (_gate)
            {
                return _otherState;
            }
        }
    }

    /// <inheritdoc />
    public bool IsDiagnosticTraceRunning => _trace.IsRunning;

    /// <inheritdoc />
    public string? DiagnosticTraceFile => _trace.CurrentFile;

    /// <inheritdoc />
    public bool StartDiagnosticTrace()
    {
        IReadOnlyList<SensorSelection> sensors;
        lock (_gate)
        {
            sensors = [.. _settings?.Sensors ?? []];
        }

        return _trace.Start(sensors);
    }

    /// <inheritdoc />
    public void StopDiagnosticTrace() => _trace.Stop();

    private DevicePoller? Primary => _pollers.TryGetValue(AppSettings.PrimaryDeviceId, out var p) ? p : null;

    /// <summary>Gets whether settings describe a device (a host is set).</summary>
    /// <param name="settings">The settings.</param>
    public static bool IsConfigured(AppSettings? settings) => settings is not null && !string.IsNullOrWhiteSpace(settings.Device.Host);

    /// <summary>Returns the stagger offset of the device at <paramref name="index"/> among <paramref name="count"/> devices.</summary>
    /// <param name="index">The device index (0 = PC).</param>
    /// <param name="count">The number of devices.</param>
    /// <param name="interval">The poll interval.</param>
    public static TimeSpan StaggerOffset(int index, int count, TimeSpan interval) =>
        count <= 1 || index <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(interval.Ticks * index / count);

    /// <inheritdoc />
    public string? GetDevicePassword(string deviceId)
    {
        lock (_gate)
        {
            return AppSettings.IsPrimary(deviceId) ? _password : _devicePasswords.GetValueOrDefault(deviceId);
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            Reconcile(rebuildCalculator: false);
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        List<DevicePoller> pollers;
        lock (_gate)
        {
            _started = false;
            pollers = [.. _pollers.Values];
            _pollers.Clear();
            _last = null;
        }

        foreach (var entry in pollers)
        {
            Unsubscribe(entry.Poller);
        }

        await Task.WhenAll(pollers.Select(p => p.Poller.StopAsync())).ConfigureAwait(false);
        foreach (var entry in pollers)
        {
            (entry.Client as IDisposable)?.Dispose();
        }

        lock (_gate)
        {
            GoOfflineLocked();
        }

        _energy.Flush();
        _trace.Stop();
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_gate)
        {
            if (!IsConfigured(_settings) || _userPaused)
            {
                return;
            }

            _userPaused = true;
            PauseAllLocked();
        }

        _logger.LogInformation("Polling paused by the user.");
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Resume()
    {
        var keysCreated = false;
        lock (_gate)
        {
            if (!_userPaused)
            {
                return;
            }

            if (_sensorsRemoved)
            {
                keysCreated = TryApplyLayout(_settings is null ? _registry.Layout : SensorLayout.Build(_settings));
                _sensorsRemoved = false;
            }

            _userPaused = false;
            Reconcile(rebuildCalculator: false);
        }

        _logger.LogInformation("Polling resumed by the user.");
        if (keysCreated)
        {
            SensorKeysCreated?.Invoke(this, EventArgs.Empty);
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public ApplyOutcome Apply(AppSettings settings, string? newPassword = null, IReadOnlyDictionary<string, string>? devicePasswords = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();
        List<string> changed;
        var layoutChanged = false;
        lock (_gate)
        {
            var old = _settings;
            var oldPasswords = new Dictionary<string, string?>(_devicePasswords, StringComparer.OrdinalIgnoreCase);
            string? password;
            try
            {
                if (newPassword is not null)
                {
                    _store.SetPassword(settings, newPassword);
                }

                ApplyDevicePasswords(settings, devicePasswords);
                settings.Normalize();
                password = newPassword is null ? _store.GetPassword(settings) : (newPassword.Length == 0 ? null : newPassword);
                _store.Save(settings);
            }
            catch (Exception ex) when (IsIoError(ex))
            {
                _logger.LogError(ex, "Saving settings failed.");
                return ApplyOutcome.Failed("The settings could not be saved: " + ex.Message);
            }

            changed = Diff(old, _password, settings, password);
            var calculatorChanged = changed.Contains(Keys.Psu) || changed.Contains(Keys.VoltageMode) || changed.Contains(Keys.Smoothing);
            _settings = settings;
            _password = password;
            LoadDevicePasswords(settings);
            if (!changed.Contains(Keys.Devices) && DevicePasswordsDiffer(oldPasswords, _devicePasswords))
            {
                changed.Add(Keys.Devices);
            }

            if (IsConfigured(settings) && !_sensorsRemoved)
            {
                var layout = SensorLayout.Build(settings);
                if (!layout.Equals(_registry.Layout))
                {
                    layoutChanged = TryApplyLayout(layout);
                    if (!changed.Contains(Keys.SensorLayout))
                    {
                        changed.Add(Keys.SensorLayout);
                    }
                }
            }
            else
            {
                _registry.SetLayout(SensorLayout.Build(settings));
            }

            if (changed.Contains(Keys.StartWithWindows) && !TryApplyAutostart(settings.StartWithWindows))
            {
                errors.Add("The Windows startup entry could not be changed.");
            }

            _energy.Enabled = settings.EnergyLoggingEnabled;
            Reconcile(calculatorChanged);
        }

        if (changed.Count > 0)
        {
            _logger.LogInformation("Settings changed: {Keys}.", string.Join(", ", changed));
        }

        if (layoutChanged)
        {
            SensorKeysCreated?.Invoke(this, EventArgs.Empty);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RaiseStateChanged();
        return errors.Count == 0 ? new ApplyOutcome(true, null, changed) : new ApplyOutcome(false, string.Join(" ", errors), changed);
    }

    /// <inheritdoc />
    public ApplyOutcome ApplyFromWizard(AppSettings draft, string? password, bool startWithWindows, IReadOnlyDictionary<string, string>? devicePasswords = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (_gate)
        {
            try
            {
                _store.SetPassword(draft, password);
                ApplyDevicePasswords(draft, devicePasswords);
                draft.StartWithWindows = startWithWindows;
                draft.Normalize();
                _store.Save(draft);
                _registry.ApplyLayout(SensorLayout.Build(draft));
                _autostart.Apply(startWithWindows);
            }
            catch (Exception ex) when (IsIoError(ex))
            {
                _logger.LogError(ex, "Applying the setup wizard failed.");
                return ApplyOutcome.Failed("Applying the setup failed: " + ex.Message);
            }

            _settings = draft;
            _password = string.IsNullOrEmpty(password) ? null : password;
            LoadDevicePasswords(draft);
            _userPaused = false;
            _sensorsRemoved = false;
            _started = true;
            _energy.Enabled = draft.EnergyLoggingEnabled;
            RebuildAllClients();
            Reconcile(rebuildCalculator: true);
        }

        _logger.LogInformation("Setup wizard applied.");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RaiseStateChanged();
        return new ApplyOutcome(true, null, ["Wizard"]);
    }

    /// <inheritdoc />
    public async Task<DatabaseUpdateResult> CheckDatabaseUpdateAsync(CancellationToken cancellationToken = default)
    {
        string url;
        PsuDatabase current;
        lock (_gate)
        {
            url = _settings?.GetEffectiveDatabaseUpdateUrl() ?? AppSettings.DefaultDatabaseUpdateUrl;
            current = _database;
        }

        var result = await _databaseUpdater.CheckForUpdateAsync(url, current, cancellationToken).ConfigureAwait(false);
        if (result.Status == DatabaseUpdateStatus.Updated && result.Database is { } updated)
        {
            lock (_gate)
            {
                _database = updated;
                _databaseOrigin = _databaseLoader.WriteOrigin;
                Reconcile(rebuildCalculator: true);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
            RaiseStateChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public void RemoveSensors()
    {
        lock (_gate)
        {
            _userPaused = true;
            foreach (var entry in _pollers.Values)
            {
                entry.Poller.Pause();
            }

            ResetLiveLocked();
            try
            {
                _registry.RemoveAll();
            }
            catch (Exception ex) when (IsIoError(ex))
            {
                _logger.LogError(ex, "Removing the HWiNFO sensors failed.");
            }

            _sensorsRemoved = true;
        }

        _logger.LogInformation("HWiNFO sensors removed; polling paused.");
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestConnectionAsync(string host, string? password, CancellationToken cancellationToken = default)
    {
        IShellyClient client;
        try
        {
            ShellyClient.BuildBaseUri(host);
            client = _clientFactory(host.Trim(), string.IsNullOrEmpty(password) ? null : password);
        }
        catch (ArgumentException)
        {
            return new ConnectionTestResult(false, null, null, "Enter a valid IP address or host name.");
        }

        try
        {
            using var timeout = new CancellationTokenSource(TestTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            ShellyDeviceInfo? info = null;
            try
            {
                info = await client.GetDeviceInfoAsync(linked.Token).ConfigureAwait(false);
            }
            catch (ShellyException ex) when (ex is not ShellyAuthenticationException)
            {
                // The reading below decides; the device info is optional.
            }

            var reading = await client.GetReadingAsync(linked.Token).ConfigureAwait(false);
            return new ConnectionTestResult(true, reading, info, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, null, null, $"No answer within {TestTimeout.TotalSeconds:0} seconds.");
        }
        catch (ShellyAuthenticationException)
        {
            return new ConnectionTestResult(false, null, null, "The device rejected the password.");
        }
        catch (Exception ex) when (ex is ShellyException or HttpRequestException or IOException)
        {
            return new ConnectionTestResult(false, null, null, ex.Message);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    private static bool IsIoError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or CryptographicException or InvalidOperationException or ArgumentException;

    private static List<string> Diff(AppSettings? old, string? oldPassword, AppSettings now, string? newPassword)
    {
        var changed = new List<string>();
        if (old is null)
        {
            changed.Add(Keys.Host);
            changed.Add(Keys.Password);
            changed.Add(Keys.Psu);
            changed.Add(Keys.Sensors);
            changed.Add(Keys.StartWithWindows);
            return changed;
        }

        void Check(bool different, string key)
        {
            if (different)
            {
                changed.Add(key);
            }
        }

        Check(!string.Equals(old.Device.Host.Trim(), now.Device.Host.Trim(), StringComparison.OrdinalIgnoreCase), Keys.Host);
        Check(!string.Equals(oldPassword ?? string.Empty, newPassword ?? string.Empty, StringComparison.Ordinal), Keys.Password);
        Check(old.Device.Name != now.Device.Name || old.Device.Model != now.Device.Model, Keys.DeviceName);
        Check(Json(old.AdditionalDevices.Select(d => new { d.Id, d.Label, d.Host, d.Name, d.Model, d.IncludeInTotals })) !=
              Json(now.AdditionalDevices.Select(d => new { d.Id, d.Label, d.Host, d.Name, d.Model, d.IncludeInTotals })), Keys.Devices);
        Check(Json(old.SensorNames.OrderBy(p => p.Key, StringComparer.Ordinal)) != Json(now.SensorNames.OrderBy(p => p.Key, StringComparer.Ordinal)), Keys.SensorNames);
        Check(old.ChartMode != now.ChartMode, Keys.ChartMode);
        Check(old.ColourMode != now.ColourMode || old.BaseColour != now.BaseColour || old.Device.Colour != now.Device.Colour ||
              !old.AdditionalDevices.Select(d => d.Colour).SequenceEqual(now.AdditionalDevices.Select(d => d.Colour)), Keys.Colours);
        Check(Json(old.Psu) != Json(now.Psu), Keys.Psu);
        Check(old.VoltageMode != now.VoltageMode, Keys.VoltageMode);
        Check(old.SmoothingSamples != now.SmoothingSamples, Keys.Smoothing);
        Check(old.DisplayIntervalMs != now.DisplayIntervalMs, Keys.DisplayInterval);
        Check(Json(old.Sensors) != Json(now.Sensors), Keys.Sensors);
        Check(old.CustomFormula != now.CustomFormula, Keys.CustomFormula);
        Check(old.StartWithWindows != now.StartWithWindows, Keys.StartWithWindows);
        Check(old.HideTrayIcon != now.HideTrayIcon, Keys.HideTrayIcon);
        Check(old.EnergyLoggingEnabled != now.EnergyLoggingEnabled, Keys.EnergyLogging);
        Check(old.EnergyPricePerKwh != now.EnergyPricePerKwh || old.EnergyCurrency != now.EnergyCurrency, Keys.EnergyPrice);
        Check(old.DatabaseUpdateUrl != now.DatabaseUpdateUrl, Keys.DatabaseUrl);
        return changed;
    }

    private static bool DevicePasswordsDiffer(Dictionary<string, string?> a, Dictionary<string, string?> b) =>
        a.Count != b.Count || a.Any(p => !b.TryGetValue(p.Key, out var v) || !string.Equals(v, p.Value, StringComparison.Ordinal));

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, SettingsStore.JsonOptions);

    private static PowerCalculator CreateCalculator(AppSettings settings, PsuCurveSet curves) =>
        new(curves, settings.VoltageMode, settings.SmoothingSamples);

    private DeviceStatus Status(string id, string label, ShellyDeviceSettings device, bool included)
    {
        var entry = _pollers.GetValueOrDefault(id);
        var state = entry?.Poller.State ?? OnlineState.Unknown;
        var live = state == OnlineState.Online && ComputeState() == RuntimeState.Running ? entry?.LastAc : null;
        return new DeviceStatus(id, label, device.Host, device.Name, device.Model, state, live, included);
    }

    private void ApplyDevicePasswords(AppSettings settings, IReadOnlyDictionary<string, string>? passwords)
    {
        if (passwords is null)
        {
            return;
        }

        foreach (var (id, password) in passwords)
        {
            if (settings.FindAdditionalDevice(id) is { } device && password is not null)
            {
                _store.SetDevicePassword(device, password);
            }
        }
    }

    private void LoadDevicePasswords(AppSettings settings)
    {
        _devicePasswords.Clear();
        foreach (var device in settings.AdditionalDevices)
        {
            _devicePasswords[device.Id] = _store.GetDevicePassword(device);
        }
    }

    private RuntimeState ComputeState()
    {
        if (!IsConfigured(_settings))
        {
            return RuntimeState.Unconfigured;
        }

        if (PsuCurveResolver.Resolve(_settings!.Psu, _database) is null)
        {
            return RuntimeState.CurveUnavailable;
        }

        return _userPaused ? RuntimeState.Paused : RuntimeState.Running;
    }

    /// <summary>Brings the pollers in line with the settings and state. Caller holds the lock.</summary>
    private void Reconcile(bool rebuildCalculator)
    {
        if (!_started)
        {
            return;
        }

        var settings = _settings;
        var curves = IsConfigured(settings) ? PsuCurveResolver.Resolve(settings!.Psu, _database) : null;
        if (curves is null)
        {
            // Unconfigured or no curve: keep the sensors offline.
            PauseAllLocked();
            return;
        }

        _energy.DeviceKey = EnergyLogger.BuildDeviceKey(settings!.Device.Host, null);
        _energy.SetDevices(EnergyLogger.DevicesFrom(settings));
        _energy.PollInterval = settings.GetPollingOptions().Interval;
        _energy.Enabled = settings.EnergyLoggingEnabled;

        var options = settings.GetPollingOptions();
        var wanted = new List<(string Id, string Host, string? Password)> { (AppSettings.PrimaryDeviceId, settings.Device.Host.Trim(), _password) };
        wanted.AddRange(settings.AdditionalDevices.Where(d => !string.IsNullOrWhiteSpace(d.Host)).Select(d => (d.Id, d.Host.Trim(), _devicePasswords.GetValueOrDefault(d.Id))));

        // Removed devices: stop their pollers (the new layout no longer has their sensors).
        foreach (var id in _pollers.Keys.Where(id => !wanted.Any(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            var removed = _pollers[id];
            _pollers.Remove(id);
            Unsubscribe(removed.Poller);
            _ = StopAndDisposeAsync(removed);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var (id, host, password) = wanted[i];
            var isPrimary = i == 0;
            if (!_pollers.TryGetValue(id, out var entry))
            {
                var client = CreateClient(id, host, password);
                if (client is null)
                {
                    continue;
                }

                var poller = new PollingService(
                    client,
                    isPrimary ? CreateCalculator(settings, curves) : null,
                    options,
                    _loggerFactory.CreateLogger<PollingService>(),
                    _time,
                    id,
                    StaggerOffset(i, wanted.Count, options.Interval));
                entry = new DevicePoller(id, poller, client, host, password);
                _pollers[id] = entry;
                poller.ReadingReceived += OnPollerReading;
                poller.OnlineStateChanged += OnPollerOnlineStateChanged;
                poller.PollFailed += OnPollerFailed;
                if (_userPaused)
                {
                    poller.Pause();
                }

                poller.Start();
                continue;
            }

            if (!string.Equals(entry.Host, host, StringComparison.OrdinalIgnoreCase) || !string.Equals(entry.Password, password, StringComparison.Ordinal))
            {
                var client = CreateClient(id, host, password);
                if (client is not null)
                {
                    var old = entry.Client;
                    entry.Client = client;
                    entry.Host = host;
                    entry.Password = password;
                    entry.Poller.UpdateClient(client);
                    (old as IDisposable)?.Dispose();
                    entry.LastAc = null;
                    if (isPrimary)
                    {
                        _last = null;
                    }
                }
            }

            if (isPrimary && rebuildCalculator)
            {
                entry.Poller.UpdateCalculator(CreateCalculator(settings, curves));
            }

            entry.Poller.UpdateOptions(options);
            if (_userPaused)
            {
                entry.Poller.Pause();
            }
            else
            {
                entry.Poller.Resume();
            }
        }

        if (_userPaused)
        {
            ResetLiveLocked();
        }
    }

    private IShellyClient? CreateClient(string id, string host, string? password)
    {
        try
        {
            return _clientFactory(host, password);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "The host of device {Device} is invalid.", id);
            return null;
        }
    }

    /// <summary>Recreates every client (the wizard may have changed hosts or passwords). Caller holds the lock.</summary>
    private void RebuildAllClients()
    {
        foreach (var entry in _pollers.Values)
        {
            // Force a rebuild on the next reconcile.
            entry.Host = string.Empty;
        }
    }

    private async Task StopAndDisposeAsync(DevicePoller entry)
    {
        try
        {
            await entry.Poller.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping the poller of {Device} failed.", entry.Id);
        }
        finally
        {
            (entry.Client as IDisposable)?.Dispose();
        }
    }

    private void Unsubscribe(PollingService poller)
    {
        poller.ReadingReceived -= OnPollerReading;
        poller.OnlineStateChanged -= OnPollerOnlineStateChanged;
        poller.PollFailed -= OnPollerFailed;
    }

    /// <summary>Pauses every poller and sets the sensors offline. Caller holds the lock.</summary>
    private void PauseAllLocked()
    {
        foreach (var entry in _pollers.Values)
        {
            entry.Poller.Pause();
        }

        if (!_sensorsRemoved)
        {
            GoOfflineLocked();
        }
        else
        {
            ResetLiveLocked();
        }
    }

    /// <summary>Clears the live values and writes the sensors offline (every value 0). Caller holds the lock.</summary>
    private void GoOfflineLocked()
    {
        ResetLiveLocked();
        if (!_sensorsRemoved)
        {
            TryWrite(_registry.SetOffline);
        }
    }

    private void ResetLiveLocked()
    {
        _last = null;
        _lastReadingWrite = null;
        _other.Reset();
        _otherState = _otherState with { Watts = 0, HasValue = false };
        foreach (var entry in _pollers.Values)
        {
            entry.LastAc = null;
        }
    }

    private bool TryApplyLayout(SensorLayout layout)
    {
        try
        {
            return _registry.ApplyLayout(layout);
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            _logger.LogError(ex, "Creating the HWiNFO sensor keys failed.");
            _registry.SetLayout(layout);
            return false;
        }
    }

    private bool TryApplyAutostart(bool enabled)
    {
        try
        {
            _autostart.Apply(enabled);
            return true;
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            _logger.LogError(ex, "Changing the Windows startup entry failed.");
            return false;
        }
    }

    private void TryWrite(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogError(ex, "Writing HWiNFO sensor values failed.");
        }
    }

    /// <summary>Whether events from a poller should be applied now. Caller holds the lock.</summary>
    private bool IsCurrent(object? sender, out DevicePoller entry)
    {
        entry = null!;
        if (sender is not PollingService poller || !_started || _userPaused || _sensorsRemoved || poller.IsPaused)
        {
            return false;
        }

        if (!_pollers.TryGetValue(poller.DeviceId, out var found) || !ReferenceEquals(found.Poller, poller))
        {
            return false;
        }

        entry = found;
        return true;
    }

    private double ComputeTotal()
    {
        var total = _last?.Power.AcWatts ?? 0;
        foreach (var device in _settings?.AdditionalDevices ?? [])
        {
            if (device.IncludeInTotals && _pollers.TryGetValue(device.Id, out var entry) && entry.LastAc is double ac)
            {
                total += ac;
            }
        }

        return total;
    }

    /// <summary>Writes every power value of the layout. Caller holds the lock.</summary>
    private void WriteAllLocked()
    {
        var values = new Dictionary<SensorKey, double>
        {
            [SensorKey.PcAc] = _last?.Power.AcWatts ?? 0,
            [SensorKey.PcDc] = _last?.Power.DcWatts ?? 0,
            [SensorKey.PcLoss] = _last?.Power.LossWatts ?? 0,
            [SensorKey.PcOther] = _last is null ? 0 : _other.Value,
        };
        foreach (var device in _settings?.AdditionalDevices ?? [])
        {
            var entry = _pollers.GetValueOrDefault(device.Id);
            var online = entry is not null && entry.Poller.State == OnlineState.Online && entry.LastAc is not null;
            values[SensorKey.DeviceAc(device.Id)] = online ? entry!.LastAc!.Value : 0;
        }

        if (_settings?.HasAdditionalDevices == true)
        {
            values[SensorKey.Total] = ComputeTotal();
        }

        TryWrite(() => _registry.WriteValues(values));
    }

    /// <summary>
    /// Writes every power value after a reading, unless a reading-driven write happened less than
    /// <see cref="HwinfoWriteInterval"/> ago. Caller holds the lock.
    /// </summary>
    private void WriteFromReadingLocked()
    {
        var now = _time.GetUtcNow();
        if (_lastReadingWrite is DateTimeOffset last && now - last < HwinfoWriteInterval - HwinfoWriteTolerance)
        {
            return;
        }

        _lastReadingWrite = now;
        WriteAllLocked();
    }

    private void OnPollerReading(object? sender, PowerReadingEventArgs e)
    {
        // Component readings are read outside the lock (shared memory or registry).
        IReadOnlyList<SensorSelection> sensors;
        lock (_gate)
        {
            if (!IsCurrent(sender, out _))
            {
                return;
            }

            sensors = e.IsPrimary ? [.. _settings?.Sensors ?? []] : [];
        }

        ComponentReadings? components = null;
        if (e.IsPrimary)
        {
            try
            {
                components = _components?.Read(sensors) ?? ComponentReadings.NotRunning;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Reading the component sensors failed.");
                components = ComponentReadings.NotRunning;
            }
        }

        DiagnosticTraceRow? traceRow = null;
        lock (_gate)
        {
            if (!IsCurrent(sender, out var entry))
            {
                return;
            }

            entry.LastAc = e.Power.AcWatts;
            if (e.IsPrimary)
            {
                _last = e;
                var other = _other.Update(e.Power.DcWatts, components!);
                _otherState = new OtherComponentsState(other, components!.Source, components.HwinfoRunning, components.MissingGadgetSensors, _other.HasValue);
                traceRow = new DiagnosticTraceRow(e, components, _other.LastTick, _other.Value);
            }

            WriteFromReadingLocked();
        }

        // The diagnostic trace (a no-op while idle; never throws).
        if (traceRow is not null)
        {
            _trace.Append(traceRow);
        }

        // Never let a subscriber break the polling loop.
        try
        {
            _energy.OnReading(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Energy logging failed for a reading.");
        }

        try
        {
            ReadingReceived?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A reading subscriber failed.");
        }
    }

    private void OnPollerFailed(object? sender, PollFailedEventArgs e)
    {
        lock (_gate)
        {
            if (!IsCurrent(sender, out var entry) || e.State != OnlineState.Offline)
            {
                return;
            }

            // Offline: keep this device's values zeroed on every failed poll; the others are unaffected.
            entry.LastAc = null;
            if (AppSettings.IsPrimary(e.DeviceId))
            {
                _last = null;
                _other.Reset();
                _otherState = _otherState with { Watts = 0, HasValue = false };
            }

            WriteAllLocked();

            // The first reading after the zeros writes at once.
            _lastReadingWrite = null;
        }
    }

    private void OnPollerOnlineStateChanged(object? sender, OnlineStateChangedEventArgs e)
    {
        if (e.Current != OnlineState.Online)
        {
            lock (_gate)
            {
                if (_pollers.TryGetValue(e.DeviceId, out var entry) && ReferenceEquals(entry.Poller, sender))
                {
                    entry.LastAc = null;
                    if (AppSettings.IsPrimary(e.DeviceId))
                    {
                        _last = null;
                    }
                }
            }
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A state subscriber failed.");
        }
    }

    /// <summary>One device's poller and client.</summary>
    private sealed class DevicePoller(string id, PollingService poller, IShellyClient client, string host, string? password)
    {
        public string Id { get; } = id;

        public PollingService Poller { get; } = poller;

        public IShellyClient Client { get; set; } = client;

        public string Host { get; set; } = host;

        public string? Password { get; set; } = password;

        public double? LastAc { get; set; }
    }

    /// <summary>Names of settings groups, for change detection and logging.</summary>
    private static class Keys
    {
        public const string Host = "Device.Host";
        public const string Password = "Device.Password";
        public const string DeviceName = "Device.Name";
        public const string Devices = "AdditionalDevices";
        public const string SensorNames = "SensorNames";
        public const string SensorLayout = "SensorLayout";
        public const string ChartMode = "ChartMode";
        public const string Colours = "Colours";
        public const string Psu = "Psu";
        public const string VoltageMode = "VoltageMode";
        public const string Smoothing = "SmoothingSamples";
        public const string DisplayInterval = "DisplayInterval";
        public const string Sensors = "Sensors";
        public const string CustomFormula = "CustomFormula";
        public const string StartWithWindows = "StartWithWindows";
        public const string HideTrayIcon = "HideTrayIcon";
        public const string EnergyLogging = "EnergyLogging";
        public const string EnergyPrice = "EnergyPrice";
        public const string DatabaseUrl = "DatabaseUpdateUrl";
    }
}
