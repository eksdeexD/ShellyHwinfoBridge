using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Hwinfo;

public class HwinfoSensorRegistryTests
{
    private const string Base = SensorNaming.BaseKeyPath;
    private const string MonitorId = "11111111-1111-1111-1111-111111111111";
    private const string LampId = "22222222-2222-2222-2222-222222222222";

    private readonly InMemoryRegistryBackend _backend = new();

    private HwinfoSensorRegistry Registry => new(_backend);

    private string? Value(string subkey, string name = "Value") => _backend.GetString(Base + "\\" + subkey, name);

    private static AppSettings WithDevices(params (string Id, string Label, bool Include)[] devices)
    {
        var settings = new AppSettings();
        settings.AdditionalDevices.AddRange(devices.Select(d => new AdditionalDeviceSettings { Id = d.Id, Label = d.Label, Host = "10.0.0.9", IncludeInTotals = d.Include }));
        settings.Normalize();
        return settings;
    }

    [Fact]
    public void EnsureKeysCreatesAllSensorsWithTheNewDefaultNamesOnceThenReportsNothingCreated()
    {
        Assert.True(Registry.EnsureKeys());

        Assert.Equal("PC Power (AC)", Value("Power0", "Name"));
        Assert.Equal("PC Power (DC)", Value("Power1", "Name"));
        Assert.Equal("PC PSU Loss", Value("Power2", "Name"));
        Assert.Equal("PC Other Components", Value("Power3", "Name"));
        Assert.Null(Value("Power4", "Name"));
        Assert.Equal("Shelly Online", Value("Other2", "Name"));
        Assert.Equal("W", Value("Power0", "Unit"));
        Assert.Equal(string.Empty, Value("Other2", "Unit"));
        Assert.Equal("0.0", Value("Power1"));
        Assert.Equal("0", Value("Other2"));
        Assert.Equal("Energy Today", Value("Other0", "Name"));
        Assert.Equal("Energy This Month", Value("Other1", "Name"));
        Assert.Equal("kWh", Value("Other0", "Unit"));
        Assert.Equal("kWh", Value("Other1", "Unit"));
        Assert.Equal("0.00", Value("Other0"));
        Assert.Equal("0.00", Value("Other1"));

        Assert.False(Registry.EnsureKeys());
    }

    [Fact]
    public void MigrationRenamesOldSensorsAndReplacesTheFormulaWithANumber()
    {
        // A version 1 installation: old names and a formula in Power3.
        _backend.SetString(Base + "\\Power0", "Name", "AC Power (Wall)");
        _backend.SetString(Base + "\\Power0", "Unit", "W");
        _backend.SetString(Base + "\\Power0", "Value", "312.4");
        _backend.SetString(Base + "\\Power3", "Name", "Other Components");
        _backend.SetString(Base + "\\Power3", "Unit", "W");
        _backend.SetString(Base + "\\Power3", "Value", "\"DC Power (Est.)\" - \"GPU Power\"");

        Assert.True(Registry.EnsureKeys());

        Assert.Equal("PC Power (AC)", Value("Power0", "Name"));
        Assert.Equal("312.4", Value("Power0"));
        Assert.Equal("PC Other Components", Value("Power3", "Name"));
        Assert.Equal("0.0", Value("Power3"));
    }

    [Fact]
    public void LayoutAssignsPowerAndOtherSubkeysInSpecOrder()
    {
        var layout = SensorLayout.Build(WithDevices((MonitorId, "Monitor", true), (LampId, "Lamp", false)));
        var map = layout.Slots.ToDictionary(s => s.Subkey, s => s.Name);

        Assert.Equal("PC Power (AC)", map["Power0"]);
        Assert.Equal("PC Power (DC)", map["Power1"]);
        Assert.Equal("PC PSU Loss", map["Power2"]);
        Assert.Equal("PC Other Components", map["Power3"]);
        Assert.Equal("Monitor Power (AC)", map["Power4"]);
        Assert.Equal("Lamp Power (AC)", map["Power5"]);
        Assert.Equal("Total Power (AC)", map["Power6"]);
        Assert.Equal("Shelly Online", map["Other2"]);
        Assert.Equal("Energy Today", map["Other0"]);
        Assert.Equal("Energy This Month", map["Other1"]);
        Assert.Equal("Monitor Online", map["Other3"]);
        Assert.Equal("Lamp Online", map["Other4"]);
        Assert.Equal(12, layout.Slots.Count);
        Assert.Equal("Power4", layout[SensorKey.DeviceAc(MonitorId)]!.Subkey);
        Assert.Empty(layout.DuplicateNames());
    }

