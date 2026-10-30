using System.Net;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Shelly;

public class ShellyClientTests
{
    private const string SwitchComponents = """
        {"components":[{"key":"ble"},{"key":"cloud"},{"key":"switch:0"},{"key":"sys"}],"cfg_rev":12,"offset":0,"total":4}
        """;

    private const string Pm1Components = """
        {"components":[{"key":"pm1:0"},{"key":"sys"},{"key":"wifi"}],"cfg_rev":3,"offset":0,"total":3}
        """;

    private const string SwitchStatus = """
        {"id":0,"source":"init","output":true,"apower":151.9,"voltage":231.4,"current":0.712,
         "aenergy":{"total":1234.5,"by_minute":[0,0,0],"minute_ts":1700000000},"temperature":{"tC":38.2,"tF":100.8}}
        """;

    private const string Pm1Status = """
        {"id":0,"voltage":118.2,"current":1.05,"apower":110.3,"freq":60.0,"aenergy":{"total":10.0}}
        """;

    [Fact]
    public async Task DetectsSwitchAndReadsStatus()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetComponents?offset=0", SwitchComponents)
            .Add("/rpc/Switch.GetStatus?id=0", SwitchStatus);
        using var client = new ShellyClient("192.168.1.50", handler);

        var reading = await client.GetReadingAsync();

        Assert.Equal(ShellyComponentType.Switch, client.Component);
        Assert.Equal(new ShellyReading(151.9, 231.4, 0.712, 1234.5), reading);

        await client.GetReadingAsync();
        Assert.Single(handler.Requests, r => r.StartsWith("/rpc/Shelly.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetectsPm1AndReadsStatus()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetComponents?offset=0", Pm1Components)
            .Add("/rpc/PM1.GetStatus?id=0", Pm1Status);
        using var client = new ShellyClient("pm-mini.local", handler);

        var reading = await client.GetReadingAsync();

        Assert.Equal(ShellyComponentType.PM1, client.Component);
        Assert.Equal(new ShellyReading(110.3, 118.2, 1.05, 10.0), reading);
    }

    [Theory]
    [InlineData("""{"id":0,"apower":12.5,"voltage":230.1}""")]
    [InlineData("""{"id":0,"apower":12.5,"voltage":230.1,"aenergy":5}""")]
    [InlineData("""{"id":0,"apower":12.5,"voltage":230.1,"aenergy":{"by_minute":[1,2,3]}}""")]
    [InlineData("""{"id":0,"apower":12.5,"voltage":230.1,"aenergy":{"total":"12"}}""")]
    [InlineData("""{"id":0,"apower":12.5,"voltage":230.1,"aenergy":{"total":-1}}""")]
    public void EnergyTotalIsNullWhenMissingOrMalformed(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var reading = ShellyClient.ParseReading(doc.RootElement);

        Assert.Equal(12.5, reading.ActivePowerWatts);
        Assert.Null(reading.EnergyTotalWh);
    }

    [Fact]
    public async Task ReadsSwitchEnergyTotalWithoutVoltage()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetComponents?offset=0", SwitchComponents)
            .Add("/rpc/Switch.GetStatus?id=0", """{"id":0,"apower":0,"aenergy":{"total":0,"by_minute":[0,0,0]}}""");
        using var client = new ShellyClient("192.168.1.50", handler);

        var reading = await client.GetReadingAsync();

