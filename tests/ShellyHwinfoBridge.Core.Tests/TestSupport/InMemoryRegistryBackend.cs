using ShellyHwinfoBridge.Core.Hwinfo;

namespace ShellyHwinfoBridge.Core.Tests.TestSupport;

internal sealed class InMemoryRegistryBackend : IRegistryBackend
{
    public Dictionary<string, Dictionary<string, string>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool KeyExists(string path) => Keys.ContainsKey(path);

    public void CreateKey(string path)
    {
        if (!Keys.ContainsKey(path))
        {
            Keys[path] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? GetString(string path, string name) =>
        Keys.TryGetValue(path, out var values) && values.TryGetValue(name, out var v) ? v : null;

    public IReadOnlyDictionary<string, string> GetStringValues(string path) =>
        Keys.TryGetValue(path, out var values)
            ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void SetString(string path, string name, string value)
    {
        CreateKey(path);
        Keys[path][name] = value;
    }

    public void DeleteValue(string path, string name)
    {
        if (Keys.TryGetValue(path, out var values))
        {
            values.Remove(name);
        }
    }

    public void DeleteTree(string path)
    {
        foreach (var key in Keys.Keys.Where(k => k.Equals(path, StringComparison.OrdinalIgnoreCase) || k.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Keys.Remove(key);
        }
    }
}
