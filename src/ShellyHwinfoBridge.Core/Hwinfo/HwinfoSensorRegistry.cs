namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>
/// Writes the custom sensors under <c>HKCU\Software\HWiNFO64\Sensors\Custom\Shelly Power</c>:
/// Power0 AC Power (Wall), Power1 DC Power (Est.), Power2 PSU Loss, Power3 Other Components (formula),
/// Other0 Shelly Online, Other1 Energy Today (kWh), Other2 Energy This Month (kWh). Every value is REG_SZ with
/// Name, Unit and Value entries.
/// </summary>
public sealed class HwinfoSensorRegistry : IHwinfoSensorRegistry
{
    /// <summary>Subkey of the AC sensor.</summary>
    public const string AcSubkey = "Power0";

    /// <summary>Subkey of the DC sensor.</summary>
    public const string DcSubkey = "Power1";

    /// <summary>Subkey of the loss sensor.</summary>
    public const string LossSubkey = "Power2";

    /// <summary>Subkey of the formula sensor.</summary>
    public const string OtherSubkey = "Power3";

    /// <summary>Subkey of the online sensor.</summary>
    public const string OnlineSubkey = "Other0";

    /// <summary>Subkey of the energy-today sensor.</summary>
    public const string EnergyTodaySubkey = "Other1";

    /// <summary>Subkey of the energy-this-month sensor.</summary>
    public const string EnergyMonthSubkey = "Other2";

    private const string NameValue = "Name";
    private const string UnitValue = "Unit";
    private const string ValueValue = "Value";

    private static readonly (string Subkey, string Name, string Unit, string Default)[] Sensors =
    [
        (AcSubkey, HwinfoSensorNames.AcPower, "W", "0.0"),
        (DcSubkey, HwinfoSensorNames.DcPower, "W", "0.0"),
        (LossSubkey, HwinfoSensorNames.PsuLoss, "W", "0.0"),
        (OtherSubkey, HwinfoSensorNames.OtherComponents, "W", "0.0"),
        (OnlineSubkey, HwinfoSensorNames.ShellyOnline, string.Empty, "0"),
        (EnergyTodaySubkey, HwinfoSensorNames.EnergyToday, "kWh", "0.00"),
        (EnergyMonthSubkey, HwinfoSensorNames.EnergyThisMonth, "kWh", "0.00"),
    ];

    private readonly IRegistryBackend _backend;
    private readonly string _basePath;

    /// <summary>Creates a writer.</summary>
    /// <param name="backend">Registry access.</param>
    /// <param name="basePath">Base key path relative to HKCU; defaults to <see cref="HwinfoSensorNames.BaseKeyPath"/>.</param>
    public HwinfoSensorRegistry(IRegistryBackend backend, string basePath = HwinfoSensorNames.BaseKeyPath)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        _basePath = basePath;
    }

    /// <inheritdoc />
    public bool EnsureKeys()
    {
        var changed = false;
        foreach (var (subkey, name, unit, defaultValue) in Sensors)
        {
            var path = PathOf(subkey);
            if (!_backend.KeyExists(path))
            {
                _backend.CreateKey(path);
                changed = true;
            }

            changed |= EnsureValue(path, NameValue, name);
            changed |= EnsureValue(path, UnitValue, unit);
            if (_backend.GetString(path, ValueValue) is null)
            {
                _backend.SetString(path, ValueValue, defaultValue);
            }
        }

        return changed;
    }

    /// <inheritdoc />
    public void WriteReadings(double acWatts, double dcWatts, double lossWatts, bool online)
    {
        _backend.SetString(PathOf(AcSubkey), ValueValue, SensorValueFormatter.FormatWatts(acWatts));
        _backend.SetString(PathOf(DcSubkey), ValueValue, SensorValueFormatter.FormatWatts(dcWatts));
        _backend.SetString(PathOf(LossSubkey), ValueValue, SensorValueFormatter.FormatWatts(lossWatts));
        _backend.SetString(PathOf(OnlineSubkey), ValueValue, SensorValueFormatter.FormatFlag(online));
    }

    /// <inheritdoc />
    public void WriteFormula(string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        _backend.SetString(PathOf(OtherSubkey), ValueValue, formula);
    }

    /// <inheritdoc />
    public void WriteEnergy(double todayKwh, double monthKwh)
    {
        _backend.SetString(PathOf(EnergyTodaySubkey), ValueValue, SensorValueFormatter.FormatKwh(todayKwh));
        _backend.SetString(PathOf(EnergyMonthSubkey), ValueValue, SensorValueFormatter.FormatKwh(monthKwh));
    }

    /// <inheritdoc />
    public void SetOffline() => WriteReadings(0, 0, 0, online: false);

    /// <inheritdoc />
    public void RemoveAll() => _backend.DeleteTree(_basePath);

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
