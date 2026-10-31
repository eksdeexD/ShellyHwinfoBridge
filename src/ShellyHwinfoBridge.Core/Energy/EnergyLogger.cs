using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>A device whose energy is logged.</summary>
/// <param name="Id">The device id (<see cref="AppSettings.PrimaryDeviceId"/> for the PC's plug).</param>
/// <param name="Label">The label ("PC", "Monitor").</param>
/// <param name="DeviceKey">The plug key (see <see cref="EnergyLogger.BuildDeviceKey"/>); a change rebases the counter.</param>
/// <param name="IncludeInTotals">Whether the device counts in the totals, the HWiNFO energy sensors and the chart.</param>
public sealed record EnergyDevice(string Id, string Label, string DeviceKey, bool IncludeInTotals);

/// <summary>One device's chart buckets.</summary>
/// <param name="Id">The device id.</param>
/// <param name="Label">The label.</param>
/// <param name="Buckets">The buckets, oldest first (same starts as the combined buckets).</param>
public sealed record DeviceEnergySeries(string Id, string Label, IReadOnlyList<EnergyBucket> Buckets);

/// <summary>Everything the energy chart needs for one period.</summary>
/// <param name="Combined">The buckets summed over the PC and every included device (DC estimate from the PC only).</param>
/// <param name="Devices">The included devices' own buckets, PC first, in display order.</param>
public sealed record EnergyChartData(IReadOnlyList<EnergyBucket> Combined, IReadOnlyList<DeviceEnergySeries> Devices);

/// <summary>
/// Logs hourly energy per device from the pollers' readings: each device has its own <see cref="EnergyAccumulator"/>
/// and store (the PC's in the energy folder, an additional device's in <c>energy\&lt;deviceId&gt;</c>, same CSV columns,
/// DC always 0). Totals, the chart and the two HWiNFO energy sensors sum the PC and the devices included in totals;
/// excluding a device hides its whole history (the data stays on disk).
/// </summary>
/// <remarks>
/// <para>Subscribe <see cref="OnReading(object?, PowerReadingEventArgs)"/> to the readings of every device; calls are
/// sequential per device. All members are thread-safe. <see cref="Updated"/> is raised on the caller's thread, outside
/// the lock.</para>
/// <para>Counter state is saved per device at most every <see cref="StateSaveInterval"/>, on every hour change and in
/// <see cref="Flush"/>. A decreasing counter (plug reset) discards that delta, per device. Store failures are logged
/// and do not stop polling.</para>
/// </remarks>
public sealed class EnergyLogger
{
    /// <summary>The longest time between counter-state saves.</summary>
    public static readonly TimeSpan StateSaveInterval = TimeSpan.FromSeconds(60);

    private readonly IHwinfoSensorRegistry _registry;
    private readonly Func<string, IEnergyStore> _storeFactory;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<DeviceLog> _devices = [];

    private bool _enabled = true;
    private TimeSpan _pollInterval = TimeSpan.FromMilliseconds(PollingOptions.DefaultIntervalMs);

    /// <summary>Creates a logger with the PC's device only.</summary>
    /// <param name="store">The PC's row and state storage.</param>
    /// <param name="registry">Receives the energy totals each reading.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="timeZone">The local time zone for "today" and "this month".</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="deviceStoreFactory">
    /// Creates the store of an additional device from its id; <c>null</c> uses <c>&lt;PC store directory&gt;\&lt;id&gt;</c>
    /// when <paramref name="store"/> is an <see cref="EnergyCsvStore"/>, else an in-memory store.
    /// </param>
    public EnergyLogger(IEnergyStore store, IHwinfoSensorRegistry registry, TimeProvider timeProvider, TimeZoneInfo timeZone, ILogger? logger = null, Func<string, IEnergyStore>? deviceStoreFactory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        _logger = logger ?? NullLogger.Instance;
        _storeFactory = deviceStoreFactory ?? (store is EnergyCsvStore csv
            ? id => new EnergyCsvStore(Path.Combine(csv.Directory, id))
            : _ => new InMemoryEnergyStore());
        _devices.Add(new DeviceLog(new EnergyDevice(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, string.Empty, true), store, this));
    }

