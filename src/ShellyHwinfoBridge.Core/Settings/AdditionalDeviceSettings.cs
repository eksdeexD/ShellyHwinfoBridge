using ShellyHwinfoBridge.Core.Hwinfo;

namespace ShellyHwinfoBridge.Core.Settings;

/// <summary>
/// An additional Shelly device ("Add a Shelly for your monitor or another device"): measured on the AC side only,
/// published to HWiNFO as <c>&lt;Label&gt; Power (AC)</c> and <c>&lt;Label&gt; Online</c>, logged to
/// <c>energy\&lt;Id&gt;\YYYY-MM.csv</c>.
/// </summary>
public sealed class AdditionalDeviceSettings : ShellyDeviceSettings
{
    /// <summary>Gets or sets the stable device id (a GUID in "D" format), assigned when the device is added.</summary>
    public string Id { get; set; } = NewId();

    /// <summary>Gets or sets the label (default "Monitor"); see <see cref="DeviceLabelRules"/>.</summary>
    public string Label { get; set; } = DeviceLabelRules.DefaultLabel;

    /// <summary>
    /// Gets or sets whether the device counts in Total Power (AC) and in the energy totals and chart (default on).
    /// When off, the device is still polled and published to HWiNFO.
    /// </summary>
    public bool IncludeInTotals { get; set; } = true;

    /// <summary>Creates a new device id.</summary>
    public static string NewId() => Guid.NewGuid().ToString("D");
}

/// <summary>The energy chart's display mode.</summary>
public enum EnergyChartMode
{
    /// <summary>One bar per period: the PC plus every included device.</summary>
    Combined,

    /// <summary>One bar per included device per period (grouped bars).</summary>
    PerDevice,
}

/// <summary>How device colours are chosen.</summary>
public enum ColourMode
{
    /// <summary>The PC uses the base colour; the others get colours derived from it (complementary, triad, tetrad).</summary>
    AutomaticScheme,

    /// <summary>Every device has its own colour.</summary>
    Custom,
}

/// <summary>Validation rules for additional device labels.</summary>
public static class DeviceLabelRules
{
    /// <summary>The default label of a new device.</summary>
    public const string DefaultLabel = "Monitor";

    /// <summary>The longest label.</summary>
    public const int MaxLength = 24;

    /// <summary>
    /// Validates a label against the other devices' labels: 1 to <see cref="MaxLength"/> characters after trimming, no
    /// double quote, not "PC", unique (case-insensitive) and giving sensor names that do not collide with the other
    /// sensors of the group (so "Total" and "Shelly" are rejected).
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="otherLabels">The labels of the other additional devices.</param>
    /// <returns>A user-facing error, or <c>null</c> when the label is valid.</returns>
    public static string? Validate(string? label, IEnumerable<string?> otherLabels)
    {
        ArgumentNullException.ThrowIfNull(otherLabels);
        var text = (label ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "Enter a label.";
        }

        if (text.Length > MaxLength)
        {
            return $"Use at most {MaxLength} characters.";
        }

        if (text.Contains('"', StringComparison.Ordinal))
        {
            return "The label can't contain a double quote.";
        }

        if (string.Equals(text, AppSettings.PrimaryLabel, StringComparison.OrdinalIgnoreCase))
        {
            return "\"PC\" is reserved for the PC's plug.";
        }

        var others = otherLabels.Select(o => (o ?? string.Empty).Trim()).ToList();
        if (others.Any(o => string.Equals(o, text, StringComparison.OrdinalIgnoreCase)))
        {
            return "Another device already has this label.";
        }

        var own = new[] { SensorNaming.DefaultDeviceAcName(text), SensorNaming.DefaultDeviceOnlineName(text) };
        var fixedNames = SensorNaming.FixedDefaultNames;
        if (own.Any(n => fixedNames.Contains(n, StringComparer.OrdinalIgnoreCase)))
        {
            return "This label would give a HWiNFO sensor the same name as another one. Choose another label.";
        }

        return null;
    }

    /// <summary>Returns the first free default label: "Monitor", "Monitor 2", "Monitor 3", ….</summary>
    /// <param name="otherLabels">The labels already in use.</param>
    public static string NextDefaultLabel(IEnumerable<string?> otherLabels)
    {
        ArgumentNullException.ThrowIfNull(otherLabels);
        var used = otherLabels.ToList();
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? DefaultLabel : DefaultLabel + " " + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Validate(candidate, used) is null)
            {
                return candidate;
            }
        }
    }
}
