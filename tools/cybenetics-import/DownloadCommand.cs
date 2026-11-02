using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CybeneticsImport;

/// <summary>The outcome of a download run.</summary>
/// <param name="Downloaded">Pages written.</param>
/// <param name="Skipped">Rows skipped because the page is already saved.</param>
/// <param name="Failed">Rows that failed (bad report id or URL, or the request that stopped the run).</param>
/// <param name="Requests">HTTP requests made.</param>
/// <param name="Stopped">Whether the run stopped early (daily cap, refused request, challenge page or network error).</param>
/// <param name="StopReason">Why it stopped, or <c>null</c>.</param>
/// <param name="CapReached">Whether it stopped because the daily cap was reached (not an error: rerun the next day).</param>
public sealed record DownloadSummary(int Downloaded, int Skipped, int Failed, int Requests, bool Stopped, string? StopReason = null, bool CapReached = false);

/// <summary>
/// Downloads the report pages of a catalogue within Cybenetics' written permission of 2026-11-02: only the
/// <c>reportUrl</c> of each listed row (the public report page, saved as <c>&lt;reportId&gt;.htm</c>), one request at a
/// time with at least <see cref="MinInterval"/> between requests (also across runs), the <see cref="UserAgent"/>
/// naming the tool and the contact address, at most a daily number of pages, never a page that is already saved, and
/// no retries: anything but a report page stops the run.
/// </summary>
public sealed class ReportDownloader : IDisposable
{
    /// <summary>The User-Agent sent with every request.</summary>
    public const string UserAgent = "ShellyHwinfoBridge-import/1.0 (+https://github.com/eksdeexD/ShellyHwinfoBridge; zapador@zapador.net)";

    /// <summary>The default for the number of report pages fetched per calendar day.</summary>
    public const int DefaultMaxPerDay = 150;

    /// <summary>The default number of report pages fetched in one batch before <see cref="DefaultBatchPause"/>.</summary>
    public const int DefaultBatchSize = 50;

    /// <summary>The default pause after each full batch.</summary>
    public static readonly TimeSpan DefaultBatchPause = TimeSpan.FromMinutes(15);

    /// <summary>The state file in the reports directory: pages fetched per date, and the time of the last request.</summary>
    public const string StateFileName = "download-state.json";