    [Fact]
    public void LayoutWithoutAdditionalDevicesHasNoTotal()
    {
        Assert.False(SensorLayout.Default.Contains(SensorKey.Total));
        Assert.Equal(7, SensorLayout.Default.Slots.Count);
    }

    [Fact]
    public void LayoutUsesCustomNamesAndIgnoresInvalidOnes()
    {
        var settings = WithDevices((MonitorId, "Monitor", true));
        settings.SensorNames[SensorKey.PcAc.Id] = "Wall";
        settings.SensorNames[SensorKey.DeviceAc(MonitorId).Id] = "Screen";
        settings.SensorNames[SensorKey.PcDc.Id] = "  ";
        var layout = SensorLayout.Build(settings);

        Assert.Equal("Wall", layout[SensorKey.PcAc]!.Name);
        Assert.Equal("Screen", layout[SensorKey.DeviceAc(MonitorId)]!.Name);
        Assert.Equal("PC Power (DC)", layout[SensorKey.PcDc]!.Name);
    }

    [Fact]
    public void ApplyLayoutDeletesStaleSubkeysWhenADeviceIsRemovedAndShiftsLaterDevicesDown()
    {
        var registry = Registry;
        Assert.True(registry.ApplyLayout(SensorLayout.Build(WithDevices((MonitorId, "Monitor", true), (LampId, "Lamp", true)))));
        Assert.Equal("Total Power (AC)", Value("Power6", "Name"));
        Assert.Equal("Lamp Online", Value("Other4", "Name"));

        // Remove the monitor: the lamp moves to Power4/Other3, Total to Power5; Power6 and Other4 go away.
        Assert.True(registry.ApplyLayout(SensorLayout.Build(WithDevices((LampId, "Lamp", true)))));
        Assert.Equal("Lamp Power (AC)", Value("Power4", "Name"));
        Assert.Equal("Total Power (AC)", Value("Power5", "Name"));
        Assert.Equal("Lamp Online", Value("Other3", "Name"));
        Assert.False(_backend.KeyExists(Base + "\\Power6"));
        Assert.False(_backend.KeyExists(Base + "\\Other4"));

        // Back to the PC only.
        Assert.True(registry.ApplyLayout(SensorLayout.Default));
        Assert.False(_backend.KeyExists(Base + "\\Power4"));
        Assert.False(_backend.KeyExists(Base + "\\Other3"));
        Assert.False(registry.ApplyLayout(SensorLayout.Default));
    }

    [Fact]
    public void ApplyLayoutLeavesForeignSubkeysAlone()
    {
        _backend.SetString(Base + "\\Temp0", "Name", "Something");
        Registry.EnsureKeys();
        Assert.True(_backend.KeyExists(Base + "\\Temp0"));
    }

    [Fact]
    public void ApplyLayoutReportsARenameAsAChange()
    {
        var registry = Registry;
        registry.ApplyLayout(SensorLayout.Build(WithDevices((MonitorId, "Monitor", true))));
        Assert.True(registry.ApplyLayout(SensorLayout.Build(WithDevices((MonitorId, "Screen", true)))));
        Assert.Equal("Screen Power (AC)", Value("Power4", "Name"));
        Assert.Equal("Screen Online", Value("Other3", "Name"));
    }

    [Fact]
    public void EnsureKeysAddsEnergySensorsToAnExistingInstallAndKeepsTheirValues()
    {
        Registry.EnsureKeys();
        _backend.DeleteTree(Base + "\\Other1");
        _backend.DeleteTree(Base + "\\Other2");

        Assert.True(Registry.EnsureKeys());
        Registry.WriteEnergy(2.414, 48.7);
        Assert.False(Registry.EnsureKeys());
        Assert.Equal("2.41", Value("Other0"));
        Assert.Equal("48.70", Value("Other1"));
    }

    [Theory]
    [InlineData(2.414, "2.41")]
    [InlineData(2.416, "2.42")]
    [InlineData(1234.5, "1234.50")]
    [InlineData(0.004, "0.00")]
    [InlineData(-1, "0.00")]
    [InlineData(double.PositiveInfinity, "0.00")]
    public void KwhFormatterUsesTwoDecimalsInvariant(double value, string expected)
    {
        using var _ = new CultureScope("da-DK");
        Assert.Equal(expected, SensorValueFormatter.FormatKwh(value));
    }

    [Fact]
    public void EnsureKeysRepairsMissingKeyAndWrongNameButKeepsValues()
    {
        Registry.EnsureKeys();
        _backend.SetString(Base + "\\Power3", "Value", "48.1");
        _backend.DeleteTree(Base + "\\Power2");
        _backend.SetString(Base + "\\Power0", "Name", "Something else");

        Assert.True(Registry.EnsureKeys());
        Assert.Equal("PC PSU Loss", Value("Power2", "Name"));
        Assert.Equal("PC Power (AC)", Value("Power0", "Name"));
        Assert.Equal("48.1", Value("Power3"));
    }

