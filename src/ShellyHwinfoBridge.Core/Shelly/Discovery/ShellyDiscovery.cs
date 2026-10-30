using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShellyHwinfoBridge.Core.Shelly.Discovery;

/// <summary>A Shelly device found on the network.</summary>
/// <param name="InstanceName">The mDNS instance name, e.g. <c>shellyplugmg3-0a1b2c3d4e5f</c> (also the device id).</param>
/// <param name="Name">The user-assigned device name, if set.</param>
/// <param name="Model">The model / SKU (e.g. <c>S3PL-10112EU</c>) from <c>Shelly.GetDeviceInfo</c>, if queried.</param>
/// <param name="App">The application name (e.g. <c>PlugMG3</c>) from TXT or <c>Shelly.GetDeviceInfo</c>.</param>
/// <param name="Generation">The device generation (2, 3, ...), if known.</param>
/// <param name="Address">The IP address (IPv4 preferred).</param>
/// <param name="Port">The HTTP port.</param>
/// <param name="AuthEnabled">Whether the device requires a password, if known.</param>
public sealed record DiscoveredShelly(
    string InstanceName,
    string? Name,
    string? Model,
    string? App,
    int? Generation,
    IPAddress Address,
    int Port,
    bool? AuthEnabled)
{
    /// <summary>Gets the host to store in settings: the IP, plus the port when it is not 80.</summary>
    public string Host => new UriBuilder(Uri.UriSchemeHttp, Address.ToString(), Port is 0 ? 80 : Port).Uri.Authority;

    /// <summary>Gets the name to show in a list: the user name, else the instance name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? InstanceName : Name;

    /// <summary>Gets the model to show: the SKU, else the app name.</summary>
    public string? DisplayModel => string.IsNullOrWhiteSpace(Model) ? App : Model;
}

/// <summary>
/// Finds Shelly devices through mDNS (wizard step 2): browses <c>_shelly._tcp.local</c> and accepts
/// <c>_http._tcp.local</c> instances whose name starts with "shelly", then optionally asks each device for
/// <c>/rpc/Shelly.GetDeviceInfo</c> to fill in its name and model.
/// </summary>
public sealed class ShellyDiscovery
{
    /// <summary>The Shelly service type.</summary>
    public const string ShellyServiceType = "_shelly._tcp.local.";

    /// <summary>The generic HTTP service type.</summary>
    public const string HttpServiceType = "_http._tcp.local.";

    /// <summary>The default browse duration.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan DeviceInfoTimeout = TimeSpan.FromSeconds(2);

    private readonly IMdnsBrowser _browser;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly ILogger _logger;

