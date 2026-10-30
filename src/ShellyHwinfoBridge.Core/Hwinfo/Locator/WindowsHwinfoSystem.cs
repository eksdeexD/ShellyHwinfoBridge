using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ShellyHwinfoBridge.Core.Hwinfo.Locator;

/// <summary><see cref="IHwinfoSystem"/> over the Windows registry, file system and processes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHwinfoSystem : IHwinfoSystem
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorCancelled = 1223;

    /// <inheritdoc />
    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path)
    {
        using var key = Open(root, path);
        return key?.GetSubKeyNames() ?? [];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path)
    {
        using var key = Open(root, path);
        return key?.GetValueNames() ?? [];
    }

    /// <inheritdoc />
    public string? GetString(RegistryRoot root, string path, string name)
    {
        using var key = Open(root, path);
        return key?.GetValue(name) as string;
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public IReadOnlyList<RunningProcess> GetProcesses(string processName)
    {
        var result = new List<RunningProcess>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                result.Add(new RunningProcess(process.Id, TryGetImagePath(process.Id)));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public bool RequestClose(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.CloseMainWindow();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true; // Already gone.
        }

        using (process)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // No SYNCHRONIZE access to an elevated process: poll for its disappearance instead.
                return await PollForExitAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public bool Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            return true;
        }
        catch (ArgumentException)
        {
            return true; // Already gone.
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool Start(string executablePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false; // UAC prompt declined.
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static RegistryKey? Open(RegistryRoot root, string path)
    {
        var (hive, view) = root switch
        {
            RegistryRoot.LocalMachine64 => (RegistryHive.LocalMachine, RegistryView.Registry64),
            RegistryRoot.LocalMachine32 => (RegistryHive.LocalMachine, RegistryView.Registry32),
            _ => (RegistryHive.CurrentUser, RegistryView.Default),
        };
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            return baseKey.OpenSubKey(path);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static async Task<bool> PollForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static string? TryGetImagePath(int processId)
    {
        // QueryFullProcessImageName with limited rights also works for elevated processes, unlike Process.MainModule.
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[32768];
        var size = buffer.Length;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, int flags, [Out] char[] buffer, ref int size);
}
