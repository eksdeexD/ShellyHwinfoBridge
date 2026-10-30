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
/// The bridge's composition of settings, Shelly client, <see cref="PollingService"/>, HWiNFO registry writer,
/// autostart and <see cref="EnergyLogger"/>. See <see cref="IBridgeRuntime"/>.
/// </summary>
/// <remarks>
/// <para>Settings changes rebuild only what changed. A replaced Shelly client is disposed immediately; an in-flight
/// request on it fails and counts as one poll failure (never enough to go offline).</para>
/// <para>All members are thread-safe; events are raised outside the internal lock.</para>
/// </remarks>
public sealed class BridgeRuntime : IBridgeRuntime
{
    /// <summary>The timeout of <see cref="TestConnectionAsync"/>.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    private readonly SettingsStore _store;
    private readonly PsuDatabaseLoader _databaseLoader;
    private readonly DatabaseUpdater _databaseUpdater;
    private readonly IHwinfoSensorRegistry _registry;
    private readonly AutostartManager _autostart;
    private readonly EnergyLogger _energy;
    private readonly Func<string, string?, IShellyClient> _clientFactory;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private AppSettings? _settings;
    private string? _password;
    private PsuDatabase _database;
    private PollingService? _poller;
    private IShellyClient? _client;
    private PowerReadingEventArgs? _last;
    private bool _started;
    private bool _userPaused;
    private bool _sensorsRemoved;

    /// <summary>Creates the runtime; nothing is polled until <see cref="Start"/>.</summary>
    /// <param name="settings">The loaded settings, or <c>null</c> on first run.</param>
    /// <param name="store">Settings persistence and password protection.</param>
    /// <param name="databaseLoader">Loads the PSU database.</param>
    /// <param name="databaseUpdater">Checks for database updates.</param>
    /// <param name="registry">The HWiNFO sensor writer.</param>
    /// <param name="autostart">The Run-key manager.</param>
    /// <param name="energy">The energy logger (fed with every reading).</param>
    /// <param name="clientFactory">Creates a Shelly client for a host and password; defaults to <see cref="ShellyClient"/>.</param>
    /// <param name="timeProvider">The clock; defaults to the system clock.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
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
        ILoggerFactory? loggerFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _databaseLoader = databaseLoader ?? throw new ArgumentNullException(nameof(databaseLoader));
        _databaseUpdater = databaseUpdater ?? throw new ArgumentNullException(nameof(databaseUpdater));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        _energy = energy ?? throw new ArgumentNullException(nameof(energy));
        _clientFactory = clientFactory ?? ((host, password) => new ShellyClient(host, password));
        _time = timeProvider ?? TimeProvider.System;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<BridgeRuntime>();
        _database = _databaseLoader.Load().Database;
        if (settings is not null)
        {
            settings.Normalize();
            _settings = settings;
            _password = store.GetPassword(settings);
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
                return _poller?.State ?? OnlineState.Unknown;
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

    /// <summary>Gets whether settings describe a device (a host is set).</summary>
    /// <param name="settings">The settings.</param>
    public static bool IsConfigured(AppSettings? settings) => settings is not null && !string.IsNullOrWhiteSpace(settings.Device.Host);

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
            if (IsConfigured(_settings))
            {
                TryWriteFormula(_settings!);
            }

            Reconcile(rebuildClient: false, rebuildCalculator: false);
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        PollingService? poller;
        IShellyClient? client;
        lock (_gate)
        {
            _started = false;
            poller = _poller;
            client = _client;
            _poller = null;
            _client = null;
            _last = null;
        }

        if (poller is not null)
        {
            poller.ReadingReceived -= OnPollerReading;
            poller.OnlineStateChanged -= OnPollerOnlineStateChanged;
            await poller.StopAsync().ConfigureAwait(false);
        }

        (client as IDisposable)?.Dispose();
        _energy.Flush();
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
            _last = null;
            _poller?.Pause();
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
                keysCreated = TryEnsureKeys();
                if (_settings is not null)
                {
                    TryWriteFormula(_settings);
                }

                _sensorsRemoved = false;
            }

