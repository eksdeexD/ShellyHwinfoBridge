using ShellyHwinfoBridge.Core.Hwinfo;

namespace ShellyHwinfoBridge.Core.Sensors;

/// <summary>
/// Builds the HWiNFO formula for the "Other Components" sensor:
/// <c>"DC Power (Est.)" - "&lt;s1&gt;" - "&lt;s2&gt;" …</c>. HWiNFO references sensors by name in double quotes.
/// </summary>
public static class HwinfoFormula
{
    /// <summary>
    /// Returns whether a name can be referenced in a formula: not blank and without a double quote
    /// (HWiNFO formulas have no escape for quotes).
    /// </summary>
    /// <param name="name">The sensor name.</param>
    public static bool IsValidSensorName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('"', StringComparison.Ordinal);

    /// <summary>Quotes a sensor name for use in a formula.</summary>
    /// <param name="name">The sensor name; surrounding whitespace is trimmed.</param>
    /// <exception cref="ArgumentException">The name is blank or contains a double quote.</exception>
    public static string Quote(string name)
    {
        if (!IsValidSensorName(name))
        {
            throw new ArgumentException($"Sensor name '{name}' cannot be used in a HWiNFO formula.", nameof(name));
        }

        return "\"" + name.Trim() + "\"";
    }

    /// <summary>Builds the formula subtracting each sensor from the estimated DC power.</summary>
    /// <param name="sensorNames">The selected component sensor names, in order.</param>
    public static string Build(IEnumerable<string> sensorNames)
    {
        ArgumentNullException.ThrowIfNull(sensorNames);
        return string.Join(" - ", sensorNames.Select(Quote).Prepend(Quote(HwinfoSensorNames.DcPower)));
    }
}
