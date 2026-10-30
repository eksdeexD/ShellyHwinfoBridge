using ShellyHwinfoBridge.Core.Energy;
using static ShellyHwinfoBridge.Core.Tests.Energy.EnergyTestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Energy;

public class EnergyAccumulatorTests
{
    private const string Plug = "10.0.0.2|shellyplugmg3-aa";
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(3);

    [Fact]
    public void CounterDeltaAndDcIntegrationWithinAnHour()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 0, 10);

        Assert.Empty(acc.AddSample(t0, 360, 340, 1000, Plug, Gap));
        Assert.Empty(acc.AddSample(t0.AddSeconds(1), 360, 340, 1000.1, Plug, Gap));
        Assert.Empty(acc.AddSample(t0.AddSeconds(2), 720, 680, 1000.3, Plug, Gap));

        var row = Assert.Single(acc.Current);
        Assert.Equal(Utc(2026, 10, 30, 18), row.HourStartUtc);
        Assert.Equal(0.3, row.AcWh, 9);
        Assert.Equal((340 + ((340 + 680) / 2.0)) / 3600, row.DcEstWh, 9);
        Assert.Equal(3, row.Samples);
        Assert.Equal(360, row.AcWMin);
        Assert.Equal(720, row.AcWMax);
        Assert.Equal(480, row.AcWAvg!.Value, 9);
        Assert.Equal(new EnergyCounterState(Plug, 1000.3, t0.AddSeconds(2)), acc.CounterState);
    }

    [Fact]
    public void HourRolloverSplitsTheDeltaAtTheBoundaryAndCompletesTheHour()
    {
        var acc = new EnergyAccumulator();
        var before = Utc(2026, 10, 30, 18, 59, 59).AddMilliseconds(500);
        acc.AddSample(before, 3600, 3600, 5000, Plug, Gap);

        var completed = acc.AddSample(before.AddSeconds(1), 3600, 3600, 5001, Plug, Gap);

        var done = Assert.Single(completed);
        Assert.Equal(Utc(2026, 10, 30, 18), done.HourStartUtc);
        Assert.Equal(0.5, done.AcWh, 9);
        Assert.Equal(0.5, done.DcEstWh, 9);
        Assert.Equal(1, done.Samples);
        var current = Assert.Single(acc.Current);
        Assert.Equal(Utc(2026, 10, 30, 19), current.HourStartUtc);
        Assert.Equal(0.5, current.AcWh, 9);
        Assert.Equal(0.5, current.DcEstWh, 9);
    }

    [Fact]
    public void MultiHourGapAfterRestartIsBackfilledProportionally()
    {
        // The app exited at 18:30 with counter 1000 and starts again at 21:30 with 1300 (100 W average).
        var acc = new EnergyAccumulator(new EnergyCounterState(Plug, 1000, Utc(2026, 10, 30, 18, 30)));

        var completed = acc.AddSample(Utc(2026, 10, 30, 21, 30), 150, 140, 1300, Plug, Gap);

        Assert.Equal([Utc(2026, 10, 30, 18), Utc(2026, 10, 30, 19), Utc(2026, 10, 30, 20)], completed.Select(r => r.HourStartUtc));
        Assert.Equal([50.0, 100.0, 100.0], completed.Select(r => Math.Round(r.AcWh, 9)));
        Assert.All(completed, r => Assert.Equal(new EnergyHourRecord(r.HourStartUtc, r.AcWh, 0, 0, null, null, null), r));
        var current = Assert.Single(acc.Current);
        Assert.Equal(50, current.AcWh, 9);
        Assert.Equal(0, current.DcEstWh);
        Assert.Equal(1, current.Samples);
    }

    [Fact]
    public void CounterResetIsDiscardedAndRebased()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 10);
        acc.AddSample(t0, 100, 90, 5000, Plug, Gap);

        acc.AddSample(t0.AddSeconds(1), 100, 90, 0.2, Plug, Gap);
        Assert.Equal(EnergySampleNote.CounterReset, acc.LastNote);
        acc.AddSample(t0.AddSeconds(2), 100, 90, 0.3, Plug, Gap);
        Assert.Equal(EnergySampleNote.None, acc.LastNote);

        var row = Assert.Single(acc.Current);
        Assert.Equal(0.1, row.AcWh, 9);
        Assert.Equal(2 * 90 / 3600.0, row.DcEstWh, 9);
    }

    [Fact]
    public void ImplausibleDeltaIsDiscarded()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 10);
        acc.AddSample(t0, 100, 90, 1000, Plug, Gap);

        // 10 kW for 2 minutes = 333 Wh is the most accepted for a short interval.
        acc.AddSample(t0.AddSeconds(1), 100, 90, 1400, Plug, Gap);
        Assert.Equal(EnergySampleNote.ImplausibleDelta, acc.LastNote);
        acc.AddSample(t0.AddSeconds(2), 100, 90, 1410, Plug, Gap);
        Assert.Equal(EnergySampleNote.None, acc.LastNote);

        Assert.Equal(10, Assert.Single(acc.Current).AcWh, 9);
    }

    [Fact]
    public void ImplausibleDeltaOverALongGapIsDiscarded()
    {
        var acc = new EnergyAccumulator(new EnergyCounterState(Plug, 1000, Utc(2026, 10, 30, 10)));

        var completed = acc.AddSample(Utc(2026, 10, 30, 12), 100, 90, 1000 + 25_000, Plug, Gap);

        Assert.Equal(EnergySampleNote.ImplausibleDelta, acc.LastNote);
        Assert.Empty(completed);
        Assert.Equal(0, Assert.Single(acc.Current).AcWh);
    }

    [Fact]
    public void DifferentDeviceRebasesWithoutBackfill()
    {
        var acc = new EnergyAccumulator(new EnergyCounterState("10.0.0.2|old", 1000, Utc(2026, 10, 27, 12)));

        var completed = acc.AddSample(Utc(2026, 10, 30, 12), 100, 90, 21_000, Plug, Gap);

        Assert.Equal(EnergySampleNote.DeviceChanged, acc.LastNote);
        Assert.Empty(completed);
        Assert.Equal(0, Assert.Single(acc.Current).AcWh);
        Assert.Equal(new EnergyCounterState(Plug, 21_000, Utc(2026, 10, 30, 12)), acc.CounterState);

        acc.AddSample(Utc(2026, 10, 30, 12, 0, 1), 100, 90, 21_000.5, Plug, Gap);
        Assert.Equal(0.5, Assert.Single(acc.Current).AcWh, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3600)]
    public void ClockJumpBackwardsOrZeroElapsedRebases(int seconds)
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 30);
        acc.AddSample(t0, 100, 90, 1000, Plug, Gap);

        acc.AddSample(t0.AddSeconds(seconds), 100, 90, 1001, Plug, Gap);

        Assert.Equal(EnergySampleNote.ClockJump, acc.LastNote);
        Assert.All(acc.Current, r => Assert.Equal(0, r.AcWh));
        Assert.All(acc.Current, r => Assert.Equal(0, r.DcEstWh));

        acc.AddSample(t0.AddSeconds(seconds + 1), 100, 90, 1001.2, Plug, Gap);
        Assert.Equal(0.2, acc.Current.Sum(r => r.AcWh), 9);
    }

    [Fact]
    public void GapLongerThan400DaysIsDiscarded()
    {
        var acc = new EnergyAccumulator(new EnergyCounterState(Plug, 1000, Utc(2025, 1, 1)));

        var completed = acc.AddSample(Utc(2026, 10, 30), 100, 90, 2000, Plug, Gap);

        Assert.Equal(EnergySampleNote.GapTooLong, acc.LastNote);
        Assert.Empty(completed);
    }

    [Fact]
    public void WithoutCounterAcIsIntegratedFromSamples()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 30);
        acc.AddSample(t0, 3600, 3000, null, Plug, Gap);
        acc.AddSample(t0.AddSeconds(1), 3600, 3000, null, Plug, Gap);
        acc.AddSample(t0.AddSeconds(2), 7200, 6000, null, Plug, Gap);

        var row = Assert.Single(acc.Current);
        Assert.Equal(1 + 1.5, row.AcWh, 9);
        Assert.Equal((3000 + 4500) / 3600.0, row.DcEstWh, 9);
        Assert.Null(acc.CounterState!.LastCounterWh);
    }

    [Fact]
    public void DcIsNotIntegratedOverGapsLongerThanTheCurrentIntegrationGap()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 30);
        acc.AddSample(t0, 3600, 3600, 1000, Plug, Gap);

        // 10 s later: too long for a 3 s gap (1 s polling) but fine for a 15 s gap (5 s polling).
        acc.AddSample(t0.AddSeconds(10), 3600, 3600, 1010, Plug, Gap);
        Assert.Equal(0, Assert.Single(acc.Current).DcEstWh);
        Assert.Equal(10, Assert.Single(acc.Current).AcWh, 9);

        acc.AddSample(t0.AddSeconds(20), 3600, 3600, 1020, Plug, TimeSpan.FromSeconds(15));
        Assert.Equal(10, Assert.Single(acc.Current).DcEstWh, 9);
    }

    [Fact]
    public void FlushPartialReturnsTheHourInProgressAndClearsIt()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 30);
        acc.AddSample(t0, 100, 90, 1000, Plug, Gap);
        acc.AddSample(t0.AddSeconds(1), 100, 90, 1000.1, Plug, Gap);

        var partial = Assert.Single(acc.FlushPartial());
        Assert.Equal(2, partial.Samples);
        Assert.Empty(acc.Current);

        // The rest of the hour continues from the same baseline.
        acc.AddSample(t0.AddSeconds(2), 100, 90, 1000.2, Plug, Gap);
        Assert.Equal(0.1, Assert.Single(acc.Current).AcWh, 9);
    }

    [Fact]
    public void RebaseMakesTheNextSampleABaseline()
    {
        var acc = new EnergyAccumulator();
        var t0 = Utc(2026, 10, 30, 18, 30);
        acc.AddSample(t0, 100, 90, 1000, Plug, Gap);
        acc.Rebase();
        acc.AddSample(t0.AddMinutes(10), 100, 90, 1020, Plug, Gap);

        Assert.Equal(0, Assert.Single(acc.Current).AcWh);
    }
}
