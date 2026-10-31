using System.Globalization;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>
/// Writes the custom sensors under <c>HKCU\Software\HWiNFO64\Sensors\Custom\Shelly Power</c> following a
/// <see cref="SensorLayout"/>: one <c>PowerN</c>/<c>OtherN</c> subkey per sensor, each with REG_SZ Name, Unit and Value.
/// </summary>
/// <remarks>Thread-safe: every member takes an internal lock.</remarks>
public sealed class HwinfoSensorRegistry : IHwinfoSensorRegistry
{
    private const string NameValue = "Name";
    private const string UnitValue = "Unit";
    private const string ValueValue = "Value";

    private readonly IRegistryBackend _backend;
    private readonly string _basePath;
    private readonly object _gate = new();
    private SensorLayout _layout = SensorLayout.Default;

    /// <summary>Creates a writer with the <see cref="SensorLayout.Default"/> layout.</summary>
    /// <param name="backend">Registry access.</param>
    /// <param name="basePath">Base key path relative to HKCU; defaults to <see cref="SensorNaming.BaseKeyPath"/>.</param>
    public HwinfoSensorRegistry(IRegistryBackend backend, string basePath = SensorNaming.BaseKeyPath)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        _basePath = basePath;
    }

    /// <inheritdoc />
    public SensorLayout Layout
    {
        get
        {
            lock (_gate)
            {
                return _layout;
            }
        }
    }

    /// <inheritdoc />
    public void SetLayout(SensorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
        {
            _layout = layout;
        }
    }

    /// <inheritdoc />
    public bool EnsureKeys() => ApplyLayout(Layout);

    /// <inheritdoc />
    public bool ApplyLayout(SensorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
        {
            _layout = layout;
            var changed = false;
            foreach (var slot in layout.Slots)
            {
                var path = PathOf(slot.Subkey);
                if (!_backend.KeyExists(path))
                {
                    _backend.CreateKey(path);
                    changed = true;
                }

                changed |= EnsureValue(path, NameValue, slot.Name);
                changed |= EnsureValue(path, UnitValue, slot.Unit);
                if (!IsNumber(_backend.GetString(path, ValueValue)))
                {
                    // Missing, or an old formula (Other Components was a HWiNFO formula sensor before).
                    _backend.SetString(path, ValueValue, slot.DefaultValue);
                }
            }

            var used = layout.Slots.Select(s => s.Subkey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var subkey in _backend.GetSubKeyNames(_basePath))
            {
                if (SensorLayout.IsManagedSubkey(subkey) && !used.Contains(subkey))
                {
                    _backend.DeleteTree(PathOf(subkey));
                    changed = true;
                }
            }

            return changed;
        }
    }

    /// <inheritdoc />
    public void WriteValues(IReadOnlyDictionary<SensorKey, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            foreach (var (key, value) in values)
            {
                if (_layout[key] is { } slot)
                {
                    _backend.SetString(PathOf(slot.Subkey), ValueValue, Format(key, value));
                }
            }
        }
    }

    /// <inheritdoc />
    public void WriteEnergy(double todayKwh, double monthKwh) =>
        WriteValues(new Dictionary<SensorKey, double> { [SensorKey.EnergyToday] = todayKwh, [SensorKey.EnergyMonth] = monthKwh });

    /// <inheritdoc />
    public void SetOffline()
    {
        lock (_gate)
        {
            foreach (var slot in _layout.Slots.Where(s => s.Key.ValueKind != SensorValueKind.Kwh && _backend.KeyExists(PathOf(s.Subkey))))
            {
                _backend.SetString(PathOf(slot.Subkey), ValueValue, slot.DefaultValue);
            }
        }
    }

    /// <inheritdoc />
    public void RemoveAll()
    {
        lock (_gate)
        {
            _backend.DeleteTree(_basePath);
        }
    }

    private static string Format(SensorKey key, double value) => key.ValueKind switch
    {
        SensorValueKind.Flag => SensorValueFormatter.FormatFlag(value != 0),
        SensorValueKind.Kwh => SensorValueFormatter.FormatKwh(value),
        _ => SensorValueFormatter.FormatWatts(value),
    };

    private static bool IsNumber(string? value) =>
        value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);

    private string PathOf(string subkey) => _basePath + "\\" + subkey;

    private bool EnsureValue(string path, string valueName, string expected)
    {
        if (string.Equals(_backend.GetString(path, valueName), expected, StringComparison.Ordinal))
        {
            return false;
        }

        _backend.SetString(path, valueName, expected);
        return true;
    }
}
