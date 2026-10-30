using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShellyHwinfoBridge.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON and handles the protected password.</summary>
public sealed class SettingsStore
{
    /// <summary>The JSON options of the settings file (also used by <see cref="AppSettings.Clone"/>).</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IPasswordProtector _protector;

    /// <summary>Creates a store.</summary>
    /// <param name="filePath">The settings file path.</param>
    /// <param name="protector">Password protection.</param>
    public SettingsStore(string filePath, IPasswordProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>Gets the settings file path.</summary>
    public string FilePath { get; }

    /// <summary>Gets whether the settings file exists (false on first run).</summary>
    public bool Exists => File.Exists(FilePath);

    /// <summary>Creates the default store: <c>%APPDATA%\ShellyHwinfoBridge\settings.json</c> with DPAPI.</summary>
    [SupportedOSPlatform("windows")]
    public static SettingsStore CreateDefault() => new(AppPaths.SettingsFile, new DpapiPasswordProtector());

    /// <summary>Loads and normalizes the settings, or returns <c>null</c> when the file does not exist.</summary>
    /// <exception cref="InvalidDataException">The file exists but cannot be parsed.</exception>
    public AppSettings? Load()
    {
        if (!Exists)
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                ?? throw new InvalidDataException("The settings file is empty.");
            settings.Normalize();
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The settings file {FilePath} is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>Normalizes and saves the settings atomically.</summary>
    /// <param name="settings">The settings.</param>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>Returns the decrypted device password, or <c>null</c> when none is set or it cannot be decrypted.</summary>
    /// <param name="settings">The settings.</param>
    public string? GetPassword(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = settings.Device.ProtectedPassword;
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Encrypts and stores the device password; <c>null</c> or empty clears it.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="password">The password.</param>
    public void SetPassword(AppSettings settings, string? password)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Device.ProtectedPassword = string.IsNullOrEmpty(password) ? null : _protector.Protect(password);
    }
}
