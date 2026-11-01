using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.App;
using Xunit.Abstractions;

namespace ShellyHwinfoBridge.Core.Tests.App;

public sealed class OtherComponentsCalculatorTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 11, 54, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);

    private static ComponentReadings With(params double?[] values) => new(OtherSource.SharedMemory, values, true, []);

    [Fact]
    public void SubtractsComponentsAndHoldsInvalidResults()
    {
        var calc = new OtherComponentsCalculator(_time);

        Assert.Equal(0, calc.Value);
        Assert.Equal(45, calc.Update(295, With(100, 150)), 9);
        Assert.True(calc.HasValue);
        Assert.Equal(OtherTickOutcome.Accepted, calc.LastTick.Outcome);

        // Negative with fewer than 3 samples (a GPU spike): held, but it enters the window.
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(45, calc.Update(295, With(100, 400)), 9);
        Assert.Equal(OtherTickOutcome.Discarded, calc.LastTick.Outcome);
        Assert.Equal(-205, calc.LastTick.Raw!.Value, 9);
        Assert.Equal(2, calc.WindowCount);

        // A missing component: held, not added to the window.
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(45, calc.Update(295, With(100, null)), 9);
        Assert.Null(calc.LastTick.ComponentSum);
        Assert.Null(calc.LastTick.Raw);

        // Non-finite: held, not added to the window.
        Assert.Equal(45, calc.Update(double.NaN, With(100, 150)), 9);
        Assert.Equal(45, calc.Update(295, With(double.PositiveInfinity, 150)), 9);
        Assert.Equal(2, calc.WindowCount);

        // No source: not published, back to 0, the window is cleared.
        Assert.Equal(0, calc.Update(300, new ComponentReadings(OtherSource.None, [], true, ["GPU Power"])));
        Assert.False(calc.HasValue);
        Assert.Equal(0, calc.WindowCount);
        Assert.Equal(OtherTickOutcome.NoSource, calc.LastTick.Outcome);
    }

    [Fact]
    public void StartsAtZeroUntilAValidReading()
    {
        var calc = new OtherComponentsCalculator(_time);
        Assert.Equal(0, calc.Update(100, new ComponentReadings(OtherSource.Gadget, [200.0], true, [])));
        Assert.False(calc.HasValue);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(100, calc.Update(100, new ComponentReadings(OtherSource.Gadget, [], true, [])));
        Assert.True(calc.HasValue);
    }

    [Fact]
    public void PublishesTheRawValueUntilThreeSamplesThenTheMedian()
    {
        var calc = new OtherComponentsCalculator(_time);

        Assert.Equal(50, Tick(calc, 150, 100), 9);
        Assert.Equal(200, Tick(calc, 300, 100), 9);

        // Three samples: median of 50, 200, 60 = 60 (the spike is gone).
        Assert.Equal(60, Tick(calc, 160, 100), 9);
        Assert.Equal(OtherTickOutcome.Accepted, calc.LastTick.Outcome);
        Assert.Equal(60, calc.LastTick.Raw!.Value, 9);
        Assert.Equal(100, calc.LastTick.ComponentSum!.Value, 9);

        // Four samples: the mean of the two middle values (55, 60 of 50, 55, 60, 200).
        Assert.Equal(57.5, Tick(calc, 155, 100), 9);
    }

    [Fact]
    public void NegativeRawValuesCountInTheMedian()
    {
        var calc = new OtherComponentsCalculator(_time);
        Tick(calc, 150, 100);
        Tick(calc, 160, 100);

        // 50, 60, -100 → median 50: the negative enters the median, the result is published.
        Assert.Equal(50, Tick(calc, 0, 100), 9);
        Assert.Equal(OtherTickOutcome.Accepted, calc.LastTick.Outcome);
        Assert.Equal(-100, calc.LastTick.Raw!.Value, 9);
    }

    [Fact]
    public void NegativeMedianHoldsThePreviousValue()
    {
        var calc = new OtherComponentsCalculator(_time);
        Tick(calc, 150, 100);
        Tick(calc, 160, 100);
        Assert.Equal(50, Tick(calc, 140, 100), 9);

        // 50, 60, 40, -10 → (40 + 50) / 2 = 45; 50, 60, 40, -10, -20 → 40; plus -30 → (−10 + 40) / 2 = 15.
        Assert.Equal(45, Tick(calc, 90, 100), 9);
        Assert.Equal(40, Tick(calc, 80, 100), 9);
        Assert.Equal(15, Tick(calc, 70, 100), 9);

        // 50, 60, 40, -10, -20, -30, -40 → median -10: held.
        Assert.Equal(15, Tick(calc, 60, 100), 9);
        Assert.Equal(OtherTickOutcome.Discarded, calc.LastTick.Outcome);
        Assert.True(calc.HasValue);
    }

    [Fact]
    public void OldSamplesLeaveTheWindowAfterTenSeconds()
    {
        var calc = new OtherComponentsCalculator(_time);

        // Five samples 2 s apart at 50 W: the window holds all of them.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(50, Tick(calc, 150, 100, seconds: 2), 9);
        }

        Assert.Equal(5, calc.WindowCount);

        // A sample exactly 10 s after the first pushes the first out (10 s old = expired).
        Assert.Equal(50, Tick(calc, 250, 100, seconds: 2), 9);
        Assert.Equal(5, calc.WindowCount);

        // After a gap longer than the window only the new sample remains: the raw value is published again.
        Assert.Equal(150, Tick(calc, 250, 100, seconds: OtherComponentsCalculator.WindowSeconds + 1), 9);
        Assert.Equal(1, calc.WindowCount);
    }

    [Fact]
    public void ResetClearsTheWindow()
    {
        var calc = new OtherComponentsCalculator(_time);
        Tick(calc, 150, 100);
        Tick(calc, 150, 100);
        Tick(calc, 150, 100);
        Assert.Equal(3, calc.WindowCount);

        calc.Reset();
        Assert.Equal(0, calc.WindowCount);
        Assert.Equal(0, calc.Value);
        Assert.False(calc.HasValue);

        // The first sample after a reset is published as is.
        Assert.Equal(200, Tick(calc, 300, 100), 9);
    }

    [Fact]
    public void ReplayOfTheDiagnosticTraceRemovesTheSpikes()
    {
        // App/other-trace.csv: t_s, dc_w and component_sum_w of the one-hour diagnostic trace of 2026-11-01
        // (2 s polling, from 12:54:00 local); 237.62 W at 13:34:20 is the spike that started the investigation.
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "App", "other-trace.csv"))
            .Skip(1)
            .Where(l => l.Length > 0)
            .Select(l => l.Split(','))
            .Select(c => (T: Parse(c[0]), Dc: Parse(c[1]), Sum: Parse(c[2])))
            .ToList();
        Assert.Equal(1792, rows.Count);

        var calc = new OtherComponentsCalculator(_time);
        var published = new List<double>();
        foreach (var row in rows)
        {
            _time.SetUtcNow(Start + TimeSpan.FromSeconds(row.T));
            published.Add(calc.Update(row.Dc, With(row.Sum)));
        }

        var raw = rows.Select(r => r.Dc - r.Sum).ToList();
        output.WriteLine(FormattableString.Invariant($"raw min {raw.Min():0.00} max {raw.Max():0.00}; published min {published.Min():0.00} max {published.Max():0.00}"));

        Assert.True(published.Max() <= 125, FormattableString.Invariant($"published max {published.Max():0.00}"));
        Assert.True(published.Min() >= 25, FormattableString.Invariant($"published min {published.Min():0.00}"));

        // The spike of 13:34:20 (t ≈ 2420 s, raw 237.62).
        var spike = rows.FindIndex(r => Math.Abs(r.T - 2420) < 0.5);
        Assert.Equal(237.62, raw[spike], 2);
        output.WriteLine(FormattableString.Invariant($"13:34:20: published {published[spike]:0.00}"));
        Assert.True(published[spike] < 125, FormattableString.Invariant($"published at the spike {published[spike]:0.00}"));

        // A real load change at 13:30:50 (raw 140.16): followed within 3 rows (6 s).
        var change = rows.FindIndex(r => Math.Abs(r.T - 2210) < 0.5);
        Assert.Equal(140.16, raw[change], 2);
        var after = published.Skip(change + 1).Take(3).ToList();
        output.WriteLine("13:30:50: published before " + published[change - 1].ToString("0.00", CultureInfo.InvariantCulture)
            + ", at " + published[change].ToString("0.00", CultureInfo.InvariantCulture)
            + ", after " + string.Join(", ", after.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture))));
        Assert.Contains(after, v => v >= 95);
    }

    private double Tick(OtherComponentsCalculator calc, double dc, double sum, double seconds = 1)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        return calc.Update(dc, With(sum));
    }

    private static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
