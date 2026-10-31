using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Sensors;

namespace ShellyHwinfoBridge.Core.Tests.Sensors;

public class SensorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bad \"name\"")]
    public void SensorNamesMustBeNonBlankWithoutQuotes(string name)
    {
        Assert.False(SensorNaming.IsValidName(name));
    }

    [Fact]
    public void SensorNamesAcceptNormalNamesUpToTheLimit()
    {
        Assert.True(SensorNaming.IsValidName("  GPU Power "));
        Assert.True(SensorNaming.IsValidName(new string('x', SensorNaming.MaxNameLength)));
        Assert.False(SensorNaming.IsValidName(new string('x', SensorNaming.MaxNameLength + 1)));
    }

    [Fact]
    public void DetectsDuplicateNamesInSelection()
    {
        var warnings = SensorSelectionAnalyzer.FindDuplicateNames(["GPU Power", "CPU Package Power", "gpu power"]);
        var w = Assert.Single(warnings);
        Assert.Equal(("GPU Power", 2), (w.Name, w.Count));
    }

    [Fact]
    public void DetectsSelectedNameDuplicatedAmongAllSensors()
    {
        var warnings = SensorSelectionAnalyzer.FindDuplicateNames(
            ["GPU Power"],
            ["GPU Power", "GPU Power", "CPU Package Power"]);
        Assert.Equal(2, Assert.Single(warnings).Count);
        Assert.Empty(SensorSelectionAnalyzer.FindDuplicateNames(["CPU Package Power"], ["GPU Power", "GPU Power", "CPU Package Power"]));
    }

    [Fact]
    public void DetectsSubComponentOfPackagePowerOnSameDevice()
    {
        var package = new SensorInfo("CPU [#0]: Intel Core i9", "CPU Package Power");
        var cores = new SensorInfo("CPU [#0]: Intel Core i9", "IA Cores Power");
        var gpu = new SensorInfo("GPU [#0]: NVIDIA", "GPU Power");
        var otherDeviceCores = new SensorInfo("CPU [#1]: Something", "IA Cores Power");

        var warnings = SensorSelectionAnalyzer.FindSubComponentPairs([package, cores, gpu, otherDeviceCores]);

        var w = Assert.Single(warnings);
        Assert.Equal((package, cores), (w.Aggregate, w.SubComponent));
    }

    [Fact]
    public void TwoAggregatesOnSameDeviceReportedOnce()
    {
        var a = new SensorInfo("GPU [#0]", "GPU Power");
        var b = new SensorInfo("GPU [#0]", "Total Board Power");
        Assert.Single(SensorSelectionAnalyzer.FindSubComponentPairs([a, b]));
    }

    [Fact]
    public void ManualEntriesWithoutDeviceAreNeverPaired()
    {
        Assert.Empty(SensorSelectionAnalyzer.FindSubComponentPairs([new("", "CPU Package Power"), new("", "CPU Core Power")]));
    }

    [Theory]
    [InlineData("CPU Package Power", SensorCategory.Cpu)]
    [InlineData("GPU Power", SensorCategory.Gpu)]
    [InlineData("GPU Board Power", SensorCategory.Gpu)]
    [InlineData("Total Board Power", SensorCategory.Gpu)]
    [InlineData("DIMM1 Power", SensorCategory.Ram)]
    [InlineData("DIMM [#3] Power", SensorCategory.Ram)]
    [InlineData("IA Cores Power", null)]
    [InlineData("CPU Core Power", null)]
    [InlineData("PL1 Power Limit", null)]
    public void CandidateHeuristics(string name, SensorCategory? expected)
    {
        Assert.Equal(expected, SensorHeuristics.GetCandidateCategory(name));
    }

    [Theory]
    [InlineData("CPU SoC Power", SensorCategory.Cpu)]
    [InlineData("GPU 16-pin HVPWR Power", SensorCategory.Gpu)]
    [InlineData("DRAM Power", SensorCategory.Ram)]
    [InlineData("Capture Card Power", SensorCategory.OtherMeasured)]
    public void GuessCategory(string name, SensorCategory expected)
    {
        Assert.Equal(expected, SensorHeuristics.GuessCategory(name));
    }

    [Fact]
    public void SuggestSelectionPicksOneTotalPerDeviceAndAllDimms()
    {
        SensorInfo[] available =
        [
            new("CPU", "CPU Package Power"),
            new("CPU", "IA Cores Power"),
            new("GPU", "GPU Power"),
            new("GPU", "Total Board Power"),
            new("GPU", "GPU Core (NVVDD) Input Power"),
            new("DIMM", "DIMM1 Power"),
            new("DIMM", "DIMM3 Power"),
        ];

        var suggested = SensorHeuristics.SuggestSelection(available).Select(s => s.Name);
        Assert.Equal(["CPU Package Power", "GPU Power", "DIMM1 Power", "DIMM3 Power"], suggested);
    }

    private const string Dimm1 = "DDR5 DIMM [#1]: Patriot Memory (PDP Systems) 6000 Series (P0 CHANNEL A/DIMM 1)";
    private const string Dimm3 = "DDR5 DIMM [#3]: Patriot Memory (PDP Systems) 6000 Series (P0 CHANNEL B/DIMM 1)";

    [Theory]
    [InlineData("Total Power", Dimm1, SensorCategory.Ram)]
    [InlineData("Total Power", Dimm3, SensorCategory.Ram)]
    [InlineData("Total Power", "DRAM [#0]: Module", SensorCategory.Ram)]
    [InlineData("Total Power", "GPU [#0]: NVIDIA GeForce RTX 4090", null)]
    [InlineData("CPU Package Power", "CPU [#0]: Intel Core i9-14900K", SensorCategory.Cpu)]
    [InlineData("DIMM1 Power", "Memory", SensorCategory.Ram)]
    [InlineData("IA Cores Power", "CPU [#0]: Intel Core i9-14900K", null)]
    public void CandidateHeuristicsUseTheDevice(string name, string device, SensorCategory? expected)
    {
        Assert.Equal(expected, SensorHeuristics.GetCandidateCategory(name, device));
    }

    [Fact]
    public void GuessCategoryUsesAMemoryDevice()
    {
        Assert.Equal(SensorCategory.Ram, SensorHeuristics.GuessCategory("Total Power", Dimm1));
        Assert.Equal(SensorCategory.Cpu, SensorHeuristics.GuessCategory("CPU SoC Power", "Board"));
        Assert.Equal(SensorCategory.OtherMeasured, SensorHeuristics.GuessCategory("Total Power", "Capture card"));
        Assert.True(SensorHeuristics.IsMemoryDevice(Dimm3));
        Assert.False(SensorHeuristics.IsMemoryDevice(null));
    }

    [Fact]
    public void SuggestSelectionPreChecksTheTotalPowerOfEachDimmDevice()
    {
        // Stock HWiNFO names (work/ui-pass/rampowernames.png): a "Total Power" W sensor under each "DDR5 DIMM [#n]" device.
        SensorInfo[] available =
        [
            new("CPU [#0]: Intel Core i9-14900K", "CPU Package Power"),
            new("GPU [#0]: NVIDIA GeForce RTX 4090", "GPU Power"),
            new("GPU [#0]: NVIDIA GeForce RTX 4090", "Total Power"),
            new(Dimm1, "Total Power"),
            new(Dimm3, "Total Power"),
        ];

        var suggested = SensorHeuristics.SuggestSelection(available);
        Assert.Equal(
            [("CPU [#0]: Intel Core i9-14900K", "CPU Package Power"), ("GPU [#0]: NVIDIA GeForce RTX 4090", "GPU Power"), (Dimm1, "Total Power"), (Dimm3, "Total Power")],
            suggested.Select(s => (s.Device, s.Name)));
    }

    [Fact]
    public void SuggestSelectionPicksOnePowerSensorPerMemoryDevice()
    {
        SensorInfo[] available =
        [
            new(Dimm1, "VDD Power"),
            new(Dimm1, "Total Power"),
            new("DIMM board", "DIMM1 Power"),
            new("DIMM board", "Other Power"),
        ];

        // A memory device with a "DIMMn Power" sensor keeps the name rule only.
        var suggested = SensorHeuristics.SuggestSelection(available);
        Assert.Equal([(Dimm1, "Total Power"), ("DIMM board", "DIMM1 Power")], suggested.Select(s => (s.Device, s.Name)));
    }
}
