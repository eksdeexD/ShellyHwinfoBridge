using System.Text.Json.Serialization;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Sensors;

namespace ShellyHwinfoBridge.Core.Settings;

/// <summary>All user settings, persisted as JSON in <c>%APPDATA%\ShellyHwinfoBridge\settings.json</c>.</summary>
/// <remarks>
/// Schema 2 added <see cref="AdditionalDevices"/>, <see cref="SensorNames"/>, <see cref="ChartMode"/>,
/// <see cref="ColourMode"/> / <see cref="BaseColour"/>; <see cref="DisplayIntervalMs"/> later replaced the poll interval
/// (see <see cref="PollIntervalMs"/>). Version 1 files load
/// unchanged with the defaults; <see cref="Normalize"/> raises <see cref="SchemaVersion"/> to the current version.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>The current settings schema version.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The most devices (primary plus additional) the app supports.</summary>
    public const int MaxDevices = 4;

    /// <summary>The fixed device id of the primary (PC) Shelly.</summary>
    public const string PrimaryDeviceId = "pc";

    /// <summary>The label of the primary (PC) Shelly, reserved for it.</summary>
    public const string PrimaryLabel = "PC";

    /// <summary>Gets or sets the settings schema version.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Gets or sets the primary Shelly device (the PC's plug, id <see cref="PrimaryDeviceId"/>).</summary>
    public ShellyDeviceSettings Device { get; set; } = new();

    /// <summary>
    /// Gets or sets the additional Shelly devices (a monitor or another device, AC only), in list order; at most
    /// <see cref="MaxDevices"/> − 1.
    /// </summary>
    public List<AdditionalDeviceSettings> AdditionalDevices { get; set; } = [];

    /// <summary>
    /// Gets or sets the custom HWiNFO sensor names: sensor key (<see cref="SensorKey.Id"/>) → name. A missing or empty
    /// entry means the default name.
    /// </summary>
    public Dictionary<string, string> SensorNames { get; set; } = [];

    /// <summary>Gets or sets the energy chart mode (the toggle is shown when more than one device exists).</summary>
    public EnergyChartMode ChartMode { get; set; } = EnergyChartMode.Combined;

    /// <summary>Gets or sets how device colours are chosen.</summary>
    public ColourMode ColourMode { get; set; } = ColourMode.AutomaticScheme;

    /// <summary>Gets or sets the base colour of the automatic scheme (<c>#RRGGBB</c>); it is the PC's colour.</summary>
    public string BaseColour { get; set; } = DeviceColours.DefaultBase;

    /// <summary>Gets or sets the PSU selection.</summary>
    public PsuSettings Psu { get; set; } = new();

    /// <summary>Gets or sets the input voltage mode (auto or manual override).</summary>
    public InputVoltageMode VoltageMode { get; set; } = InputVoltageMode.Auto;

    /// <summary>Gets or sets the AC moving-average window; 1 = off.</summary>
    public int SmoothingSamples { get; set; } = 1;

    /// <summary>The shortest display interval in milliseconds.</summary>
    public const int MinDisplayIntervalMs = 1000;

    /// <summary>The longest display interval in milliseconds.</summary>
    public const int MaxDisplayIntervalMs = 5000;

    /// <summary>The default display interval in milliseconds.</summary>
    public const int DefaultDisplayIntervalMs = 2000;

    /// <summary>The step of the display interval slider in milliseconds.</summary>
    public const int DisplayIntervalStepMs = 500;

    private int? _displayIntervalMs;

    /// <summary>
    /// Gets or sets how often the main window's stat cards, the status pill and the tray tooltip refresh, in
    /// milliseconds (<see cref="MinDisplayIntervalMs"/>–<see cref="MaxDisplayIntervalMs"/>). Polling itself always
    /// runs at <see cref="PollingOptions.DefaultIntervalMs"/>.
    /// </summary>
    public int DisplayIntervalMs
    {
        get => _displayIntervalMs ?? DefaultDisplayIntervalMs;
        set => _displayIntervalMs = value;
    }

    /// <summary>
    /// Gets or sets the retired poll interval of earlier versions (polling is fixed at
    /// <see cref="PollingOptions.DefaultIntervalMs"/> now). Read only to migrate it into <see cref="DisplayIntervalMs"/>
    /// when a file has no display interval yet; <see cref="Normalize"/> clears it, so it is not written again.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PollIntervalMs { get; set; }

    /// <summary>Gets or sets the HTTP timeout in milliseconds (always below the interval).</summary>
    public int HttpTimeoutMs { get; set; } = PollingOptions.DefaultTimeoutMs;

    /// <summary>Gets or sets the selected component sensors (subtracted from DC to compute Other Components).</summary>
    public List<SensorSelection> Sensors { get; set; } = [];

    /// <summary>
    /// Gets or sets a formerly hand-edited HWiNFO formula. Retired: kept in the file for a possible future
    /// "components expression" feature, never applied (Other Components is computed by the app).
    /// </summary>
    public string? CustomFormula { get; set; }

    /// <summary>Gets or sets whether the tray icon is hidden.</summary>
    public bool HideTrayIcon { get; set; }

    /// <summary>Gets or sets whether the app starts with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>The longest accepted currency text.</summary>
    public const int MaxCurrencyLength = 8;

    /// <summary>The highest accepted price per kWh.</summary>
    public const decimal MaxPricePerKwh = 1000m;

    /// <summary>Gets or sets whether hourly energy is logged (default on; also on for settings files written before it existed).</summary>
    public bool EnergyLoggingEnabled { get; set; } = true;

    /// <summary>Gets or sets the optional price per kWh used to show cost; <c>null</c> hides cost.</summary>
    public decimal? EnergyPricePerKwh { get; set; }

    /// <summary>Gets or sets the optional currency text shown after costs (e.g. <c>DKK</c>), at most <see cref="MaxCurrencyLength"/> characters.</summary>
    public string? EnergyCurrency { get; set; }

    /// <summary>The built-in database update URL (the project's GitHub raw URL).</summary>
    public const string DefaultDatabaseUpdateUrl = "https://raw.githubusercontent.com/eksdeexD/ShellyHwinfoBridge/main/psu-database.json";

    /// <summary>
    /// Gets or sets the URL used by "Check for database update"; <c>null</c> for <see cref="DefaultDatabaseUpdateUrl"/>.
    /// </summary>
    public string? DatabaseUpdateUrl { get; set; }

    /// <summary>Gets whether at least one additional device is configured.</summary>
    [JsonIgnore]
    public bool HasAdditionalDevices => AdditionalDevices is { Count: > 0 };

    /// <summary>Gets the number of devices (primary plus additional).</summary>
    [JsonIgnore]
    public int DeviceCount => 1 + (AdditionalDevices?.Count ?? 0);

    /// <summary>Gets the database update URL to use: the configured one, or the built-in default.</summary>
    public string GetEffectiveDatabaseUpdateUrl() =>
        string.IsNullOrWhiteSpace(DatabaseUpdateUrl) ? DefaultDatabaseUpdateUrl : DatabaseUpdateUrl.Trim();

    /// <summary>Returns the additional device with an id (case-insensitive), or <c>null</c>.</summary>
    /// <param name="deviceId">The device id.</param>
    public AdditionalDeviceSettings? FindAdditionalDevice(string? deviceId) =>
        deviceId is null ? null : AdditionalDevices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns whether an id is the primary device's id.</summary>
    /// <param name="deviceId">The device id.</param>
    public static bool IsPrimary(string? deviceId) => string.Equals(deviceId, PrimaryDeviceId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns a device's label: "PC" for the primary, else the additional device's label, or <c>null</c> when unknown.</summary>
    /// <param name="deviceId">The device id.</param>
    public string? GetDeviceLabel(string? deviceId) => IsPrimary(deviceId) ? PrimaryLabel : FindAdditionalDevice(deviceId)?.Label;

    /// <summary>Returns the ids of every device in display order: <see cref="PrimaryDeviceId"/> first, then the additional devices.</summary>
    public IReadOnlyList<string> GetDeviceIds() => [PrimaryDeviceId, .. AdditionalDevices.Select(d => d.Id)];

    /// <summary>Returns whether a device counts in Total Power and the energy totals (the primary always does).</summary>
    /// <param name="deviceId">The device id.</param>
    public bool IsIncludedInTotals(string? deviceId) => IsPrimary(deviceId) || FindAdditionalDevice(deviceId)?.IncludeInTotals == true;

    /// <summary>
    /// Returns the polling options described by these settings, normalized: the fixed interval
    /// <see cref="PollingOptions.DefaultIntervalMs"/> and <see cref="HttpTimeoutMs"/>.
    /// </summary>
    public PollingOptions GetPollingOptions() =>
        new PollingOptions { IntervalMs = PollingOptions.DefaultIntervalMs, TimeoutMs = HttpTimeoutMs }.Normalize();

    /// <summary>
    /// Snaps a display interval to <see cref="DisplayIntervalStepMs"/> and clamps it to
    /// <see cref="MinDisplayIntervalMs"/>–<see cref="MaxDisplayIntervalMs"/>.
    /// </summary>
    /// <param name="milliseconds">The interval.</param>
    public static int NormalizeDisplayInterval(int milliseconds)
    {
        var clamped = Math.Clamp(milliseconds, MinDisplayIntervalMs, MaxDisplayIntervalMs);
        return (int)(Math.Round(clamped / (double)DisplayIntervalStepMs, MidpointRounding.AwayFromZero) * DisplayIntervalStepMs);
    }

    /// <summary>
    /// Clamps numeric settings into their valid ranges, replaces missing objects, repairs the device list (ids, labels,
    /// colours, the device cap) and drops invalid custom sensor names. Raises <see cref="SchemaVersion"/> to the current
    /// version.
    /// </summary>
    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        Device ??= new ShellyDeviceSettings();
        Device.Host ??= string.Empty;
        Device.Colour = DeviceColours.Normalize(Device.Colour);
        Psu ??= new PsuSettings();
        Sensors ??= [];
        NormalizeDevices();
        NormalizeSensorNames();
        if (!Enum.IsDefined(ChartMode))
        {
            ChartMode = EnergyChartMode.Combined;
        }

        if (!Enum.IsDefined(ColourMode))
        {
            ColourMode = ColourMode.AutomaticScheme;
        }

        BaseColour = DeviceColours.Normalize(BaseColour) ?? DeviceColours.DefaultBase;
        // Migration: a file from before the display interval keeps its poll interval as the display interval.
        if (PollIntervalMs is int legacy && _displayIntervalMs is null)
        {
            _displayIntervalMs = legacy;
        }

        PollIntervalMs = null;
        DisplayIntervalMs = NormalizeDisplayInterval(DisplayIntervalMs);
        HttpTimeoutMs = GetPollingOptions().TimeoutMs;
        SmoothingSamples = Math.Clamp(SmoothingSamples, 1, MovingAverage.MaxSamples);
        if (!Enum.IsDefined(VoltageMode))
        {
            VoltageMode = InputVoltageMode.Auto;
        }

        if (EnergyPricePerKwh is decimal price)
        {
            EnergyPricePerKwh = Math.Clamp(price, 0m, MaxPricePerKwh);
        }

        EnergyCurrency = string.IsNullOrWhiteSpace(EnergyCurrency) ? null : EnergyCurrency.Trim();
        if (EnergyCurrency is { Length: > MaxCurrencyLength })
        {
            EnergyCurrency = EnergyCurrency[..MaxCurrencyLength];
        }
    }

    /// <summary>Returns a deep copy (a JSON round-trip with the settings file's options), for editing without side effects.</summary>
    public AppSettings Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;

    private void NormalizeDevices()
    {
        var devices = new List<AdditionalDeviceSettings>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in AdditionalDevices ?? [])
        {
            if (device is null || devices.Count >= MaxDevices - 1)
            {
                continue;
            }

            if (!Guid.TryParse(device.Id, out var guid) || !ids.Add(guid.ToString("D")))
            {
                guid = Guid.NewGuid();
                ids.Add(guid.ToString("D"));
            }

            device.Id = guid.ToString("D");
            device.Host = (device.Host ?? string.Empty).Trim();
            device.Colour = DeviceColours.Normalize(device.Colour);
            device.Label = (device.Label ?? string.Empty).Trim();
            devices.Add(device);
        }

        // A label that breaks the rules (a hand-edited file) gets a fresh default label, so sensor names stay unique.
        for (var i = 0; i < devices.Count; i++)
        {
            var others = devices.Take(i).Select(d => d.Label).ToList();
            if (DeviceLabelRules.Validate(devices[i].Label, others) is not null)
            {
                devices[i].Label = DeviceLabelRules.NextDefaultLabel(others);
            }
        }

        AdditionalDevices = devices;
    }

    private void NormalizeSensorNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in SensorNames ?? [])
        {
            if (key is null || !SensorKey.TryParse(key, out var parsed) || !SensorNaming.IsValidName(value))
            {
                continue;
            }

            names[parsed.Id] = value.Trim();
        }

        SensorNames = names;
    }
}