    /// <summary>Raised after a reading was accounted or the stored rows changed.</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// Gets or sets whether readings are logged. Disabling writes the partial hours; enabling starts from a new
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
                foreach (var device in _devices)
                {
                    if (!value)
                    {
                        device.Flush();
                    }
                    else
                    {
                        device.Rebase();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the key of the PC's plug (host, plus the device id when known). A different key than the one the
    /// counter state belongs to rebases the counter without back-filling.
    /// </summary>
    public string DeviceKey
    {
        get
        {
            lock (_gate)
            {
                return _devices[0].Device.DeviceKey;
            }
        }

        set
        {
            lock (_gate)
            {
                _devices[0].Device = _devices[0].Device with { DeviceKey = value ?? string.Empty };
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

    /// <summary>Gets the logged devices, PC first.</summary>
    public IReadOnlyList<EnergyDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Select(d => d.Device).ToList();
            }
        }
    }

    /// <summary>Builds the device key from a host and an optional device id.</summary>
    /// <param name="host">The configured host.</param>
    /// <param name="deviceId">The device id from <c>Shelly.GetDeviceInfo</c>, if known.</param>
    public static string BuildDeviceKey(string? host, string? deviceId) =>
        (host ?? string.Empty).Trim().ToLowerInvariant() + "|" + (deviceId ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Returns the energy devices described by settings: the PC, then every additional device in list order.</summary>
    /// <param name="settings">The settings.</param>
    public static IReadOnlyList<EnergyDevice> DevicesFrom(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var devices = new List<EnergyDevice> { new(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, BuildDeviceKey(settings.Device.Host, null), true) };
        devices.AddRange(settings.AdditionalDevices.Select(d => new EnergyDevice(d.Id, d.Label, BuildDeviceKey(d.Host, null), d.IncludeInTotals)));
        return devices;
    }

    /// <summary>
    /// Sets the logged devices (the first must be the PC). New devices get their store; removed devices write their
    /// partial hour and are dropped (their files stay); labels, keys and include flags are updated.
    /// </summary>
    /// <param name="devices">The devices.</param>
    public void SetDevices(IReadOnlyList<EnergyDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count == 0 || !AppSettings.IsPrimary(devices[0].Id))
        {
            throw new ArgumentException("The first device must be the PC.", nameof(devices));
        }

        var changed = false;
        lock (_gate)
        {
            var next = new List<DeviceLog>();
            foreach (var device in devices)
            {
                var existing = _devices.FirstOrDefault(d => string.Equals(d.Device.Id, device.Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    existing = new DeviceLog(device, _storeFactory(device.Id), this);
                    changed = true;
                }
                else
                {
                    changed |= existing.Device != device;
                    existing.Device = device;
                }

                next.Add(existing);
            }

            foreach (var removed in _devices.Where(d => !next.Contains(d)))
            {
                removed.Flush();
                changed = true;
            }

            _devices.Clear();
            _devices.AddRange(next);
        }

        if (changed)
        {
            Updated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Accounts one reading (call for each successful poll of any device).</summary>
    /// <param name="sender">The poller (unused).</param>
    /// <param name="e">The reading.</param>
    public void OnReading(object? sender, PowerReadingEventArgs e) => OnReading(e);

    /// <summary>Accounts one reading (call for each successful poll of any device); readings of unknown devices are ignored.</summary>
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

            var device = Find(e.DeviceId);
            if (device is null)
            {
                return;
            }

            var now = _time.GetUtcNow();
            var dc = device.IsPrimary ? e.Power.DcWatts : 0;
            device.AddSample(now, e.Power.AcWatts, dc, e.Raw.EnergyTotalWh, 3 * _pollInterval);
            totals = TotalsLocked(now);
        }

        TryWrite(totals);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns today's and this month's totals over the PC and the included devices, including the hour in progress.</summary>
    public EnergyTotals GetTotals()
    {
        lock (_gate)
        {
            return TotalsLocked(_time.GetUtcNow());
        }
    }

    /// <summary>Returns one device's totals (whether or not it is included), or zero for an unknown device.</summary>
    /// <param name="deviceId">The device id.</param>
    public EnergyTotals GetDeviceTotals(string deviceId)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return Find(deviceId)?.Totals(now) ?? EnergyTotals.Zero;
        }
    }

    /// <summary>Returns the combined chart buckets for a period (PC plus included devices), including the hour in progress.</summary>
    /// <param name="period">The period.</param>
    public IReadOnlyList<EnergyBucket> GetBuckets(EnergyPeriod period) => GetChartData(period).Combined;

    /// <summary>Returns the combined buckets and each included device's buckets for a period.</summary>
    /// <param name="period">The period.</param>
    public EnergyChartData GetChartData(EnergyPeriod period)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var series = _devices
                .Where(d => d.Device.IncludeInTotals || d.IsPrimary)
                .Select(d => new DeviceEnergySeries(d.Device.Id, d.Device.Label, d.Buckets(period, now)))
                .ToList();
            return new EnergyChartData(EnergyAggregator.Combine(series.Select(s => s.Buckets)), series);
        }
    }