        Assert.Equal(new ShellyReading(0, null, null, 0), reading);
    }

    [Fact]
    public async Task ReadsDeviceInfo()
    {
        var handler = new StubHttpHandler().Add("/rpc/Shelly.GetDeviceInfo", """
            {"name":"PlugMGen3-2","id":"shellyplugmg3-0a1b2c3d4e5f","mac":"0A1B2C3D4E5F","slot":0,"model":"S3PL-10112EU",
             "gen":3,"fw_id":"20250101-000000/1.5.0-gabcdef","ver":"1.5.0","app":"PlugMG3","auth_en":true,"auth_domain":"shellyplugmg3-0a1b2c3d4e5f"}
            """);
        using var client = new ShellyClient("192.168.1.50", handler);

        var info = await client.GetDeviceInfoAsync();

        Assert.Equal(new ShellyDeviceInfo("PlugMGen3-2", "S3PL-10112EU", "PlugMG3", 3, "shellyplugmg3-0a1b2c3d4e5f", true), info);
    }

    [Fact]
    public async Task DeviceInfoWithoutNameHasNullFields()
    {
        var handler = new StubHttpHandler().Add("/rpc/Shelly.GetDeviceInfo", """{"name":null,"id":"shellypmmini-aabbcc","gen":"x"}""");
        using var client = new ShellyClient("10.0.0.2", handler);

        var info = await client.GetDeviceInfoAsync();

        Assert.Equal(new ShellyDeviceInfo(null, null, null, null, "shellypmmini-aabbcc", null), info);
    }

    [Fact]
    public async Task DeviceInfoUnauthorizedRaisesAuthenticationException()
    {
        var handler = new StubHttpHandler().Add("/rpc/Shelly.GetDeviceInfo", "", HttpStatusCode.Unauthorized);
        using var client = new ShellyClient("10.0.0.2", handler);

        await Assert.ThrowsAsync<ShellyAuthenticationException>(() => client.GetDeviceInfoAsync());
    }

    [Fact]
    public async Task FallsBackToGetStatusWhenGetComponentsMissing()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetStatus", """{"sys":{},"wifi":{},"pm1:0":{"apower":5.0}}""");
        using var client = new ShellyClient("10.0.0.2", handler);

        Assert.Equal(ShellyComponentType.PM1, await client.DetectComponentAsync());
        Assert.Contains("/rpc/Shelly.GetComponents?offset=0", handler.Requests);
    }

    [Fact]
    public async Task FollowsGetComponentsPagination()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetComponents?offset=0", """{"components":[{"key":"ble"},{"key":"cloud"}],"offset":0,"total":3}""")
            .Add("/rpc/Shelly.GetComponents?offset=2", """{"components":[{"key":"switch:0"}],"offset":2,"total":3}""");
        using var client = new ShellyClient("10.0.0.2", handler);

        Assert.Equal(ShellyComponentType.Switch, await client.DetectComponentAsync());
    }

    [Fact]
    public async Task ThrowsWhenNoMeteringComponent()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetComponents?offset=0", """{"components":[{"key":"input:0"}],"offset":0,"total":1}""")
            .Add("/rpc/Shelly.GetStatus", """{"input:0":{}}""");
        using var client = new ShellyClient("10.0.0.2", handler);

        await Assert.ThrowsAsync<ShellyException>(() => client.GetReadingAsync());
    }

    [Fact]
    public async Task UnauthorizedRaisesAuthenticationException()
    {
        var handler = new StubHttpHandler().Add("/rpc/Shelly.GetComponents?offset=0", "", HttpStatusCode.Unauthorized);
        using var client = new ShellyClient("10.0.0.2", handler);

        await Assert.ThrowsAsync<ShellyAuthenticationException>(() => client.DetectComponentAsync());
    }

    [Fact]
    public async Task MissingApowerIsAnError()
    {
        var handler = new StubHttpHandler()
            .Add("/rpc/Shelly.GetComponents?offset=0", SwitchComponents)
            .Add("/rpc/Switch.GetStatus?id=0", """{"id":0,"output":true}""");
        using var client = new ShellyClient("10.0.0.2", handler);

        await Assert.ThrowsAsync<ShellyException>(() => client.GetReadingAsync());
        Assert.Null(client.Component);
    }

    [Theory]
    [InlineData("192.168.1.50", "http://192.168.1.50/")]
    [InlineData(" plug.local ", "http://plug.local/")]
    [InlineData("http://10.0.0.5:8080/some/path", "http://10.0.0.5:8080/")]
    public void BuildsBaseUri(string input, string expected)
    {
        Assert.Equal(new Uri(expected), ShellyClient.BuildBaseUri(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://host")]
    [InlineData("http://")]
    public void RejectsInvalidHosts(string input)
    {
        Assert.Throws<ArgumentException>(() => ShellyClient.BuildBaseUri(input));
    }
}
