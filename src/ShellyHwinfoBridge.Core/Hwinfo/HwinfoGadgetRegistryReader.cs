using System.Globalization;
using System.Text.RegularExpressions;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>One value HWiNFO exports for its gadget ("Report value in Gadget").</summary>
/// <param name="Index">The N of <c>Sensor&lt;N&gt;</c>/<c>Label&lt;N&gt;</c>/<c>Value&lt;N&gt;</c>/<c>ValueRaw&lt;N&gt;</c>.</param>
/// <param name="Sensor">The sensor (device) name, e.g. "Shelly Power", or <c>null</c> when missing.</param>
/// <param name="Label">The reading's label, e.g. "Other Components".</param>
/// <param name="Value">The parsed number (unit stripped).</param>
public sealed record GadgetReading(int Index, string? Sensor, string Label, double Value);

/// <summary>
/// Reads values HWiNFO writes to the registry for its gadget: when "Report value in Gadget" is ticked for a reading,
/// HWiNFO keeps <c>Sensor&lt;N&gt;</c>, <c>Label&lt;N&gt;</c>, <c>Value&lt;N&gt;</c> (with unit) and <c>ValueRaw&lt;N&gt;</c>
/// string values under <c>HKCU\Software\HWiNFO64\VSB</c> (on some installations <c>HKLM\Software\HWiNFO64\VSB</c>),
/// N starting at 0 and possibly sparse, rewritten every HWiNFO poll. Indexes change when the user adds or removes
/// exported values, so readings are always matched by label (and sensor), never by a stored index. Unlike shared memory
/// this export is not limited to 12-hour sessions in the free version (to be confirmed on a real installation).
/// </summary>
public sealed partial class HwinfoGadgetRegistryReader
{
    /// <summary>The export key, relative to HKEY_CURRENT_USER.</summary>
    public const string DefaultKeyPath = @"Software\HWiNFO64\VSB";

    /// <summary>Indexes at or above this are ignored.</summary>
    public const int MaxIndex = 512;

    private readonly IRegistryBackend _backend;
    private readonly IRegistryBackend? _fallback;
    private readonly string _keyPath;

    /// <summary>Creates a reader.</summary>
    /// <param name="backend">The registry (HKEY_CURRENT_USER).</param>
    /// <param name="fallback">A registry read when <paramref name="backend"/> has no export (HKEY_LOCAL_MACHINE), or <c>null</c>.</param>
    /// <param name="keyPath">The export key; defaults to <see cref="DefaultKeyPath"/>.</param>
    public HwinfoGadgetRegistryReader(IRegistryBackend backend, IRegistryBackend? fallback = null, string keyPath = DefaultKeyPath)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _fallback = fallback;
        _keyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
    }

    /// <summary>
    /// Reads every exported value with a label and a parsable number, by index: from the primary registry, or from the
    /// fallback when the primary has none. Returns an empty list when neither key exists or can be read.
    /// </summary>
    public IReadOnlyList<GadgetReading> ReadAll()
    {
        var readings = ReadFrom(_backend);
        return readings.Count == 0 && _fallback is not null ? ReadFrom(_fallback) : readings;
    }

    private IReadOnlyList<GadgetReading> ReadFrom(IRegistryBackend registry)
    {
        IReadOnlyDictionary<string, string> values;
        try
        {
            values = registry.GetStringValues(_keyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }

        // Indexes come from the Label<N> names, so sparse exports are read up to the highest index present.
        var indexes = new SortedSet<int>();
        foreach (var name in values.Keys)
        {
            var match = LabelName().Match(name);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < MaxIndex)
            {
                indexes.Add(index);
            }
        }

        var readings = new List<GadgetReading>(indexes.Count);
        foreach (var index in indexes)
        {
            var n = index.ToString(CultureInfo.InvariantCulture);
            var label = Get(values, "Label" + n)?.Trim();
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            if (TryParseValue(Get(values, "ValueRaw" + n), Get(values, "Value" + n), out var value))
            {
                var sensor = Get(values, "Sensor" + n)?.Trim();
                readings.Add(new GadgetReading(index, string.IsNullOrEmpty(sensor) ? null : sensor, label, value));
            }
        }

        return readings;
    }

    /// <summary>
    /// Finds a reading by label (case-insensitive). When <paramref name="sensor"/> is given, a reading of that sensor
    /// is preferred; otherwise (or when none matches it) the first reading with the label is returned.
    /// </summary>
    /// <param name="label">The label, e.g. "Other Components".</param>
    /// <param name="sensor">The preferred sensor name, e.g. "Shelly Power", or <c>null</c>.</param>
    public GadgetReading? Find(string label, string? sensor = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        var matches = ReadAll().Where(r => string.Equals(r.Label, label.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (sensor is not null && matches.FirstOrDefault(r => string.Equals(r.Sensor, sensor.Trim(), StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact;
        }

        return matches.FirstOrDefault();
    }

    /// <summary>Returns the bridge's "Other Components" reading (preferring the "Shelly Power" sensor), or <c>null</c>.</summary>
    public double? TryReadOtherComponents() => Find(HwinfoSensorNames.OtherComponents, HwinfoSensorNames.DeviceName)?.Value;

    /// <summary>
    /// Parses an exported number: <paramref name="raw"/> (ValueRaw) first, then <paramref name="display"/> (Value) with
    /// its unit stripped. A dot is the decimal separator (InvariantCulture); a lone decimal comma is accepted too.
    /// </summary>
    /// <param name="raw">The ValueRaw string, or <c>null</c>.</param>
    /// <param name="display">The Value string with unit ("48.1 W"), or <c>null</c>.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><c>true</c> when a finite number was found.</returns>
    public static bool TryParseValue(string? raw, string? display, out double value)
    {
        if (TryParseNumber(raw, out value))
        {
            return true;
        }

        if (display is not null && LeadingNumber().Match(display) is { Success: true } match && TryParseNumber(match.Value, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            // "48,1" from a locale with a decimal comma.
            var commas = s.Count(c => c == ',');
            if (commas != 1 || s.Contains('.', StringComparison.Ordinal)
                || !double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                value = 0;
                return false;
            }
        }

        return double.IsFinite(value);
    }

    [GeneratedRegex(@"^Label(\d{1,6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelName();

    [GeneratedRegex(@"^\s*[-+]?(\d+([.,]\d+)?|[.,]\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumber();
}
