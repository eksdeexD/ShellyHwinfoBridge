using ShellyHwinfoBridge.Core.Energy;
using static ShellyHwinfoBridge.Core.Tests.Energy.EnergyTestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Energy;

public class EnergyAggregatorTests
{
    private static readonly TimeZoneInfo Cet = CentralEurope;

    private static EnergyHourRecord Row(DateTimeOffset hourUtc, double acWh, double dcWh = 0) =>
        new(hourUtc, acWh, dcWh, 3600, acWh, acWh, acWh);

    [Theory]
    [InlineData(EnergyPeriod.Hours, 24)]
    [InlineData(EnergyPeriod.Days, 30)]
    [InlineData(EnergyPeriod.Months, 12)]
    public void BucketCounts(EnergyPeriod period, int expected)
    {
        var buckets = EnergyAggregator.Aggregate([], period, Utc(2026, 10, 30, 18, 40), Cet);

        Assert.Equal(expected, buckets.Count);
        Assert.Equal(expected, buckets.Select(b => b.StartUtc).Distinct().Count());
        Assert.All(buckets, b => Assert.False(b.HasData));
        Assert.True(buckets.Zip(buckets.Skip(1)).All(p => p.First.StartUtc < p.Second.StartUtc));
    }

    [Fact]
    public void HoursEndWithTheCurrentHourAndLabelInLocalTime()
    {
        var now = Utc(2026, 10, 30, 18, 40);
        var rows = new[] { Row(Utc(2026, 10, 30, 18), 340, 310), Row(Utc(2026, 10, 29, 19), 100), Row(Utc(2026, 10, 30, 19), 999) };

        var buckets = EnergyAggregator.Aggregate(rows, EnergyPeriod.Hours, now, Cet);

        Assert.Equal(Utc(2026, 10, 30, 18), buckets[^1].StartUtc);
        Assert.Equal(new DateTime(2026, 10, 30, 19, 0, 0), buckets[^1].StartLocal);
        Assert.Equal(0.34, buckets[^1].AcKwh, 9);
        Assert.Equal(0.31, buckets[^1].DcEstKwh, 9);
        Assert.True(buckets[^1].HasData);
        Assert.Equal(Utc(2026, 10, 29, 19), buckets[0].StartUtc);
        Assert.Equal(0.1, buckets[0].AcKwh, 9);
        Assert.Equal(2, buckets.Count(b => b.HasData));
    }

    [Fact]
    public void AutumnChangeGivesTwoDistinctBarsLabelledTwoOClock()
    {
        // 2026-10-25: 03:00 CEST -> 02:00 CET, so local 02:00 happens at 00:00Z and at 01:00Z.
        var now = Utc(2026, 10, 25, 12);
        var rows = new[] { Row(Utc(2026, 10, 25, 0), 100), Row(Utc(2026, 10, 25, 1), 200) };

        var buckets = EnergyAggregator.Aggregate(rows, EnergyPeriod.Hours, now, Cet);

        Assert.Equal(24, buckets.Select(b => b.StartUtc).Distinct().Count());
        var twos = buckets.Where(b => b.StartLocal == new DateTime(2026, 10, 25, 2, 0, 0)).ToList();
        Assert.Equal(2, twos.Count);
        Assert.Equal([0.1, 0.2], twos.Select(b => Math.Round(b.AcKwh, 9)));
    }

    [Fact]
    public void SpringChangeSkipsTwoOClockInHourLabels()
    {
        var buckets = EnergyAggregator.Aggregate([], EnergyPeriod.Hours, Utc(2026, 3, 29, 12), Cet);

        Assert.DoesNotContain(buckets, b => b.StartLocal == new DateTime(2026, 3, 29, 2, 0, 0));
        Assert.Contains(buckets, b => b.StartLocal == new DateTime(2026, 3, 29, 3, 0, 0));
        Assert.Equal(24, buckets.Select(b => b.StartLocal).Distinct().Count());
    }

    [Fact]
    public void DaysFollowTheLocalCalendarAcrossTheSpringChange()
    {
        // Local day 2026-03-29 has 23 hours: 2026-03-28T23:00Z .. 2026-03-29T22:00Z.
        var rows = Enumerable.Range(0, 25).Select(h => Row(Utc(2026, 3, 28, 22).AddHours(h), 100)).ToList();

        var buckets = EnergyAggregator.Aggregate(rows, EnergyPeriod.Days, Utc(2026, 3, 30, 10), Cet);

        var day = buckets.Single(b => b.StartLocal == new DateTime(2026, 3, 29));
        Assert.Equal(Utc(2026, 3, 28, 23), day.StartUtc);
        Assert.Equal(2.3, day.AcKwh, 9);
        Assert.Equal(0.1, buckets.Single(b => b.StartLocal == new DateTime(2026, 3, 28)).AcKwh, 9);
        Assert.Equal(0.1, buckets.Single(b => b.StartLocal == new DateTime(2026, 3, 30)).AcKwh, 9);
        Assert.Equal(new DateTime(2026, 3, 30), buckets[^1].StartLocal);
        Assert.Equal(new DateTime(2026, 3, 1), buckets[0].StartLocal);
    }

