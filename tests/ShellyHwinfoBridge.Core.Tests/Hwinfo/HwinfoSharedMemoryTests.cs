using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Hwinfo;

public class HwinfoSharedMemoryTests
{
    private const uint Temp = 1;
    private const uint Volt = 2;
    private const uint Power = 5;

    private static HwinfoBufferBuilder Sample() => new HwinfoBufferBuilder()
        .Sensor("CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced")
        .Sensor("GPU [#0]: NVIDIA GeForce RTX 4080")
        .Sensor("Shelly Power")
        .Reading(0, Temp, "CPU (Tctl/Tdie)", "°C", 61.5)
        .Reading(0, Power, "CPU Package Power", "W", 48.25)
        .Reading(0, Power, "CPU Core Power (SVI3 TFN)", "W", 30.1)
        .Reading(0, Volt, "CPU Core Voltage (SVI3 TFN)", "V", 1.05)
        .Reading(1, Power, "GPU Power", "W", 210.4)
        .Reading(1, Power, "GPU Memory Power", "W", 12.2, userLabel: "GPU VRAM Power")
        .Reading(1, Volt, "GPU Core Voltage", "V", 0.95)
        .Reading(2, Power, "AC Power (Wall)", "W", 312.4)
        .Reading(2, Power, "DC Power (Est.)", "W", 294.8);

    [Fact]
    public void ParsesHeaderSensorsAndReadings()
    {
        var snapshot = HwinfoSharedMemoryParser.Parse(Sample().Build());

        Assert.Equal(2u, snapshot.Version);
        Assert.Equal(1u, snapshot.Revision);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_798_000_000), snapshot.PollTime);
        Assert.Equal(3, snapshot.Sensors.Count);
        Assert.Equal(9, snapshot.Readings.Count);

        var temp = snapshot.Readings[0];
        Assert.Equal(HwinfoReadingType.Temperature, temp.Type);
        Assert.Equal("°C", temp.Unit);
        Assert.Equal("CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced", temp.Device);
        Assert.Equal(61.5, temp.Value);
        Assert.Equal(60.5, temp.Min);
        Assert.Equal(62.5, temp.Max);

        var renamed = snapshot.Readings[5];
        Assert.Equal("GPU Memory Power", renamed.LabelOriginal);
        Assert.Equal("GPU VRAM Power", renamed.Label);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 24)]
    public void UsesHeaderStridesForOldAndNewLayouts(bool utf8, int extraBytes)
    {
        var builder = Sample();
        builder.WithUtf8 = utf8;
        builder.ExtraElementBytes = extraBytes;

        var snapshot = HwinfoSharedMemoryParser.Parse(builder.Build());

        Assert.Equal("DC Power (Est.)", snapshot.Readings[^1].Label);
        Assert.Equal(294.8, snapshot.Readings[^1].Value);
        Assert.Equal("Shelly Power", snapshot.Readings[^1].Device);
    }

    [Fact]
    public void GroupsWattReadingsByDevice()
    {
        var groups = HwinfoSharedMemoryParser.Parse(Sample().Build()).GetPowerSensors();

        Assert.Equal(["CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced", "GPU [#0]: NVIDIA GeForce RTX 4080", "Shelly Power"], groups.Select(g => g.Device));
        Assert.Equal(["CPU Package Power", "CPU Core Power (SVI3 TFN)"], groups[0].Readings.Select(r => r.Label));
        Assert.All(groups.SelectMany(g => g.Readings), r => Assert.Equal("W", r.Unit));
    }

    [Fact]
    public void PowerSensorsFeedTheSelectionHeuristics()
    {
        var groups = HwinfoSharedMemoryParser.Parse(Sample().Build()).GetPowerSensors();

        var suggested = SensorHeuristics.SuggestSelection(groups.SelectMany(g => g.Sensors));

        Assert.Equal(["CPU Package Power", "GPU Power"], suggested.Select(s => s.Name));
        Assert.Equal("GPU [#0]: NVIDIA GeForce RTX 4080", suggested[1].Device);
    }

    [Fact]
    public void ReaderReadsValuesByLabelAndDevice()
    {
        var buffer = Sample().Build();
        var reader = new HwinfoSharedMemoryReader(() => buffer);

        Assert.True(reader.IsAvailable);
        Assert.True(reader.TryReadValue("ac power (wall)", out var ac));
        Assert.Equal(312.4, ac);
        Assert.True(reader.TryReadValue("DC Power (Est.)", "Shelly Power", out var dc));
        Assert.Equal(294.8, dc);
        Assert.True(reader.TryReadValue("GPU Memory Power", out _));
        Assert.False(reader.TryReadValue("Other Components", out _));
        Assert.False(reader.TryReadValue("GPU Power", "Shelly Power", out _));
        Assert.Equal(9, reader.GetReadings().Count);
    }

    [Fact]
    public void DeviceMatchesHwinfoPrefixedName()
    {
        var buffer = new HwinfoBufferBuilder()
            .Sensor("Custom: Shelly Power")
            .Reading(0, Power, "AC Power (Wall)", "W", 100)
            .Build();

        Assert.NotNull(HwinfoSharedMemoryParser.Parse(buffer).FindReading("AC Power (Wall)", "Shelly Power"));
    }

    [Fact]
    public void ReaderIsUnavailableWhenMissingInactiveOrCorrupt()
    {
        Assert.False(new HwinfoSharedMemoryReader(() => null).IsAvailable);
        Assert.Empty(new HwinfoSharedMemoryReader(() => null).GetPowerSensors());

        var dead = Sample();
        dead.Signature = "DAED";
        Assert.False(new HwinfoSharedMemoryReader(dead.Build).IsAvailable);

        var truncated = Sample().Build()[..500];
        Assert.False(new HwinfoSharedMemoryReader(() => truncated).IsAvailable);
        Assert.False(new HwinfoSharedMemoryReader(() => truncated).TryReadValue("GPU Power", out _));

        Assert.False(new HwinfoSharedMemoryReader(() => throw new IOException("gone")).IsAvailable);
    }

    [Fact]
    public void RequiredLengthCoversBothSections()
    {
        var buffer = Sample().Build();
        Assert.Equal(buffer.Length, HwinfoSharedMemoryParser.GetRequiredLength(buffer.AsSpan(0, HwinfoSharedMemoryParser.HeaderSize)));
        Assert.Equal(0, HwinfoSharedMemoryParser.GetRequiredLength(buffer.AsSpan(0, 10)));
    }

    [Fact]
    public void RejectsImplausibleHeader()
    {
        var buffer = Sample().Build();
        BitConverter.GetBytes(10u).CopyTo(buffer, 36); // reading element smaller than the SDK minimum
        Assert.Throws<HwinfoSharedMemoryException>(() => HwinfoSharedMemoryParser.Parse(buffer));
    }
}
