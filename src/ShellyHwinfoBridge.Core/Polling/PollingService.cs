using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Polling;

/// <summary>
/// Polls one Shelly device at a fixed interval and reports readings, failures and online-state changes. The caller
/// (<see cref="App.BridgeRuntime"/>) publishes the values to HWiNFO.
/// </summary>
/// <remarks>
/// <para>Requests never overlap: the loop awaits each poll before waiting for the next tick, and
/// <see cref="PollOnceAsync"/> skips if a poll is already running. Each device has its own poller, so one device's
/// slow or failing requests never delay another's.</para>
/// <para>The first poll happens after <see cref="StartDelay"/> (used to stagger several devices), then every
/// interval.</para>
/// <para>After <see cref="PollingOptions.OfflineAfterFailures"/> consecutive failures the device is reported
/// offline; polling continues and the first success brings it back online.</para>
/// <para>The primary device has a <see cref="PowerCalculator"/> (PSU curve); an additional device has none and its
/// readings carry DC and loss 0.</para>
/// <para>Events are raised on a thread-pool thread; UI subscribers must marshal to their own thread. No event is raised
/// for a poll that completes after <see cref="Pause"/> or <see cref="StopAsync"/>.</para>
/// </remarks>
public sealed class PollingService : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private IShellyClient _client;
    private PowerCalculator? _calculator;
    private PollingOptions _options;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _busy;
    private bool _paused;
    private bool _stopping;
    private int _failures;
    private OnlineState _state = OnlineState.Unknown;

    /// <summary>Creates a poller.</summary>
    /// <param name="client">The Shelly client.</param>
    /// <param name="calculator">Converts AC to DC (the primary device), or <c>null</c> for an AC-only device.</param>
    /// <param name="options">Interval and timeout; normalized on use.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock for the interval and timeout; defaults to the system clock.</param>
    /// <param name="deviceId">The device id carried by the events.</param>
    /// <param name="startDelay">The delay before the first poll (stagger); zero polls immediately.</param>
    public PollingService(
        IShellyClient client,
        PowerCalculator? calculator,
        PollingOptions? options = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null,
        string deviceId = AppSettings.PrimaryDeviceId,
        TimeSpan startDelay = default)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _calculator = calculator;
        _options = (options ?? new PollingOptions()).Normalize();
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        DeviceId = deviceId;
        StartDelay = startDelay > TimeSpan.Zero ? startDelay : TimeSpan.Zero;
    }

    /// <summary>Raised after each successful poll.</summary>
    public event EventHandler<PowerReadingEventArgs>? ReadingReceived;

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event EventHandler<OnlineStateChangedEventArgs>? OnlineStateChanged;

    /// <summary>Raised after each failed poll (also while offline).</summary>
    public event EventHandler<PollFailedEventArgs>? PollFailed;

    /// <summary>Gets the device id.</summary>
    public string DeviceId { get; }

    /// <summary>Gets the delay before the first poll.</summary>
    public TimeSpan StartDelay { get; }

    /// <summary>Gets the current online state.</summary>
    public OnlineState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Gets the number of consecutive failed polls.</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _failures);

    /// <summary>Gets whether polling is paused.</summary>
    public bool IsPaused => Volatile.Read(ref _paused);

    /// <summary>Gets whether the polling loop is running.</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>Gets the current (normalized) options.</summary>
    public PollingOptions Options => _options;

    /// <summary>Starts the polling loop; the first poll happens after <see cref="StartDelay"/>. Does nothing if already running.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _stopping = false;
            _loopCts = new CancellationTokenSource();
            _loopTask = RunAsync(_loopCts.Token);
        }

        _logger.LogInformation(
            "Polling {Device} started (interval {Interval} ms, timeout {Timeout} ms, offset {Offset} ms).",
            DeviceId,
            _options.IntervalMs,
            _options.TimeoutMs,
            (int)StartDelay.TotalMilliseconds);
    }

    /// <summary>Stops the loop and waits for an in-flight poll; the state becomes <see cref="OnlineState.Unknown"/>.</summary>
    public async Task StopAsync()
    {
        Task? loop;
        lock (_gate)
        {
            _stopping = true;
            _loopCts?.Cancel();
            loop = _loopTask;
        }

        if (loop is not null)
        {
            await loop.ConfigureAwait(false);
        }

        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _loopCts?.Dispose();
            _loopCts = null;
            _loopTask = null;
        }

        GoIdle("stopped");
    }

    /// <summary>Pauses polling; the state becomes <see cref="OnlineState.Unknown"/>.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
        }

        GoIdle("paused");
    }

    /// <summary>Resumes polling at the next tick.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
        }

        _logger.LogInformation("Polling {Device} resumed.", DeviceId);
    }

    /// <summary>Applies new interval/timeout; takes effect from the next tick.</summary>
    /// <param name="options">The new options; normalized.</param>
    public void UpdateOptions(PollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            _options = options.Normalize();
            if (_timer is not null)
            {
                _timer.Period = _options.Interval;
            }
        }
    }

    /// <summary>Replaces the calculator (new curve, voltage mode or smoothing); <c>null</c> for AC only.</summary>
    /// <param name="calculator">The new calculator.</param>
    public void UpdateCalculator(PowerCalculator? calculator)
    {
        lock (_gate)
        {
            _calculator = calculator;
        }
    }

    /// <summary>Replaces the Shelly client (new host or password). The caller disposes the old client.</summary>
    /// <param name="client">The new client.</param>
    public void UpdateClient(IShellyClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            _client = client;
        }
    }

    /// <summary>
    /// Performs one poll (one tick). Returns <c>false</c> without polling when paused or when a poll is already running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the poll.</param>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (IsPaused || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return false;
        }

        try
        {
            IShellyClient client;
            PollingOptions options;
            lock (_gate)
            {
                client = _client;
                options = _options;
            }

            ShellyReading reading;
            TimeSpan duration;
            try
            {
                using var timeout = new CancellationTokenSource(options.Timeout, _time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                var started = _time.GetTimestamp();
                reading = await client.GetReadingAsync(linked.Token).ConfigureAwait(false);
                duration = _time.GetElapsedTime(started);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnFailure(ex);
                return true;
            }

            OnSuccess(reading, duration);
            return true;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Converts a reading without a PSU curve: AC only, DC and loss 0.</summary>
    /// <param name="acWatts">The measured AC power.</param>
    /// <param name="volts">The measured voltage.</param>
    public static PowerReading AcOnly(double acWatts, double? volts)
    {
        var ac = double.IsFinite(acWatts) && acWatts > 0 ? acWatts : 0;
        return new PowerReading(ac, ac, 0, 0, VoltageSelector.Resolve(InputVoltageMode.Auto, volts));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            if (StartDelay > TimeSpan.Zero)
            {
                await Task.Delay(StartDelay, _time, cancellationToken).ConfigureAwait(false);
            }

            PeriodicTimer timer;
            lock (_gate)
            {
                timer = new PeriodicTimer(_options.Interval, _time);
                _timer = timer;
            }

            do
            {
                await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Polling loop of {Device} failed unexpectedly.", DeviceId);
        }
    }

    private void OnSuccess(ShellyReading reading, TimeSpan duration)
    {
        PowerReading power;
        OnlineState previous;
        lock (_gate)
        {
            if (_paused || _stopping)
            {
                return;
            }

            _failures = 0;
            power = _calculator is { } calculator
                ? calculator.Calculate(reading.ActivePowerWatts, reading.Volts)
                : AcOnly(reading.ActivePowerWatts, reading.Volts);
            previous = _state;
            _state = OnlineState.Online;
        }

        if (previous != OnlineState.Online)
        {
            _logger.LogInformation("Shelly {Device} online.", DeviceId);
            OnlineStateChanged?.Invoke(this, new OnlineStateChangedEventArgs(previous, OnlineState.Online, null, DeviceId));
        }

        ReadingReceived?.Invoke(this, new PowerReadingEventArgs(reading, power, DeviceId, duration));
    }

    private void OnFailure(Exception error)
    {
        OnlineState previous;
        int failures;
        var wentOffline = false;
        lock (_gate)
        {
            if (_paused || _stopping)
            {
                return;
            }

            _failures++;
            failures = _failures;
            previous = _state;
            _logger.LogDebug(error, "Poll of {Device} failed ({Failures} in a row).", DeviceId, _failures);
            if (_failures >= PollingOptions.OfflineAfterFailures && previous != OnlineState.Offline)
            {
                _state = OnlineState.Offline;
                _calculator?.Reset();
                wentOffline = true;
            }
        }

        if (wentOffline)
        {
            _logger.LogWarning("Shelly {Device} offline after {Failures} failed polls: {Error}", DeviceId, PollingOptions.OfflineAfterFailures, error.Message);
            OnlineStateChanged?.Invoke(this, new OnlineStateChangedEventArgs(previous, OnlineState.Offline, error, DeviceId));
        }

        PollFailed?.Invoke(this, new PollFailedEventArgs(DeviceId, failures, State, error));
    }

    private void GoIdle(string what)
    {
        OnlineState previous;
        lock (_gate)
        {
            _failures = 0;
            _calculator?.Reset();
            previous = _state;
            _state = OnlineState.Unknown;
        }

        _logger.LogInformation("Polling {Device} {What}.", DeviceId, what);
        if (previous != OnlineState.Unknown)
        {
            OnlineStateChanged?.Invoke(this, new OnlineStateChangedEventArgs(previous, OnlineState.Unknown, null, DeviceId));
        }
    }
}
