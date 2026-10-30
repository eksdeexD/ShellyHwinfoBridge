using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Hwinfo;

public class HwinfoSensorRegistryTests
{
    private const string Base = HwinfoSensorNames.BaseKeyPath;

    private readonly InMemoryRegistryBackend _backend = new();

    private HwinfoSensorRegistry Registry => new(_backend);

    private string? Value(string subkey, string name = "Value") => _backend.GetString(Base + "\\" + subkey, name);

    [Fact]
    public void EnsureKeysCreatesAllSensorsOnceThenReportsNothingCreated()
    {
        Assert.True(Registry.EnsureKeys());

        Assert.Equal("AC Power (Wall)", Value("Power0", "Name"));
        Assert.Equal("DC Power (Est.)", Value("Power1", "Name"));
        Assert.Equal("PSU Loss", Value("Power2", "Name"));
        Assert.Equal("Other Components", Value("Power3", "Name"));
        Assert.Equal("Shelly Online", Value("Other0", "Name"));
        Assert.Equal("W", Value("Power0", "Unit"));
        Assert.Equal(string.Empty, Value("Other0", "Unit"));
        Assert.Equal("0.0", Value("Power1"));
        Assert.Equal("0", Value("Other0"));
        Assert.Equal("Energy Today", Value("Other1", "Name"));
        Assert.Equal("Energy This Month", Value("Other2", "Name"));
        Assert.Equal("kWh", Value("Other1", "Unit"));
        Assert.Equal("kWh", Value("Other2", "Unit"));
        Assert.Equal("0.00", Value("Other1"));
        Assert.Equal("0.00", Value("Other2"));

        Assert.False(Registry.EnsureKeys());
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
        Assert.Equal("2.41", Value("Other1"));
        Assert.Equal("48.70", Value("Other2"));
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
        Registry.WriteFormula("\"DC Power (Est.)\" - \"GPU Power\"");
        _backend.DeleteTree(Base + "\\Power2");
        _backend.SetString(Base + "\\Power0", "Name", "Something else");

        Assert.True(Registry.EnsureKeys());
        Assert.Equal("PSU Loss", Value("Power2", "Name"));
        Assert.Equal("AC Power (Wall)", Value("Power0", "Name"));
        Assert.Equal("\"DC Power (Est.)\" - \"GPU Power\"", Value("Power3"));
    }

    [Fact]
    public void WritesReadingsWithDotDecimalUnderDanishCulture()
    {
        // SPEC acceptance test 9.
        using var _ = new CultureScope("da-DK");
        Registry.EnsureKeys();
        Registry.WriteReadings(151.9, 141.727, 10.173, online: true);

        Assert.Equal("151.9", Value("Power0"));
        Assert.Equal("141.7", Value("Power1"));
        Assert.Equal("10.2", Value("Power2"));
        Assert.Equal("1", Value("Other0"));
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
    public void SetOfflineZeroesValuesAndKeepsKeys()
    {
        Registry.EnsureKeys();
        Registry.WriteReadings(100, 90, 10, online: true);
        Registry.WriteEnergy(2.41, 48.7);
        Registry.SetOffline();

        Assert.Equal("2.41", Value("Other1"));
        Assert.Equal("48.70", Value("Other2"));
        Assert.Equal("0.0", Value("Power0"));
        Assert.Equal("0.0", Value("Power1"));
        Assert.Equal("0.0", Value("Power2"));
        Assert.Equal("0", Value("Other0"));
        Assert.Equal("AC Power (Wall)", Value("Power0", "Name"));
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
}
