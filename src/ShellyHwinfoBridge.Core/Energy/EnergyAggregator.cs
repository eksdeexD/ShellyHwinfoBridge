namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>The energy chart's period.</summary>
public enum EnergyPeriod
{
    /// <summary>The last 24 hours, one bar per hour.</summary>
    Hours,

    /// <summary>The last 30 local days, one bar per day.</summary>
    Days,

    /// <summary>The last 12 local months, one bar per month.</summary>
    Months,
}

/// <summary>One bar of the energy chart.</summary>
/// <param name="StartUtc">Start of the bucket in UTC (the key; distinct for both repeated autumn hours).</param>
/// <param name="StartLocal">Start of the bucket in local time, for labels.</param>
/// <param name="AcKwh">Wall energy in kWh.</param>
/// <param name="DcEstKwh">Estimated DC energy in kWh (only covers time the app was running).</param>
/// <param name="HasData">Whether any stored row fell into the bucket.</param>
public sealed record EnergyBucket(DateTimeOffset StartUtc, DateTime StartLocal, double AcKwh, double DcEstKwh, bool HasData);

/// <summary>Energy totals for today and this month (local calendar).</summary>
/// <param name="TodayAcKwh">Wall energy today in kWh.</param>
/// <param name="MonthAcKwh">Wall energy this month in kWh.</param>
/// <param name="TodayDcEstKwh">Estimated DC energy today in kWh.</param>
/// <param name="MonthDcEstKwh">Estimated DC energy this month in kWh.</param>
public sealed record EnergyTotals(double TodayAcKwh, double MonthAcKwh, double TodayDcEstKwh, double MonthDcEstKwh)
{
    /// <summary>All zero.</summary>
    public static EnergyTotals Zero { get; } = new(0, 0, 0, 0);

    /// <summary>Returns the sum of these totals and <paramref name="other"/>.</summary>
    /// <param name="other">The totals to add.</param>
    public EnergyTotals Add(EnergyTotals other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(TodayAcKwh + other.TodayAcKwh, MonthAcKwh + other.MonthAcKwh, TodayDcEstKwh + other.TodayDcEstKwh, MonthDcEstKwh + other.MonthDcEstKwh);
    }
}

/// <summary>
/// Groups hourly rows into chart buckets and totals. Hour buckets are keyed by UTC hour and only labelled in local
/// time (so the repeated autumn hour gives two bars); day and month buckets follow the local calendar.
/// </summary>
public static class EnergyAggregator
{
    /// <summary>Number of hour buckets.</summary>
    public const int HourBuckets = 24;

    /// <summary>Number of day buckets.</summary>
    public const int DayBuckets = 30;

    /// <summary>Number of month buckets.</summary>
    public const int MonthBuckets = 12;

