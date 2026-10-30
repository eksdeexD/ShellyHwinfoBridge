namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>Writes the bridge's custom sensors for HWiNFO.</summary>
public interface IHwinfoSensorRegistry
{
    /// <summary>
    /// Creates or repairs all sensor keys and their Name/Unit values. Existing values are kept.
    /// </summary>
    /// <returns>
    /// <c>true</c> when any key had to be created or a Name/Unit repaired, meaning HWiNFO must be restarted
    /// to pick up the sensors.
    /// </returns>
    bool EnsureKeys();

    /// <summary>Writes the live readings and the online flag.</summary>
    /// <param name="acWatts">AC power (wall).</param>
    /// <param name="dcWatts">Estimated DC power.</param>
    /// <param name="lossWatts">PSU loss.</param>
    /// <param name="online">Whether the Shelly is reachable.</param>
    void WriteReadings(double acWatts, double dcWatts, double lossWatts, bool online);

    /// <summary>Writes the "Other Components" formula.</summary>
    /// <param name="formula">The HWiNFO formula.</param>
    void WriteFormula(string formula);

    /// <summary>Writes the energy totals ("Energy Today", "Energy This Month") in kWh with two decimals.</summary>
    /// <param name="todayKwh">Energy used today.</param>
    /// <param name="monthKwh">Energy used this month.</param>
    void WriteEnergy(double todayKwh, double monthKwh);

    /// <summary>Sets Online = 0 and writes 0 to AC, DC and Loss. Keys and the energy totals are kept.</summary>
    void SetOffline();

    /// <summary>Deletes the whole base key (used when uninstalling).</summary>
    void RemoveAll();
}