/// <summary>A Shelly device: the primary (PC) plug, and the base of <see cref="AdditionalDeviceSettings"/>.</summary>
public class ShellyDeviceSettings
{
    /// <summary>Gets or sets the IP address or host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the device name, for display.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the device model, for display.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets the DPAPI-protected password (base64); <c>null</c> when auth is off.</summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>
    /// Gets or sets the device's chart colour (<c>#RRGGBB</c>) used in <see cref="ColourMode.Custom"/>; <c>null</c> for
    /// the automatic colour.
    /// </summary>
    public string? Colour { get; set; }
}

/// <summary>How the PSU curve is chosen.</summary>
public enum PsuSelectionMode
{
    /// <summary>An entry from the PSU database.</summary>
    Database,

    /// <summary>A generic curve from wattage and 80 Plus rating.</summary>
    Generic,

    /// <summary>A manually entered curve.</summary>
    Manual,
}

/// <summary>The PSU selection.</summary>
public sealed class PsuSettings
{
    /// <summary>Gets or sets the selection mode.</summary>
    public PsuSelectionMode Mode { get; set; } = PsuSelectionMode.Database;

    /// <summary>Gets or sets the database entry id (database mode).</summary>
    public string? DatabaseId { get; set; }

    /// <summary>Gets or sets the rated wattage (generic mode).</summary>
    public int? GenericWattage { get; set; }

    /// <summary>Gets or sets the 80 Plus rating (generic mode).</summary>
    public EightyPlusRating? GenericRating { get; set; }

    /// <summary>Gets or sets the manual curve rows (manual mode).</summary>
    public List<CurvePoint>? ManualCurve { get; set; }
}

/// <summary>A selected component sensor and its category.</summary>
public sealed class SensorSelection
{
    /// <summary>Gets or sets the HWiNFO sensor name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the category.</summary>
    public SensorCategory Category { get; set; } = SensorCategory.OtherMeasured;

    /// <summary>Gets or sets the HWiNFO device name, if known.</summary>
    public string? Device { get; set; }
}
