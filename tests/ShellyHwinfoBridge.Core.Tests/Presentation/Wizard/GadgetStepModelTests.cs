using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

public class GadgetStepModelTests
{
    private const string Dimm1 = "DDR5 DIMM [#1]: Patriot Memory (PDP Systems) 6000 Series (P0 CHANNEL A/DIMM 1)";
    private const string Dimm3 = "DDR5 DIMM [#3]: Patriot Memory (PDP Systems) 6000 Series (P0 CHANNEL B/DIMM 1)";

    private static List<SensorSelection> Sensors() =>
    [
        new() { Name = "CPU Package Power", Category = SensorCategory.Cpu, Device = "CPU [#0]: Intel Core i9-14900K" },
        new() { Name = "GPU Power", Category = SensorCategory.Gpu },
        new() { Name = "Total Power", Category = SensorCategory.Ram, Device = Dimm1 },
        new() { Name = "Total Power", Category = SensorCategory.Ram, Device = Dimm3 },
    ];

    [Fact]
    public void MarkersFollowTheGadgetExport()
    {
        var export = new List<GadgetReading>
        {
            new(0, "CPU [#0]: Intel Core i9-14900K", "CPU Package Power", 120),
            new(1, "DDR5 DIMM [#1]", "Total Power", 1.1),
            new(2, "Shelly Power", "GPU Power", 1),
        };
        var model = new GadgetStepModel(() => export.ToList());
        model.Load(Sensors());

        Assert.True(model.ExportAvailable);
        Assert.Null(model.StatusText);
        Assert.Equal(
            [
                new GadgetSensorItem("CPU Package Power", true),
                new GadgetSensorItem("GPU Power", false),
                new GadgetSensorItem("Total Power (DDR5 DIMM [#1])", true),
                new GadgetSensorItem("Total Power (DDR5 DIMM [#3])", false),
            ],
            model.Items);
        Assert.False(model.AllReported);
        Assert.True(model.ShouldConfirmNext);
        Assert.Equal(["GPU Power", "Total Power (DDR5 DIMM [#3])"], model.Unreported);
        Assert.Contains("•  GPU Power", model.ConfirmText, StringComparison.Ordinal);
        Assert.Contains("•  Total Power (DDR5 DIMM [#3])", model.ConfirmText, StringComparison.Ordinal);
        Assert.Contains("HWiNFO Pro", model.ConfirmText, StringComparison.Ordinal);
        Assert.DoesNotContain("CPU Package Power", model.ConfirmText, StringComparison.Ordinal);

        // The user ticks the remaining sensors in HWiNFO: the next refresh turns every marker green.
        var changes = 0;
        model.Changed += (_, _) => changes++;
        export.Add(new(3, "GPU [#0]: NVIDIA GeForce RTX 4090", "GPU Power", 300));
        export.Add(new(4, "DDR5 DIMM [#3]", "Total Power", 0.9));
        model.Refresh();
        Assert.Equal(1, changes);
        Assert.True(model.AllReported);
        Assert.False(model.ShouldConfirmNext);
        Assert.Empty(model.Unreported);

        // Nothing changed: no event.
        model.Refresh();
        Assert.Equal(1, changes);
    }

    [Fact]
    public void WithoutHwinfoEverySensorIsUnreportedAndTheNoteSaysWhy()
    {
        var model = new GadgetStepModel(() => null);
        model.Load(Sensors());

        Assert.False(model.ExportAvailable);
        Assert.Equal(GadgetStepModel.NotRunningText, model.StatusText);
        Assert.Equal(4, model.Items.Count);
        Assert.All(model.Items, i => Assert.False(i.Reported));
        Assert.True(model.ShouldConfirmNext);
    }