    /// <summary>Returns the UTC range [from, to) of rows a period needs; it ends after the hour/day/month in progress.</summary>
    /// <param name="period">The period.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="timeZone">The local time zone.</param>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) RangeFor(EnergyPeriod period, DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var starts = BucketStarts(period, nowUtc, timeZone);
        var last = starts[^1];
        var end = period switch
        {
            EnergyPeriod.Hours => last.Utc.AddHours(1),
            EnergyPeriod.Days => LocalToUtc(last.Local.AddDays(1), timeZone),
            _ => LocalToUtc(last.Local.AddMonths(1), timeZone),
        };
        return (starts[0].Utc, end);
    }

    /// <summary>Buckets the rows for a period: 24 hours, 30 days or 12 months, oldest first, ending with the current one.</summary>
    /// <param name="rows">Hourly rows (any order; rows outside the range are ignored).</param>
    /// <param name="period">The period.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="timeZone">The local time zone.</param>
    public static IReadOnlyList<EnergyBucket> Aggregate(IEnumerable<EnergyHourRecord> rows, EnergyPeriod period, DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(timeZone);
        var starts = BucketStarts(period, nowUtc, timeZone);
        var ac = new double[starts.Count];
        var dc = new double[starts.Count];
        var has = new bool[starts.Count];
        var index = new Dictionary<DateTime, int>();
        for (var i = 0; i < starts.Count; i++)
        {
            index[period == EnergyPeriod.Hours ? starts[i].Utc.UtcDateTime : starts[i].Local] = i;
        }

        foreach (var row in rows)
        {
            var hour = EnergyHourRecord.HourStart(row.HourStartUtc);
            var key = period switch
            {
                EnergyPeriod.Hours => hour.UtcDateTime,
                EnergyPeriod.Days => ToLocal(hour, timeZone).Date,
                _ => FirstOfMonth(ToLocal(hour, timeZone)),
            };
            if (index.TryGetValue(key, out var i))
            {
                ac[i] += row.AcWh / 1000.0;
                dc[i] += row.DcEstWh / 1000.0;
                has[i] = true;
            }
        }

        var buckets = new EnergyBucket[starts.Count];
        for (var i = 0; i < starts.Count; i++)
        {
            buckets[i] = new EnergyBucket(starts[i].Utc, starts[i].Local, ac[i], dc[i], has[i]);
        }

        return buckets;
    }

    /// <summary>
    /// Sums several devices' buckets (all built for the same period and time): energy adds up, a bucket has data when
    /// any device has. With no series the result is empty.
    /// </summary>
    /// <param name="series">The devices' buckets.</param>
    public static IReadOnlyList<EnergyBucket> Combine(IEnumerable<IReadOnlyList<EnergyBucket>> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        EnergyBucket[]? result = null;
        foreach (var buckets in series)
        {
            if (result is null)
            {
                result = [.. buckets];
                continue;
            }

            for (var i = 0; i < Math.Min(result.Length, buckets.Count); i++)
            {
                var a = result[i];
                var b = buckets[i];
                result[i] = a with { AcKwh = a.AcKwh + b.AcKwh, DcEstKwh = a.DcEstKwh + b.DcEstKwh, HasData = a.HasData || b.HasData };
            }
        }

        return result ?? [];
    }

    /// <summary>Sums today's and this month's energy (local calendar).</summary>
    /// <param name="rows">Hourly rows (any order).</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="timeZone">The local time zone.</param>
    public static EnergyTotals Totals(IEnumerable<EnergyHourRecord> rows, DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(timeZone);
        var (dayStart, dayEnd) = TodayRange(nowUtc, timeZone);
        var (monthStart, monthEnd) = MonthRange(nowUtc, timeZone);
        double todayAc = 0, todayDc = 0, monthAc = 0, monthDc = 0;
        foreach (var row in rows)
        {
            var hour = EnergyHourRecord.HourStart(row.HourStartUtc);
            if (hour >= monthStart && hour < monthEnd)
            {
                monthAc += row.AcWh;
                monthDc += row.DcEstWh;
            }

            if (hour >= dayStart && hour < dayEnd)
            {
                todayAc += row.AcWh;
                todayDc += row.DcEstWh;
            }
        }

        return new EnergyTotals(todayAc / 1000.0, monthAc / 1000.0, todayDc / 1000.0, monthDc / 1000.0);
    }

    /// <summary>Returns the UTC range of the local day containing <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="timeZone">The local time zone.</param>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) TodayRange(DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var today = ToLocal(nowUtc, timeZone).Date;
        return (LocalToUtc(today, timeZone), LocalToUtc(today.AddDays(1), timeZone));
    }

    /// <summary>Returns the UTC range of the local month containing <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="timeZone">The local time zone.</param>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) MonthRange(DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var month = FirstOfMonth(ToLocal(nowUtc, timeZone));
        return (LocalToUtc(month, timeZone), LocalToUtc(month.AddMonths(1), timeZone));
    }

    /// <summary>
    /// Converts a local wall-clock time to UTC: an invalid time (skipped by a spring-forward change) moves to the first
    /// valid instant after it, an ambiguous time (autumn) takes the earlier instant.
    /// </summary>
    /// <param name="local">The local time (its Kind is ignored).</param>
    /// <param name="timeZone">The time zone.</param>
    public static DateTimeOffset LocalToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var i = 0; i < 4 * 24 && timeZone.IsInvalidTime(unspecified); i++)
        {
            unspecified = unspecified.AddMinutes(15);
        }

        var offset = timeZone.IsAmbiguousTime(unspecified)
            ? timeZone.GetAmbiguousTimeOffsets(unspecified).Max()
            : timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static DateTime ToLocal(DateTimeOffset utc, TimeZoneInfo timeZone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, timeZone), DateTimeKind.Unspecified);

    private static DateTime FirstOfMonth(DateTime local) => new(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static List<(DateTimeOffset Utc, DateTime Local)> BucketStarts(EnergyPeriod period, DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        var starts = new List<(DateTimeOffset Utc, DateTime Local)>();
        switch (period)
        {
            case EnergyPeriod.Hours:
                var hour = EnergyHourRecord.HourStart(nowUtc);
                for (var i = HourBuckets - 1; i >= 0; i--)
                {
                    var utc = hour.AddHours(-i);
                    starts.Add((utc, ToLocal(utc, timeZone)));
                }

                break;

            case EnergyPeriod.Days:
                var today = ToLocal(nowUtc, timeZone).Date;
                for (var i = DayBuckets - 1; i >= 0; i--)
                {
                    var day = today.AddDays(-i);
                    starts.Add((LocalToUtc(day, timeZone), day));
                }

                break;

            case EnergyPeriod.Months:
                var month = FirstOfMonth(ToLocal(nowUtc, timeZone));
                for (var i = MonthBuckets - 1; i >= 0; i--)
                {
                    var m = month.AddMonths(-i);
                    starts.Add((LocalToUtc(m, timeZone), m));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(period), period, null);
        }

        return starts;
    }
}
