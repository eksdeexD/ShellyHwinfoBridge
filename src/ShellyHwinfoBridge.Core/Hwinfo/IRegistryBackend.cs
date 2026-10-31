namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>
/// Minimal string-value registry access, so registry logic can be tested without the Windows registry.
/// Paths are relative to the backend's root key (HKEY_CURRENT_USER for the app's own keys).
/// </summary>
public interface IRegistryBackend
{
    /// <summary>Returns whether a key exists.</summary>
    /// <param name="path">Key path.</param>
    bool KeyExists(string path);

    /// <summary>Creates a key (and any missing parents).</summary>
    /// <param name="path">Key path.</param>
    void CreateKey(string path);

    /// <summary>Reads a REG_SZ value; returns <c>null</c> when missing or of another type.</summary>
    /// <param name="path">Key path.</param>
    /// <param name="name">Value name.</param>
    string? GetString(string path, string name);

    /// <summary>Reads all REG_SZ values of a key at once (names compare case-insensitively); empty when the key is missing.</summary>
    /// <param name="path">Key path.</param>
    IReadOnlyDictionary<string, string> GetStringValues(string path);

    /// <summary>Writes a REG_SZ value, creating the key if needed.</summary>
    /// <param name="path">Key path.</param>
    /// <param name="name">Value name.</param>
    /// <param name="value">Value data.</param>
    void SetString(string path, string name, string value);

    /// <summary>Deletes a value; does nothing if the key or value does not exist.</summary>
    /// <param name="path">Key path.</param>
    /// <param name="name">Value name.</param>
    void DeleteValue(string path, string name);

    /// <summary>Deletes a key and all its subkeys; does nothing if it does not exist.</summary>
    /// <param name="path">Key path.</param>
    void DeleteTree(string path);
}
