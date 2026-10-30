using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Polling;

/// <summary>
/// Polls the Shelly at a fixed interval and writes AC, DC, loss and the online flag to HWiNFO.
/// </summary>
/// <remarks>
/// <para>Requests never overlap: the loop awaits each poll before waiting for the next tick, and
/// <see cref="PollOnceAsync"/> skips if a poll is already running.</para>
/// <para>After <see cref="PollingOptions.OfflineAfterFailures"/> consecutive failures the device is reported
/// offline (Online = 0, values 0); polling continues and the first success brings it back online.</para>
/// <para>Pausing and stopping set the sensors offline so HWiNFO never shows stale values.</para>
/// <para>Events are raised on a thread-pool thread; UI subscribers must marshal to their own thread.</para>
/// </remarks>
public sealed class PollingService : IAsyncDisposable
{
    private readonly IHwinfoSensorRegistry _registry;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private IShellyClient _client;
    private PowerCalculator _calculator;
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
    /// <param name="registry">The HWiNFO sensor writer.</param>
    /// <param name="calculator">Converts AC to DC.</param>
    /// <param name="options">Interval and timeout; normalized on use.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock for the interval and timeout; defaults to the system clock.</param>
    public PollingService(
        IShellyClient client,
        IHwinfoSensorRegistry registry,
        PowerCalculator calculator,
        PollingOptions? options = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        _options = (options ?? new PollingOptions()).Normalize();
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after each successful poll.</summary>
    public event EventHandler<PowerReadingEventArgs>? ReadingReceived;

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event EventHandler<OnlineStateChangedEventArgs>? OnlineStateChanged;

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

    /// <summary>Starts the polling loop; the first poll happens immediately. Does nothing if already running.</summary>
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
            _timer = new PeriodicTimer(_options.Interval, _time);
            _loopTask = RunAsync(_timer, _loopCts.Token);
        }

        _logger.LogInformation("Polling started (interval {Interval} ms, timeout {Timeout} ms).", _options.IntervalMs, _options.TimeoutMs);
    }

    /// <summary>Stops the loop, waits for an in-flight poll, then sets the sensors offline.</summary>
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

        GoIdle("Polling stopped.");
    }

    /// <summary>Pauses polling and sets the sensors offline.</summary>
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

        GoIdle("Polling paused.");
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

        _logger.LogInformation("Polling resumed.");
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

    /// <summary>Replaces the calculator (new curve, voltage mode or smoothing).</summary>
    /// <param name="calculator">The new calculator.</param>
    public void UpdateCalculator(PowerCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
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
            try
            {
                using var timeout = new CancellationTokenSource(options.Timeout, _time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                reading = await client.GetReadingAsync(linked.Token).ConfigureAwait(false);
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

            OnSuccess(reading);
            return true;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
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
            _logger.LogError(ex, "Polling loop failed unexpectedly.");
        }
    }

    private void OnSuccess(ShellyReading reading)
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
            power = _calculator.Calculate(reading.ActivePowerWatts, reading.Volts);
            TryWrite(() => _registry.WriteReadings(power.AcWatts, power.DcWatts, power.LossWatts, online: true));
            previous = _state;
            _state = OnlineState.Online;
        }

        if (previous != OnlineState.Online)
        {
            _logger.LogInformation("Shelly online.");
            OnlineStateChanged?.Invoke(this, new OnlineStateChangedEventArgs(previous, OnlineState.Online, null));
        }

        ReadingReceived?.Invoke(this, new PowerReadingEventArgs(reading, power));
    }

    private void OnFailure(Exception error)
    {
        OnlineState previous;
        lock (_gate)
        {
            if (_paused || _stopping)
            {
                return;
            }

            _failures++;
            _logger.LogDebug(error, "Poll failed ({Failures} in a row).", _failures);
            if (_failures < PollingOptions.OfflineAfterFailures)
            {
                return;
            }

            // Keep the sensors zeroed on every failed poll while offline.
            TryWrite(_registry.SetOffline);
            previous = _state;
            if (previous == OnlineState.Offline)
            {
                return;
            }

            _state = OnlineState.Offline;
            _calculator.Reset();
        }

        _logger.LogWarning("Shelly offline after {Failures} failed polls: {Error}", PollingOptions.OfflineAfterFailures, error.Message);
        OnlineStateChanged?.Invoke(this, new OnlineStateChangedEventArgs(previous, OnlineState.Offline, error));
    }

    private void GoIdle(string message)
    {
        OnlineState previous;
        lock (_gate)
        {
            TryWrite(_registry.SetOffline);
            _failures = 0;
            _calculator.Reset();
            previous = _state;
            _state = OnlineState.Unknown;
        }

        _logger.LogInformation("{Message}", message);
        if (previous != OnlineState.Unknown)
        {
            OnlineStateChanged?.Invoke(this, new OnlineStateChangedEventArgs(previous, OnlineState.Unknown, null));
        }
    }

    private void TryWrite(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Writing HWiNFO sensor values failed.");
        }
    }
}
