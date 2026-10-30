using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;

namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>
/// Logs hourly energy from the poller's readings: feeds an <see cref="EnergyAccumulator"/>, appends completed hours
/// to the store, keeps today's and this month's totals, and publishes them to HWiNFO on every reading.
/// </summary>
/// <remarks>
/// <para>Subscribe <see cref="OnReading(object?, PowerReadingEventArgs)"/> to <see cref="PollingService.ReadingReceived"/>; calls are sequential.
/// All members are thread-safe. <see cref="Updated"/> is raised on the caller's thread, outside the lock.</para>
/// <para>The counter state is saved at most every <see cref="StateSaveInterval"/>, on every hour change and in
/// <see cref="Flush"/>. Store failures are logged and do not stop polling.</para>
/// </remarks>
public sealed class EnergyLogger
{
    /// <summary>The longest time between counter-state saves.</summary>
    public static readonly TimeSpan StateSaveInterval = TimeSpan.FromSeconds(60);

    private readonly IEnergyStore _store;
    private readonly IHwinfoSensorRegistry _registry;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<EnergyPeriod, IReadOnlyList<EnergyHourRecord>> _storedRows = [];

    private EnergyAccumulator? _accumulator;
    private List<EnergyHourRecord>? _monthRows;
    private (DateTimeOffset FromUtc, DateTimeOffset ToUtc) _monthRange;
    private DateTimeOffset _cacheHour;
    private DateTimeOffset _lastStateSave = DateTimeOffset.MinValue;
    private bool _enabled = true;
    private string _deviceKey = string.Empty;
    private TimeSpan _pollInterval = TimeSpan.FromMilliseconds(PollingOptions.DefaultIntervalMs);

