using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Settings;

public sealed class SettingsV2Tests : IDisposable
{
    /// <summary>A settings file as version 1 of the app wrote it (enum names as strings, camelCase).</summary>
    private const string Version1Json = """
        {
          "schemaVersion": 1,
          "device": {
            "host": "192.168.1.42",
            "name": "PlugMGen3-2",
            "model": "S3PL-10112EU",
            "protectedPassword": "enc:terces"
          },
          "psu": {
            "mode": "Database",
            "databaseId": "seasonic-prime-fanless-tx-700",
            "genericWattage": null,
            "genericRating": null,
            "manualCurve": null
          },
          "voltageMode": "Auto",
          "smoothingSamples": 3,
          "pollIntervalMs": 1000,
          "httpTimeoutMs": 800,
          "sensors": [
            { "name": "CPU Package Power", "category": "Cpu", "device": "CPU [#0]: AMD Ryzen 7 7800X3D" },
            { "name": "GPU Power", "category": "Gpu", "device": null }
          ],
          "customFormula": "\"DC Power (Est.)\" - \"CPU Package Power\" - 10",
          "hideTrayIcon": false,
          "startWithWindows": true,
          "energyLoggingEnabled": true,
          "energyPricePerKwh": 0.32,
          "energyCurrency": "DKK",
          "databaseUpdateUrl": null
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shb-v2-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private SettingsStore Store()
    {
        Directory.CreateDirectory(_dir);
        return new SettingsStore(Path.Combine(_dir, "settings.json"), new FakePasswordProtector());
    }

    [Fact]
    public void Version1FileLoadsUnchangedWithV2Defaults()
    {
        var store = Store();
        File.WriteAllText(store.FilePath, Version1Json);

        var settings = store.Load()!;

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal("192.168.1.42", settings.Device.Host);
        Assert.Equal("PlugMGen3-2", settings.Device.Name);
        Assert.Equal("secret", store.GetPassword(settings));
        Assert.Equal("seasonic-prime-fanless-tx-700", settings.Psu.DatabaseId);
        Assert.Equal(3, settings.SmoothingSamples);
        Assert.Equal(["CPU Package Power", "GPU Power"], settings.Sensors.Select(s => s.Name));
        Assert.Equal(SensorCategory.Gpu, settings.Sensors[1].Category);
        Assert.True(settings.StartWithWindows);
        Assert.Equal(0.32m, settings.EnergyPricePerKwh);
        Assert.Equal("\"DC Power (Est.)\" - \"CPU Package Power\" - 10", settings.CustomFormula);

        Assert.Empty(settings.AdditionalDevices);
        Assert.Empty(settings.SensorNames);
        Assert.Equal(EnergyChartMode.Combined, settings.ChartMode);
        Assert.Equal(ColourMode.AutomaticScheme, settings.ColourMode);
        Assert.Equal(DeviceColours.DefaultBase, settings.BaseColour);
        Assert.False(settings.SmoothComponents);
        Assert.Null(settings.Device.Colour);
    }

    [Fact]
    public void Version2RoundTripsEveryNewField()
    {
        var store = Store();
        var settings = new AppSettings { Device = new ShellyDeviceSettings { Host = "10.0.0.2", Colour = "#112233" } };
        var monitor = new AdditionalDeviceSettings { Label = "Monitor", Host = "10.0.0.3", IncludeInTotals = false, Colour = "#abcdef" };
        settings.AdditionalDevices.Add(monitor);
        store.SetDevicePassword(monitor, "mon");
        settings.SensorNames[SensorKey.PcAc.Id] = "Wall";
        settings.SensorNames[SensorKey.DeviceAc(monitor.Id).Id] = "Screen";
        settings.ChartMode = EnergyChartMode.PerDevice;
        settings.ColourMode = ColourMode.Custom;
        settings.BaseColour = "#00AA00";
        settings.SmoothComponents = true;
        settings.CustomFormula = "kept";
        store.Save(settings);

        var loaded = store.Load()!;
        var device = Assert.Single(loaded.AdditionalDevices);
        Assert.Equal(monitor.Id, device.Id);
        Assert.Equal("Monitor", device.Label);
        Assert.False(device.IncludeInTotals);
        Assert.Equal("#ABCDEF", device.Colour);
        Assert.Equal("mon", store.GetDevicePassword(device));
        Assert.Equal("#112233", loaded.Device.Colour);
        Assert.Equal("Wall", loaded.SensorNames["PcAc"]);
        Assert.Equal("Screen", loaded.SensorNames["DeviceAc:" + monitor.Id]);
        Assert.Equal(EnergyChartMode.PerDevice, loaded.ChartMode);
        Assert.Equal(ColourMode.Custom, loaded.ColourMode);
        Assert.Equal("#00AA00", loaded.BaseColour);
        Assert.True(loaded.SmoothComponents);
        Assert.Equal("kept", loaded.CustomFormula);
        Assert.Contains("\"additionalDevices\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Monitor", null)]
    [InlineData("  Monitor  ", null)]
    [InlineData("TV", null)]
    [InlineData("", "Enter a label.")]
    [InlineData("   ", "Enter a label.")]
    [InlineData("PC", "\"PC\" is reserved for the PC's plug.")]
    [InlineData("pc", "\"PC\" is reserved for the PC's plug.")]
    [InlineData("My \"big\" screen", "The label can't contain a double quote.")]
    [InlineData("monitor", "Another device already has this label.")]
    [InlineData("Total", "This label would give a HWiNFO sensor the same name as another one. Choose another label.")]
    [InlineData("Energy Today", null)]
    [InlineData("Shelly", null)]
    public void LabelRules(string label, string? error)
    {
        var others = label.Trim() == "monitor" ? new[] { "Monitor" } : Array.Empty<string>();
        Assert.Equal(error, DeviceLabelRules.Validate(label, others));
    }

    [Fact]
    public void LabelLengthIsOneToTwentyFourCharacters()
    {
        Assert.Null(DeviceLabelRules.Validate(new string('a', 24), []));
        Assert.Equal("Use at most 24 characters.", DeviceLabelRules.Validate(new string('a', 25), []));
        Assert.Null(DeviceLabelRules.Validate("A", []));
    }

    [Fact]
    public void NextDefaultLabelSkipsUsedLabels()
    {
        Assert.Equal("Monitor", DeviceLabelRules.NextDefaultLabel([]));
        Assert.Equal("Monitor 2", DeviceLabelRules.NextDefaultLabel(["monitor"]));
        Assert.Equal("Monitor 3", DeviceLabelRules.NextDefaultLabel(["Monitor", "Monitor 2"]));
    }

    [Fact]
    public void NormalizeCapsTheDeviceListAndRepairsIdsLabelsAndColours()
    {
        var settings = new AppSettings();
        var first = new AdditionalDeviceSettings { Id = "not-a-guid", Label = "Monitor", Host = " 10.0.0.3 ", Colour = "ff0000" };
        settings.AdditionalDevices.Add(first);
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = first.Id, Label = "monitor", Colour = "nope" });
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = "PC" });
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = "Fourth" });
        settings.AdditionalDevices.Add(null!);
        settings.SensorNames["PcAc"] = "Bad \"name\"";
        settings.SensorNames["Nope"] = "x";
        settings.SensorNames["PcDc"] = "  DC  ";
        settings.BaseColour = "orange";
        settings.ChartMode = (EnergyChartMode)42;

        settings.Normalize();

        Assert.Equal(AppSettings.MaxDevices - 1, settings.AdditionalDevices.Count);
        Assert.Equal(AppSettings.MaxDevices, settings.DeviceCount);
        Assert.All(settings.AdditionalDevices, d => Assert.True(Guid.TryParse(d.Id, out _)));
        Assert.Equal(3, settings.AdditionalDevices.Select(d => d.Id).Distinct().Count());
        Assert.Equal("10.0.0.3", settings.AdditionalDevices[0].Host);
        Assert.Equal("#FF0000", settings.AdditionalDevices[0].Colour);
        Assert.Null(settings.AdditionalDevices[1].Colour);
        Assert.Equal(["Monitor", "Monitor 2", "Monitor 3"], settings.AdditionalDevices.Select(d => d.Label));
        Assert.Equal(new Dictionary<string, string> { ["PcDc"] = "DC" }, settings.SensorNames);
        Assert.Equal(DeviceColours.DefaultBase, settings.BaseColour);
        Assert.Equal(EnergyChartMode.Combined, settings.ChartMode);
    }

    [Fact]
    public void DeviceHelpers()
    {
        var settings = new AppSettings();
        var monitor = new AdditionalDeviceSettings { Label = "Monitor", IncludeInTotals = false };
        settings.AdditionalDevices.Add(monitor);

        Assert.True(settings.HasAdditionalDevices);
        Assert.Equal([AppSettings.PrimaryDeviceId, monitor.Id], settings.GetDeviceIds());
        Assert.Equal("PC", settings.GetDeviceLabel("pc"));
        Assert.Equal("Monitor", settings.GetDeviceLabel(monitor.Id.ToUpperInvariant()));
        Assert.True(settings.IsIncludedInTotals("pc"));
        Assert.False(settings.IsIncludedInTotals(monitor.Id));
        Assert.Null(settings.GetDeviceLabel("unknown"));
    }

    [Fact]
    public void CloneIsDeep()
    {
        var settings = new AppSettings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = "Monitor" });
        settings.SensorNames["PcAc"] = "Wall";
        var clone = settings.Clone();
        clone.AdditionalDevices[0].Label = "TV";
        clone.SensorNames["PcAc"] = "x";

        Assert.Equal("Monitor", settings.AdditionalDevices[0].Label);
        Assert.Equal("Wall", settings.SensorNames["PcAc"]);
        Assert.Equal(settings.AdditionalDevices[0].Id, clone.AdditionalDevices[0].Id);
    }
}
