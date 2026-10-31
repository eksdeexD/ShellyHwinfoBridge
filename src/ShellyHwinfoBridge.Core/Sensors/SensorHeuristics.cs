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
/// <item>a power sensor of a device whose name contains <c>DIMM</c> or <c>DRAM</c> (stock HWiNFO: <c>Total Power</c> under <c>DDR5 DIMM [#1]: …</c>) → RAM</item>
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

    /// <summary>
    /// Returns the category if the sensor is a pre-check candidate, otherwise <c>null</c>: the name rules of
    /// <see cref="GetCandidateCategory(string)"/>, plus any power sensor of a memory device (<see cref="IsMemoryDevice"/>),
    /// which is RAM. Stock HWiNFO names a DDR5 module's power "Total Power" under "DDR5 DIMM [#1]: …".
    /// </summary>
    /// <param name="name">The sensor name.</param>
    /// <param name="device">The HWiNFO device name, if known.</param>
    public static SensorCategory? GetCandidateCategory(string name, string? device) =>
        GetCandidateCategory(name) ?? (IsMemoryDevice(device) ? SensorCategory.Ram : null);

    /// <summary>Returns whether an HWiNFO device is a memory module: its name contains "DIMM" or "DRAM".</summary>
    /// <param name="device">The device name.</param>
    public static bool IsMemoryDevice(string? device) =>
        device is not null &&
        (device.Contains("DIMM", StringComparison.OrdinalIgnoreCase) || device.Contains("DRAM", StringComparison.OrdinalIgnoreCase));

    /// <summary>Guesses a category from the name, then from the device (a memory device is RAM).</summary>
    /// <param name="name">The sensor name.</param>
    /// <param name="device">The HWiNFO device name, if known.</param>
    public static SensorCategory GuessCategory(string name, string? device) =>
        GetCandidateCategory(name, device) ?? GuessCategory(name);

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
    /// Suggests the sensors to pre-check: all candidates, but at most one CPU/GPU total per device (so sub-components
    /// and second totals of the same device are never pre-checked), and one power sensor per memory device that has no
    /// "DIMM… Power" sensor (its "Total Power" when it has one).
    /// </summary>
    /// <param name="available">All W sensors read from HWiNFO.</param>
    public static IReadOnlyList<SensorInfo> SuggestSelection(IEnumerable<SensorInfo> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var list = available.ToList();
        var memoryPicks = list
            .Where(s => GetCandidateCategory(s.Name) is null && IsMemoryDevice(s.Device))
            .GroupBy(s => s.Device ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(g => !list.Any(o => string.Equals(o.Device, g.Key, StringComparison.OrdinalIgnoreCase) && GetCandidateCategory(o.Name) == SensorCategory.Ram))
            .Select(g => g.FirstOrDefault(s => s.Name.Trim().Equals("Total Power", StringComparison.OrdinalIgnoreCase))
                ?? g.FirstOrDefault(s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                ?? g.First())
            .ToHashSet();
        var result = new List<SensorInfo>();
        var devicesWithTotal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sensor in list)
        {
            var category = GetCandidateCategory(sensor.Name);
            if (category is null)
            {
                if (memoryPicks.Contains(sensor))
                {
                    result.Add(sensor);
                }

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
