using System.Text.RegularExpressions;

namespace ShellyHwinfoBridge.Core.Sensors;

/// <summary>
/// Name heuristics for the sensor picker.
/// </summary>
/// <remarks>
/// <para>Pre-check candidates (case-insensitive):</para>
/// <list type="bullet">
/// <item><c>CPU Package Power</c> → CPU</item>
/// <item><c>GPU Power</c>, <c>GPU Board Power</c>, <c>Total Board Power</c> → GPU</item>
/// <item><c>DIMM* Power</c> (e.g. <c>DIMM1 Power</c>, <c>DIMM [#2] Power</c>) → RAM</item>
/// </list>
/// <para>
/// "Aggregate" sensors are totals that already include other readings of the same device: any name
/// containing <c>Package Power</c>, plus the GPU totals above. See <see cref="SensorSelectionAnalyzer"/>.
/// </para>
/// </remarks>
public static partial class SensorHeuristics
{
    private static readonly string[] GpuTotals = ["GPU Power", "GPU Board Power", "Total Board Power"];

    /// <summary>Returns the category if the name is a pre-check candidate, otherwise <c>null</c>.</summary>
    /// <param name="name">The sensor name.</param>
    public static SensorCategory? GetCandidateCategory(string name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Equals("CPU Package Power", StringComparison.OrdinalIgnoreCase))
        {
            return SensorCategory.Cpu;
        }

        if (GpuTotals.Any(g => n.Equals(g, StringComparison.OrdinalIgnoreCase)))
        {
            return SensorCategory.Gpu;
        }

        return DimmPowerRegex().IsMatch(n) ? SensorCategory.Ram : null;
    }

    /// <summary>Returns whether the sensor is a total that includes other sensors of the same device.</summary>
    /// <param name="name">The sensor name.</param>
    public static bool IsAggregate(string name)
    {
        var n = (name ?? string.Empty).Trim();
        return n.Contains("Package Power", StringComparison.OrdinalIgnoreCase) ||
               GpuTotals.Any(g => n.Equals(g, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Guesses a category for any sensor the user checks (used as the initial category).</summary>
    /// <param name="name">The sensor name.</param>
    public static SensorCategory GuessCategory(string name)
    {
        if (GetCandidateCategory(name) is SensorCategory c)
        {
            return c;
        }

        var n = name ?? string.Empty;
        if (n.Contains("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return SensorCategory.Cpu;
        }

        if (n.Contains("GPU", StringComparison.OrdinalIgnoreCase))
        {
            return SensorCategory.Gpu;
        }

        return n.Contains("DIMM", StringComparison.OrdinalIgnoreCase) || n.Contains("DRAM", StringComparison.OrdinalIgnoreCase)
            ? SensorCategory.Ram
            : SensorCategory.OtherMeasured;
    }

    /// <summary>
    /// Suggests the sensors to pre-check: all candidates, but at most one CPU/GPU total per device
    /// (so sub-components and second totals of the same device are never pre-checked).
    /// </summary>
    /// <param name="available">All W sensors read from HWiNFO.</param>
    public static IReadOnlyList<SensorInfo> SuggestSelection(IEnumerable<SensorInfo> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var result = new List<SensorInfo>();
        var devicesWithTotal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sensor in available)
        {
            var category = GetCandidateCategory(sensor.Name);
            if (category is null)
            {
                continue;
            }

            if (category != SensorCategory.Ram && !devicesWithTotal.Add(sensor.Device ?? string.Empty))
            {
                continue;
            }

            result.Add(sensor);
        }

        return result;
    }

    [GeneratedRegex(@"^DIMM.*Power$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimmPowerRegex();
}
