using System.Globalization;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Hwinfo;

public class HwinfoGadgetRegistryReaderTests
{
    private const string Key = HwinfoGadgetRegistryReader.DefaultKeyPath;

    private readonly InMemoryRegistryBackend _registry = new();

    private void Export(int index, string? sensor, string? label, string? value, string? raw)
    {
        var n = index.ToString(CultureInfo.InvariantCulture);
        if (sensor is not null)
        {
            _registry.SetString(Key, "Sensor" + n, sensor);
        }

        if (label is not null)
        {
            _registry.SetString(Key, "Label" + n, label);
        }

        if (value is not null)
        {
            _registry.SetString(Key, "Value" + n, value);
        }

        if (raw is not null)
        {
            _registry.SetString(Key, "ValueRaw" + n, raw);
        }
    }

    [Fact]
    public void MissingKeyGivesNothing()
    {
        var reader = new HwinfoGadgetRegistryReader(_registry);

        Assert.Empty(reader.ReadAll());
        Assert.Null(reader.TryReadOtherComponents());
    }

    [Fact]
    public void ReadsSparseIndexes()
    {
        Export(0, "CPU [#0]: AMD Ryzen 7 7800X3D", "CPU Package Power", "62.3 W", "62.3");
        Export(7, "Shelly Power", "AC Power (Wall)", "312.4 W", "312.4");
        Export(41, "Shelly Power", "Other Components", "48.1 W", "48.1");
        var reader = new HwinfoGadgetRegistryReader(_registry);

        Assert.Equal([0, 7, 41], reader.ReadAll().Select(r => r.Index));
        Assert.Equal(48.1, reader.TryReadOtherComponents()!.Value, 6);
    }

    [Fact]
    public void IgnoresIndexesFromTheCapOn()
    {
        Export(HwinfoGadgetRegistryReader.MaxIndex, "Shelly Power", "Other Components", "48.1 W", "48.1");
        var reader = new HwinfoGadgetRegistryReader(_registry);

        Assert.Null(reader.TryReadOtherComponents());
    }

    [Fact]
    public void MissingLabelIsSkipped()
    {
        Export(0, "Shelly Power", null, "48.1 W", "48.1");
        Export(1, "Shelly Power", "  ", "10 W", "10");
        var reader = new HwinfoGadgetRegistryReader(_registry);

        Assert.Empty(reader.ReadAll());
        Assert.Null(reader.TryReadOtherComponents());
    }

    [Fact]
    public void PrefersTheShellyPowerSensorButAcceptsTheLabelAlone()
    {
        Export(0, "Some Other Device", "Other Components", "5 W", "5");
        Export(3, "Shelly Power", "other components", "48.1 W", "48.1");
        Assert.Equal(48.1, new HwinfoGadgetRegistryReader(_registry).TryReadOtherComponents()!.Value, 6);

        _registry.DeleteValue(Key, "Label3");
        Assert.Equal(5, new HwinfoGadgetRegistryReader(_registry).TryReadOtherComponents()!.Value, 6);
    }

    [Theory]
    [InlineData("48.1", "47 W", 48.1)]
    [InlineData(null, "48.1 W", 48.1)]
    [InlineData("", "48.1 W", 48.1)]
    [InlineData("n/a", "-3.5 W", -3.5)]
    [InlineData("48,1", null, 48.1)]
    [InlineData(null, "48,1 W", 48.1)]
    [InlineData("1234.5", null, 1234.5)]
    public void ParsesValueRawThenValue(string? raw, string? value, double expected)
    {
        Assert.True(HwinfoGadgetRegistryReader.TryParseValue(raw, value, out var parsed));
        Assert.Equal(expected, parsed, 6);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("abc", "W")]
    [InlineData("1,234,5", "")]
    public void RejectsUnparsableValues(string? raw, string? value) =>
        Assert.False(HwinfoGadgetRegistryReader.TryParseValue(raw, value, out _));

    [Fact]
    public void FallsBackToTheSecondRegistryWhenTheFirstHasNoExport()
    {
        var machine = new InMemoryRegistryBackend();
        machine.SetString(Key, "Sensor0", "Shelly Power");
        machine.SetString(Key, "Label0", "Other Components");
        machine.SetString(Key, "ValueRaw0", "21.5");

        Assert.Equal(21.5, new HwinfoGadgetRegistryReader(_registry, machine).TryReadOtherComponents()!.Value, 6);

        Export(9, "Shelly Power", "Other Components", "48.1 W", "48.1");
        Assert.Equal(48.1, new HwinfoGadgetRegistryReader(_registry, machine).TryReadOtherComponents()!.Value, 6);
    }

    [Fact]
    public void MatchesByLabelWhenIndexesMove()
    {
        Export(0, "Shelly Power", "Other Components", "48.1 W", "48.1");
        var reader = new HwinfoGadgetRegistryReader(_registry);
        Assert.Equal(48.1, reader.TryReadOtherComponents()!.Value, 6);

        // The user exports another value first: HWiNFO renumbers.
        _registry.DeleteTree(Key);
        Export(0, "GPU [#0]", "GPU Power", "88.6 W", "88.6");
        Export(1, "Shelly Power", "Other Components", "50.2 W", "50.2");
        Assert.Equal(50.2, reader.TryReadOtherComponents()!.Value, 6);
    }

    [Fact]
    public void ParsingDoesNotDependOnTheCurrentCulture()
    {
        using var _ = new CultureScope("da-DK");
        Export(2, "Shelly Power", "Other Components", "48,1 W", "48.1");

        Assert.Equal(48.1, new HwinfoGadgetRegistryReader(_registry).TryReadOtherComponents()!.Value, 6);
        Assert.True(HwinfoGadgetRegistryReader.TryParseValue(null, "1.5 W", out var dot));
        Assert.Equal(1.5, dot, 6);
    }
}
