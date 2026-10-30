using System.Globalization;
using System.Text.RegularExpressions;
using ShellyHwinfoBridge.Core.Energy;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>One bar of the energy chart.</summary>
/// <param name="Value">Wall energy in kWh (0 without data).</param>
/// <param name="Label">The X label under the bar, or <c>null</c>.</param>
/// <param name="Tooltip">The hover readout, e.g. "Thu 18:00 · 0.34 kWh · PC est. 0.31 kWh".</param>
/// <param name="HasData">Whether any data fell into the bar.</param>
public sealed record ChartBarModel(double Value, string? Label, string Tooltip, bool HasData);

/// <summary>The energy chart: bars, the Y maximum and the Y labels (bottom to top: "0", "0.2", "0.4 kWh").</summary>
/// <param name="Bars">The bars, oldest first.</param>
/// <param name="YMax">The nice Y maximum in kWh.</param>
/// <param name="YLabels">Labels for 0, YMax / 2 and YMax; the top one carries the unit.</param>
public sealed record EnergyChartModel(IReadOnlyList<ChartBarModel> Bars, double YMax, IReadOnlyList<string> YLabels)
{
    /// <summary>Every how many hour bars a label is shown (on local hours divisible by this).</summary>
    public const int HourLabelEvery = 4;

    /// <summary>Every how many day bars a label is shown (counted back from today).</summary>
    public const int DayLabelEvery = 7;

    /// <summary>Every how many month bars a label is shown (counted back from this month).</summary>
    public const int MonthLabelEvery = 3;

    /// <summary>Builds the chart from aggregated buckets.</summary>
    /// <param name="buckets">The buckets (from <see cref="EnergyLogger.GetBuckets"/>), oldest first.</param>
    /// <param name="period">The period.</param>
    /// <param name="culture">The culture for labels and numbers.</param>
    public static EnergyChartModel Build(IReadOnlyList<EnergyBucket> buckets, EnergyPeriod period, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(culture);
        var dayPattern = DayMonthPattern(culture);
        var bars = new List<ChartBarModel>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
        {
            var b = buckets[i];
            var fromEnd = buckets.Count - 1 - i;
            string? label = period switch
            {
                EnergyPeriod.Hours => b.StartLocal.Hour % HourLabelEvery == 0 ? b.StartLocal.ToString("HH", culture) : null,
                EnergyPeriod.Days => fromEnd % DayLabelEvery == 0 ? b.StartLocal.ToString(dayPattern, culture) : null,
                _ => fromEnd % MonthLabelEvery == 0 ? b.StartLocal.ToString("MMM", culture) : null,
            };
            var when = period switch
            {
                EnergyPeriod.Hours => b.StartLocal.ToString("ddd HH:mm", culture),
                EnergyPeriod.Days => b.StartLocal.ToString("ddd ", culture) + b.StartLocal.ToString(dayPattern, culture),
                _ => b.StartLocal.ToString("MMMM yyyy", culture),
            };
            var tooltip = b.HasData
                ? $"{when} · {UiFormat.Kwh(b.AcKwh, culture)} · PC est. {UiFormat.Kwh(b.DcEstKwh, culture)}"
                : $"{when} · no data";
            bars.Add(new ChartBarModel(b.HasData ? Math.Max(0, b.AcKwh) : 0, label, tooltip, b.HasData));
        }

        var max = bars.Count == 0 ? 0 : bars.Max(b => b.Value);
        var yMax = ChartScale.Nice(max);
        return new EnergyChartModel(bars, yMax, ChartScale.Labels(yMax, culture));
    }

    /// <summary>The culture's short date pattern without the year ("M/d" for en-US, "dd-MM" for da-DK).</summary>
    /// <param name="culture">The culture.</param>
    public static string DayMonthPattern(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var withoutYear = Regex.Replace(pattern, "[^dM]*y+[^dM]*", match => match.Index == 0 || match.Index + match.Length == pattern.Length ? string.Empty : match.Value.Replace("y", string.Empty, StringComparison.Ordinal));
        withoutYear = withoutYear.Trim(' ', '.', '/', '-', ',');
        return withoutYear.Contains('d', StringComparison.Ordinal) && withoutYear.Contains('M', StringComparison.Ordinal) ? withoutYear : "d MMM";
    }
}

/// <summary>Picks a "nice" Y-axis maximum and its labels.</summary>
public static class ChartScale
{
    private static readonly double[] Steps = [1, 2, 3, 4, 5, 6, 8, 10];

    /// <summary>The maximum used when there is no data.</summary>
    public const double EmptyMax = 0.1;

    /// <summary>
    /// Returns the smallest m × 10^n ≥ <paramref name="max"/> with m in {1, 2, 3, 4, 5, 6, 8}, so the labels at 0, half
    /// and the maximum are round numbers (0.34 → 0.4, 0.45 → 0.5, 7 → 8, 12 → 20).
    /// </summary>
    /// <param name="max">The largest value.</param>
    public static double Nice(double max)
    {
        if (!double.IsFinite(max) || max <= 0)
        {
            return EmptyMax;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var step in Steps)
        {
            var candidate = step * magnitude;
            if (candidate >= max * (1 - 1e-9))
            {
                return Math.Round(candidate, 10);
            }
        }

        return Math.Round(10 * magnitude, 10);
    }

    /// <summary>Returns the labels for 0, half and <paramref name="yMax"/>: ["0", "0.2", "0.4 kWh"].</summary>
    /// <param name="yMax">The maximum.</param>
    /// <param name="culture">The culture.</param>
    public static IReadOnlyList<string> Labels(double yMax, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return [0.0.ToString("0", culture), Format(yMax / 2, culture), Format(yMax, culture) + " kWh"];
    }

    private static string Format(double value, CultureInfo culture) => Math.Round(value, 6).ToString("0.######", culture);
}