    /// <summary>Creates a discovery service.</summary>
    /// <param name="browser">The mDNS browser; <see cref="ZeroconfMdnsBrowser"/> by default.</param>
    /// <param name="handlerFactory">Creates the HTTP handler for <c>GetDeviceInfo</c> calls (tests inject stubs).</param>
    /// <param name="logger">Optional logger.</param>
    public ShellyDiscovery(IMdnsBrowser? browser = null, Func<HttpMessageHandler>? handlerFactory = null, ILogger? logger = null)
    {
        _browser = browser ?? new ZeroconfMdnsBrowser();
        _handlerFactory = handlerFactory ?? (() => new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Browses for Shelly devices.</summary>
    /// <param name="duration">How long to browse; <see cref="DefaultDuration"/> when <c>null</c>.</param>
    /// <param name="queryDeviceInfo">Whether to call <c>Shelly.GetDeviceInfo</c> on each device afterwards.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The devices, one per IP address, sorted by display name.</returns>
    public async Task<IReadOnlyList<DiscoveredShelly>> DiscoverAsync(TimeSpan? duration = null, bool queryDeviceInfo = true, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MdnsServiceInstance> instances;
        try
        {
            instances = await _browser.BrowseAsync([ShellyServiceType, HttpServiceType], duration ?? DefaultDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "mDNS browse failed.");
            return [];
        }

        var devices = FromInstances(instances);
        _logger.LogInformation("mDNS discovery found {Count} Shelly device(s).", devices.Count);
        if (!queryDeviceInfo || devices.Count == 0)
        {
            return devices;
        }

        using var http = new HttpClient(_handlerFactory(), disposeHandler: true) { Timeout = DeviceInfoTimeout };
        var enriched = await Task.WhenAll(devices.Select(d => EnrichAsync(http, d, cancellationToken))).ConfigureAwait(false);
        return Sort(enriched);
    }

    /// <summary>
    /// Turns browse results into devices: keeps <c>_shelly._tcp</c> instances and <c>_http._tcp</c> instances named
    /// "shelly…", merges entries for the same IP, and reads <c>app</c> and <c>gen</c> from TXT records.
    /// </summary>
    /// <param name="instances">The browse results.</param>
    /// <remarks>
    /// Gen2+ devices advertise each service twice: under their device id (<c>shellyplugmg3-a1b2c3d4e5f6</c>) and,
    /// when the user has named the device, under that name (<c>PlugMGen3-2</c>). The device id becomes
    /// <see cref="DiscoveredShelly.InstanceName"/> and a <c>_shelly._tcp</c> instance with another name becomes
    /// <see cref="DiscoveredShelly.Name"/>.
    /// </remarks>
    public static IReadOnlyList<DiscoveredShelly> FromInstances(IEnumerable<MdnsServiceInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var groups = instances
            .Where(i => i.Addresses.Count > 0)
            .GroupBy(i => PickAddress(i.Addresses)!)
            .Where(g => g.Any(IsShelly));

        var devices = new List<DiscoveredShelly>();
        foreach (var group in groups)
        {
            var all = group.OrderBy(i => IsType(i, ShellyServiceType) ? 0 : 1).ToList();
            var accepted = all.Where(IsShelly).ToList();
            var id = all.Select(i => i.InstanceName.Trim()).FirstOrDefault(StartsWithShelly) ?? accepted[0].InstanceName.Trim();
            var userName = all
                .Where(i => IsType(i, ShellyServiceType))
                .Select(i => i.InstanceName.Trim())
                .FirstOrDefault(n => n.Length > 0 && !StartsWithShelly(n));
            var port = accepted.Select(i => i.Port).FirstOrDefault(p => p > 0);

            devices.Add(new DiscoveredShelly(
                id,
                userName,
                Model: null,
                App: all.Select(i => Txt(i, "app")).FirstOrDefault(v => v is not null),
                Generation: all.Select(i => int.TryParse(Txt(i, "gen"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var gen) ? gen : (int?)null)
                    .FirstOrDefault(g => g is not null),
                group.Key,
                port > 0 ? port : 80,
                AuthEnabled: null));
        }

        return Sort(devices);
    }

    private static bool StartsWithShelly(string name) => name.StartsWith("shelly", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fills name, model, app, generation and auth flag from a <c>Shelly.GetDeviceInfo</c> response.</summary>
    /// <param name="device">The device.</param>
    /// <param name="info">The parsed response.</param>
    public static DiscoveredShelly ApplyDeviceInfo(DiscoveredShelly device, JsonElement info)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (info.ValueKind != JsonValueKind.Object)
        {
            return device;
        }

        var parsed = ShellyDeviceInfo.Parse(info);
        return device with
        {
            Name = parsed.Name ?? device.Name,
            Model = parsed.Model ?? device.Model,
            App = parsed.App ?? device.App,
            Generation = parsed.Generation ?? device.Generation,
            AuthEnabled = parsed.AuthEnabled ?? device.AuthEnabled,
        };
    }

    private static bool IsShelly(MdnsServiceInstance instance) =>
        IsType(instance, ShellyServiceType) ||
        (IsType(instance, HttpServiceType) && instance.InstanceName.TrimStart().StartsWith("shelly", StringComparison.OrdinalIgnoreCase));

    private static bool IsType(MdnsServiceInstance instance, string type) =>
        instance.ServiceType.Trim().TrimEnd('.').Equals(type.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static IPAddress? PickAddress(IReadOnlyList<IPAddress> addresses) =>
        addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a))
        ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
        ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal)
        ?? addresses.FirstOrDefault();

    private static bool IsLinkLocal(IPAddress ipv4)
    {
        var b = ipv4.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    private static string? Txt(MdnsServiceInstance instance, string key) =>
        instance.Txt.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static IReadOnlyList<DiscoveredShelly> Sort(IEnumerable<DiscoveredShelly> devices) =>
        devices.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Address.ToString(), StringComparer.Ordinal).ToList();

    private async Task<DiscoveredShelly> EnrichAsync(HttpClient http, DiscoveredShelly device, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(ShellyClient.BuildBaseUri(device.Host), "rpc/Shelly.GetDeviceInfo");
            using var response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return device;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return ApplyDeviceInfo(device, doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogDebug("GetDeviceInfo failed for {Host}: {Message}", device.Host, ex.Message);
            return device;
        }
    }
}
