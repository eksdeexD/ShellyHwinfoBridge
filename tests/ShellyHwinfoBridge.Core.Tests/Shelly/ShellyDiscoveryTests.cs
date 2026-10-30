using System.Net;
using System.Text;
using System.Text.Json;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using Zeroconf;

namespace ShellyHwinfoBridge.Core.Tests.Shelly;

public class ShellyDiscoveryTests
{
    private const string PlugInfo = """
        {"name":"Desk PC","id":"shellyplugmg3-0a1b2c3d4e5f","mac":"0A1B2C3D4E5F","slot":0,"model":"S3PL-20112EU",
         "gen":3,"fw_id":"20250101-000000/1.5.0-gabcdef","ver":"1.5.0","app":"PlugMG3","auth_en":true,"auth_domain":"shellyplugmg3-0a1b2c3d4e5f"}
        """;

    private static readonly Dictionary<string, string> NoTxt = [];

    private static MdnsServiceInstance Instance(string name, string type, string ip, int port = 80, Dictionary<string, string>? txt = null) =>
        new(name, type, [IPAddress.Parse(ip)], port, txt ?? NoTxt);

    [Fact]
    public void KeepsShellyServicesAndShellyNamedHttpServices()
    {
        var devices = ShellyDiscovery.FromInstances(
        [
            Instance("shellyplugmg3-0a1b2c3d4e5f", "_shelly._tcp.local.", "192.168.1.22", txt: new() { ["gen"] = "3", ["app"] = "PlugMG3", ["ver"] = "1.5.0" }),
            Instance("ShellyPlusPlugS-AABBCC", "_http._tcp.local.", "192.168.1.23", txt: new() { ["gen"] = "2", ["app"] = "PlusPlugS" }),
            Instance("Living Room Printer", "_http._tcp.local.", "192.168.1.30"),
            Instance("nas", "_smb._tcp.local.", "192.168.1.31"),
        ]);

        Assert.Equal(2, devices.Count);
        var plug = devices.Single(d => d.App == "PlugMG3");
        Assert.Equal("shellyplugmg3-0a1b2c3d4e5f", plug.InstanceName);
        Assert.Equal(3, plug.Generation);
        Assert.Equal(IPAddress.Parse("192.168.1.22"), plug.Address);
        Assert.Equal("192.168.1.22", plug.Host);
        Assert.Equal("PlugMG3", plug.DisplayModel);
        Assert.Equal("shellyplugmg3-0a1b2c3d4e5f", plug.DisplayName);
        Assert.Contains(devices, d => d.InstanceName == "ShellyPlusPlugS-AABBCC" && d.Generation == 2);
    }

    [Fact]
    public void MergesBothServiceTypesOfTheSameDevice()
    {
        var devices = ShellyDiscovery.FromInstances(
        [
            Instance("shellyplugmg3-0a1b2c3d4e5f", "_http._tcp.local", "192.168.1.22"),
            Instance("shellyplugmg3-0a1b2c3d4e5f", "_shelly._tcp.local", "192.168.1.22", txt: new() { ["app"] = "PlugMG3" }),
        ]);

        Assert.Equal("PlugMG3", Assert.Single(devices).App);
    }

    [Fact]
    public void PrefersRoutableIpv4AndKeepsNonDefaultPort()
    {
        var instance = new MdnsServiceInstance(
            "shellypmmini-1",
            "_shelly._tcp.local.",
            [IPAddress.Parse("fe80::1"), IPAddress.Parse("169.254.1.2"), IPAddress.Parse("192.168.1.40")],
            8080,
            NoTxt);

        var device = Assert.Single(ShellyDiscovery.FromInstances([instance]));

        Assert.Equal(IPAddress.Parse("192.168.1.40"), device.Address);
        Assert.Equal("192.168.1.40:8080", device.Host);
    }

    [Fact]
    public void AppliesDeviceInfo()
    {
        var device = Assert.Single(ShellyDiscovery.FromInstances([Instance("shellyplugmg3-0a1b2c3d4e5f", "_shelly._tcp.local.", "192.168.1.22")]));
        using var doc = JsonDocument.Parse(PlugInfo);

        var result = ShellyDiscovery.ApplyDeviceInfo(device, doc.RootElement);

        Assert.Equal("Desk PC", result.DisplayName);
        Assert.Equal("S3PL-20112EU", result.DisplayModel);
        Assert.Equal("PlugMG3", result.App);
        Assert.Equal(3, result.Generation);
        Assert.True(result.AuthEnabled);
    }

    [Fact]
    public void NullNameInDeviceInfoKeepsInstanceName()
    {
        var device = Assert.Single(ShellyDiscovery.FromInstances([Instance("shellyplugmg3-1", "_shelly._tcp.local.", "10.0.0.5")]));
        using var doc = JsonDocument.Parse("""{"name":null,"model":"S3PL-20112EU","gen":3,"app":"PlugMG3","auth_en":false}""");

        var result = ShellyDiscovery.ApplyDeviceInfo(device, doc.RootElement);

        Assert.Equal("shellyplugmg3-1", result.DisplayName);
        Assert.False(result.AuthEnabled);
    }

