namespace ShellyHwinfoBridge.Core.Sensors;

/// <summary>The category a selected component sensor is assigned to.</summary>
public enum SensorCategory
{
    /// <summary>CPU power.</summary>
    Cpu,

    /// <summary>GPU power.</summary>
    Gpu,

    /// <summary>Memory (DIMM) power.</summary>
    Ram,

    /// <summary>Anything else that reports power, e.g. a second GPU or a capture card.</summary>
    OtherMeasured,
}

/// <summary>A power sensor as reported by HWiNFO.</summary>
/// <param name="Device">The HWiNFO device (sensor group) name; empty when unknown (manual entry).</param>
/// <param name="Name">The sensor (reading) name, used to reference it in formulas.</param>
public sealed record SensorInfo(string Device, string Name);
