using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>The outcome of a database update check.</summary>
public enum DatabaseUpdateStatus
{
    /// <summary>A newer, valid database was downloaded and written to the downloaded copy (<see cref="PsuDatabaseLoader.WritePath"/>).</summary>
    Updated,

    /// <summary>The downloaded database is not newer than the current one (by its <c>updated</c> date); nothing changed.</summary>
    AlreadyCurrent,

    /// <summary>The downloaded file failed validation, or the local file could not be written; nothing changed.</summary>
    Invalid,

    /// <summary>The download failed (network error, timeout or HTTP error status); nothing changed.</summary>
    NetworkError,
}

/// <summary>The result of <see cref="DatabaseUpdater.CheckForUpdateAsync"/>.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Message">A short user-facing description.</param>
/// <param name="Database">The database now in effect when <see cref="DatabaseUpdateStatus.Updated"/>; otherwise <c>null</c>.</param>
/// <param name="Errors">Validation errors when <see cref="DatabaseUpdateStatus.Invalid"/>.</param>
public sealed record DatabaseUpdateResult(
    DatabaseUpdateStatus Status,
    string Message,
    PsuDatabase? Database,
    IReadOnlyList<DatabaseValidationError> Errors);

/// <summary>
/// Implements "Check for database update": downloads <c>psu-database.json</c> from a URL, validates it and, only if it
/// is valid and newer than the current database, writes it to
/// <c>%APPDATA%\ShellyHwinfoBridge\psu-database.json</c> through <see cref="PsuDatabaseLoader.TryReplace"/>. Runs only
/// on demand; there are no background updates.
/// </summary>
public sealed class DatabaseUpdater : IDisposable
{
    /// <summary>The maximum accepted download size.</summary>
    public const int MaxDownloadBytes = 16 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly PsuDatabaseLoader _loader;
    private readonly ILogger _logger;

    /// <summary>Creates an updater.</summary>
    /// <param name="loader">The loader that owns the database files.</param>
    /// <param name="handler">Optional HTTP handler (tests inject canned responses); a default handler otherwise.</param>
    /// <param name="logger">Optional logger.</param>
    public DatabaseUpdater(PsuDatabaseLoader loader, HttpMessageHandler? handler = null, ILogger? logger = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _logger = logger ?? NullLogger.Instance;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.MaxResponseContentBufferSize = MaxDownloadBytes;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.AppName}/{typeof(DatabaseUpdater).Assembly.GetName().Version?.ToString(3) ?? "0"}");
    }

    /// <summary>
    /// Downloads and, if appropriate, installs a database update.
    /// </summary>
    /// <param name="url">The update URL (see <c>AppSettings.GetEffectiveDatabaseUpdateUrl</c>).</param>
    /// <param name="current">The database currently in use; when <c>null</c> it is loaded through the loader.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DatabaseUpdateResult> CheckForUpdateAsync(string url, PsuDatabase? current = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return Fail(DatabaseUpdateStatus.NetworkError, $"'{url}' is not a valid http(s) URL.");
        }

        string json;
        try
        {
            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Fail(DatabaseUpdateStatus.NetworkError, $"The server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return Fail(DatabaseUpdateStatus.NetworkError, $"Download failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(DatabaseUpdateStatus.NetworkError, "Download timed out.");
        }

        var errors = PsuDatabaseValidator.ValidateJson(json, out var downloaded);
        if (downloaded is null)
        {
            _logger.LogWarning("Rejected PSU database update from {Url}: {Errors}", uri, string.Join("; ", errors));
            return new DatabaseUpdateResult(DatabaseUpdateStatus.Invalid, "The downloaded database is invalid; the current one was kept.", null, errors);
        }

        if (!TryParseDate(downloaded.Updated, out var downloadedDate))
        {
            var error = new DatabaseValidationError("updated", "Missing or not a yyyy-MM-dd date.");
            return new DatabaseUpdateResult(DatabaseUpdateStatus.Invalid, "The downloaded database has no valid 'updated' date; the current one was kept.", null, [error]);
        }

        current ??= _loader.Load().Database;
        if (TryParseDate(current.Updated, out var currentDate) && downloadedDate <= currentDate)
        {
            _logger.LogInformation("PSU database is up to date ({Updated}).", current.Updated);
            return new DatabaseUpdateResult(DatabaseUpdateStatus.AlreadyCurrent, $"The database is already up to date ({current.Updated}).", null, []);
        }

        var replace = _loader.TryReplace(json);
        if (!replace.Replaced)
        {
            return new DatabaseUpdateResult(DatabaseUpdateStatus.Invalid, "The update could not be installed; the current database was kept.", null, replace.Errors);
        }

        _logger.LogInformation("PSU database updated from {Url} to {Updated}.", uri, downloaded.Updated);
        return new DatabaseUpdateResult(
            DatabaseUpdateStatus.Updated,
            $"Updated to the database of {downloaded.Updated} ({downloaded.Psus.Count} PSUs).",
            replace.Database,
            []);
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>Parses an ISO <c>yyyy-MM-dd</c> date.</summary>
    /// <param name="text">The text.</param>
    /// <param name="date">The parsed date.</param>
    internal static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private DatabaseUpdateResult Fail(DatabaseUpdateStatus status, string message)
    {
        _logger.LogWarning("PSU database update failed: {Message}", message);
        return new DatabaseUpdateResult(status, message, null, []);
    }
}
