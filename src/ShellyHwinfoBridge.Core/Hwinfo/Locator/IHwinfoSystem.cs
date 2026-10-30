namespace ShellyHwinfoBridge.Core.Hwinfo.Locator;

/// <summary>A registry hive and view to read from.</summary>
public enum RegistryRoot
{
    /// <summary><c>HKEY_CURRENT_USER</c>.</summary>
    CurrentUser,

    /// <summary><c>HKEY_LOCAL_MACHINE</c>, 64-bit view.</summary>
    LocalMachine64,

    /// <summary><c>HKEY_LOCAL_MACHINE</c>, 32-bit view (<c>WOW6432Node</c>).</summary>
    LocalMachine32,
}

/// <summary>A running process.</summary>
/// <param name="Id">The process id.</param>
/// <param name="ExecutablePath">The full image path, or <c>null</c> if it could not be read.</param>
public sealed record RunningProcess(int Id, string? ExecutablePath);

/// <summary>
/// The operating-system access <see cref="HwinfoLocator"/> needs: read-only registry, file existence and process
/// control. Abstracted so path resolution and the restart sequence can be tested without touching the system.
/// </summary>
public interface IHwinfoSystem
{
    /// <summary>Returns the subkey names of a key, or an empty list when the key does not exist.</summary>
    /// <param name="root">The hive and view.</param>
    /// <param name="path">The key path.</param>
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path);

    /// <summary>Returns the value names of a key, or an empty list when the key does not exist.</summary>
    /// <param name="root">The hive and view.</param>
    /// <param name="path">The key path.</param>
    IReadOnlyList<string> GetValueNames(RegistryRoot root, string path);

    /// <summary>Reads a string value (REG_SZ or REG_EXPAND_SZ, expanded); <c>null</c> when missing.</summary>
    /// <param name="root">The hive and view.</param>
    /// <param name="path">The key path.</param>
    /// <param name="name">The value name.</param>
    string? GetString(RegistryRoot root, string path, string name);

    /// <summary>Returns whether a file exists.</summary>
    /// <param name="path">The file path.</param>
    bool FileExists(string path);

    /// <summary>Returns the running processes with the given name (without <c>.exe</c>).</summary>
    /// <param name="processName">The process name.</param>
    IReadOnlyList<RunningProcess> GetProcesses(string processName);

    /// <summary>Asks a process to close its main window; <c>false</c> if it has none or the request could not be sent.</summary>
    /// <param name="processId">The process id.</param>
    bool RequestClose(int processId);

    /// <summary>Waits for a process to exit; <c>true</c> if it exited (or no longer exists) within the timeout.</summary>
    /// <param name="processId">The process id.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Terminates a process; <c>false</c> if that was denied (e.g. it runs elevated and we do not).</summary>
    /// <param name="processId">The process id.</param>
    bool Kill(int processId);

    /// <summary>Starts an executable through the shell (so a UAC prompt can appear); <c>false</c> if it failed or was cancelled.</summary>
    /// <param name="executablePath">The executable.</param>
    bool Start(string executablePath);
}
