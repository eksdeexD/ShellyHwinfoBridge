namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>What happened to the energy delta of the last sample, for logging.</summary>
public enum EnergySampleNote
{
    /// <summary>The sample was accounted normally (or it was the first one).</summary>
    None,

    /// <summary>The device changed (different host or id): the counter was rebased without back-fill.</summary>
    DeviceChanged,

    /// <summary>The counter went down (plug reset): the delta was discarded and the counter rebased.</summary>
    CounterReset,

    /// <summary>The counter rose faster than <see cref="EnergyAccumulator.MaxPlausibleWatts"/>: the delta was discarded.</summary>
    ImplausibleDelta,

    /// <summary>The clock went backwards or did not advance: nothing was accounted and the counter was rebased.</summary>
    ClockJump,

    /// <summary>The gap since the last sample is longer than <see cref="EnergyAccumulator.MaxBackfill"/>: the delta was discarded.</summary>
    GapTooLong,
}

/// <summary>
/// Turns power samples and the plug's cumulative counter into hourly energy rows. Pure: the caller passes the
/// timestamps, so it needs no clock.
/// </summary>
/// <remarks>
/// <para>AC energy is the counter delta between samples. Without a counter (or when this or the previous sample
/// lacks one) AC is integrated from the samples like DC.</para>
/// <para>DC (and AC without a counter) is integrated with the trapezoid rule, only when the time since the previous
/// sample is at most the integration gap passed with the sample (normally 3 × the poll interval); longer gaps are skipped.</para>
/// <para>Every delta is spread over [previous sample, this sample] in proportion to the time in each UTC hour (one
/// code path for a 1 s poll across an hour boundary and for days the app was off). Hours without samples become
/// back-filled rows with 0 samples and no DC.</para>
/// <para>A different device key, a decreasing counter, a clock that did not advance, an implausible rise or a gap
/// longer than <see cref="MaxBackfill"/> discard the delta and rebase.</para>
/// </remarks>
public sealed class EnergyAccumulator
{
    /// <summary>The highest plausible average power between two counter readings, in watts.</summary>
    public const double MaxPlausibleWatts = 10_000;

    /// <summary>The longest gap whose counter delta is back-filled.</summary>
    public static readonly TimeSpan MaxBackfill = TimeSpan.FromDays(400);

    /// <summary>
    /// The shortest time used for the plausibility check, so a counter that only updates every minute or so is not
    /// rejected when it jumps between two 1 s polls.
    /// </summary>
    public static readonly TimeSpan MinPlausibilityWindow = TimeSpan.FromMinutes(2);

    private readonly SortedDictionary<DateTimeOffset, HourBucket> _open = [];

    private string? _deviceKey;
    private double? _lastCounterWh;
    private DateTimeOffset? _lastUtc;
    private (double AcW, double DcW)? _lastPower;

    /// <summary>Creates an accumulator.</summary>
    /// <param name="restored">The counter state saved by the previous run, or <c>null</c>.</param>
    public EnergyAccumulator(EnergyCounterState? restored = null)
    {
        if (restored is not null)
        {
            _deviceKey = restored.DeviceKey;
            _lastCounterWh = restored.LastCounterWh;
            _lastUtc = restored.LastSampleUtc;
        }
    }

    /// <summary>Gets what happened to the last sample's delta.</summary>
    public EnergySampleNote LastNote { get; private set; }

    /// <summary>Gets the counter state to persist, or <c>null</c> before any sample or restore.</summary>
    public EnergyCounterState? CounterState =>
        _deviceKey is null || _lastUtc is null ? null : new EnergyCounterState(_deviceKey, _lastCounterWh, _lastUtc.Value);

    /// <summary>Gets the rows in progress (the current hour, plus any not yet completed), oldest first.</summary>
    public IReadOnlyList<EnergyHourRecord> Current => _open.Values.Select(b => b.ToRecord()).ToList();

    /// <summary>Adds one sample and returns the hours that are now complete (oldest first).</summary>
    /// <param name="utc">When the sample was taken.</param>
    /// <param name="acWatts">Measured AC power.</param>
    /// <param name="dcWatts">Estimated DC power.</param>
    /// <param name="counterWh">The plug's cumulative counter, or <c>null</c> when the device has none.</param>
    /// <param name="deviceKey">Identifies the plug (host and device id).</param>
    /// <param name="maxIntegrationGap">The longest gap integrated from samples (normally 3 × the current poll interval).</param>
    public IReadOnlyList<EnergyHourRecord> AddSample(DateTimeOffset utc, double acWatts, double dcWatts, double? counterWh, string deviceKey, TimeSpan maxIntegrationGap)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        acWatts = Clean(acWatts);
        dcWatts = Clean(dcWatts);
        if (counterWh is double c && (!double.IsFinite(c) || c < 0))
        {
            counterWh = null;
        }

