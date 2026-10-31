using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>
/// Display texts for selected component sensors: HWiNFO can report several sensors with the same name (e.g. "Total
/// Power" under each "DDR5 DIMM [#n]: …" device), so such names are shown with their device.
/// </summary>
public static class SensorDisplay
{
    /// <summary>
    /// Returns the short form of an HWiNFO device name: the part before ": " ("DDR5 DIMM [#1]" for "DDR5 DIMM [#1]:
    /// Patriot Memory …"), or the whole name; empty for <c>null</c>.
    /// </summary>
    /// <param name="device">The device name.</param>
    public static string ShortDevice(string? device)
    {
        var d = (device ?? string.Empty).Trim();
        var colon = d.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 ? d[..colon].Trim() : d;
    }

    /// <summary>
    /// Returns one label per sensor: its name, or "name (short device)" when another sensor of the list has the same
    /// name and the device is known.
    /// </summary>
    /// <param name="sensors">The selected sensors.</param>
    public static IReadOnlyList<string> Labels(IReadOnlyList<SensorSelection> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        var counts = sensors
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return sensors.Select(s =>
        {
            var name = s.Name.Trim();
            var device = ShortDevice(s.Device);
            return counts[name] > 1 && device.Length > 0 ? $"{name} ({device})" : name;
        }).ToList();
    }
}
