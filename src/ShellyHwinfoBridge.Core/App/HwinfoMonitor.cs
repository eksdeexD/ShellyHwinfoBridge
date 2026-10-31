using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;

namespace ShellyHwinfoBridge.Core.App;

/// <summary>Where the "Other Components" value in <see cref="HwinfoMonitorSnapshot.OtherWatts"/> comes from.</summary>
public enum OtherSource
{
    /// <summary>Not available.</summary>
    None,

    /// <summary>HWiNFO's shared memory.</summary>
    SharedMemory,

    /// <summary>HWiNFO's gadget registry export (<see cref="HwinfoGadgetRegistryReader"/>).</summary>
    Gadget,
}

/// <summary>What the app currently knows about HWiNFO.</summary>
/// <param name="IsInstalled">Whether HWiNFO64 appears to be installed.</param>
/// <param name="IsRunning">Whether an HWiNFO64 process is running.</param>
/// <param name="SharedMemoryAvailable">Whether HWiNFO's shared memory can be read.</param>
/// <param name="SensorsVisible">Whether HWiNFO shows the bridge's sensors (the AC reading on "Shelly Power").</param>
/// <param name="OtherWatts">HWiNFO's own "Other Components" reading on "Shelly Power", or <c>null</c> when not available.</param>
/// <param name="ExecutablePath">The HWiNFO64.exe path, when known.</param>
/// <param name="OtherSource">Where <paramref name="OtherWatts"/> comes from.</param>
public sealed record HwinfoMonitorSnapshot(
    bool IsInstalled,
    bool IsRunning,
    bool SharedMemoryAvailable,
    bool SensorsVisible,
    double? OtherWatts,
    string? ExecutablePath,
    OtherSource OtherSource = OtherSource.None)
{
    /// <summary>The state before the first check.</summary>
    public static HwinfoMonitorSnapshot Unknown { get; } = new(false, false, false, false, null, null);
}

/// <summary>
/// Watches HWiNFO in the background for the main window and the tray tooltip: the process list every
/// <see cref="ProcessInterval"/>, installation details every <see cref="DetectInterval"/> (or when the running state
/// changes) and the shared memory every <see cref="ActiveSharedMemoryInterval"/> while a window or the tray icon is
/// visible (<see cref="Active"/>), else every <see cref="IdleSharedMemoryInterval"/>. Polling never depends on it.
/// "Other Components" comes from shared memory when it shows the bridge's sensors, else from HWiNFO's gadget registry
/// export (read on the same schedule), else it is not available (<see cref="OtherSource"/>).
/// </summary>
public sealed class HwinfoMonitor : IDisposable
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
    private double? _other;
    private OtherSource _otherSource;
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
    /// <param name="gadget">Reads HWiNFO's gadget registry export (the fallback for "Other Components"), or <c>null</c>.</param>
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
                _other,
                _status?.ExecutablePath,
                _otherSource);
            changed = snapshot != _current;
            _current = snapshot;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadSharedMemory()
    {
        _other = null;
        _otherSource = OtherSource.None;
        if (!_running)
        {
            // The gadget export stays in the registry after HWiNFO exits: it is only current while HWiNFO runs.
            _sharedMemory = false;
            _sensorsVisible = false;
            return;
        }

        var snapshot = _reader.TryGetSnapshot();
        _sharedMemory = snapshot is not null;
        _sensorsVisible = snapshot?.FindReading(HwinfoSensorNames.AcPower, HwinfoSensorNames.DeviceName) is not null;
        if (snapshot?.FindReading(HwinfoSensorNames.OtherComponents, HwinfoSensorNames.DeviceName)?.Value is double shared)
        {
            _other = shared;
            _otherSource = OtherSource.SharedMemory;
            return;
        }

        if (_gadget?.TryReadOtherComponents() is double exported)
        {
            // HWiNFO exports the value, so it shows the bridge's sensors.
            _other = exported;
            _otherSource = OtherSource.Gadget;
            _sensorsVisible = true;
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
