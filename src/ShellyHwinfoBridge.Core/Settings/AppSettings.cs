using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;

namespace ShellyHwinfoBridge.Core.Settings;

/// <summary>All user settings, persisted as JSON in <c>%APPDATA%\ShellyHwinfoBridge\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>The current settings schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets or sets the settings schema version.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Gets or sets the Shelly device.</summary>
    public ShellyDeviceSettings Device { get; set; } = new();

    /// <summary>Gets or sets the PSU selection.</summary>
    public PsuSettings Psu { get; set; } = new();

    /// <summary>Gets or sets the input voltage mode (auto or manual override).</summary>
    public InputVoltageMode VoltageMode { get; set; } = InputVoltageMode.Auto;

    /// <summary>Gets or sets the AC moving-average window; 1 = off.</summary>
    public int SmoothingSamples { get; set; } = 1;

    /// <summary>Gets or sets the poll interval in milliseconds (500–5000).</summary>
    public int PollIntervalMs { get; set; } = PollingOptions.DefaultIntervalMs;

    /// <summary>Gets or sets the HTTP timeout in milliseconds (always below the interval).</summary>
    public int HttpTimeoutMs { get; set; } = PollingOptions.DefaultTimeoutMs;

    /// <summary>Gets or sets the selected component sensors.</summary>
    public List<SensorSelection> Sensors { get; set; } = [];

    /// <summary>Gets or sets a manually edited formula; <c>null</c> to generate it from <see cref="Sensors"/>.</summary>
    public string? CustomFormula { get; set; }

    /// <summary>Gets or sets whether the tray icon is hidden.</summary>
    public bool HideTrayIcon { get; set; }

    /// <summary>Gets or sets whether the app starts with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Gets or sets the URL used by "Check for database update"; <c>null</c> for the built-in default.</summary>
    public string? DatabaseUpdateUrl { get; set; }

    /// <summary>Returns the formula to write: the custom one if set, otherwise one built from the sensors.</summary>
    public string GetEffectiveFormula() =>
        string.IsNullOrWhiteSpace(CustomFormula) ? HwinfoFormula.Build(Sensors.Select(s => s.Name)) : CustomFormula;

    /// <summary>Returns the polling options described by these settings, normalized.</summary>
    public PollingOptions GetPollingOptions() =>
        new PollingOptions { IntervalMs = PollIntervalMs, TimeoutMs = HttpTimeoutMs }.Normalize();

    /// <summary>Clamps numeric settings into their valid ranges and replaces missing objects.</summary>
    public void Normalize()
    {
        Device ??= new ShellyDeviceSettings();
        Psu ??= new PsuSettings();
        Sensors ??= [];
        var polling = GetPollingOptions();
        PollIntervalMs = polling.IntervalMs;
        HttpTimeoutMs = polling.TimeoutMs;
        SmoothingSamples = Math.Clamp(SmoothingSamples, 1, MovingAverage.MaxSamples);
        if (!Enum.IsDefined(VoltageMode))
        {
            VoltageMode = InputVoltageMode.Auto;
        }
    }
}

/// <summary>The Shelly device.</summary>
public sealed class ShellyDeviceSettings
{
    /// <summary>Gets or sets the IP address or host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the device name, for display.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the device model, for display.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets the DPAPI-protected password (base64); <c>null</c> when auth is off.</summary>
    public string? ProtectedPassword { get; set; }
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
