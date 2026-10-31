using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary><see cref="IRegistryBackend"/> over <c>HKEY_CURRENT_USER</c> (or, for reading, another root).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryBackend : IRegistryBackend
{
    private readonly RegistryKey _root;

    /// <summary>Creates a backend over <c>HKEY_CURRENT_USER</c>.</summary>
    public WindowsRegistryBackend()
        : this(Registry.CurrentUser)
    {
    }

    private WindowsRegistryBackend(RegistryKey root) => _root = root;

    /// <summary>
    /// Creates a backend over <c>HKEY_LOCAL_MACHINE</c>, for reading only (HWiNFO's gadget export can live there).
    /// </summary>
    public static WindowsRegistryBackend LocalMachine() => new(Registry.LocalMachine);

    /// <inheritdoc />
    public bool KeyExists(string path)
    {
        using var key = _root.OpenSubKey(path);
        return key is not null;
    }

    /// <inheritdoc />
    public void CreateKey(string path)
    {
        using var key = _root.CreateSubKey(path, writable: true);
    }

    /// <inheritdoc />
    public string? GetString(string path, string name)
    {
        using var key = _root.OpenSubKey(path);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return key.GetValueKind(name) == RegistryValueKind.String ? key.GetValue(name) as string : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetStringValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var key = _root.OpenSubKey(path);
        if (key is null)
        {
            return values;
        }

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValueKind(name) == RegistryValueKind.String && key.GetValue(name) is string value)
            {
                values[name] = value;
            }
        }

        return values;
    }

    /// <inheritdoc />
    public void SetString(string path, string name, string value)
    {
        using var key = _root.CreateSubKey(path, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void DeleteValue(string path, string name)
    {
        using var key = _root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public void DeleteTree(string path) => _root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
}