    /// <summary>The minimum time between two requests.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates a downloader.</summary>
    /// <param name="handler">The HTTP handler (a fake one in tests).</param>
    /// <param name="delay">Waits between requests (<see cref="Task.Delay(TimeSpan, CancellationToken)"/> by default).</param>
    /// <param name="pause">Waits between batches (<see cref="Task.Delay(TimeSpan, CancellationToken)"/> by default).</param>
    /// <param name="now">The current local time (<see cref="DateTimeOffset.Now"/> by default); its date is the calendar day of the cap.</param>
    public ReportDownloader(HttpMessageHandler handler, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _delay = delay ?? Task.Delay;
        _pause = pause ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>Downloads every missing <c>&lt;reportId&gt;.htm</c> from the rows' <c>reportUrl</c>.</summary>
    /// <param name="rows">The catalogue rows.</param>
    /// <param name="reportsDir">The directory the pages (and <see cref="StateFileName"/>) are stored in.</param>
    /// <param name="log">Progress and errors.</param>
    /// <param name="maxPerDay">At most this many requests per calendar day, counted across runs.</param>
    /// <param name="batchSize">Requests per batch; after each full batch the run pauses for <paramref name="batchPause"/>.</param>
    /// <param name="batchPause">The pause between batches (<see cref="DefaultBatchPause"/> when <c>null</c>).</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task<DownloadSummary> DownloadAsync(
        IReadOnlyList<CatalogRow> rows,
        string reportsDir,
        TextWriter log,
        int maxPerDay = DefaultMaxPerDay,
        int batchSize = DefaultBatchSize,
        TimeSpan? batchPause = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(reportsDir);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var pauseLength = batchPause ?? DefaultBatchPause;
        ArgumentOutOfRangeException.ThrowIfLessThan(pauseLength, TimeSpan.Zero, nameof(batchPause));
        Directory.CreateDirectory(reportsDir);
        var statePath = Path.Combine(reportsDir, StateFileName);
        var state = DownloadState.Load(statePath);

        int downloaded = 0, skipped = 0, failed = 0, requests = 0, inBatch = 0;
        DownloadSummary Stop(string reason, int extraFailed = 0, bool cap = false)
        {
            log.WriteLine("stopped: " + reason);
            return new DownloadSummary(downloaded, skipped, failed + extraFailed, requests, true, reason, cap);
        }

        foreach (var row in rows)
        {
            var reportId = row.ReportId;
            if (!Catalog.IsSafeFileName(reportId))
            {
                log.WriteLine($"error: catalogue line {row.LineNumber}: reportId '{reportId}' is not a safe file name.");
                failed++;
                continue;
            }

            var target = Path.Combine(reportsDir, reportId + ".htm");
            if (File.Exists(target) || File.Exists(Path.Combine(reportsDir, reportId + ".html")))
            {
                log.WriteLine($"{reportId}: already saved, skipped.");
                skipped++;
                continue;
            }

            if (!Uri.TryCreate(row["reportUrl"], UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                log.WriteLine($"error: catalogue line {row.LineNumber}: reportUrl must be an absolute https URL.");
                failed++;
                continue;
            }

            var day = DateOnly.FromDateTime(_now().DateTime);
            if (state.CountFor(day) >= maxPerDay)
            {
                return Stop($"the daily cap of {maxPerDay} report pages is reached for {Iso(day)}; the next allowed day is {Iso(day.AddDays(1))}.", cap: true);
            }

            // A full batch is followed by a pause; the cap above and the interval below still apply after it.
            if (inBatch >= batchSize)
            {
                log.WriteLine($"batch of {batchSize} done; pausing {pauseLength.TotalMinutes:0.#} min, resuming at {_now().Add(pauseLength).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}.");
                await _pause(pauseLength, cancellationToken).ConfigureAwait(false);
                inBatch = 0;
            }

            // At least MinInterval since the previous request, including one made by an earlier run.
            var wait = state.LastRequest is { } last ? MinInterval - (_now() - last) : TimeSpan.Zero;
            if (wait > TimeSpan.Zero)
            {
                await _delay(wait > MinInterval ? MinInterval : wait, cancellationToken).ConfigureAwait(false);
            }

            requests++;
            inBatch++;
            state.Record(DateOnly.FromDateTime(_now().DateTime), _now());
            state.Save(statePath);
            string body;
            try
            {
                using var response = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return Stop($"{reportId}: the server answered {(int)response.StatusCode} {response.StatusCode} instead of the report page (a challenge, a block or a wrong URL); no retry, no further requests.", 1);
                }

                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is HttpRequestException or IOException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                return Stop($"{reportId}: {ex.Message}; no retry, no further requests.", 1);
            }

            if (!IsReportPage(body))
            {
                return Stop($"{reportId}: the response is not a report page (no report tables or \"Testing Date\"); probably a challenge page. Nothing saved, no retry, no further requests.", 1);
            }

            var partial = target + ".part";
            await File.WriteAllTextAsync(partial, body, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(partial, target, overwrite: false);
            log.WriteLine($"{reportId}: saved {body.Length} characters to {Path.GetFileName(target)}.");
            downloaded++;
        }

        return new DownloadSummary(downloaded, skipped, failed, requests, false);
    }

    /// <summary>Returns whether a response is a Cybenetics report page: HTML with tables and the "Testing Date" row.</summary>
    /// <param name="html">The response text.</param>
    public static bool IsReportPage(string? html) =>
        html is not null
        && html.Contains("<table", StringComparison.OrdinalIgnoreCase)
        && html.Contains("Testing Date", StringComparison.OrdinalIgnoreCase);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The download state file: requests per calendar day and the time of the last request.</summary>
    private sealed class DownloadState
    {
        public Dictionary<string, int> PagesPerDay { get; set; } = [];

        public DateTimeOffset? LastRequest { get; set; }

        public static DownloadState Load(string path)
        {
            if (!File.Exists(path))
            {
                return new DownloadState();
            }

            try
            {
                return JsonSerializer.Deserialize<DownloadState>(File.ReadAllText(path)) ?? new DownloadState();
            }
            catch (JsonException ex)
            {
                // A damaged state file must not lift the cap.
                throw new InvalidDataException($"{path} is not a valid download state file ({ex.Message}); fix or delete it after checking today's count.", ex);
            }
        }

        public int CountFor(DateOnly day) => PagesPerDay.GetValueOrDefault(Iso(day));

        public void Record(DateOnly day, DateTimeOffset at)
        {
            PagesPerDay[Iso(day)] = CountFor(day) + 1;
            LastRequest = at;
        }

        public void Save(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// <c>download --catalog &lt;csv&gt; --reports &lt;dir&gt; [--max-per-day &lt;n&gt;] [--batch-size &lt;n&gt;] [--batch-pause &lt;15m|900s&gt;]</c>:
/// fetches missing report pages.
/// </summary>
public static class DownloadCommand
{
    /// <summary>Runs the download with a real HTTP handler.</summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="log">Progress and errors.</param>
    internal static int Run(OptionSet options, TextWriter log)
    {
        var rows = Catalog.Load(options.Require("catalog"));
        var reports = options.Require("reports");
        var max = PositiveOption(options, "max-per-day", ReportDownloader.DefaultMaxPerDay);
        var batchSize = PositiveOption(options, "batch-size", ReportDownloader.DefaultBatchSize);
        var batchPause = ReportDownloader.DefaultBatchPause;
        if (options.Get("batch-pause") is { } pauseText && !TryParseDuration(pauseText, out batchPause))
        {
            throw new ArgumentException("--batch-pause must be a duration such as 15m, 900s or 1h.");
        }

        using var downloader = new ReportDownloader(new SocketsHttpHandler());
        DownloadSummary summary;
        try
        {
            summary = downloader.DownloadAsync(rows, reports, log, max, batchSize, batchPause).GetAwaiter().GetResult();
        }
        catch (InvalidDataException ex)
        {
            log.WriteLine("error: " + ex.Message);
            return ImportCommand.Failed;
        }

        log.WriteLine($"{summary.Downloaded} downloaded, {summary.Skipped} skipped, {summary.Failed} failed, {summary.Requests} requests{(summary.Stopped ? "; stopped early" : string.Empty)}.");
        return summary.Failed == 0 && (!summary.Stopped || summary.CapReached) ? ImportCommand.Ok : ImportCommand.Failed;
    }

    /// <summary>
    /// Parses a duration written as a whole number with a unit: <c>900s</c>, <c>15m</c> or <c>1h</c> (no unit = seconds).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="duration">The duration when it could be read.</param>
    public static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var value = text?.Trim().ToLowerInvariant() ?? string.Empty;
        var unit = value.Length > 0 && char.IsLetter(value[^1]) ? value[^1] : 's';
        var number = value.Length > 0 && char.IsLetter(value[^1]) ? value[..^1] : value;
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return false;
        }

        TimeSpan? parsed = unit switch
        {
            's' => TimeSpan.FromSeconds(n),
            'm' => TimeSpan.FromMinutes(n),
            'h' => TimeSpan.FromHours(n),
            _ => null,
        };
        duration = parsed ?? TimeSpan.Zero;
        return parsed is not null;
    }

    private static int PositiveOption(OptionSet options, string name, int fallback)
    {
        if (options.Get(name) is not { } text)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new ArgumentException($"--{name} must be a positive whole number.");
    }
}
