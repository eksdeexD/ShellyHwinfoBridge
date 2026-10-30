using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShellyHwinfoBridge.Core.Hwinfo.Locator;

/// <summary>Where the HWiNFO executable path was found.</summary>
public enum HwinfoPathSource
{
    /// <summary>Not found.</summary>
    None,

    /// <summary>The image path of the running HWiNFO64 process.</summary>
    RunningProcess,

    /// <summary>An uninstall registry entry (installer version).</summary>
    UninstallEntry,

    /// <summary>A path value under <c>HKCU\Software\HWiNFO64</c>.</summary>
    UserSettings,
}

/// <summary>What <see cref="HwinfoLocator.Detect"/> found.</summary>
/// <param name="IsInstalled">Whether HWiNFO64 appears to be installed (registry evidence or a running process).</param>
/// <param name="IsRunning">Whether an HWiNFO64 process is running.</param>
/// <param name="ExecutablePath">The full path of <c>HWiNFO64.exe</c>, or <c>null</c> if unknown (e.g. a portable copy that is not running).</param>
/// <param name="PathSource">Where <paramref name="ExecutablePath"/> came from.</param>
public sealed record HwinfoStatus(bool IsInstalled, bool IsRunning, string? ExecutablePath, HwinfoPathSource PathSource);

/// <summary>The outcome of <see cref="HwinfoLocator.RestartAsync"/>.</summary>
public enum HwinfoRestartResult
{
    /// <summary>HWiNFO was closed and started again.</summary>
    Restarted,

    /// <summary>HWiNFO was not running; nothing was done.</summary>
    NotRunning,

    /// <summary>The executable path is unknown, so HWiNFO was left running.</summary>
    ExecutableNotFound,

    /// <summary>HWiNFO did not close and could not be terminated (usually because it runs elevated); close it manually.</summary>
    CouldNotClose,

    /// <summary>HWiNFO was closed but starting it again failed or the UAC prompt was cancelled; start it manually.</summary>
    RelaunchFailed,
}

/// <summary>
/// Detects HWiNFO64 (wizard step 1) and restarts it so it picks up new custom sensors (wizard step 5).
/// </summary>
/// <remarks>
/// <para>The executable path is resolved in this order:</para>
/// <list type="number">
/// <item>the image path of a running <c>HWiNFO64</c> process;</item>
/// <item>uninstall entries under <c>Software\Microsoft\Windows\CurrentVersion\Uninstall</c> in HKLM (64- and 32-bit
/// views) and HKCU whose key or display name starts with "HWiNFO" (ignoring symbols, so "HWiNFO® 64_is1" matches)
/// and is not the 32-bit edition: <c>DisplayIcon</c>, then <c>InstallLocation\HWiNFO64.exe</c>;</item>
/// <item>any string value under <c>HKCU\Software\HWiNFO64</c> that is a path to an existing <c>HWiNFO64.exe</c>.</item>
/// </list>
/// <para>
/// <c>HKCU\Software\HWiNFO64</c> also counts as evidence of an installation when it holds anything besides the
/// <c>Sensors</c> subkey (which this app creates itself), e.g. HWiNFO's own <c>OSD</c> or <c>Summary</c> keys left by a
/// portable copy.
/// </para>
/// </remarks>
public sealed class HwinfoLocator
{
    /// <summary>The process name of 64-bit HWiNFO.</summary>
    public const string ProcessName = "HWiNFO64";

    /// <summary>The executable file name.</summary>
    public const string ExecutableName = "HWiNFO64.exe";

    /// <summary>The uninstall key path (relative to HKLM or HKCU).</summary>
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>HWiNFO's per-user settings key.</summary>
    public const string UserSettingsKeyPath = @"Software\HWiNFO64";

    private static readonly RegistryRoot[] UninstallRoots = [RegistryRoot.LocalMachine64, RegistryRoot.LocalMachine32, RegistryRoot.CurrentUser];

    private readonly IHwinfoSystem _system;
    private readonly ILogger _logger;

