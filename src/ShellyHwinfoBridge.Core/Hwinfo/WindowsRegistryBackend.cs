using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary><see cref="IRegistryBackend"/> over <c>HKEY_CURRENT_USER</c>.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryBackend : IRegistryBackend
{
    /// <inheritdoc />
    public bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }

    /// <inheritdoc />
    public void CreateKey(string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
    }

    /// <inheritdoc />
    public string? GetString(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
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
        using var key = Registry.CurrentUser.OpenSubKey(path);
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
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void DeleteValue(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public void DeleteTree(string path) => Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
}