    /// <summary>Creates a logger.</summary>
    /// <param name="store">Row and state storage.</param>
    /// <param name="registry">Receives the energy totals each reading.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="timeZone">The local time zone for "today" and "this month".</param>
    /// <param name="logger">Optional logger.</param>
    public EnergyLogger(IEnergyStore store, IHwinfoSensorRegistry registry, TimeProvider timeProvider, TimeZoneInfo timeZone, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Raised after a reading was accounted or the stored rows changed.</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// Gets or sets whether readings are logged. Disabling writes the partial hour; enabling starts from a new
    /// baseline, so the time logging was off is not back-filled.
    /// </summary>
    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                if (!value)
                {
                    FlushLocked();
                }
                else
                {
                    _accumulator?.Rebase();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the key of the current plug (host, plus the device id when known). A different key than the one
    /// the counter state belongs to rebases the counter without back-filling.
    /// </summary>
    public string DeviceKey
    {
        get
        {
            lock (_gate)
            {
                return _deviceKey;
            }
        }

        set
        {
            lock (_gate)
            {
                _deviceKey = value ?? string.Empty;
            }
        }
    }

    /// <summary>Gets or sets the poll interval; samples further apart than 3 × this are not integrated.</summary>
    public TimeSpan PollInterval
    {
        get
        {
            lock (_gate)
            {
                return _pollInterval;
            }
        }

        set
        {
            lock (_gate)
            {
                _pollInterval = value > TimeSpan.Zero ? value : TimeSpan.FromMilliseconds(PollingOptions.DefaultIntervalMs);
            }
        }
    }

    /// <summary>Builds the device key from a host and an optional device id.</summary>
    /// <param name="host">The configured host.</param>
    /// <param name="deviceId">The device id from <c>Shelly.GetDeviceInfo</c>, if known.</param>
    public static string BuildDeviceKey(string? host, string? deviceId) =>
        (host ?? string.Empty).Trim().ToLowerInvariant() + "|" + (deviceId ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Accounts one reading (call for each successful poll).</summary>
    /// <param name="sender">The poller (unused).</param>
    /// <param name="e">The reading.</param>
    public void OnReading(object? sender, PowerReadingEventArgs e) => OnReading(e);

    /// <summary>Accounts one reading (call for each successful poll).</summary>
    /// <param name="e">The reading.</param>
    public void OnReading(PowerReadingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        EnergyTotals totals;
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            var now = _time.GetUtcNow();
            var accumulator = EnsureLoaded(now);
            var completed = accumulator.AddSample(now, e.Power.AcWatts, e.Power.DcWatts, e.Raw.EnergyTotalWh, _deviceKey, 3 * _pollInterval);
            if (accumulator.LastNote != EnergySampleNote.None)
            {
                _logger.LogInformation("Energy counter rebased: {Reason}.", accumulator.LastNote);
            }

            if (completed.Count > 0)
            {
                Append(completed);
            }

            if (completed.Count > 0 || now - _lastStateSave >= StateSaveInterval)
            {
                SaveState(now);
            }

            totals = TotalsLocked(now);
        }

        TryWrite(totals);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns today's and this month's totals, including the hour in progress.</summary>
    public EnergyTotals GetTotals()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            EnsureLoaded(now);
            return TotalsLocked(now);
        }
    }

    /// <summary>Returns the chart buckets for a period, including the hour in progress.</summary>
    /// <param name="period">The period.</param>
    public IReadOnlyList<EnergyBucket> GetBuckets(EnergyPeriod period)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var accumulator = EnsureLoaded(now);
            if (!_storedRows.TryGetValue(period, out var stored))
            {
                var (from, to) = EnergyAggregator.RangeFor(period, now, _timeZone);
                stored = ReadStore(from, to);
                _storedRows[period] = stored;
            }

            return EnergyAggregator.Aggregate(stored.Concat(accumulator.Current), period, now, _timeZone);
        }
    }

    /// <summary>Writes the partial hour and the counter state (clean exit).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            FlushLocked();
        }
    }

    private void FlushLocked()
    {
        if (_accumulator is null)
        {
            return;
        }

        var partial = _accumulator.FlushPartial().Where(r => r.Samples > 0 || r.AcWh > 0 || r.DcEstWh > 0).ToList();
        if (partial.Count > 0)
        {
            Append(partial);
        }

        SaveState(_time.GetUtcNow());
    }

    private EnergyAccumulator EnsureLoaded(DateTimeOffset now)
    {
        _accumulator ??= new EnergyAccumulator(LoadState());
        var hour = EnergyHourRecord.HourStart(now);
        if (hour != _cacheHour)
        {
            _cacheHour = hour;
            _storedRows.Clear();
        }

        var month = EnergyAggregator.MonthRange(now, _timeZone);
        if (_monthRows is null || month != _monthRange)
        {
            _monthRange = month;
            _monthRows = [.. ReadStore(month.FromUtc, month.ToUtc)];
        }

        return _accumulator;
    }

    private EnergyTotals TotalsLocked(DateTimeOffset now) =>
        EnergyAggregator.Totals(_monthRows!.Concat(_accumulator!.Current), now, _timeZone);

    private void Append(IReadOnlyList<EnergyHourRecord> rows)
    {
        _monthRows?.AddRange(rows.Where(r => r.HourStartUtc >= _monthRange.FromUtc && r.HourStartUtc < _monthRange.ToUtc));
        _storedRows.Clear();
        try
        {
            _store.Append(rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Writing {Count} energy row(s) failed.", rows.Count);
        }
    }

    private void SaveState(DateTimeOffset now)
    {
        if (_accumulator?.CounterState is not { } state)
        {
            return;
        }

        _lastStateSave = now;
        try
        {
            _store.SaveState(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the energy counter state failed.");
        }
    }

    private EnergyCounterState? LoadState()
    {
        try
        {
            return _store.LoadState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Loading the energy counter state failed.");
            return null;
        }
    }

    private IReadOnlyList<EnergyHourRecord> ReadStore(DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            return _store.Read(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Reading energy rows failed.");
            return [];
        }
    }

    private void TryWrite(EnergyTotals totals)
    {
        try
        {
            _registry.WriteEnergy(totals.TodayAcKwh, totals.MonthAcKwh);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Writing HWiNFO energy values failed.");
        }
    }
}
