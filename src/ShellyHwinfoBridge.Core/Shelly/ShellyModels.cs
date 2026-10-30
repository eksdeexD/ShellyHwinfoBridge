namespace ShellyHwinfoBridge.Core.Shelly;

/// <summary>The Shelly component that provides power metering.</summary>
public enum ShellyComponentType
{
    /// <summary>A <c>Switch</c> component (plugs such as Plug M Gen3, Plus Plug S).</summary>
    Switch,

    /// <summary>A <c>PM1</c> component (e.g. PM Mini).</summary>
    PM1,
}

/// <summary>A single power reading from a Shelly device.</summary>
/// <param name="ActivePowerWatts">Active power (<c>apower</c>) in watts.</param>
/// <param name="Volts">Mains voltage, if reported.</param>
/// <param name="Amps">Current, if reported.</param>
public sealed record ShellyReading(double ActivePowerWatts, double? Volts, double? Amps);

/// <summary>A failure talking to a Shelly device.</summary>
public class ShellyException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public ShellyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The device rejected the credentials (HTTP 401).</summary>
public sealed class ShellyAuthenticationException : ShellyException
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">The message.</param>
    public ShellyAuthenticationException(string message)
        : base(message)
    {
    }
}

/// <summary>Reads power from a Shelly device.</summary>
public interface IShellyClient
{
    /// <summary>Reads the current power, voltage and current.</summary>
    /// <param name="cancellationToken">Cancels the request (also used for timeouts).</param>
    /// <exception cref="ShellyException">The device could not be read.</exception>
    Task<ShellyReading> GetReadingAsync(CancellationToken cancellationToken = default);
}