    [Fact]
    public async Task DiscoverQueriesDeviceInfoAndToleratesFailures()
    {
        var browser = new FakeBrowser(
            Instance("shellyplugmg3-0a1b2c3d4e5f", "_shelly._tcp.local.", "192.168.1.22"),
            Instance("shellyplugsg3-dead", "_shelly._tcp.local.", "192.168.1.24"));
        var handler = new HostHandler { ["192.168.1.22"] = PlugInfo };
        var discovery = new ShellyDiscovery(browser, () => handler);

        var devices = await discovery.DiscoverAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["_shelly._tcp.local.", "_http._tcp.local."], browser.RequestedTypes);
        Assert.Equal(["Desk PC", "shellyplugsg3-dead"], devices.Select(d => d.DisplayName));
        Assert.Null(devices[1].Model);
    }

    [Fact]
    public async Task DiscoverWithoutDeviceInfoMakesNoRequests()
    {
        var handler = new HostHandler();
        var discovery = new ShellyDiscovery(new FakeBrowser(Instance("shelly1", "_shelly._tcp.local.", "10.0.0.9")), () => handler);

        Assert.Single(await discovery.DiscoverAsync(queryDeviceInfo: false));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void MapsZeroconfHostsAsObservedFromAPlugMGen3()
    {
        // Structure observed from Zeroconf 3.7.16 against a Shelly Plug M Gen3 (firmware 1.8.x) named "PlugMGen3-2":
        // one host per IP; each service advertised under the device id and under the user-assigned name.
        var host = new FakeHost
        {
            DisplayName = "PlugMGen3-2",
            Id = "192.168.1.22",
            IPAddress = "192.168.1.22",
            IPAddresses = ["192.168.1.22"],
            Services = new Dictionary<string, IService>
            {
                ["PlugMGen3-2._http._tcp.local."] = Service("_http._tcp.local.", "PlugMGen3-2._http._tcp.local.", new() { ["gen"] = "3" }),
                ["shellyplugmg3-a1b2c3d4e5f6._shelly._tcp.local."] = Service("_shelly._tcp.local.", "shellyplugmg3-a1b2c3d4e5f6._shelly._tcp.local.", new() { ["gen"] = "3", ["app"] = "PlugMG3", ["ver"] = "1.8.99-plugmg3prod0" }),
                ["PlugMGen3-2._shelly._tcp.local."] = Service("_shelly._tcp.local.", "PlugMGen3-2._shelly._tcp.local.", new() { ["gen"] = "3", ["app"] = "PlugMG3", ["ver"] = "1.8.99-plugmg3prod0" }),
                ["shellyplugmg3-a1b2c3d4e5f6._http._tcp.local."] = Service("_http._tcp.local.", "shellyplugmg3-a1b2c3d4e5f6._http._tcp.local.", new() { ["gen"] = "3" }),
            },
        };

        var instances = ZeroconfMdnsBrowser.Map(host).ToList();

        Assert.Equal(4, instances.Count);
        var shelly = instances.Single(i => i.InstanceName == "shellyplugmg3-a1b2c3d4e5f6" && i.ServiceType == "_shelly._tcp.local.");
        Assert.Equal("PlugMG3", shelly.Txt["APP"]);
        Assert.Equal(IPAddress.Parse("192.168.1.22"), Assert.Single(shelly.Addresses));
        Assert.Contains(instances, i => i.InstanceName == "PlugMGen3-2" && i.ServiceType == "_http._tcp.local.");

        var device = Assert.Single(ShellyDiscovery.FromInstances(instances));
        Assert.Equal("shellyplugmg3-a1b2c3d4e5f6", device.InstanceName);
        Assert.Equal("PlugMGen3-2", device.Name);
        Assert.Equal("PlugMG3", device.App);
        Assert.Equal(3, device.Generation);
        Assert.Equal("192.168.1.22", device.Host);
    }

    [Fact]
    public void ZeroconfServiceWithoutTypeFallsBackToKey()
    {
        var host = new FakeHost
        {
            DisplayName = "shellyplugsg3-1",
            IPAddress = "10.0.0.7",
            Services = new Dictionary<string, IService> { ["shellyplugsg3-1._http._tcp.local."] = new FakeService { Port = 80 } },
        };

        var instance = Assert.Single(ZeroconfMdnsBrowser.Map(host));
        Assert.Equal("shellyplugsg3-1", instance.InstanceName);
        Assert.Equal("_http._tcp.local.", instance.ServiceType);
    }

    private static FakeService Service(string type, string fullName, Dictionary<string, string> txt) =>
        new() { Name = type, ServiceName = fullName, Port = 80, Properties = [txt] };

    private sealed class FakeBrowser(params MdnsServiceInstance[] instances) : IMdnsBrowser
    {
        public List<string> RequestedTypes { get; } = [];

        public Task<IReadOnlyList<MdnsServiceInstance>> BrowseAsync(IReadOnlyList<string> serviceTypes, TimeSpan duration, CancellationToken cancellationToken)
        {
            RequestedTypes.AddRange(serviceTypes);
            return Task.FromResult<IReadOnlyList<MdnsServiceInstance>>(instances);
        }
    }

    private sealed class HostHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _bodies = [];
        private int _calls;

        public int Calls => _calls;

        public string this[string host]
        {
            set => _bodies[host] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Assert.Equal("/rpc/Shelly.GetDeviceInfo", request.RequestUri!.AbsolutePath);
            if (!_bodies.TryGetValue(request.RequestUri.Host, out var body))
            {
                throw new HttpRequestException("Connection refused.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeHost : IZeroconfHost
    {
        public string DisplayName { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;

        public string IPAddress { get; set; } = string.Empty;

        public IReadOnlyList<string> IPAddresses { get; set; } = [];

        public IReadOnlyDictionary<string, IService> Services { get; set; } = new Dictionary<string, IService>();
    }

    private sealed class FakeService : IService
    {
        public string Name { get; set; } = string.Empty;

        public string ServiceName { get; set; } = string.Empty;

        public int Port { get; set; }

        public int Ttl { get; set; } = 120;

        public IReadOnlyList<IReadOnlyDictionary<string, string>> Properties { get; set; } = [];
    }
}
