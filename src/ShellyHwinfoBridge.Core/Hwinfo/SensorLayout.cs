using System.Globalization;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>Registry location, default names and name rules of the bridge's HWiNFO custom sensors.</summary>
public static class SensorNaming
{
    /// <summary>Base key, relative to HKEY_CURRENT_USER.</summary>
    public const string BaseKeyPath = @"Software\HWiNFO64\Sensors\Custom\Shelly Power";

    /// <summary>The sensor (device) name HWiNFO shows the custom sensors under.</summary>
    public const string DeviceName = "Shelly Power";

    /// <summary>Default name of <see cref="SensorKind.PcAc"/>.</summary>
    public const string PcAcName = "PC Power (AC)";

    /// <summary>Default name of <see cref="SensorKind.PcDc"/>.</summary>
    public const string PcDcName = "PC Power (DC)";

    /// <summary>Default name of <see cref="SensorKind.PcLoss"/>.</summary>
    public const string PcLossName = "PC PSU Loss";

    /// <summary>Default name of <see cref="SensorKind.PcOther"/>.</summary>
    public const string PcOtherName = "PC Other Components";

    /// <summary>Default name of <see cref="SensorKind.Total"/>.</summary>
    public const string TotalName = "Total Power (AC)";

    /// <summary>Default name of <see cref="SensorKind.EnergyToday"/>.</summary>
    public const string EnergyTodayName = "Energy Today";

    /// <summary>Default name of <see cref="SensorKind.EnergyMonth"/>.</summary>
    public const string EnergyMonthName = "Energy This Month";

    /// <summary>The longest custom sensor name.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Gets the default names of the sensors that do not depend on a device label.</summary>
    public static IReadOnlyList<string> FixedDefaultNames { get; } =
        [PcAcName, PcDcName, PcLossName, PcOtherName, TotalName, EnergyTodayName, EnergyMonthName];

    /// <summary>Returns the default AC sensor name of an additional device ("Monitor Power (AC)").</summary>
    /// <param name="label">The device label.</param>
    public static string DefaultDeviceAcName(string label) => (label ?? string.Empty).Trim() + " Power (AC)";

    /// <summary>
    /// Returns whether a name can be used for a sensor (or be referenced by name in a HWiNFO formula): not blank, at most
    /// <see cref="MaxNameLength"/> characters after trimming and without a double quote.
    /// </summary>
    /// <param name="name">The name.</param>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxNameLength && !name.Contains('"', StringComparison.Ordinal);

    /// <summary>Returns the default name of a sensor (per-device kinds use the device's label from the settings).</summary>
    /// <param name="key">The sensor.</param>
    /// <param name="settings">The settings (for device labels).</param>
    public static string DefaultName(SensorKey key, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return key.Kind switch
        {
            SensorKind.PcAc => PcAcName,
            SensorKind.PcDc => PcDcName,
            SensorKind.PcLoss => PcLossName,
            SensorKind.PcOther => PcOtherName,
            SensorKind.Total => TotalName,
            SensorKind.EnergyToday => EnergyTodayName,
            SensorKind.EnergyMonth => EnergyMonthName,
            _ => DefaultDeviceAcName(settings.GetDeviceLabel(key.DeviceId) ?? key.DeviceId ?? string.Empty),
        };
    }

    /// <summary>Returns the name HWiNFO shows: the custom name from <see cref="AppSettings.SensorNames"/>, else the default.</summary>
    /// <param name="key">The sensor.</param>
    /// <param name="settings">The settings.</param>
    public static string ResolveName(SensorKey key, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.SensorNames is { } names && names.TryGetValue(key.Id, out var custom) && IsValidName(custom)
            ? custom.Trim()
            : DefaultName(key, settings);
    }
}

/// <summary>One published sensor: its key, registry subkey (<c>Power3</c>, <c>Other1</c>), name and unit.</summary>
/// <param name="Key">The sensor.</param>
/// <param name="Subkey">The subkey under <see cref="SensorNaming.BaseKeyPath"/>.</param>
/// <param name="Name">The name HWiNFO shows.</param>
/// <param name="Unit">The unit.</param>
public sealed record SensorSlot(SensorKey Key, string Subkey, string Name, string Unit)
{
    /// <summary>Gets the value written when the key is created (and while there is no reading).</summary>
    public string DefaultValue => Key.ValueKind == SensorValueKind.Kwh ? SensorValueFormatter.FormatKwh(0) : SensorValueFormatter.FormatWatts(0);
}

