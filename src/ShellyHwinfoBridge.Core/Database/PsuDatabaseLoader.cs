using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>Where a loaded database came from.</summary>
public enum DatabaseOrigin
{
    /// <summary>The <c>psu-database.json</c> shipped next to the executable (installed with the app).</summary>
    Shipped,

    /// <summary>The copy downloaded by "Check for database update" (<c>%APPDATA%\ShellyHwinfoBridge\psu-database.json</c>).</summary>
    Downloaded,

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
/// Loads the PSU database and safely replaces the writable copy with validated new content (SPEC "Database updates
/// under Program Files"). Candidates, in order of preference:
/// <list type="number">
/// <item>the downloaded copy (<see cref="DownloadedPath"/>, normally <c>%APPDATA%\ShellyHwinfoBridge\psu-database.json</c>)
/// when it exists, is valid and is newer than the copy that would be used otherwise (see <see cref="IsNewer"/>);</item>
/// <item>the shipped copy next to the executable (<see cref="LocalPath"/>, read-only under Program Files) when valid;</item>
/// <item>the copy embedded in the Core assembly.</item>
/// </list>
/// </summary>
public sealed class PsuDatabaseLoader
{
    /// <summary>The database file name.</summary>
    public const string FileName = "psu-database.json";

    private const string ResourceName = "ShellyHwinfoBridge.Core.psu-database.json";

    private readonly ILogger _logger;

    /// <summary>Creates a loader without a separate downloaded copy: updates replace <paramref name="localPath"/>.</summary>
    /// <param name="localPath">Path of the local (shipped) database file.</param>
    /// <param name="logger">Optional logger.</param>
    public PsuDatabaseLoader(string localPath, ILogger? logger = null)
        : this(localPath, null, logger)
    {
    }

    /// <summary>Creates a loader with a shipped and a downloaded copy.</summary>
    /// <param name="localPath">Path of the shipped database file (next to the executable).</param>
    /// <param name="downloadedPath">
    /// Path the updates are written to (<see cref="DefaultDownloadedPath"/>); <c>null</c> writes updates to
    /// <paramref name="localPath"/>.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public PsuDatabaseLoader(string localPath, string? downloadedPath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        LocalPath = localPath;
        DownloadedPath = string.IsNullOrWhiteSpace(downloadedPath) ? null : downloadedPath;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the default shipped path: next to the executable.</summary>
    public static string DefaultLocalPath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Gets the default downloaded path: <see cref="AppPaths.DownloadedDatabaseFile"/>.</summary>
    public static string DefaultDownloadedPath => AppPaths.DownloadedDatabaseFile;

    /// <summary>Gets the shipped database file path.</summary>
    public string LocalPath { get; }

    /// <summary>Gets the downloaded database file path, or <c>null</c> when updates replace <see cref="LocalPath"/>.</summary>
    public string? DownloadedPath { get; }

    /// <summary>Gets the file <see cref="TryReplace"/> writes to.</summary>
    public string WritePath => DownloadedPath ?? LocalPath;

    /// <summary>Gets the origin of a database written by <see cref="TryReplace"/>.</summary>
    public DatabaseOrigin WriteOrigin => DownloadedPath is null ? DatabaseOrigin.Shipped : DatabaseOrigin.Downloaded;

    /// <summary>
    /// Returns whether <paramref name="candidate"/> is newer than <paramref name="baseline"/>: a later <c>updated</c>
    /// date (yyyy-MM-dd) when both have one; with equal or missing dates, more entries.
    /// </summary>
    /// <param name="candidate">The candidate (the downloaded copy).</param>
    /// <param name="baseline">The copy used otherwise (shipped or embedded).</param>
    public static bool IsNewer(PsuDatabase candidate, PsuDatabase baseline)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baseline);
        if (DatabaseUpdater.TryParseDate(candidate.Updated, out var a) && DatabaseUpdater.TryParseDate(baseline.Updated, out var b) && a != b)
        {
            return a > b;
        }

        return candidate.Psus.Count > baseline.Psus.Count;
    }

    /// <summary>
    /// Loads the database: the downloaded copy when it is valid and newer than the shipped one, else the shipped copy,
    /// else the embedded copy (a missing or invalid file is skipped).
    /// </summary>
    public LoadedDatabase Load()
    {
        var fallback = TryRead(LocalPath, "shipped") is { } shipped
            ? new LoadedDatabase(shipped, DatabaseOrigin.Shipped)
            : null;
        if (DownloadedPath is not null && TryRead(DownloadedPath, "downloaded") is { } downloaded)
        {
            fallback ??= new LoadedDatabase(LoadEmbedded(), DatabaseOrigin.Embedded);
            if (IsNewer(downloaded, fallback.Database))
            {
                return new LoadedDatabase(downloaded, DatabaseOrigin.Downloaded);
            }

            _logger.LogInformation(
                "Downloaded PSU database ({Updated}, {Count} entries) is not newer than the {Origin} copy; using that.",
                downloaded.Updated,
                downloaded.Psus.Count,
                fallback.Origin.ToString().ToLowerInvariant());
            return fallback;
        }

        return fallback ?? new LoadedDatabase(LoadEmbedded(), DatabaseOrigin.Embedded);
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
    /// Validates <paramref name="newJson"/> and, only if it is valid, atomically replaces the file at
    /// <see cref="WritePath"/> (the downloaded copy). The existing file is left untouched when validation or writing fails.
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

        var target = WritePath;
        var tempPath = target + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, newJson);
            File.Move(tempPath, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            _logger.LogError(ex, "Could not write PSU database to {Path}.", target);
            return new DatabaseReplaceResult(false, null, [new DatabaseValidationError(string.Empty, $"Could not write file: {ex.Message}")]);
        }

        _logger.LogInformation("PSU database written to {Path} ({Count} entries, updated {Updated}).", target, db.Psus.Count, db.Updated);
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

    private PsuDatabase? TryRead(string path, string what)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var errors = PsuDatabaseValidator.ValidateJson(File.ReadAllText(path), out var db);
            if (db is null)
            {
                _logger.LogWarning("The {What} PSU database {Path} is invalid ({Errors}); skipping it.", what, path, string.Join("; ", errors));
            }

            return db;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the {What} PSU database {Path}; skipping it.", what, path);
            return null;
        }
    }
}
