using System.Net;
using System.Text.Json;

namespace ShellyHwinfoBridge.Core.Shelly;

/// <summary>
/// HTTP RPC client for Gen2/Gen3 Shelly devices with a <c>Switch</c> or <c>PM1</c> component.
/// </summary>
/// <remarks>
/// The component is detected once via <c>/rpc/Shelly.GetComponents</c> (falling back to
/// <c>/rpc/Shelly.GetStatus</c>) and cached. Readings come from <c>Switch.GetStatus?id=0</c> or
/// <c>PM1.GetStatus?id=0</c>. Password-protected devices use HTTP digest auth with user <c>admin</c>;
/// .NET's <see cref="SocketsHttpHandler"/> implements RFC 7616 digest (MD5 and SHA-256), which the
/// device challenges with.
/// </remarks>
public sealed class ShellyClient : IShellyClient, IDisposable
{
    /// <summary>The fixed digest-auth user name used by Shelly Gen2+ devices.</summary>
    public const string DigestUserName = "admin";

    private const int MaxComponentPages = 20;

    private readonly HttpClient _http;

    /// <summary>Creates a client for a host, with optional digest-auth password.</summary>
    /// <param name="host">IP address, host name or http URL.</param>
    /// <param name="password">The device password, or <c>null</c>/empty when auth is off.</param>
    public ShellyClient(string host, string? password = null)
        : this(host, CreateHandler(BuildBaseUri(host), password), disposeHandler: true)
    {
    }

    /// <summary>Creates a client over a custom handler (used by tests).</summary>
    /// <param name="host">IP address, host name or http URL.</param>
    /// <param name="handler">The message handler.</param>
    /// <param name="disposeHandler">Whether disposing the client disposes the handler.</param>
    public ShellyClient(string host, HttpMessageHandler handler, bool disposeHandler = true)
    {
        BaseAddress = BuildBaseUri(host);
        _http = new HttpClient(handler, disposeHandler)
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>Gets the device base address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the detected component, or <c>null</c> before detection.</summary>
    public ShellyComponentType? Component { get; private set; }

    /// <summary>Builds the base URI from user input such as <c>192.168.1.50</c>, <c>plug.local</c> or <c>http://plug/</c>.</summary>
    /// <param name="host">The host input.</param>
    /// <exception cref="ArgumentException">The input is not a valid host.</exception>
    public static Uri BuildBaseUri(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var text = host.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{host}' is not a valid host name or address.", nameof(host));
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>Detects (and caches) which metering component the device has.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ShellyException">No supported component was found or the device could not be reached.</exception>
    public async Task<ShellyComponentType> DetectComponentAsync(CancellationToken cancellationToken = default)
    {
        var detected = await DetectFromComponentsAsync(cancellationToken).ConfigureAwait(false)
            ?? await DetectFromStatusAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ShellyException("The device has no Switch or PM1 component with power metering.");
        Component = detected;
        return detected;
    }

    /// <inheritdoc />
    public async Task<ShellyReading> GetReadingAsync(CancellationToken cancellationToken = default)
    {
        var component = Component ?? await DetectComponentAsync(cancellationToken).ConfigureAwait(false);
        var method = component == ShellyComponentType.Switch ? "Switch.GetStatus" : "PM1.GetStatus";
        try
        {
            using var doc = await GetJsonAsync($"rpc/{method}?id=0", cancellationToken).ConfigureAwait(false);
            return ParseReading(doc.RootElement);
        }
        catch (ShellyException)
        {
            // Re-detect next time in case the device was replaced or reconfigured.
            Component = null;
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    internal static ShellyReading ParseReading(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("apower", out var apower) ||
            apower.ValueKind != JsonValueKind.Number)
        {
            throw new ShellyException("The status response does not contain 'apower'.");
        }

        return new ShellyReading(apower.GetDouble(), OptionalNumber(root, "voltage"), OptionalNumber(root, "current"));
    }

    private static double? OptionalNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    private static ShellyComponentType? FromKey(string? key) => key?.ToLowerInvariant() switch
    {
        "switch:0" => ShellyComponentType.Switch,
        "pm1:0" => ShellyComponentType.PM1,
        _ => null,
    };

    private static ShellyComponentType? Prefer(IEnumerable<string?> keys)
    {
        var found = keys.Select(FromKey).Where(c => c is not null).Select(c => c!.Value).ToList();
        return found.Count == 0 ? null : found.Contains(ShellyComponentType.Switch) ? ShellyComponentType.Switch : ShellyComponentType.PM1;
    }

    private static HttpMessageHandler CreateHandler(Uri baseUri, string? password)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        };
        if (!string.IsNullOrEmpty(password))
        {
            // Restrict to Digest so the password is never sent with Basic auth.
            handler.Credentials = new CredentialCache
            {
                { baseUri, "Digest", new NetworkCredential(DigestUserName, password) },
            };
        }

        return handler;
    }

    private async Task<ShellyComponentType?> DetectFromComponentsAsync(CancellationToken cancellationToken)
    {
        var offset = 0;
        for (var page = 0; page < MaxComponentPages; page++)
        {
            JsonDocument doc;
            try
            {
                doc = await GetJsonAsync($"rpc/Shelly.GetComponents?offset={offset}", cancellationToken).ConfigureAwait(false);
            }
            catch (ShellyException ex) when (ex is not ShellyAuthenticationException)
            {
                return null; // Older firmware without GetComponents: fall back to GetStatus.
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("components", out var components) ||
                    components.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var keys = components.EnumerateArray()
                    .Select(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null)
                    .ToList();
                if (Prefer(keys) is ShellyComponentType type)
                {
                    return type;
                }

                offset += keys.Count;
                var total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : offset;
                if (keys.Count == 0 || offset >= total)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private async Task<ShellyComponentType?> DetectFromStatusAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync("rpc/Shelly.GetStatus", cancellationToken).ConfigureAwait(false);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            ? Prefer(doc.RootElement.EnumerateObject().Select(p => p.Name))
            : null;
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(relativeUrl, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ShellyException($"Could not reach {BaseAddress}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShellyException($"Request to {BaseAddress} timed out.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ShellyAuthenticationException("The device rejected the password (HTTP 401).");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ShellyException($"{relativeUrl} returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new ShellyException($"{relativeUrl} returned invalid JSON.", ex);
            }
        }
    }
}
