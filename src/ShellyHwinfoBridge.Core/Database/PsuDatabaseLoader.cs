using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>Where a loaded database came from.</summary>
public enum DatabaseOrigin
{
    /// <summary>The local <c>psu-database.json</c> file.</summary>
    LocalFile,

    /// <summary>The copy embedded in the application.</summary>
    Embedded,
}

/// <summary>A loaded database and its origin.</summary>
/// <param name="Database">The database.</param>
/// <param name="Origin">Where it was loaded from.</param>
public sealed record LoadedDatabase(PsuDatabase Database, DatabaseOrigin Origin);

/// <summary>The result of trying to replace the local database file.</summary>
/// <param name="Replaced">Whether the local file was replaced.</param>
/// <param name="Database">The new database when replaced.</param>
/// <param name="Errors">Validation or I/O errors when not replaced.</param>
public sealed record DatabaseReplaceResult(bool Replaced, PsuDatabase? Database, IReadOnlyList<DatabaseValidationError> Errors);

/// <summary>
/// Loads <c>psu-database.json</c> from next to the executable, falling back to the embedded copy when the file is
/// missing or invalid, and safely replaces the local file with validated new content.
/// </summary>
public sealed class PsuDatabaseLoader
{
    /// <summary>The database file name.</summary>
    public const string FileName = "psu-database.json";

    private const string ResourceName = "ShellyHwinfoBridge.Core.psu-database.json";

    private readonly ILogger _logger;

    /// <summary>Creates a loader.</summary>
    /// <param name="localPath">Path of the local database file.</param>
    /// <param name="logger">Optional logger.</param>
    public PsuDatabaseLoader(string localPath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        LocalPath = localPath;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the default local path: next to the executable.</summary>
    public static string DefaultLocalPath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Gets the local database file path.</summary>
    public string LocalPath { get; }

    /// <summary>Loads the local database, or the embedded copy when the local file is missing or invalid.</summary>
    public LoadedDatabase Load()
    {
        if (File.Exists(LocalPath))
        {
            try
            {
                var errors = PsuDatabaseValidator.ValidateJson(File.ReadAllText(LocalPath), out var db);
                if (db is not null)
                {
                    return new LoadedDatabase(db, DatabaseOrigin.LocalFile);
                }

                _logger.LogWarning("Local PSU database {Path} is invalid ({Errors}); using embedded copy.", LocalPath, string.Join("; ", errors));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not read local PSU database {Path}; using embedded copy.", LocalPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Could not read local PSU database {Path}; using embedded copy.", LocalPath);
            }
        }

        return new LoadedDatabase(LoadEmbedded(), DatabaseOrigin.Embedded);
    }

    /// <summary>Loads the database embedded in the Core assembly.</summary>
    public static PsuDatabase LoadEmbedded()
    {
        using var stream = typeof(PsuDatabaseLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var errors = PsuDatabaseValidator.ValidateJson(reader.ReadToEnd(), out var db);
        return db ?? throw new InvalidOperationException("Embedded PSU database is invalid: " + string.Join("; ", errors));
    }

    /// <summary>
    /// Validates <paramref name="newJson"/> and, only if it is valid, atomically replaces the local file.
    /// The existing file is left untouched when validation or writing fails.
    /// </summary>
    /// <param name="newJson">The candidate database JSON (e.g. a downloaded update).</param>
    public DatabaseReplaceResult TryReplace(string? newJson)
    {
        var errors = PsuDatabaseValidator.ValidateJson(newJson, out var db);
        if (db is null)
        {
            _logger.LogWarning("Rejected PSU database update: {Errors}", string.Join("; ", errors));
            return new DatabaseReplaceResult(false, null, errors);
        }

        var tempPath = LocalPath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(LocalPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, newJson);
            File.Move(tempPath, LocalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            _logger.LogError(ex, "Could not write PSU database to {Path}.", LocalPath);
            return new DatabaseReplaceResult(false, null, [new DatabaseValidationError(string.Empty, $"Could not write file: {ex.Message}")]);
        }

        _logger.LogInformation("PSU database replaced ({Count} entries, updated {Updated}).", db.Psus.Count, db.Updated);
        return new DatabaseReplaceResult(true, db, []);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup of the temporary file.
        }
    }
}