    /// <summary>Creates a locator.</summary>
    /// <param name="system">Operating-system access.</param>
    /// <param name="logger">Optional logger.</param>
    public HwinfoLocator(IHwinfoSystem system, ILogger? logger = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether HWiNFO64 appears to be installed.</summary>
    public bool IsInstalled => Detect().IsInstalled;

    /// <summary>Gets whether HWiNFO64 is running.</summary>
    public bool IsRunning => _system.GetProcesses(ProcessName).Count > 0;

    /// <summary>Gets the path of <c>HWiNFO64.exe</c>, or <c>null</c> if unknown.</summary>
    public string? ExePath => Detect().ExecutablePath;

    /// <summary>Creates a locator over the real system.</summary>
    /// <param name="logger">Optional logger.</param>
    [SupportedOSPlatform("windows")]
    public static HwinfoLocator CreateDefault(ILogger? logger = null) => new(new WindowsHwinfoSystem(), logger);

    /// <summary>Detects installation, running state and executable path.</summary>
    public HwinfoStatus Detect()
    {
        var processes = _system.GetProcesses(ProcessName);
        var running = processes.Count > 0;

        var fromProcess = processes.Select(p => p.ExecutablePath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (fromProcess is not null)
        {
            return new HwinfoStatus(true, running, fromProcess, HwinfoPathSource.RunningProcess);
        }

        var fromUninstall = FindInUninstallEntries();
        if (fromUninstall.Path is not null)
        {
            return new HwinfoStatus(true, running, fromUninstall.Path, HwinfoPathSource.UninstallEntry);
        }

        var fromUser = FindInUserSettings();
        if (fromUser is not null)
        {
            return new HwinfoStatus(true, running, fromUser, HwinfoPathSource.UserSettings);
        }

        var installed = running || fromUninstall.EntryFound || HasUserSettingsEvidence();
        return new HwinfoStatus(installed, running, null, HwinfoPathSource.None);
    }

    /// <summary>
    /// Closes the running HWiNFO64 gracefully (close request, wait), terminates it if it does not exit, and starts it
    /// again from the detected path. Does nothing when HWiNFO is not running.
    /// </summary>
    /// <param name="gracePeriod">How long to wait after the close request before terminating; default 10 seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<HwinfoRestartResult> RestartAsync(TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default)
    {
        var status = Detect();
        if (!status.IsRunning)
        {
            return HwinfoRestartResult.NotRunning;
        }

        if (status.ExecutablePath is null)
        {
            _logger.LogWarning("HWiNFO is running but its executable path is unknown; not restarting.");
            return HwinfoRestartResult.ExecutableNotFound;
        }

        var grace = gracePeriod ?? TimeSpan.FromSeconds(10);
        foreach (var process in _system.GetProcesses(ProcessName))
        {
            if (_system.RequestClose(process.Id) &&
                await _system.WaitForExitAsync(process.Id, grace, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            _logger.LogInformation("HWiNFO (pid {Pid}) did not close gracefully; terminating it.", process.Id);
            if (!_system.Kill(process.Id) ||
                !await _system.WaitForExitAsync(process.Id, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning("Could not close HWiNFO (pid {Pid}).", process.Id);
                return HwinfoRestartResult.CouldNotClose;
            }
        }

        if (!_system.Start(status.ExecutablePath))
        {
            _logger.LogWarning("Could not start HWiNFO from {Path}.", status.ExecutablePath);
            return HwinfoRestartResult.RelaunchFailed;
        }

        _logger.LogInformation("Restarted HWiNFO from {Path}.", status.ExecutablePath);
        return HwinfoRestartResult.Restarted;
    }

    /// <summary>Normalizes a name to lower-case letters and digits ("HWiNFO® 64_is1" → "hwinfo64is1").</summary>
    /// <param name="name">The name.</param>
    internal static string Normalize(string? name) =>
        new((name ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Extracts a path from a <c>DisplayIcon</c> value such as <c>"C:\x\HWiNFO64.exe",0</c>.</summary>
    /// <param name="value">The value.</param>
    internal static string? ParseIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _))
        {
            text = text[..comma];
        }

        return text.Trim();
    }

    private static bool IsHwinfoName(string? name)
    {
        var n = Normalize(name);
        return n.StartsWith("hwinfo", StringComparison.Ordinal) && !n.StartsWith("hwinfo32", StringComparison.Ordinal);
    }

    private static bool IsHwinfo64Exe(string path) =>
        Path.GetFileName(path).Equals(ExecutableName, StringComparison.OrdinalIgnoreCase);

    private (bool EntryFound, string? Path) FindInUninstallEntries()
    {
        var entryFound = false;
        foreach (var root in UninstallRoots)
        {
            foreach (var sub in _system.GetSubKeyNames(root, UninstallKeyPath))
            {
                var keyPath = $@"{UninstallKeyPath}\{sub}";
                if (!IsHwinfoName(sub) && !IsHwinfoName(_system.GetString(root, keyPath, "DisplayName")))
                {
                    continue;
                }

                entryFound = true;
                var icon = ParseIconPath(_system.GetString(root, keyPath, "DisplayIcon"));
                if (icon is not null && IsHwinfo64Exe(icon) && _system.FileExists(icon))
                {
                    return (true, icon);
                }

                var location = _system.GetString(root, keyPath, "InstallLocation")?.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(location))
                {
                    var candidate = Path.Combine(location, ExecutableName);
                    if (_system.FileExists(candidate))
                    {
                        return (true, candidate);
                    }
                }
            }
        }

        return (entryFound, null);
    }

    private string? FindInUserSettings() =>
        _system.GetValueNames(RegistryRoot.CurrentUser, UserSettingsKeyPath)
            .Select(name => _system.GetString(RegistryRoot.CurrentUser, UserSettingsKeyPath, name)?.Trim().Trim('"'))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && IsHwinfo64Exe(v) && _system.FileExists(v));

    private bool HasUserSettingsEvidence() =>
        _system.GetValueNames(RegistryRoot.CurrentUser, UserSettingsKeyPath).Count > 0 ||
        _system.GetSubKeyNames(RegistryRoot.CurrentUser, UserSettingsKeyPath)
            .Any(k => !k.Equals("Sensors", StringComparison.OrdinalIgnoreCase));
}
