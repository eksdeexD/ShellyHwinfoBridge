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
}
