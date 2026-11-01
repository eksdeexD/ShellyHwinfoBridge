using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.App;

/// <summary>What the app currently knows about HWiNFO.</summary>
/// <param name="IsInstalled">Whether HWiNFO64 appears to be installed.</param>
/// <param name="IsRunning">Whether an HWiNFO64 process is running.</param>
/// <param name="SharedMemoryAvailable">Whether HWiNFO's shared memory can be read.</param>
/// <param name="SensorsVisible">Whether HWiNFO shows the bridge's sensors (any reading on "Shelly Power").</param>
/// <param name="ExecutablePath">The HWiNFO64.exe path, when known.</param>
/// <param name="ComponentSource">
/// Which component source exists: shared memory, else the gadget export (when it exports anything), else none. Whether
/// the export covers every selected sensor is reported per tick in <see cref="ComponentReadings"/>.
/// </param>
public sealed record HwinfoMonitorSnapshot(
    bool IsInstalled,
    bool IsRunning,
    bool SharedMemoryAvailable,
    bool SensorsVisible,
    string? ExecutablePath,
    OtherSource ComponentSource = OtherSource.None)
{
    /// <summary>The state before the first check.</summary>
    public static HwinfoMonitorSnapshot Unknown { get; } = new(false, false, false, false, null);
}

/// <summary>
/// Watches HWiNFO in the background for the main window and the tray tooltip: the process list every
/// <see cref="ProcessInterval"/>, installation details every <see cref="DetectInterval"/> (or when the running state
/// changes) and the shared memory every <see cref="ActiveSharedMemoryInterval"/> while a window or the tray icon is
/// visible (<see cref="Active"/>), else every <see cref="IdleSharedMemoryInterval"/>. Polling never depends on it.
/// As an <see cref="IComponentSensorSource"/> it reads the selected component sensors fresh on each call while HWiNFO
/// runs: from shared memory, else from HWiNFO's gadget registry export when it reports every selected sensor.
/// </summary>
public sealed class HwinfoMonitor : IComponentSensorSource, IDisposable
{
    /// <summary>How often the process list is checked.</summary>
    public static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often the full detection (registry, path) runs.</summary>
    public static readonly TimeSpan DetectInterval = TimeSpan.FromSeconds(60);

    /// <summary>Shared-memory interval while <see cref="Active"/>.</summary>
    public static readonly TimeSpan ActiveSharedMemoryInterval = TimeSpan.FromSeconds(2);

    /// <summary>Shared-memory interval while not <see cref="Active"/>.</summary>
    public static readonly TimeSpan IdleSharedMemoryInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan LoopStep = TimeSpan.FromSeconds(1);

    private readonly HwinfoLocator _locator;
    private readonly HwinfoSharedMemoryReader _reader;
    private readonly HwinfoGadgetRegistryReader? _gadget;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private HwinfoMonitorSnapshot _current = HwinfoMonitorSnapshot.Unknown;
    private HwinfoStatus? _status;
    private bool _running;
    private bool _sharedMemory;
    private bool _sensorsVisible;
    private OtherSource _componentSource;
    private DateTimeOffset _lastProcess = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDetect = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSharedMemory = DateTimeOffset.MinValue;
    private volatile bool _active = true;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Creates a monitor.</summary>
    /// <param name="locator">Finds the HWiNFO process and installation.</param>
    /// <param name="reader">Reads HWiNFO's shared memory.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="gadget">Reads HWiNFO's gadget registry export (the fallback component source), or <c>null</c>.</param>
    public HwinfoMonitor(HwinfoLocator locator, HwinfoSharedMemoryReader reader, TimeProvider? timeProvider = null, ILogger? logger = null, HwinfoGadgetRegistryReader? gadget = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _gadget = gadget;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Raised (on the monitor's thread) when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the latest snapshot.</summary>
    public HwinfoMonitorSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Gets or sets whether someone is looking (main window or tray icon visible): faster shared-memory reads.</summary>
    public bool Active
    {
        get => _active;
        set => _active = value;
    }

    /// <summary>Starts the background loop (the first check runs immediately).</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = RunAsync(_cts.Token);
        }
    }