    [Fact]
    public void AutumnDayHas25Hours()
    {
        var rows = Enumerable.Range(0, 25).Select(h => Row(Utc(2026, 10, 24, 22).AddHours(h), 100)).ToList();

        var totals = EnergyAggregator.Totals(rows, Utc(2026, 10, 25, 22, 30), Cet);

        Assert.Equal(2.5, totals.TodayAcKwh, 9);
    }

    [Fact]
    public void MonthsFollowTheLocalCalendar()
    {
        // 2026-10-31T23:30Z is already November locally.
        var rows = new[] { Row(Utc(2026, 10, 31, 23), 500), Row(Utc(2026, 10, 31, 22), 300), Row(Utc(2024, 11, 15, 12), 50) };

        var buckets = EnergyAggregator.Aggregate(rows, EnergyPeriod.Months, Utc(2026, 11, 2), Cet);

        Assert.Equal(new DateTime(2026, 11, 1), buckets[^1].StartLocal);
        Assert.Equal(Utc(2026, 10, 31, 23), buckets[^1].StartUtc);
        Assert.Equal(0.5, buckets[^1].AcKwh, 9);
        Assert.Equal(0.3, buckets[^2].AcKwh, 9);
        Assert.Equal(new DateTime(2025, 12, 1), buckets[0].StartLocal);
        Assert.Equal(2, buckets.Count(b => b.HasData));
    }

    [Fact]
    public void TotalsForTodayAndMonth()
    {
        var rows = new[]
        {
            Row(Utc(2026, 10, 29, 22), 1000, 900), // 2026-10-29 23:00 local: yesterday
            Row(Utc(2026, 10, 29, 23), 400, 350), // 2026-10-30 00:00 local: today
            Row(Utc(2026, 10, 30, 18), 2010, 1850),
            Row(Utc(2026, 9, 30, 21), 7000), // 2026-09-30 23:00 local: last month
            Row(Utc(2026, 9, 30, 22), 3000), // 2026-10-01 00:00 local: this month
        };

        var totals = EnergyAggregator.Totals(rows, Utc(2026, 10, 30, 18, 40), Cet);

        Assert.Equal(2.41, totals.TodayAcKwh, 9);
        Assert.Equal(2.2, totals.TodayDcEstKwh, 9);
        Assert.Equal(6.41, totals.MonthAcKwh, 9);
        Assert.Equal(3.1, totals.MonthDcEstKwh, 9);
    }

    [Fact]
    public void TotalsAtLocalMidnightStartANewDay()
    {
        var rows = new[] { Row(Utc(2026, 10, 30, 22), 400) }; // 23:00 local on the 30th

        var atMidnight = EnergyAggregator.Totals(rows, Utc(2026, 10, 30, 23), Cet);
        var justBefore = EnergyAggregator.Totals(rows, Utc(2026, 10, 30, 22, 59, 59), Cet);

        Assert.Equal(0, atMidnight.TodayAcKwh);
        Assert.Equal(0.4, atMidnight.MonthAcKwh, 9);
        Assert.Equal(0.4, justBefore.TodayAcKwh, 9);
    }

    [Fact]
    public void RangeCoversAllBuckets()
    {
        var now = Utc(2026, 10, 30, 18, 40);
        Assert.Equal((Utc(2026, 10, 29, 19), Utc(2026, 10, 30, 19)), EnergyAggregator.RangeFor(EnergyPeriod.Hours, now, Cet));
        Assert.Equal((Utc(2026, 9, 30, 22), Utc(2026, 10, 30, 23)), EnergyAggregator.RangeFor(EnergyPeriod.Days, now, Cet));
        Assert.Equal((Utc(2025, 10, 31, 23), Utc(2026, 10, 31, 23)), EnergyAggregator.RangeFor(EnergyPeriod.Months, now, Cet));
    }

    [Fact]
    public void LocalToUtcHandlesInvalidAndAmbiguousTimes()
    {
        Assert.Equal(Utc(2026, 3, 29, 1), EnergyAggregator.LocalToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Cet));
        Assert.Equal(Utc(2026, 10, 25, 0, 30), EnergyAggregator.LocalToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Cet));
    }
}