    [Fact]
    public void SharedMemoryNoteFollowsTheSharedMemoryState()
    {
        var on = true;
        var model = new GadgetStepModel(() => [], () => on);
        model.Load(Sensors());
        Assert.True(model.SharedMemoryOn);
        Assert.Equal(GadgetStepModel.SharedMemoryOnText, model.SharedMemoryText);
        Assert.Equal(
            "Shared Memory is on, so Other Components already works. The markers show the gadget export, which keeps it working after Shared Memory turns off.",
            model.SharedMemoryText);
        Assert.Null(model.StatusText);

        var changes = 0;
        model.Changed += (_, _) => changes++;
        on = false;
        model.Refresh();
        Assert.Equal(1, changes);
        Assert.False(model.SharedMemoryOn);
        Assert.Null(model.SharedMemoryText);

        // Without a callback Shared Memory counts as off; without HWiNFO the not-running note shows instead.
        var none = new GadgetStepModel(() => null);
        none.Load(Sensors());
        Assert.Null(none.SharedMemoryText);
        Assert.Equal(GadgetStepModel.NotRunningText, none.StatusText);
    }

    [Fact]
    public void WithoutSensorsThereIsNothingToConfirm()
    {
        var model = new GadgetStepModel(() => []);
        model.Load([new SensorSelection { Name = "  " }]);

        Assert.Empty(model.Items);
        Assert.True(model.AllReported);
        Assert.False(model.ShouldConfirmNext);
    }

    [Fact]
    public void StepsEndStepThreeWithTheSensorList()
    {
        Assert.Equal(4, GadgetStepModel.Steps.Count);
        Assert.EndsWith("Yes.\n" + GadgetStepModel.EnableForText, GadgetStepModel.Steps[GadgetStepModel.SensorListStep], StringComparison.Ordinal);
        Assert.Contains("**Enable reporting to Gadget**", GadgetStepModel.Steps[1], StringComparison.Ordinal);
        Assert.Contains("**OK**", GadgetStepModel.Steps[3], StringComparison.Ordinal);
    }

    [Fact]
    public void GadgetMatchIsExactPerDeviceWhenTheDeviceIsKnown()
    {
        IReadOnlyList<GadgetReading> export = [new(0, "DDR5 DIMM [#1]", "Total Power", 1.1)];
        var sensors = Sensors();
        Assert.True(HwinfoMonitor.IsReportedToGadget(export, sensors[2]));
        Assert.False(HwinfoMonitor.IsReportedToGadget(export, sensors[3]));
        Assert.True(HwinfoMonitor.IsReportedToGadget(export, new SensorSelection { Name = "total power" }));
        Assert.False(HwinfoMonitor.IsReportedToGadget([new(0, "Shelly Power", "Total Power", 1)], new SensorSelection { Name = "Total Power" }));
    }

    [Fact]
    public void SensorLabelsNameTheDeviceOnlyForDuplicates()
    {
        Assert.Equal(
            ["CPU Package Power", "GPU Power", "Total Power (DDR5 DIMM [#1])", "Total Power (DDR5 DIMM [#3])"],
            SensorDisplay.Labels(Sensors()));
        Assert.Equal("DDR5 DIMM [#1]", SensorDisplay.ShortDevice(Dimm1));
        Assert.Equal("Memory", SensorDisplay.ShortDevice(" Memory "));
        Assert.Equal(string.Empty, SensorDisplay.ShortDevice(null));
    }

    [Fact]
    public void SensorPreviewEndsWithTheEffectiveOtherComponentsName()
    {
        var model = new SensorStepModel();
        model.Load(null, [new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu }]);
        Assert.Equal("PC Power (DC) − GPU Power = PC Other Components", model.CalculationPreview);

        var settings = new AppSettings();
        settings.SensorNames = new Dictionary<string, string> { [SensorKey.PcOther.Id] = "Rest of PC", [SensorKey.PcDc.Id] = "DC" };
        model.UseSensorNames(settings);
        Assert.Equal("DC − GPU Power = Rest of PC", model.CalculationPreview);
    }
}
