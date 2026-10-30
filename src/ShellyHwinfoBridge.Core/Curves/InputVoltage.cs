namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>The mains input voltage class a PSU curve was measured at.</summary>
public enum InputVoltage
{
    /// <summary>115 V (low line).</summary>
    V115 = 115,

    /// <summary>230 V (high line).</summary>
    V230 = 230,
}

/// <summary>How the input voltage curve is chosen.</summary>
public enum InputVoltageMode
{
    /// <summary>Choose from the Shelly's measured voltage (see <see cref="VoltageSelector"/>).</summary>
    Auto,

    /// <summary>Always use the 115 V curve.</summary>
    V115,

    /// <summary>Always use the 230 V curve.</summary>
    V230,
}

/// <summary>Chooses the input voltage curve from the measured mains voltage.</summary>
public static class VoltageSelector
{
    /// <summary>Measured voltages at or above this value select the 230 V curve.</summary>
    public const double HighLineThresholdVolts = 180;

    /// <summary>Returns <see cref="InputVoltage.V230"/> for ≥ 180 V, otherwise <see cref="InputVoltage.V115"/>.</summary>
    /// <param name="volts">The measured mains voltage.</param>
    public static InputVoltage FromMeasured(double volts) =>
        volts >= HighLineThresholdVolts ? InputVoltage.V230 : InputVoltage.V115;

    /// <summary>
    /// Resolves the voltage to use. A manual override always wins; in <see cref="InputVoltageMode.Auto"/>
    /// the measured voltage decides, and 230 V is assumed when no valid measurement is available.
    /// </summary>
    /// <param name="mode">The configured mode.</param>
    /// <param name="measuredVolts">The measured mains voltage, if known.</param>
    public static InputVoltage Resolve(InputVoltageMode mode, double? measuredVolts) => mode switch
    {
        InputVoltageMode.V115 => InputVoltage.V115,
        InputVoltageMode.V230 => InputVoltage.V230,
        _ => measuredVolts is double v && double.IsFinite(v) && v > 0 ? FromMeasured(v) : InputVoltage.V230,
    };
}
