using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class ColoursAndChartTests
{
    private const string MonitorId = "11111111-1111-1111-1111-111111111111";
    private const string LampId = "22222222-2222-2222-2222-222222222222";
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("#FF6600", true, "#FF6600")]
    [InlineData("ff6600", true, "#FF6600")]
    [InlineData("  #00aaFF ", true, "#00AAFF")]
    [InlineData("#FF660", false, null)]
    [InlineData("#GG6600", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void ParsesAndFormatsHexColours(string? text, bool valid, string? canonical)
    {
        Assert.Equal(valid, DeviceColours.TryParse(text, out _));
        Assert.Equal(canonical, DeviceColours.Normalize(text));
    }

    [Fact]
    public void HslRoundTripsTheAppOrange()
    {
        DeviceColours.TryParse(DeviceColours.DefaultBase, out var orange);
        var (h, s, l) = DeviceColours.ToHsl(orange);
        Assert.Equal(24, h, 6);
        Assert.Equal(1, s, 6);
        Assert.Equal(0.5, l, 6);
        Assert.Equal(orange, DeviceColours.FromHsl(h, s, l));
    }

    [Fact]
    public void SchemesRotateTheHueKeepingSaturationAndLightness()
    {
        Assert.Equal(["#FF6600"], DeviceColours.Scheme("#FF6600", 1));

        // Complementary: 24° + 180° = 204°.
        Assert.Equal(["#FF6600", "#0099FF"], DeviceColours.Scheme("#FF6600", 2));

        // Triad: 144° and 264°.
        Assert.Equal(["#FF6600", "#00FF66", "#6600FF"], DeviceColours.Scheme("#FF6600", 3));

        // Tetrad (square): 114°, 204°, 294°.
        var square = DeviceColours.Scheme("#FF6600", 4);
        Assert.Equal(4, square.Count);
        Assert.Equal("#0099FF", square[2]);
        foreach (var colour in square)
        {
            DeviceColours.TryParse(colour, out var rgb);
            var (_, s, l) = DeviceColours.ToHsl(rgb);
            Assert.Equal(1, s, 2);
            Assert.Equal(0.5, l, 2);
        }

        Assert.Equal(DeviceColours.Scheme(DeviceColours.DefaultBase, 2), DeviceColours.Scheme("not a colour", 2));
    }

    [Fact]
    public void ResolveUsesTheSchemeOrCustomColours()
    {
        var settings = new AppSettings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor", Colour = "#123456" });
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = LampId, Label = "Lamp" });

        var automatic = DeviceColours.Resolve(settings);
        Assert.Equal(["#FF6600", "#00FF66", "#6600FF"], new[] { automatic["pc"], automatic[MonitorId], automatic[LampId] });

        settings.ColourMode = ColourMode.Custom;
        settings.Device.Colour = "#AA0000";
        var custom = DeviceColours.Resolve(settings);
        Assert.Equal("#AA0000", custom["pc"]);
        Assert.Equal("#123456", custom[MonitorId]);
        Assert.Equal("#6600FF", custom[LampId]);

        settings.ColourMode = ColourMode.AutomaticScheme;
        settings.BaseColour = "#0099FF";
        Assert.Equal("#0099FF", DeviceColours.ColourOf(settings, "pc"));
        Assert.Equal(DeviceColours.Scheme("#0099FF", 3)[1], DeviceColours.ColourOf(settings, MonitorId));
    }

    private static EnergyBucket Bucket(int hour, double ac, double dc = 0, bool has = true) =>
        new(new DateTimeOffset(2026, 10, 30, hour, 0, 0, TimeSpan.Zero), new DateTime(2026, 10, 30, hour, 0, 0), ac, dc, has);

    private static EnergyChartData Data(bool withMonitor = true)
    {
        var pc = new DeviceEnergySeries("pc", "PC", [Bucket(17, 0.20, 0.18), Bucket(18, 0.29, 0.27)]);
        var monitor = new DeviceEnergySeries(MonitorId, "Monitor", [Bucket(17, 0, has: false), Bucket(18, 0.05)]);
        IReadOnlyList<DeviceEnergySeries> devices = withMonitor ? [pc, monitor] : [pc];
        return new EnergyChartData(EnergyAggregator.Combine(devices.Select(d => d.Buckets)), devices);
    }

    private static readonly Dictionary<string, string> Colours = new() { ["pc"] = "#FF6600", [MonitorId] = "#0099FF" };

    [Fact]
    public void CombinedChartAddsThePerDeviceBreakdownToTheTooltip()
    {
        var chart = EnergyChartModel.Build(Data(), EnergyPeriod.Hours, EnergyChartMode.Combined, Colours, EnUs);

        Assert.Equal(EnergyChartMode.Combined, chart.Mode);
        Assert.Empty(chart.Groups);
        Assert.Empty(chart.Legend);
        Assert.Equal(2, chart.Bars.Count);
        Assert.Equal(0.34, chart.Bars[1].Value, 9);
        Assert.Equal("Fri 18:00 · Total 0.34 kWh · PC 0.29 · Monitor 0.05", chart.Bars[1].Tooltip);
        Assert.Equal(0.4, chart.YMax, 9);
    }

    [Fact]
    public void SingleDeviceChartHasNoBreakdownAndStaysCombined()
    {
        var chart = EnergyChartModel.Build(Data(withMonitor: false), EnergyPeriod.Hours, EnergyChartMode.PerDevice, Colours, EnUs);
        Assert.Equal(EnergyChartMode.Combined, chart.Mode);
        Assert.Equal("Fri 18:00 · 0.29 kWh", chart.Bars[1].Tooltip);
        Assert.DoesNotContain(chart.Bars, b => b.Tooltip.Contains("Total", StringComparison.Ordinal));
        Assert.Empty(chart.Groups);
    }

    [Fact]
    public void PerDeviceChartGroupsBarsWithColoursLabelsAndALegend()
    {
        var chart = EnergyChartModel.Build(Data(), EnergyPeriod.Hours, EnergyChartMode.PerDevice, Colours, EnUs);

        Assert.Equal(EnergyChartMode.PerDevice, chart.Mode);
        Assert.Equal(2, chart.Groups.Count);
        var group = chart.Groups[1];
        Assert.Equal(["PC", "Monitor"], group.Bars.Select(b => b.DeviceLabel));
        Assert.Equal(["#FF6600", "#0099FF"], group.Bars.Select(b => b.Colour));
        Assert.Equal("Fri 18:00 · Monitor · 0.05 kWh", group.Bars[1].Tooltip);
        Assert.Equal(0.29, group.Bars[0].Value, 9);
        Assert.False(chart.Groups[0].Bars[1].HasData);
        Assert.Equal([new ChartLegendItem("pc", "PC", "#FF6600"), new ChartLegendItem(MonitorId, "Monitor", "#0099FF")], chart.Legend);

        // The Y axis fits the tallest single bar, not the combined one.
        Assert.Equal(0.3, chart.YMax, 9);
        Assert.Equal(2, chart.Bars.Count);
    }

    [Fact]
    public void BuildFromSettingsUsesTheStoredModeAndColours()
    {
        var settings = new AppSettings { ChartMode = EnergyChartMode.PerDevice };
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor" });
        var chart = EnergyChartModel.Build(Data(), EnergyPeriod.Hours, settings, EnUs);

        Assert.Equal(EnergyChartMode.PerDevice, chart.Mode);
        Assert.Equal(DeviceColours.Scheme(DeviceColours.DefaultBase, 2), chart.Legend.Select(l => l.Colour));
    }

    [Fact]
    public void CombineSumsDeviceBuckets()
    {
        var combined = Data().Combined;
        Assert.Equal(0.20, combined[0].AcKwh, 9);
        Assert.True(combined[0].HasData);
        Assert.Equal(0.34, combined[1].AcKwh, 9);
        Assert.Equal(0.27, combined[1].DcEstKwh, 9);
        Assert.Empty(EnergyAggregator.Combine([]));
    }

    [Fact]
    public void PerDeviceHoursShowTheLast24HoursLabelledLikeCombined()
    {
        var hours = Enumerable.Range(0, 24).ToList();
        var pc = new DeviceEnergySeries("pc", "PC", hours.Select(h => Bucket(h, 0.1)).ToList());
        var monitor = new DeviceEnergySeries(MonitorId, "Monitor", hours.Select(h => Bucket(h, 0.02)).ToList());
        IReadOnlyList<DeviceEnergySeries> devices = [pc, monitor];
        var data = new EnergyChartData(EnergyAggregator.Combine(devices.Select(d => d.Buckets)), devices);

        var perDevice = EnergyChartModel.Build(data, EnergyPeriod.Hours, EnergyChartMode.PerDevice, Colours, EnUs);
        Assert.Equal(24, perDevice.Groups.Count);
        Assert.Equal(24, perDevice.Bars.Count);
        Assert.Equal("00", perDevice.Groups[0].Label);
        Assert.Null(perDevice.Groups[^1].Label);
        Assert.Equal("22", perDevice.Groups[^2].Label);
        Assert.Equal(12, perDevice.Groups.Count(g => g.Label is not null));
        Assert.All(perDevice.Groups.Where(g => g.Label is not null), g => Assert.Equal(0, int.Parse(g.Label!, CultureInfo.InvariantCulture) % EnergyChartModel.HourLabelEvery));
        Assert.All(perDevice.Groups, g => Assert.Equal(2, g.Bars.Count));

        var combined = EnergyChartModel.Build(data, EnergyPeriod.Hours, EnergyChartMode.Combined, Colours, EnUs);
        Assert.Equal(24, combined.Bars.Count);
        Assert.Equal(12, combined.Bars.Count(b => b.Label is not null));
    }
}
