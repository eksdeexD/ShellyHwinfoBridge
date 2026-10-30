using System.Net;
using Zeroconf;

namespace ShellyHwinfoBridge.Core.Shelly.Discovery;

/// <summary>One mDNS/DNS-SD service instance found on the network.</summary>
/// <param name="InstanceName">The instance name, e.g. <c>shellyplugmg3-0a1b2c3d4e5f</c>.</param>
/// <param name="ServiceType">The service type, e.g. <c>_shelly._tcp.local.</c>.</param>
/// <param name="Addresses">The host's addresses.</param>
/// <param name="Port">The service port (from the SRV record).</param>
/// <param name="Txt">TXT record key/value pairs (keys case-insensitive).</param>
public sealed record MdnsServiceInstance(
    string InstanceName,
    string ServiceType,
    IReadOnlyList<IPAddress> Addresses,
    int Port,
    IReadOnlyDictionary<string, string> Txt);

/// <summary>Browses mDNS/DNS-SD service types.</summary>
public interface IMdnsBrowser
{
    /// <summary>Sends queries for the service types and collects answers for <paramref name="duration"/>.</summary>
    /// <param name="serviceTypes">Service types such as <c>_shelly._tcp.local.</c>.</param>
    /// <param name="duration">How long to listen.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<MdnsServiceInstance>> BrowseAsync(IReadOnlyList<string> serviceTypes, TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary><see cref="IMdnsBrowser"/> using the Zeroconf library (pure managed, queries every network interface).</summary>
public sealed class ZeroconfMdnsBrowser : IMdnsBrowser
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MdnsServiceInstance>> BrowseAsync(IReadOnlyList<string> serviceTypes, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceTypes);
        var hosts = await ZeroconfResolver.ResolveAsync(
            serviceTypes,
            scanTime: duration,
            retries: 2,
            retryDelayMilliseconds: 500,
            callback: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return hosts.SelectMany(Map).ToList();
    }

    /// <summary>Converts a Zeroconf host (one per IP, possibly with several services) into service instances.</summary>
    /// <param name="host">The Zeroconf host.</param>
    internal static IEnumerable<MdnsServiceInstance> Map(IZeroconfHost host)
    {
        var addresses = (host.IPAddresses ?? [])
            .Append(host.IPAddress)
            .Select(a => IPAddress.TryParse(a, out var ip) ? ip : null)
            .Where(ip => ip is not null)
            .Select(ip => ip!)
            .Distinct()
            .ToList();
        if (host.Services is null)
        {
            yield break;
        }

        foreach (var (key, service) in host.Services)
        {
            if (service is null)
            {
                continue;
            }

            var txt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in service.Properties ?? [])
            {
                foreach (var (k, v) in set)
                {
                    txt.TryAdd(k, v ?? string.Empty);
                }
            }

            // Zeroconf: service.Name is the service type ("_shelly._tcp.local."); the dictionary key and
            // service.ServiceName are the full instance name ("shellyplugmg3-xxxx._shelly._tcp.local.").
            var fullName = !string.IsNullOrWhiteSpace(service.ServiceName) ? service.ServiceName : key;
            var type = service.Name is { Length: > 0 } n && n.StartsWith('_') ? n : TypeOf(fullName) ?? TypeOf(key) ?? string.Empty;
            var instance = StripSuffix(fullName, type);
            if (instance.Length == 0 || instance.StartsWith('_'))
            {
                instance = StripSuffix(host.DisplayName ?? string.Empty, type);
            }

            yield return new MdnsServiceInstance(instance, type, addresses, service.Port, txt);
        }
    }

    /// <summary>Extracts the service type from a full instance name ("a.b._http._tcp.local." → "_http._tcp.local.").</summary>
    private static string? TypeOf(string fullName)
    {
        var index = fullName.IndexOf("._", StringComparison.Ordinal);
        return index >= 0 ? fullName[(index + 1)..] : null;
    }

    private static string StripSuffix(string name, string serviceType)
    {
        var trimmed = name.Trim().TrimEnd('.');
        var suffix = "." + serviceType.TrimEnd('.');
        return serviceType.Length > 0 && trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^suffix.Length]
            : trimmed;
    }
}