    [Fact]
    public void WritesValuesWithDotDecimalUnderDanishCulture()
    {
        // SPEC acceptance test 9.
        using var _ = new CultureScope("da-DK");
        var registry = Registry;
        registry.ApplyLayout(SensorLayout.Build(WithDevices((MonitorId, "Monitor", true))));
        registry.WriteValues(new Dictionary<SensorKey, double>
        {
            [SensorKey.PcAc] = 151.9,
            [SensorKey.PcDc] = 141.727,
            [SensorKey.PcLoss] = 10.173,
            [SensorKey.PcOther] = 48.06,
            [SensorKey.PcOnline] = 1,
            [SensorKey.DeviceAc(MonitorId)] = 31.25,
            [SensorKey.DeviceOnline(MonitorId)] = 1,
            [SensorKey.Total] = 183.15,
        });

        Assert.Equal("151.9", Value("Power0"));
        Assert.Equal("141.7", Value("Power1"));
        Assert.Equal("10.2", Value("Power2"));
        Assert.Equal("48.1", Value("Power3"));
        Assert.Equal("31.3", Value("Power4"));
        Assert.Equal("183.2", Value("Power5"));
        Assert.Equal("1", Value("Other2"));
        Assert.Equal("1", Value("Other3"));
    }

    [Fact]
    public void WriteValuesIgnoresKeysOutsideTheLayout()
    {
        var registry = Registry;
        registry.EnsureKeys();
        registry.WriteValues(new Dictionary<SensorKey, double> { [SensorKey.Total] = 100, [SensorKey.DeviceAc(MonitorId)] = 5 });
        Assert.False(_backend.KeyExists(Base + "\\Power4"));
    }

    [Theory]
    [InlineData(1234.56, "1234.6")]
    [InlineData(0.04, "0.0")]
    [InlineData(-0.04, "0.0")]
    [InlineData(double.NaN, "0.0")]
    [InlineData(312.46, "312.5")]
    public void FormatterUsesOneDecimalInvariant(double value, string expected)
    {
        using var _ = new CultureScope("da-DK");
        Assert.Equal(expected, SensorValueFormatter.FormatWatts(value));
    }

    [Fact]
    public void SetOfflineZeroesEveryPowerAndOnlineValueAndKeepsKeysAndEnergy()
    {
        var registry = Registry;
        registry.ApplyLayout(SensorLayout.Build(WithDevices((MonitorId, "Monitor", true))));
        registry.WriteValues(new Dictionary<SensorKey, double>
        {
            [SensorKey.PcAc] = 100,
            [SensorKey.PcDc] = 90,
            [SensorKey.PcLoss] = 10,
            [SensorKey.PcOther] = 20,
            [SensorKey.PcOnline] = 1,
            [SensorKey.DeviceAc(MonitorId)] = 30,
            [SensorKey.DeviceOnline(MonitorId)] = 1,
            [SensorKey.Total] = 130,
        });
        registry.WriteEnergy(2.41, 48.7);
        registry.SetOffline();

        Assert.Equal("2.41", Value("Other0"));
        Assert.Equal("48.70", Value("Other1"));
        foreach (var subkey in new[] { "Power0", "Power1", "Power2", "Power3", "Power4", "Power5" })
        {
            Assert.Equal("0.0", Value(subkey));
        }

        Assert.Equal("0", Value("Other2"));
        Assert.Equal("0", Value("Other3"));
        Assert.Equal("PC Power (AC)", Value("Power0", "Name"));
    }

    [Fact]
    public void RemoveAllDeletesBaseKey()
    {
        _backend.SetString(@"Software\HWiNFO64\Sensors\Custom\Other Sensor\Power0", "Name", "x");
        Registry.EnsureKeys();
        Registry.RemoveAll();

        Assert.DoesNotContain(_backend.Keys.Keys, k => k.StartsWith(Base, StringComparison.OrdinalIgnoreCase));
        Assert.Single(_backend.Keys);
    }

    [Theory]
    [InlineData("PcAc", true)]
    [InlineData("DeviceAc:abc", true)]
    [InlineData("DeviceAc", false)]
    [InlineData("PcAc:abc", false)]
    [InlineData("Nope", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    public void SensorKeyParsesItsId(string id, bool valid)
    {
        Assert.Equal(valid, SensorKey.TryParse(id, out var key));
        if (valid)
        {
            Assert.Equal(id, key.Id);
        }
    }
}