/// <summary>
/// The set of published sensors and their subkeys. Order (SPEC "HWiNFO sensors with additional devices"):
/// Power0 PC AC, Power1 PC DC, Power2 PC loss, Power3 PC Other, Power4… one AC sensor per additional device in list
/// order, then Total Power (AC) only when at least one additional device exists; Other0 Energy Today, Other1 Energy
/// This Month (no other Other slots: the former "Shelly Online" / "&lt;Label&gt; Online" sensors were removed on
/// 2026-10-31, and a previous version's Other2… subkeys are deleted as stale). Power subkeys are reassigned when the
/// device list changes.
/// </summary>
public sealed class SensorLayout : IEquatable<SensorLayout>
{
    private readonly Dictionary<SensorKey, SensorSlot> _byKey;

    private SensorLayout(IReadOnlyList<SensorSlot> slots)
    {
        Slots = slots;
        _byKey = slots.ToDictionary(s => s.Key);
    }

    /// <summary>Gets the layout of a default installation (no additional devices, default names).</summary>
    public static SensorLayout Default { get; } = Build(new AppSettings());

    /// <summary>Gets the slots: the Power subkeys in order, then the Other subkeys in order.</summary>
    public IReadOnlyList<SensorSlot> Slots { get; }

    /// <summary>Gets the slot of a sensor, or <c>null</c> when the layout has none.</summary>
    /// <param name="key">The sensor.</param>
    public SensorSlot? this[SensorKey key] => _byKey.TryGetValue(key, out var slot) ? slot : null;

    /// <summary>Returns whether the layout publishes a sensor.</summary>
    /// <param name="key">The sensor.</param>
    public bool Contains(SensorKey key) => _byKey.ContainsKey(key);

    /// <summary>Builds the layout for the settings' devices and custom names.</summary>
    /// <param name="settings">The settings.</param>
    public static SensorLayout Build(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var power = new List<SensorKey> { SensorKey.PcAc, SensorKey.PcDc, SensorKey.PcLoss, SensorKey.PcOther };
        var other = new List<SensorKey> { SensorKey.EnergyToday, SensorKey.EnergyMonth };
        var devices = settings.AdditionalDevices ?? [];
        foreach (var device in devices)
        {
            power.Add(SensorKey.DeviceAc(device.Id));
        }

        if (devices.Count > 0)
        {
            power.Add(SensorKey.Total);
        }

        var slots = new List<SensorSlot>(power.Count + other.Count);
        slots.AddRange(power.Select((k, i) => Slot(k, "Power", i, settings)));
        slots.AddRange(other.Select((k, i) => Slot(k, "Other", i, settings)));
        return new SensorLayout(slots);
    }

    /// <summary>Returns whether a subkey name belongs to the bridge's families (<c>PowerN</c> or <c>OtherN</c>).</summary>
    /// <param name="subkey">The subkey name.</param>
    public static bool IsManagedSubkey(string subkey)
    {
        if (string.IsNullOrEmpty(subkey))
        {
            return false;
        }

        var digits = subkey.StartsWith("Power", StringComparison.OrdinalIgnoreCase) ? subkey[5..]
            : subkey.StartsWith("Other", StringComparison.OrdinalIgnoreCase) ? subkey[5..]
            : null;
        return digits is { Length: > 0 } && digits.All(char.IsAsciiDigit);
    }

    /// <summary>Returns the duplicate names of the layout (case-insensitive), empty when every name is unique.</summary>
    public IReadOnlyList<string> DuplicateNames() =>
        Slots.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

    /// <inheritdoc />
    public bool Equals(SensorLayout? other) =>
        other is not null && (ReferenceEquals(this, other) || Slots.SequenceEqual(other.Slots));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SensorLayout);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var slot in Slots)
        {
            hash.Add(slot);
        }

        return hash.ToHashCode();
    }

    private static SensorSlot Slot(SensorKey key, string family, int index, AppSettings settings) =>
        new(key, family + index.ToString(CultureInfo.InvariantCulture), SensorNaming.ResolveName(key, settings), key.Unit);
}
