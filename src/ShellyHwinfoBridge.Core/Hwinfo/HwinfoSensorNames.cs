namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>Registry location and sensor names of the bridge's HWiNFO custom sensors.</summary>
public static class HwinfoSensorNames
{
    /// <summary>Base key, relative to HKEY_CURRENT_USER.</summary>
    public const string BaseKeyPath = @"Software\HWiNFO64\Sensors\Custom\Shelly Power";

    /// <summary>The sensor (device) name HWiNFO shows the custom sensors under.</summary>
    public const string DeviceName = "Shelly Power";

    /// <summary>Name of the AC (wall) power sensor.</summary>
    public const string AcPower = "AC Power (Wall)";

    /// <summary>Name of the estimated DC power sensor.</summary>
    public const string DcPower = "DC Power (Est.)";

    /// <summary>Name of the PSU loss sensor.</summary>
    public const string PsuLoss = "PSU Loss";

    /// <summary>Name of the formula sensor for the remaining components.</summary>
    public const string OtherComponents = "Other Components";

    /// <summary>Name of the online indicator sensor.</summary>
    public const string ShellyOnline = "Shelly Online";

    /// <summary>Name of the energy-today sensor (kWh).</summary>
    public const string EnergyToday = "Energy Today";

    /// <summary>Name of the energy-this-month sensor (kWh).</summary>
    public const string EnergyThisMonth = "Energy This Month";
}
