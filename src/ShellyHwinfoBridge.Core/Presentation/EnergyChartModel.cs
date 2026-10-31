using System.Globalization;
using System.Text.RegularExpressions;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>One bar of the energy chart.</summary>
/// <param name="Value">Energy in kWh (0 without data).</param>
/// <param name="Label">The X label under the bar (or under the group), or <c>null</c>.</param>
/// <param name="TooltipLines">
/// The hover tooltip, one entry per line. Combined: the bucket ("Fri 18:00", "Fri 30 Oct", "Oct 2026"), the energy
/// ("0.34 kWh"; with more than one device "Total 0.34 kWh" followed by the per-device breakdown "PC 0.29 · Monitor 0.05"),
/// or the bucket and "no data". Per device: the bucket, the
/// device label and its energy ("Fri 18:00", "Monitor", "0.05 kWh").
/// </param>
/// <param name="HasData">Whether any data fell into the bar.</param>
/// <param name="Colour">The bar colour (<c>#RRGGBB</c>) in per-device mode; <c>null</c> for the default (combined) colour.</param>
/// <param name="DeviceId">The device of the bar in per-device mode, else <c>null</c>.</param>
/// <param name="DeviceLabel">The device label in per-device mode, else <c>null</c>.</param>
public sealed record ChartBarModel(
    double Value,
    string? Label,
    IReadOnlyList<string> TooltipLines,
    bool HasData,
    string? Colour = null,
    string? DeviceId = null,
    string? DeviceLabel = null)
{
    /// <summary>Gets the tooltip on one line ("Fri 18:00 · Total 0.34 kWh · PC 0.29 · Monitor 0.05"), e.g. for accessibility.</summary>
    public string Tooltip => string.Join(" · ", TooltipLines);
}

/// <summary>One period of the per-device chart: up to four bars side by side.</summary>
/// <param name="Label">The X label under the group, or <c>null</c>.</param>
/// <param name="Bars">One bar per drawn device, in legend order.</param>
public sealed record ChartGroupModel(string? Label, IReadOnlyList<ChartBarModel> Bars);

/// <summary>A legend entry: swatch colour and device label.</summary>
/// <param name="DeviceId">The device id.</param>
/// <param name="Label">The label ("PC", "Monitor").</param>
/// <param name="Colour">The swatch colour (<c>#RRGGBB</c>).</param>
public sealed record ChartLegendItem(string DeviceId, string Label, string Colour);