    /// <summary>Stops the background loop.</summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _loop = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    /// <summary>Runs every check that is due now (the loop calls this each second; tests call it directly).</summary>
    public void Tick() => Check(force: false);

    /// <summary>Runs all checks now, regardless of the intervals.</summary>
    public void Refresh() => Check(force: true);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Tick();
                await Task.Delay(LoopStep, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The HWiNFO monitor stopped unexpectedly.");
        }
    }

    private void Check(bool force)
    {
        HwinfoMonitorSnapshot snapshot;
        bool changed;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (force || now - _lastProcess >= ProcessInterval)
            {
                _lastProcess = now;
                var running = SafeIsRunning();
                if (force || _status is null || running != _running || now - _lastDetect >= DetectInterval)
                {
                    _lastDetect = now;
                    _status = SafeDetect() ?? _status;
                    running = _status?.IsRunning ?? running;
                }

                _running = running;
            }

            var sharedMemoryInterval = _active ? ActiveSharedMemoryInterval : IdleSharedMemoryInterval;
            if (force || now - _lastSharedMemory >= sharedMemoryInterval)
            {
                _lastSharedMemory = now;
                ReadSharedMemory();
            }

            snapshot = new HwinfoMonitorSnapshot(
                (_status?.IsInstalled ?? false) || _running,
                _running,
                _sharedMemory,
                _sensorsVisible,
                _status?.ExecutablePath,
                _componentSource);
            changed = snapshot != _current;
            _current = snapshot;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads fresh on every call (HWiNFO's process state comes from the last check). While HWiNFO does not run nothing is
    /// read: the gadget export stays in the registry after HWiNFO exits and would be stale.
    /// </remarks>
    public ComponentReadings Read(IReadOnlyList<SensorSelection> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        bool running;
        lock (_gate)
        {
            running = _running;
        }

        return running ? ReadComponents(_reader, _gadget, sensors, _logger) : ComponentReadings.NotRunning;
    }

    /// <summary>
    /// Reads the selected sensors from shared memory, else from the gadget export (a source only when it reports every
    /// selected sensor). Assumes HWiNFO runs.
    /// </summary>
    /// <param name="reader">The shared-memory reader.</param>
    /// <param name="gadget">The gadget export reader, or <c>null</c>.</param>
    /// <param name="sensors">The selected sensors.</param>
    /// <param name="logger">Optional logger.</param>
    public static ComponentReadings ReadComponents(HwinfoSharedMemoryReader reader, HwinfoGadgetRegistryReader? gadget, IReadOnlyList<SensorSelection> sensors, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sensors);
        HwinfoSnapshot? snapshot = null;
        try
        {
            snapshot = reader.TryGetSnapshot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger?.LogDebug(ex, "Reading shared memory for the component sensors failed.");
        }

        if (snapshot is not null)
        {
            var values = sensors.Select(s => FindShared(snapshot, s)).ToList();
            return new ComponentReadings(OtherSource.SharedMemory, values, true, [])
            {
                HwinfoPollTime = snapshot.PollTime == DateTimeOffset.MinValue ? null : snapshot.PollTime,
            };
        }

        var exported = gadget?.ReadAll() ?? [];
        var found = sensors.Select(s => FindGadget(exported, s)).ToList();
        var missing = sensors.Where((_, i) => found[i] is null).Select(s => s.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return missing.Count == 0
            ? new ComponentReadings(OtherSource.Gadget, found, true, [])
            : new ComponentReadings(OtherSource.None, found, true, missing);
    }

    /// <summary>
    /// Reads HWiNFO's gadget export now (for the live markers of the gadget how-to), or <c>null</c> while HWiNFO does not
    /// run (the export stays in the registry after HWiNFO exits and would be stale) or no export reader exists.
    /// </summary>
    public IReadOnlyList<GadgetReading>? ReadGadgetExport()
    {
        bool running;
        lock (_gate)
        {
            running = _running;
        }

        if (!running || _gadget is null)
        {
            return null;
        }

        try
        {
            return _gadget.ReadAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogDebug(ex, "Reading the gadget export failed.");
            return null;
        }
    }

    /// <summary>
    /// Returns whether HWiNFO's gadget export reports a selected sensor: matched by label, and, when the sensor's device
    /// is known, only by a value of that device (so one of two "Total Power" DIMM sensors does not count for the other).
    /// </summary>
    /// <param name="exported">The exported values.</param>
    /// <param name="sensor">The selected sensor.</param>
    public static bool IsReportedToGadget(IReadOnlyList<GadgetReading> exported, SensorSelection sensor)
    {
        ArgumentNullException.ThrowIfNull(exported);
        ArgumentNullException.ThrowIfNull(sensor);
        var name = sensor.Name.Trim();
        return exported.Any(r =>
            string.Equals(r.Label, name, StringComparison.OrdinalIgnoreCase) &&
            !IsBridgeDevice(r.Sensor) &&
            (sensor.Device is not { } device || r.Sensor is null || DeviceMatches(r.Sensor, device)));
    }

    private static double? FindShared(HwinfoSnapshot snapshot, SensorSelection sensor)
    {
        var reading = (sensor.Device is { } device ? snapshot.FindReading(sensor.Name, device) : null) ?? snapshot.FindReading(sensor.Name);
        return reading is { } r && !IsBridgeDevice(r.Device) ? r.Value : null;
    }

    private static double? FindGadget(IReadOnlyList<GadgetReading> exported, SensorSelection sensor)
    {
        var name = sensor.Name.Trim();
        var matches = exported.Where(r => string.Equals(r.Label, name, StringComparison.OrdinalIgnoreCase) && !IsBridgeDevice(r.Sensor)).ToList();
        if (sensor.Device is not { } device)
        {
            return matches.FirstOrDefault()?.Value;
        }

        // With a known device, only a value of that device counts (or one the export does not name a device for), so
        // the "Total Power" of one DIMM never stands in for another's.
        var exact = matches.FirstOrDefault(r => r.Sensor is not null && DeviceMatches(r.Sensor, device));
        return (exact ?? matches.FirstOrDefault(r => r.Sensor is null))?.Value;
    }

    /// <summary>
    /// Whether an exported sensor (device) name is the selected device: equal, one ends with ": " + the other, or one is
    /// the other's short form before ": " ("DDR5 DIMM [#1]" for "DDR5 DIMM [#1]: Patriot Memory …").
    /// </summary>
    private static bool DeviceMatches(string exportedSensor, string device)
    {
        var d = device.Trim();
        var s = exportedSensor.Trim();
        return string.Equals(s, d, StringComparison.OrdinalIgnoreCase) ||
               d.EndsWith(": " + s, StringComparison.OrdinalIgnoreCase) ||
               s.EndsWith(": " + d, StringComparison.OrdinalIgnoreCase) ||
               d.StartsWith(s + ": ", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith(d + ": ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBridgeDevice(string? device)
    {
        var d = (device ?? string.Empty).Trim();
        return d.Equals(SensorNaming.DeviceName, StringComparison.OrdinalIgnoreCase) ||
               d.EndsWith(": " + SensorNaming.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    private void ReadSharedMemory()
    {
        _componentSource = OtherSource.None;
        if (!_running)
        {
            // The gadget export stays in the registry after HWiNFO exits: it is only current while HWiNFO runs.
            _sharedMemory = false;
            _sensorsVisible = false;
            return;
        }

        var snapshot = _reader.TryGetSnapshot();
        _sharedMemory = snapshot is not null;
        _sensorsVisible = snapshot?.Readings.Any(r => IsBridgeDevice(r.Device)) == true;
        if (snapshot is not null)
        {
            _componentSource = OtherSource.SharedMemory;
            return;
        }

        IReadOnlyList<GadgetReading> exported = _gadget?.ReadAll() ?? [];
        if (exported.Count > 0)
        {
            _componentSource = OtherSource.Gadget;
            if (exported.Any(r => IsBridgeDevice(r.Sensor)))
            {
                // HWiNFO exports one of the bridge's values, so it shows the bridge's sensors.
                _sensorsVisible = true;
            }
        }
    }

    private bool SafeIsRunning()
    {
        try
        {
            return _locator.IsRunning;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Checking the HWiNFO process failed.");
            return _running;
        }
    }

    private HwinfoStatus? SafeDetect()
    {
        try
        {
            return _locator.Detect();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _logger.LogDebug(ex, "Detecting HWiNFO failed.");
            return null;
        }
    }
}
