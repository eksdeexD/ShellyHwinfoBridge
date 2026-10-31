using System.Diagnostics.CodeAnalysis;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>What a published HWiNFO sensor shows.</summary>
public enum SensorKind
{
    /// <summary>The primary plug's AC reading ("PC Power (AC)").</summary>
    PcAc,

    /// <summary>The estimated DC power ("PC Power (DC)").</summary>
    PcDc,

    /// <summary>The PSU loss ("PC PSU Loss").</summary>
    PcLoss,

    /// <summary>The app-computed Other Components ("PC Other Components").</summary>
    PcOther,

    /// <summary>An additional device's AC reading ("&lt;Label&gt; Power (AC)").</summary>
    DeviceAc,

    /// <summary>PC AC plus every included additional device ("Total Power (AC)").</summary>
    Total,

    /// <summary>Energy today over the PC and the included devices ("Energy Today", kWh).</summary>
    EnergyToday,

    /// <summary>Energy this month over the PC and the included devices ("Energy This Month", kWh).</summary>
    EnergyMonth,
}

/// <summary>How a sensor's value is formatted.</summary>
public enum SensorValueKind
{
    /// <summary>Watts, one decimal (unit <c>W</c>).</summary>
    Watts,

    /// <summary>Kilowatt-hours, two decimals (unit <c>kWh</c>).</summary>
    Kwh,
}

/// <summary>
/// Identifies a published sensor independently of its name and subkey: a <see cref="SensorKind"/> plus, for
/// <see cref="SensorKind.DeviceAc"/>, the additional device's id.
/// <see cref="Id"/> is the key of <see cref="AppSettings.SensorNames"/>.
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="DeviceId">The additional device's id for per-device kinds, else <c>null</c>.</param>
public readonly record struct SensorKey(SensorKind Kind, string? DeviceId = null)
{
    /// <summary>"PC Power (AC)".</summary>
    public static SensorKey PcAc => new(SensorKind.PcAc);

    /// <summary>"PC Power (DC)".</summary>
    public static SensorKey PcDc => new(SensorKind.PcDc);

    /// <summary>"PC PSU Loss".</summary>
    public static SensorKey PcLoss => new(SensorKind.PcLoss);

    /// <summary>"PC Other Components".</summary>
    public static SensorKey PcOther => new(SensorKind.PcOther);

    /// <summary>"Total Power (AC)".</summary>
    public static SensorKey Total => new(SensorKind.Total);

    /// <summary>"Energy Today".</summary>
    public static SensorKey EnergyToday => new(SensorKind.EnergyToday);

    /// <summary>"Energy This Month".</summary>
    public static SensorKey EnergyMonth => new(SensorKind.EnergyMonth);

    /// <summary>Gets whether the kind belongs to one additional device.</summary>
    public bool IsPerDevice => Kind is SensorKind.DeviceAc;

    /// <summary>Gets the subkey family: <c>Power</c> for watt sensors, <c>Other</c> for kWh.</summary>
    public bool IsPower => ValueKind == SensorValueKind.Watts;

    /// <summary>Gets how the value is formatted.</summary>
    public SensorValueKind ValueKind => Kind is SensorKind.EnergyToday or SensorKind.EnergyMonth ? SensorValueKind.Kwh : SensorValueKind.Watts;

    /// <summary>Gets the HWiNFO unit (<c>W</c> or <c>kWh</c>).</summary>
    public string Unit => ValueKind == SensorValueKind.Kwh ? "kWh" : "W";

    /// <summary>
    /// Gets the stable string id: the kind name, plus <c>:&lt;deviceId&gt;</c> for per-device kinds
    /// (e.g. <c>PcAc</c>, <c>DeviceAc:3f2b…</c>).
    /// </summary>
    public string Id => IsPerDevice ? Kind + ":" + DeviceId : Kind.ToString();

    /// <summary>The AC sensor of an additional device.</summary>
    /// <param name="deviceId">The device id.</param>
    public static SensorKey DeviceAc(string deviceId) => new(SensorKind.DeviceAc, Check(deviceId));

    /// <summary>Parses an <see cref="Id"/>.</summary>
    /// <param name="id">The text.</param>
    /// <param name="key">The key.</param>
    public static bool TryParse(string? id, [NotNullWhen(true)] out SensorKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var text = id.Trim();
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var kindText = colon < 0 ? text : text[..colon];
        if (!Enum.TryParse<SensorKind>(kindText, ignoreCase: false, out var kind) || !Enum.IsDefined(kind) || int.TryParse(kindText, out _))
        {
            return false;
        }

        var perDevice = kind is SensorKind.DeviceAc;
        if (perDevice != (colon >= 0))
        {
            return false;
        }

        if (!perDevice)
        {
            key = new SensorKey(kind);
            return true;
        }

        var device = text[(colon + 1)..].Trim();
        if (device.Length == 0)
        {
            return false;
        }

        key = new SensorKey(kind, device.ToLowerInvariant());
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Id;

    private static string Check(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return deviceId.Trim().ToLowerInvariant();
    }
}