    /// <summary>Writes the partial hours and the counter states (clean exit).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            foreach (var device in _devices)
            {
                device.Flush();
            }
        }
    }

    private DeviceLog? Find(string? id) =>
        _devices.FirstOrDefault(d => string.Equals(d.Device.Id, id, StringComparison.OrdinalIgnoreCase));

    private EnergyTotals TotalsLocked(DateTimeOffset now)
    {
        var totals = EnergyTotals.Zero;
        foreach (var device in _devices.Where(d => d.Device.IncludeInTotals || d.IsPrimary))
        {
            totals = totals.Add(device.Totals(now));
        }

        return totals;
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

    /// <summary>One device's accumulator, store and caches. Called under the logger's lock.</summary>
    private sealed class DeviceLog(EnergyDevice device, IEnergyStore store, EnergyLogger owner)
    {
        private readonly Dictionary<EnergyPeriod, IReadOnlyList<EnergyHourRecord>> _storedRows = [];
        private EnergyAccumulator? _accumulator;
        private List<EnergyHourRecord>? _monthRows;
        private (DateTimeOffset FromUtc, DateTimeOffset ToUtc) _monthRange;
        private DateTimeOffset _cacheHour;
        private DateTimeOffset _lastStateSave = DateTimeOffset.MinValue;

        public EnergyDevice Device { get; set; } = device;

        public bool IsPrimary => AppSettings.IsPrimary(Device.Id);

        public void AddSample(DateTimeOffset now, double acWatts, double dcWatts, double? counterWh, TimeSpan maxGap)
        {
            var accumulator = EnsureLoaded(now);
            var completed = accumulator.AddSample(now, acWatts, dcWatts, counterWh, Device.DeviceKey, maxGap);
            if (accumulator.LastNote != EnergySampleNote.None)
            {
                owner._logger.LogInformation("Energy counter of {Device} rebased: {Reason}.", Device.Id, accumulator.LastNote);
            }

            if (completed.Count > 0)
            {
                Append(completed);
            }

            if (completed.Count > 0 || now - _lastStateSave >= StateSaveInterval)
            {
                SaveState(now);
            }
        }

        public EnergyTotals Totals(DateTimeOffset now)
        {
            var accumulator = EnsureLoaded(now);
            return EnergyAggregator.Totals(_monthRows!.Concat(accumulator.Current), now, owner._timeZone);
        }

        public IReadOnlyList<EnergyBucket> Buckets(EnergyPeriod period, DateTimeOffset now)
        {
            var accumulator = EnsureLoaded(now);
            if (!_storedRows.TryGetValue(period, out var stored))
            {
                var (from, to) = EnergyAggregator.RangeFor(period, now, owner._timeZone);
                stored = ReadStore(from, to);
                _storedRows[period] = stored;
            }

            return EnergyAggregator.Aggregate(stored.Concat(accumulator.Current), period, now, owner._timeZone);
        }

        public void Rebase() => _accumulator?.Rebase();

        public void Flush()
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

            SaveState(owner._time.GetUtcNow());
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

            var month = EnergyAggregator.MonthRange(now, owner._timeZone);
            if (_monthRows is null || month != _monthRange)
            {
                _monthRange = month;
                _monthRows = [.. ReadStore(month.FromUtc, month.ToUtc)];
            }

            return _accumulator;
        }

        private void Append(IReadOnlyList<EnergyHourRecord> rows)
        {
            _monthRows?.AddRange(rows.Where(r => r.HourStartUtc >= _monthRange.FromUtc && r.HourStartUtc < _monthRange.ToUtc));
            _storedRows.Clear();
            try
            {
                store.Append(rows);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                owner._logger.LogWarning(ex, "Writing {Count} energy row(s) of {Device} failed.", rows.Count, Device.Id);
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
                store.SaveState(state);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                owner._logger.LogWarning(ex, "Saving the energy counter state of {Device} failed.", Device.Id);
            }
        }

        private EnergyCounterState? LoadState()
        {
            try
            {
                return store.LoadState();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                owner._logger.LogWarning(ex, "Loading the energy counter state of {Device} failed.", Device.Id);
                return null;
            }
        }

        private IReadOnlyList<EnergyHourRecord> ReadStore(DateTimeOffset from, DateTimeOffset to)
        {
            try
            {
                return store.Read(from, to);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                owner._logger.LogWarning(ex, "Reading energy rows of {Device} failed.", Device.Id);
                return [];
            }
        }
    }
}

/// <summary>An <see cref="IEnergyStore"/> that keeps everything in memory (tests, and stores without a folder).</summary>
public sealed class InMemoryEnergyStore : IEnergyStore
{
    private readonly object _gate = new();
    private readonly List<EnergyHourRecord> _rows = [];
    private EnergyCounterState? _state;

    /// <summary>Gets a copy of every appended row.</summary>
    public IReadOnlyList<EnergyHourRecord> Rows
    {
        get
        {
            lock (_gate)
            {
                return [.. _rows];
            }
        }
    }

    /// <inheritdoc />
    public void Append(IEnumerable<EnergyHourRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            _rows.AddRange(records);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<EnergyHourRecord> Read(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        lock (_gate)
        {
            return _rows
                .Where(r => r.HourStartUtc >= fromUtc && r.HourStartUtc < toUtc)
                .GroupBy(r => r.HourStartUtc)
                .Select(g => g.Aggregate((a, b) => a.Merge(b)))
                .OrderBy(r => r.HourStartUtc)
                .ToList();
        }
    }

    /// <inheritdoc />
    public EnergyCounterState? LoadState()
    {
        lock (_gate)
        {
            return _state;
        }
    }

    /// <inheritdoc />
    public void SaveState(EnergyCounterState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }
}
