using System.Net;

namespace CybeneticsImport;

/// <summary>The outcome of a download run.</summary>
/// <param name="Downloaded">Files written.</param>
/// <param name="Skipped">Rows skipped because the PDF already exists or the row has no <c>pdfUrl</c>.</param>
/// <param name="Failed">Rows that failed.</param>
/// <param name="Requests">HTTP requests made.</param>
/// <param name="Stopped">Whether the run stopped early because the server refused or rate-limited a request.</param>
public sealed record DownloadSummary(int Downloaded, int Skipped, int Failed, int Requests, bool Stopped);

/// <summary>
/// Downloads the report PDFs of a catalogue, politely: one request at a time, at least
/// <see cref="MinInterval"/> between requests, a descriptive User-Agent, only the URLs listed in the catalogue, and
/// never a file that already exists.
/// </summary>
public sealed class ReportDownloader : IDisposable
{
    /// <summary>The User-Agent sent with every request.</summary>
    public const string UserAgent = "ShellyHwinfoBridge-import/0.1 (+https://github.com/eksdeexD/ShellyHwinfoBridge)";

    /// <summary>The minimum time between two requests.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _client;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Creates a downloader.</summary>
    /// <param name="handler">The HTTP handler (a fake one in tests).</param>
    /// <param name="delay">Waits between requests (<see cref="Task.Delay(TimeSpan, CancellationToken)"/> by default).</param>
    public ReportDownloader(HttpMessageHandler handler, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _delay = delay ?? Task.Delay;
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>Downloads every missing <c>&lt;reportId&gt;.pdf</c> from the rows' <c>pdfUrl</c>.</summary>
    /// <param name="rows">The catalogue rows.</param>
    /// <param name="reportsDir">The directory the PDFs are stored in.</param>
    /// <param name="log">Progress and errors.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task<DownloadSummary> DownloadAsync(IReadOnlyList<CatalogRow> rows, string reportsDir, TextWriter log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(reportsDir);
        ArgumentNullException.ThrowIfNull(log);
        Directory.CreateDirectory(reportsDir);

        int downloaded = 0, skipped = 0, failed = 0, requests = 0;
        foreach (var row in rows)
        {
            var reportId = row.ReportId;
            var pdfUrl = row["pdfUrl"];
            if (!Catalog.IsSafeFileName(reportId))
            {
                log.WriteLine($"error: catalogue line {row.LineNumber}: reportId '{reportId}' is not a safe file name.");
                failed++;
                continue;
            }

            var target = Path.Combine(reportsDir, reportId + ".pdf");
            if (File.Exists(target))
            {
                log.WriteLine($"{reportId}: already present, skipped.");
                skipped++;
                continue;
            }

            if (pdfUrl.Length == 0)
            {
                log.WriteLine($"{reportId}: no pdfUrl, skipped.");
                skipped++;
                continue;
            }

            if (!Uri.TryCreate(pdfUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                log.WriteLine($"error: catalogue line {row.LineNumber}: pdfUrl must be an absolute https URL.");
                failed++;
                continue;
            }

            if (requests > 0)
            {
                await _delay(MinInterval, cancellationToken).ConfigureAwait(false);
            }

            requests++;
            try
            {
                using var response = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized)
                {
                    log.WriteLine($"error: {reportId}: the server answered {(int)response.StatusCode} {response.StatusCode}; stopping without further requests.");
                    return new DownloadSummary(downloaded, skipped, failed + 1, requests, true);
                }

                if (!response.IsSuccessStatusCode)
                {
                    log.WriteLine($"error: {reportId}: HTTP {(int)response.StatusCode} {response.StatusCode}.");
                    failed++;
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F' || bytes[4] != '-')
                {
                    log.WriteLine($"error: {reportId}: the response is not a PDF; nothing saved.");
                    failed++;
                    continue;
                }

                var partial = target + ".part";
                await File.WriteAllBytesAsync(partial, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(partial, target, overwrite: false);
                log.WriteLine($"{reportId}: saved {bytes.Length} bytes.");
                downloaded++;
            }
            catch (Exception ex) when ((ex is HttpRequestException or IOException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                log.WriteLine($"error: {reportId}: {ex.Message}");
                failed++;
            }
        }

        return new DownloadSummary(downloaded, skipped, failed, requests, false);
    }
}

/// <summary><c>download --catalog &lt;csv&gt; --reports &lt;dir&gt;</c>: fetches missing report PDFs.</summary>
public static class DownloadCommand
{
    /// <summary>Runs the download with a real HTTP handler.</summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="log">Progress and errors.</param>
    internal static int Run(OptionSet options, TextWriter log)
    {
        var rows = Catalog.Load(options.Require("catalog"));
        var reports = options.Require("reports");
        using var downloader = new ReportDownloader(new SocketsHttpHandler());
        var summary = downloader.DownloadAsync(rows, reports, log).GetAwaiter().GetResult();
        log.WriteLine($"{summary.Downloaded} downloaded, {summary.Skipped} skipped, {summary.Failed} failed, {summary.Requests} requests{(summary.Stopped ? "; stopped early" : string.Empty)}.");
        return summary.Failed == 0 ? ImportCommand.Ok : ImportCommand.Failed;
    }
}
