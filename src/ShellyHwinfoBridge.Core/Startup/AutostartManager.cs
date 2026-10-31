using ShellyHwinfoBridge.Core.Hwinfo;

namespace ShellyHwinfoBridge.Core.Startup;

/// <summary>
/// Enables or disables "Start with Windows" through
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ShellyHwinfoBridge</c>.
/// </summary>
/// <remarks>
/// The value is the quoted executable path followed by <see cref="StartupArguments.AutostartSwitch"/>, e.g.
/// <c>"C:\Tools\ShellyHwinfoBridge.exe" --autostart</c>, so the app can tell a logon start from a manual one.
/// </remarks>
public sealed class AutostartManager
{
    /// <summary>The Run key path, relative to HKCU.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value name under the Run key.</summary>
    public const string ValueName = AppPaths.AppName;

    private readonly IRegistryBackend _registry;

    /// <summary>Creates a manager.</summary>
    /// <param name="registry">HKCU registry access.</param>
    /// <param name="executablePath">Full path of the executable to start (usually <see cref="Environment.ProcessPath"/>).</param>
    public AutostartManager(IRegistryBackend registry, string executablePath)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ExecutablePath = executablePath.Trim().Trim('"');
    }

    /// <summary>Gets the executable path written to the Run key.</summary>
    public string ExecutablePath { get; }

    /// <summary>Gets the command line written to the Run key.</summary>
    public string CommandLine => $"\"{ExecutablePath}\" {StartupArguments.AutostartSwitch}";

    /// <summary>Gets whether a Run entry exists (for any path).</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_registry.GetString(RunKeyPath, ValueName));

    /// <summary>
    /// Gets whether the Run entry exists but points to another executable (e.g. the exe was moved);
    /// calling <see cref="Enable"/> or <see cref="EnsureCurrent"/> repairs it.
    /// </summary>
    public bool IsStale
    {
        get
        {
            var current = _registry.GetString(RunKeyPath, ValueName);
            return !string.IsNullOrWhiteSpace(current) &&
                   !string.Equals(ExtractExecutable(current), ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Gets whether the Run entry is exactly <see cref="CommandLine"/> (this exe, with the autostart switch).</summary>
    public bool IsCurrent =>
        string.Equals(_registry.GetString(RunKeyPath, ValueName)?.Trim(), CommandLine, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the current executable path to the Run entry unless it is already <see cref="CommandLine"/>.
    /// Called on every start while "Start with Windows" is on, so the entry follows the exe when it moves
    /// (for example from a portable folder into Program Files by the installer) or when the entry went missing.
    /// </summary>
    /// <returns><c>true</c> if the entry was written.</returns>
    public bool EnsureCurrent()
    {
        if (IsCurrent)
        {
            return false;
        }

        Enable();
        return true;
    }

    /// <summary>Writes (or rewrites) the Run entry.</summary>
    public void Enable() => _registry.SetString(RunKeyPath, ValueName, CommandLine);

    /// <summary>Removes the Run entry; does nothing if it does not exist.</summary>
    public void Disable() => _registry.DeleteValue(RunKeyPath, ValueName);

    /// <summary>Enables or disables the Run entry.</summary>
    /// <param name="enabled">Whether the app should start with Windows.</param>
    public void Apply(bool enabled)
    {
        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    /// <summary>Extracts the executable path from a Run command line (quoted or not).</summary>
    /// <param name="commandLine">The command line.</param>
    internal static string ExtractExecutable(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 0 ? text[1..end] : text.Trim('"');
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? text[..(exe + 4)] : text;
    }
}