            _userPaused = false;
            Reconcile(rebuildClient: false, rebuildCalculator: false);
        }

        _logger.LogInformation("Polling resumed by the user.");
        if (keysCreated)
        {
            SensorKeysCreated?.Invoke(this, EventArgs.Empty);
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public ApplyOutcome Apply(AppSettings settings, string? newPassword = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();
        List<string> changed;
        lock (_gate)
        {
            var old = _settings;
            string? password;
            try
            {
                if (newPassword is not null)
                {
                    _store.SetPassword(settings, newPassword);
                }

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
            var clientChanged = changed.Contains(Keys.Host) || changed.Contains(Keys.Password);
            var calculatorChanged = changed.Contains(Keys.Psu) || changed.Contains(Keys.VoltageMode) || changed.Contains(Keys.Smoothing);
            _settings = settings;
            _password = password;

            if (changed.Contains(Keys.Formula) && IsConfigured(settings) && !TryWriteFormula(settings))
            {
                errors.Add("The HWiNFO formula could not be written.");
            }

            if (changed.Contains(Keys.StartWithWindows) && !TryApplyAutostart(settings.StartWithWindows))
            {
                errors.Add("The Windows startup entry could not be changed.");
            }

            _energy.Enabled = settings.EnergyLoggingEnabled;
            Reconcile(clientChanged, calculatorChanged);
        }

        if (changed.Count > 0)
        {
            _logger.LogInformation("Settings changed: {Keys}.", string.Join(", ", changed));
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RaiseStateChanged();
        return errors.Count == 0 ? new ApplyOutcome(true, null, changed) : new ApplyOutcome(false, string.Join(" ", errors), changed);
    }

    /// <inheritdoc />
    public ApplyOutcome ApplyFromWizard(AppSettings draft, string? password, bool startWithWindows)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (_gate)
        {
            try
            {
                _store.SetPassword(draft, password);
                draft.StartWithWindows = startWithWindows;
                draft.Normalize();
                _store.Save(draft);
                _registry.EnsureKeys();
                var formula = draft.GetEffectiveFormula();
                if (!string.IsNullOrWhiteSpace(formula))
                {
                    _registry.WriteFormula(formula);
                }

                _autostart.Apply(startWithWindows);
            }
            catch (Exception ex) when (IsIoError(ex))
            {
                _logger.LogError(ex, "Applying the setup wizard failed.");
                return ApplyOutcome.Failed("Applying the setup failed: " + ex.Message);
            }

            _settings = draft;
            _password = string.IsNullOrEmpty(password) ? null : password;
            _userPaused = false;
            _sensorsRemoved = false;
            _started = true;
            _energy.Enabled = draft.EnergyLoggingEnabled;
            Reconcile(rebuildClient: true, rebuildCalculator: true);
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
                Reconcile(rebuildClient: false, rebuildCalculator: true);
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
            _last = null;
            _poller?.Pause();
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
            changed.Add(Keys.Formula);
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
        Check(Json(old.Psu) != Json(now.Psu), Keys.Psu);
        Check(old.VoltageMode != now.VoltageMode, Keys.VoltageMode);
        Check(old.SmoothingSamples != now.SmoothingSamples, Keys.Smoothing);
        Check(old.GetPollingOptions() != now.GetPollingOptions(), Keys.Interval);
        Check(old.CustomFormula != now.CustomFormula || Json(old.Sensors) != Json(now.Sensors), Keys.Formula);
        Check(old.StartWithWindows != now.StartWithWindows, Keys.StartWithWindows);
        Check(old.HideTrayIcon != now.HideTrayIcon, Keys.HideTrayIcon);
        Check(old.EnergyLoggingEnabled != now.EnergyLoggingEnabled, Keys.EnergyLogging);
        Check(old.EnergyPricePerKwh != now.EnergyPricePerKwh || old.EnergyCurrency != now.EnergyCurrency, Keys.EnergyPrice);
        Check(old.DatabaseUpdateUrl != now.DatabaseUpdateUrl, Keys.DatabaseUrl);
        return changed;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, SettingsStore.JsonOptions);

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

    /// <summary>Brings the poller in line with the settings and state. Caller holds the lock.</summary>
    private void Reconcile(bool rebuildClient, bool rebuildCalculator)
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
            _last = null;
            _poller?.Pause();
            return;
        }

        _energy.DeviceKey = EnergyLogger.BuildDeviceKey(settings!.Device.Host, null);
        _energy.PollInterval = settings.GetPollingOptions().Interval;
        _energy.Enabled = settings.EnergyLoggingEnabled;

        if (_poller is null)
        {
            _client = _clientFactory(settings.Device.Host.Trim(), _password);
            _poller = new PollingService(
                _client,
                _registry,
                CreateCalculator(settings, curves),
                settings.GetPollingOptions(),
                _loggerFactory.CreateLogger<PollingService>(),
                _time);
            _poller.ReadingReceived += OnPollerReading;
            _poller.OnlineStateChanged += OnPollerOnlineStateChanged;
            if (_userPaused)
            {
                _poller.Pause();
            }

            _poller.Start();
            return;
        }

        if (rebuildClient)
        {
            var old = _client;
            _client = _clientFactory(settings.Device.Host.Trim(), _password);
            _poller.UpdateClient(_client);
            (old as IDisposable)?.Dispose();
            _last = null;
        }

        if (rebuildCalculator)
        {
            _poller.UpdateCalculator(CreateCalculator(settings, curves));
        }

        _poller.UpdateOptions(settings.GetPollingOptions());
        if (_userPaused)
        {
            _poller.Pause();
        }
        else
        {
            _poller.Resume();
        }
    }

    private static PowerCalculator CreateCalculator(AppSettings settings, PsuCurveSet curves) =>
        new(curves, settings.VoltageMode, settings.SmoothingSamples);

    private bool TryWriteFormula(AppSettings settings)
    {
        var formula = settings.GetEffectiveFormula();
        if (string.IsNullOrWhiteSpace(formula))
        {
            return true;
        }

        try
        {
            _registry.WriteFormula(formula);
            return true;
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            _logger.LogError(ex, "Writing the HWiNFO formula failed.");
            return false;
        }
    }

    private bool TryEnsureKeys()
    {
        try
        {
            return _registry.EnsureKeys();
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            _logger.LogError(ex, "Creating the HWiNFO sensor keys failed.");
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

    private void OnPollerReading(object? sender, PowerReadingEventArgs e)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _poller))
            {
                return;
            }

            _last = e;
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

    private void OnPollerOnlineStateChanged(object? sender, OnlineStateChangedEventArgs e)
    {
        if (e.Current != OnlineState.Online)
        {
            lock (_gate)
            {
                _last = null;
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

    /// <summary>Names of settings groups, for change detection and logging.</summary>
    private static class Keys
    {
        public const string Host = "Device.Host";
        public const string Password = "Device.Password";
        public const string DeviceName = "Device.Name";
        public const string Psu = "Psu";
        public const string VoltageMode = "VoltageMode";
        public const string Smoothing = "SmoothingSamples";
        public const string Interval = "PollInterval";
        public const string Formula = "Sensors";
        public const string StartWithWindows = "StartWithWindows";
        public const string HideTrayIcon = "HideTrayIcon";
        public const string EnergyLogging = "EnergyLogging";
        public const string EnergyPrice = "EnergyPrice";
        public const string DatabaseUrl = "DatabaseUpdateUrl";
    }
}
