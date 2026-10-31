namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>Writes the bridge's custom sensors for HWiNFO, following a <see cref="SensorLayout"/>.</summary>
public interface IHwinfoSensorRegistry
{
    /// <summary>Gets the layout the writer currently follows.</summary>
    SensorLayout Layout { get; }

    /// <summary>
    /// Makes <paramref name="layout"/> the current layout without touching the registry (e.g. at construction, so a
    /// later <see cref="EnsureKeys"/> creates the right keys).
    /// </summary>
    /// <param name="layout">The layout.</param>
    void SetLayout(SensorLayout layout);

    /// <summary>
    /// Makes <paramref name="layout"/> the current layout and brings the registry in line: creates missing keys, repairs
    /// Name/Unit, replaces a non-numeric Value (an old formula) with the default, and deletes <c>PowerN</c>/<c>OtherN</c>
    /// subkeys the layout no longer uses. Existing numeric values are kept.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns>
    /// <c>true</c> when anything had to be created, renamed (Name or Unit) or deleted (the key layout changed), meaning
    /// HWiNFO must be restarted to show the sensors as they are now.
    /// </returns>
    bool ApplyLayout(SensorLayout layout);

    /// <summary>Applies the current <see cref="Layout"/> (see <see cref="ApplyLayout"/>).</summary>
    /// <returns><c>true</c> when HWiNFO must be restarted.</returns>
    bool EnsureKeys();

    /// <summary>
    /// Writes sensor values: watts with one decimal, flags as 0/1, kWh with two decimals. Keys that are not part of
    /// <see cref="Layout"/> are ignored.
    /// </summary>
    /// <param name="values">The values by sensor.</param>
    void WriteValues(IReadOnlyDictionary<SensorKey, double> values);

    /// <summary>Writes the energy totals ("Energy Today", "Energy This Month") in kWh with two decimals.</summary>
    /// <param name="todayKwh">Energy used today.</param>
    /// <param name="monthKwh">Energy used this month.</param>
    void WriteEnergy(double todayKwh, double monthKwh);

    /// <summary>Writes 0 to every power sensor of the layout (PC AC, DC, Loss, Other, each device AC, Total) whose key exists (never creates keys). Keys and the energy totals are kept.</summary>
    void SetOffline();

    /// <summary>Deletes the whole base key (used when uninstalling).</summary>
    void RemoveAll();
}
