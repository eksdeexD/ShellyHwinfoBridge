namespace ShellyHwinfoBridge.Core;

/// <summary>Well-known names and per-user paths.</summary>
public static class AppPaths
{
    /// <summary>The application name, used for folders and the mutex.</summary>
    public const string AppName = "ShellyHwinfoBridge";

    /// <summary>Name of the per-session mutex that enforces a single instance.</summary>
    public const string SingleInstanceMutexName = @"Local\ShellyHwinfoBridge.SingleInstance";

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge</c>.</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge\settings.json</c>.</summary>
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge\logs</c>.</summary>
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge\energy</c> (hourly CSV files and the counter state).</summary>
    public static string EnergyDirectory => Path.Combine(DataDirectory, "energy");
}
