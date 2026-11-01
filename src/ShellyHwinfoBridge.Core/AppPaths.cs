namespace ShellyHwinfoBridge.Core;

/// <summary>Well-known names and per-user paths.</summary>
public static class AppPaths
{
    /// <summary>The application name, used for folders and the mutex.</summary>
    public const string AppName = "ShellyHwinfoBridge";

    /// <summary>Name of the per-session mutex that enforces a single instance.</summary>
    public const string SingleInstanceMutexName = @"Local\ShellyHwinfoBridge.SingleInstance";

    /// <summary>
    /// Name of the per-session event that asks the running instance to exit cleanly. The installer
    /// (<c>installer/ShellyHwinfoBridge.iss</c>) sets it by this exact name; keep both in sync.
    /// </summary>
    public const string ExitEventName = @"Local\ShellyHwinfoBridge.Exit";

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge</c>.</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge\settings.json</c>.</summary>
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge\logs</c>.</summary>
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Gets <c>%APPDATA%\ShellyHwinfoBridge\energy</c> (hourly CSV files and the counter state).</summary>
    public static string EnergyDirectory => Path.Combine(DataDirectory, "energy");

    /// <summary>
    /// Gets <c>%APPDATA%\ShellyHwinfoBridge\energy\diagnostics</c>: the diagnostic trace files (inside the folder that
    /// "Open data folder" opens, so the user finds them).
    /// </summary>
    public static string DiagnosticsDirectory => Path.Combine(EnergyDirectory, "diagnostics");

    /// <summary>
    /// Gets <c>%APPDATA%\ShellyHwinfoBridge\psu-database.json</c>: the database downloaded by "Check for database update"
    /// (the shipped copy next to the exe is read-only under Program Files).
    /// </summary>
    public static string DownloadedDatabaseFile => Path.Combine(DataDirectory, "psu-database.json");
}
