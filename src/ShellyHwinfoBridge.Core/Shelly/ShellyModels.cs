using System.Text.Json;

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
/// <param name="EnergyTotalWh">
/// The device's cumulative energy counter (<c>aenergy.total</c>) in watt-hours, if reported. The plug keeps
/// counting while the app is not running; it restarts from zero after a device reset.
/// </param>
public sealed record ShellyReading(double ActivePowerWatts, double? Volts, double? Amps, double? EnergyTotalWh = null);

/// <summary>Identity of a Shelly device from <c>Shelly.GetDeviceInfo</c>.</summary>
/// <param name="Name">The user-assigned device name, if set.</param>
/// <param name="Model">The model / SKU (e.g. <c>S3PL-10112EU</c>), if reported.</param>
/// <param name="App">The application name (e.g. <c>PlugMG3</c>), if reported.</param>
/// <param name="Generation">The device generation (2, 3, ...), if reported.</param>
/// <param name="Id">The device id (e.g. <c>shellyplugmg3-0a1b2c3d4e5f</c>), if reported.</param>
/// <param name="AuthEnabled">Whether the device requires a password, if reported.</param>
public sealed record ShellyDeviceInfo(string? Name, string? Model, string? App, int? Generation, string? Id = null, bool? AuthEnabled = null)
{
    /// <summary>Parses a <c>Shelly.GetDeviceInfo</c> response; missing or wrongly typed fields become <c>null</c>.</summary>
    /// <param name="info">The response's root element.</param>
    /// <returns>The parsed info; all fields are <c>null</c> when <paramref name="info"/> is not an object.</returns>
    public static ShellyDeviceInfo Parse(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object)
        {
            return new ShellyDeviceInfo(null, null, null, null);
        }

        return new ShellyDeviceInfo(
            String(info, "name"),
            String(info, "model"),
            String(info, "app"),
            info.TryGetProperty("gen", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetInt32(out var gen) ? gen : null,
            String(info, "id"),
            info.TryGetProperty("auth_en", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False ? a.GetBoolean() : null);
    }

    private static string? String(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
            ? e.GetString()!.Trim()
            : null;
}

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

    /// <summary>Reads the device identity (<c>Shelly.GetDeviceInfo</c>).</summary>
    /// <param name="cancellationToken">Cancels the request (also used for timeouts).</param>
    /// <exception cref="ShellyException">The device could not be read.</exception>
    Task<ShellyDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default);
}
