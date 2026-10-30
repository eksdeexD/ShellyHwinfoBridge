using ShellyHwinfoBridge.Core.Sensors;

namespace ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;

/// <summary>HWiNFO's reading type (<c>SENSOR_READING_TYPE</c> in the HWiNFO SDK).</summary>
public enum HwinfoReadingType
{
    /// <summary>No type.</summary>
    None = 0,

    /// <summary>Temperature.</summary>
    Temperature = 1,

    /// <summary>Voltage.</summary>
    Voltage = 2,

    /// <summary>Fan speed.</summary>
    Fan = 3,

    /// <summary>Current.</summary>
    Current = 4,

    /// <summary>Power.</summary>
    Power = 5,

    /// <summary>Clock.</summary>
    Clock = 6,

    /// <summary>Usage.</summary>
    Usage = 7,

    /// <summary>Anything else.</summary>
    Other = 8,
}

/// <summary>An HWiNFO sensor (device), e.g. "CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced".</summary>
/// <param name="Index">Position in the sensor section; readings refer to it.</param>
/// <param name="Id">HWiNFO's sensor id.</param>
/// <param name="Instance">HWiNFO's sensor instance.</param>
/// <param name="NameOriginal">The original name.</param>
/// <param name="NameUser">The name as renamed by the user (equals the original when not renamed).</param>
public sealed record HwinfoSensor(int Index, uint Id, uint Instance, string NameOriginal, string NameUser)
{
    /// <summary>Gets the display name: the user name, or the original when empty.</summary>
    public string Name => NameUser.Length > 0 ? NameUser : NameOriginal;
}

/// <summary>A single HWiNFO reading, e.g. "CPU Package Power" = 45.2 W.</summary>
/// <param name="Type">The reading type.</param>
/// <param name="SensorIndex">Index of the owning sensor.</param>
/// <param name="Id">HWiNFO's reading id.</param>
/// <param name="Device">The owning sensor's display name; empty if the index is out of range.</param>
/// <param name="LabelOriginal">The original label.</param>
/// <param name="LabelUser">The label as renamed by the user (equals the original when not renamed).</param>
/// <param name="Unit">The unit, e.g. <c>W</c>, <c>V</c>, <c>°C</c>.</param>
/// <param name="Value">The current value.</param>
/// <param name="Min">The minimum since HWiNFO started or was reset.</param>
/// <param name="Max">The maximum.</param>
/// <param name="Average">The average.</param>
public sealed record HwinfoReading(
    HwinfoReadingType Type,
    int SensorIndex,
    uint Id,
    string Device,
    string LabelOriginal,
    string LabelUser,
    string Unit,
    double Value,
    double Min,
    double Max,
    double Average)
{
    /// <summary>Gets the label HWiNFO shows (and formulas reference): the user label, or the original when empty.</summary>
    public string Label => LabelUser.Length > 0 ? LabelUser : LabelOriginal;

    /// <summary>Gets whether the unit is watts.</summary>
    public bool IsWatts => Unit.Trim().Equals("W", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts to the sensor-picker model used by <see cref="SensorHeuristics"/>.</summary>
    public SensorInfo ToSensorInfo() => new(Device, Label);
}

/// <summary>Readings of one HWiNFO device.</summary>
/// <param name="SensorIndex">Index of the device in the sensor section.</param>
/// <param name="Device">The device name.</param>
/// <param name="Readings">The readings, in HWiNFO order.</param>
public sealed record HwinfoDeviceGroup(int SensorIndex, string Device, IReadOnlyList<HwinfoReading> Readings)
{
    /// <summary>Gets the readings as sensor-picker entries.</summary>
    public IReadOnlyList<SensorInfo> Sensors => Readings.Select(r => r.ToSensorInfo()).ToList();
}

/// <summary>One parsed copy of HWiNFO's shared memory.</summary>
/// <param name="Version">Shared-memory structure version.</param>
/// <param name="Revision">Shared-memory structure revision.</param>
/// <param name="PollTime">Time of the last HWiNFO poll.</param>
/// <param name="Sensors">All sensors (devices).</param>
/// <param name="Readings">All readings.</param>
public sealed record HwinfoSnapshot(
    uint Version,
    uint Revision,
    DateTimeOffset PollTime,
    IReadOnlyList<HwinfoSensor> Sensors,
    IReadOnlyList<HwinfoReading> Readings)
{
    /// <summary>
    /// Returns all readings with unit <c>W</c>, grouped by device in HWiNFO order. Feed
    /// <c>groups.SelectMany(g =&gt; g.Sensors)</c> to <see cref="SensorHeuristics.SuggestSelection"/>.
    /// </summary>
    public IReadOnlyList<HwinfoDeviceGroup> GetPowerSensors() =>
        Readings
            .Where(r => r.IsWatts)
            .GroupBy(r => r.SensorIndex)
            .Select(g => new HwinfoDeviceGroup(g.Key, g.First().Device, g.ToList()))
            .ToList();

    /// <summary>
    /// Finds the first reading with the given label (case-insensitive, trimmed), optionally restricted to a device.
    /// </summary>
    /// <param name="label">The reading label, e.g. <c>AC Power (Wall)</c>.</param>
    /// <param name="device">Optional device name, e.g. <c>Shelly Power</c>; matched exactly or as a suffix after ": ".</param>
    public HwinfoReading? FindReading(string label, string? device = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        var wanted = label.Trim();
        return Readings.FirstOrDefault(r =>
            (r.Label.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
             r.LabelOriginal.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase)) &&
            (device is null || DeviceMatches(r.Device, device)));
    }

    private static bool DeviceMatches(string actual, string wanted)
    {
        var a = actual.Trim();
        var w = wanted.Trim();
        return a.Equals(w, StringComparison.OrdinalIgnoreCase) ||
               a.EndsWith(": " + w, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The shared memory exists but could not be parsed.</summary>
public sealed class HwinfoSharedMemoryException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">The message.</param>
    public HwinfoSharedMemoryException(string message)
        : base(message)
    {
    }
}