        LastNote = EnergySampleNote.None;
        if (_lastUtc is DateTimeOffset last && _deviceKey is not null)
        {
            var elapsed = utc - last;
            if (!string.Equals(_deviceKey, deviceKey, StringComparison.Ordinal))
            {
                LastNote = EnergySampleNote.DeviceChanged;
            }
            else if (elapsed <= TimeSpan.Zero)
            {
                LastNote = EnergySampleNote.ClockJump;
            }
            else if (elapsed > MaxBackfill)
            {
                LastNote = EnergySampleNote.GapTooLong;
            }
            else
            {
                Account(last, utc, elapsed, acWatts, dcWatts, counterWh, maxIntegrationGap);
            }
        }

        _deviceKey = deviceKey;
        _lastUtc = utc;
        _lastCounterWh = counterWh;
        _lastPower = (acWatts, dcWatts);
        Bucket(EnergyHourRecord.HourStart(utc)).AddSample(acWatts);
        return TakeCompleted(EnergyHourRecord.HourStart(utc));
    }

    /// <summary>Forgets the previous sample and counter, so the next sample only sets a new baseline.</summary>
    public void Rebase()
    {
        _lastUtc = null;
        _lastCounterWh = null;
        _lastPower = null;
    }

    /// <summary>Returns every row in progress (the partial hour) and clears them; used at a clean exit.</summary>
    public IReadOnlyList<EnergyHourRecord> FlushPartial()
    {
        var rows = Current;
        _open.Clear();
        return rows;
    }

    private static double Clean(double watts) => double.IsFinite(watts) && watts > 0 ? watts : 0;

    private void Account(DateTimeOffset last, DateTimeOffset now, TimeSpan elapsed, double acW, double dcW, double? counterWh, TimeSpan maxGap)
    {
        var withinGap = _lastPower is not null && elapsed <= maxGap;
        if (withinGap)
        {
            var dcWh = (_lastPower!.Value.DcW + dcW) / 2 * elapsed.TotalHours;
            Spread(last, now, dcWh, dc: true);
        }

        if (counterWh is double counter && _lastCounterWh is double previous)
        {
            var delta = counter - previous;
            var window = elapsed > MinPlausibilityWindow ? elapsed : MinPlausibilityWindow;
            if (delta < 0)
            {
                LastNote = EnergySampleNote.CounterReset;
            }
            else if (delta > MaxPlausibleWatts * window.TotalHours)
            {
                LastNote = EnergySampleNote.ImplausibleDelta;
            }
            else
            {
                Spread(last, now, delta, dc: false);
            }
        }
        else if (withinGap)
        {
            // No counter on this or the previous sample: integrate AC from the samples.
            var acWh = (_lastPower!.Value.AcW + acW) / 2 * elapsed.TotalHours;
            Spread(last, now, acWh, dc: false);
        }
    }

    private void Spread(DateTimeOffset from, DateTimeOffset to, double wh, bool dc)
    {
        if (wh <= 0)
        {
            return;
        }

        var total = (to - from).Ticks;
        var hour = EnergyHourRecord.HourStart(from);
        while (hour < to)
        {
            var next = hour.AddHours(1);
            var start = from > hour ? from : hour;
            var end = to < next ? to : next;
            var share = wh * (end - start).Ticks / total;
            if (share > 0)
            {
                var bucket = Bucket(hour);
                if (dc)
                {
                    bucket.DcWh += share;
                }
                else
                {
                    bucket.AcWh += share;
                }
            }

            hour = next;
        }
    }

    private HourBucket Bucket(DateTimeOffset hour)
    {
        if (!_open.TryGetValue(hour, out var bucket))
        {
            bucket = new HourBucket(hour);
            _open[hour] = bucket;
        }

        return bucket;
    }

    private List<EnergyHourRecord> TakeCompleted(DateTimeOffset currentHour)
    {
        var done = _open.Keys.Where(h => h < currentHour).ToList();
        var rows = new List<EnergyHourRecord>(done.Count);
        foreach (var hour in done)
        {
            rows.Add(_open[hour].ToRecord());
            _open.Remove(hour);
        }

        return rows;
    }

    private sealed class HourBucket(DateTimeOffset hour)
    {
        private double _sum;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private int _samples;

        public double AcWh { get; set; }

        public double DcWh { get; set; }

        public void AddSample(double acW)
        {
            _samples++;
            _sum += acW;
            _min = Math.Min(_min, acW);
            _max = Math.Max(_max, acW);
        }

        public EnergyHourRecord ToRecord() => _samples == 0
            ? new EnergyHourRecord(hour, AcWh, DcWh, 0, null, null, null)
            : new EnergyHourRecord(hour, AcWh, DcWh, _samples, _min, _max, _sum / _samples);
    }
}