/// <summary>
/// The energy chart: bars, the Y maximum and the Y labels (bottom to top: "0", "0.2", "0.4 kWh"). In
/// <see cref="EnergyChartMode.PerDevice"/> mode <see cref="Groups"/> holds the grouped bars and <see cref="Legend"/> the
/// drawn devices; <see cref="Bars"/> always holds the combined bars.
/// </summary>
/// <param name="Bars">The combined bars, oldest first.</param>
/// <param name="YMax">The nice Y maximum in kWh (over the bars actually drawn in the mode).</param>
/// <param name="YLabels">Labels for 0, YMax / 2 and YMax; the top one carries the unit.</param>
/// <param name="Mode">The effective mode (always Combined with a single device).</param>
public sealed record EnergyChartModel(
    IReadOnlyList<ChartBarModel> Bars,
    double YMax,
    IReadOnlyList<string> YLabels,
    EnergyChartMode Mode = EnergyChartMode.Combined)
{
    /// <summary>Every how many hour bars (or per-device groups) a label is shown (on local hours divisible by this).</summary>
    public const int HourLabelEvery = 2;

    /// <summary>Every how many day bars a label is shown (counted back from today).</summary>
    public const int DayLabelEvery = 5;

    /// <summary>Every how many month bars a label is shown (counted back from this month).</summary>
    public const int MonthLabelEvery = 1;

    /// <summary>Gets the per-device groups (empty in Combined mode).</summary>
    public IReadOnlyList<ChartGroupModel> Groups { get; init; } = [];

    /// <summary>Gets the legend (empty in Combined mode).</summary>
    public IReadOnlyList<ChartLegendItem> Legend { get; init; } = [];

    /// <summary>Builds the combined chart from aggregated buckets (one device, no breakdown).</summary>
    /// <param name="buckets">The buckets (from <see cref="EnergyLogger.GetBuckets"/>), oldest first.</param>
    /// <param name="period">The period.</param>
    /// <param name="culture">The culture for labels and numbers.</param>
    public static EnergyChartModel Build(IReadOnlyList<EnergyBucket> buckets, EnergyPeriod period, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        return Build(new EnergyChartData(buckets, []), period, EnergyChartMode.Combined, new Dictionary<string, string>(), culture);
    }

    /// <summary>Builds the chart in the settings' mode with the settings' device colours.</summary>
    /// <param name="data">The combined and per-device buckets (from <see cref="EnergyLogger.GetChartData"/>).</param>
    /// <param name="period">The period.</param>
    /// <param name="settings">The settings (chart mode, colours).</param>
    /// <param name="culture">The culture for labels and numbers.</param>
    public static EnergyChartModel Build(EnergyChartData data, EnergyPeriod period, AppSettings settings, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Build(data, period, settings.ChartMode, DeviceColours.Resolve(settings), culture);
    }

    /// <summary>Builds the chart.</summary>
    /// <param name="data">The combined buckets and the included devices' buckets (excluded devices are not in it).</param>
    /// <param name="period">The period.</param>
    /// <param name="mode">The requested mode; with fewer than two devices the chart is Combined.</param>
    /// <param name="colours">Device id → <c>#RRGGBB</c> (see <see cref="DeviceColours.Resolve"/>).</param>
    /// <param name="culture">The culture for labels and numbers.</param>
    public static EnergyChartModel Build(EnergyChartData data, EnergyPeriod period, EnergyChartMode mode, IReadOnlyDictionary<string, string> colours, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(colours);
        ArgumentNullException.ThrowIfNull(culture);
        var buckets = data.Combined;
        var devices = data.Devices;
        var dayPattern = DayMonthPattern(culture);
        var dayTooltipPattern = ShortPattern(culture.DateTimeFormat.MonthDayPattern);
        var monthTooltipPattern = ShortPattern(culture.DateTimeFormat.YearMonthPattern);
        var breakdown = devices.Count > 1;
        var perDevice = mode == EnergyChartMode.PerDevice && devices.Count > 1;

        var bars = new List<ChartBarModel>(buckets.Count);
        var groups = new List<ChartGroupModel>(perDevice ? buckets.Count : 0);
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
                EnergyPeriod.Days => b.StartLocal.ToString("ddd ", culture) + b.StartLocal.ToString(dayTooltipPattern, culture),
                _ => b.StartLocal.ToString(monthTooltipPattern, culture),
            };

            List<string> tooltip;
            if (!b.HasData)
            {
                tooltip = [when, "no data"];
            }
            else
            {
                tooltip = [when, (breakdown ? "Total " : string.Empty) + UiFormat.Kwh(b.AcKwh, culture)];
                if (breakdown)
                {
                    tooltip.Add(string.Join(" · ", devices.Select(d => d.Label + " " + Value(At(d, i)?.AcKwh ?? 0, culture))));
                }
            }

            bars.Add(new ChartBarModel(b.HasData ? Math.Max(0, b.AcKwh) : 0, label, tooltip, b.HasData));

            if (perDevice)
            {
                var deviceBars = devices.Select(d =>
                {
                    var db = At(d, i);
                    var has = db?.HasData == true;
                    var kwh = has ? Math.Max(0, db!.AcKwh) : 0;
                    IReadOnlyList<string> lines = has ? [when, d.Label, UiFormat.Kwh(kwh, culture)] : [when, d.Label, "no data"];
                    return new ChartBarModel(kwh, label, lines, has, ColourOf(colours, d.Id), d.Id, d.Label);
                }).ToList();
                groups.Add(new ChartGroupModel(label, deviceBars));
            }
        }

        var max = perDevice
            ? groups.SelectMany(g => g.Bars).Select(x => x.Value).DefaultIfEmpty(0).Max()
            : bars.Count == 0 ? 0 : bars.Max(x => x.Value);
        var yMax = ChartScale.Nice(max);
        var legend = perDevice ? devices.Select(d => new ChartLegendItem(d.Id, d.Label, ColourOf(colours, d.Id))).ToList() : [];
        return new EnergyChartModel(bars, yMax, ChartScale.Labels(yMax, culture), perDevice ? EnergyChartMode.PerDevice : EnergyChartMode.Combined) { Groups = groups, Legend = legend };
    }

    private static EnergyBucket? At(DeviceEnergySeries series, int index) => index < series.Buckets.Count ? series.Buckets[index] : null;

    private static string Value(double kwh, CultureInfo culture) => Math.Max(0, kwh).ToString("0.00", culture);

    private static string ColourOf(IReadOnlyDictionary<string, string> colours, string id) =>
        colours.TryGetValue(id, out var c) && DeviceColours.Normalize(c) is { } n ? n : DeviceColours.DefaultBase;

    /// <summary>Abbreviates the month in a culture pattern ("MMMM d" → "MMM d", "MMMM yyyy" → "MMM yyyy").</summary>
    private static string ShortPattern(string pattern) =>
        pattern.Contains("MMMM", StringComparison.Ordinal) ? pattern.Replace("MMMM", "MMM", StringComparison.Ordinal) : pattern;

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
