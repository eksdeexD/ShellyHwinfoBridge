using System.Globalization;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

public class SensorStepModelTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static SensorStepModel Loaded(IReadOnlyList<SensorSelection>? existing = null, params ShellyHwinfoBridge.Core.Hwinfo.SharedMemory.HwinfoReading[] extra)
    {
        var m = new SensorStepModel(EnUs);
        m.Load(Snapshots.Desktop(extra).GetPowerSensors(), existing ?? []);
        return m;
    }

    private static SensorRow Row(SensorStepModel m, string name) => m.Groups.SelectMany(g => g.Rows).Single(r => r.Name == name);

    [Fact]
    public void PreChecksTheHeuristicCandidatesAndSkipsTheBridgesOwnSensors()
    {
        var m = Loaded(null, Snapshots.Bridge());

        Assert.False(m.IsManual);
        Assert.Equal([Snapshots.Cpu, Snapshots.Gpu, Snapshots.Memory], m.Groups.Select(g => g.Device));
        Assert.Equal(["CPU Package Power", "GPU Power", "DIMM1 Power", "DIMM3 Power"], m.Selected.Select(s => s.Name));
        Assert.Equal([SensorCategory.Cpu, SensorCategory.Gpu, SensorCategory.Ram, SensorCategory.Ram], m.Selected.Select(s => s.Category));
        Assert.False(Row(m, "IA Cores Power").Checked);
        Assert.Null(Row(m, "IA Cores Power").Category);
        Assert.Equal("142.3 W", Row(m, "CPU Package Power").ValueText);
        Assert.Equal(Snapshots.Cpu, m.Selected[0].Device);
        Assert.Empty(m.Warnings);
        Assert.Equal("PC Power (DC) − CPU Package Power − GPU Power − DIMM1 Power − DIMM3 Power = PC Other Components", m.CalculationPreview);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void ExistingSelectionWinsAndMissingSensorsStaySelected()
    {
        var m = Loaded(
        [
            new SensorSelection { Name = "IA Cores Power", Category = SensorCategory.Cpu, Device = Snapshots.Cpu },
            new SensorSelection { Name = "Capture Card Power", Category = SensorCategory.OtherMeasured },
        ]);

        Assert.Equal(["IA Cores Power", "Capture Card Power"], m.Selected.Select(s => s.Name));
        Assert.False(Row(m, "CPU Package Power").Checked);
        var missing = m.Groups[^1];
        Assert.Equal(SensorStepModel.MissingGroup, missing.Device);
        Assert.Null(m.Selected[1].Device);
    }

    [Fact]
    public void CategoryAndCheckboxAreLinked()
    {
        var m = Loaded();
        var ia = Row(m, "IA Cores Power");

        m.SetCategory(ia.Id, SensorCategory.OtherMeasured);
        Assert.True(Row(m, "IA Cores Power").Checked);
        Assert.Equal(SensorCategory.OtherMeasured, Row(m, "IA Cores Power").Category);

        m.SetCategory(ia.Id, null);
        Assert.False(Row(m, "IA Cores Power").Checked);

        m.SetChecked(ia.Id, true);
        Assert.Equal(SensorCategory.Cpu, Row(m, "IA Cores Power").Category);
        m.SetChecked(ia.Id, false);
        Assert.Null(Row(m, "IA Cores Power").Category);
    }

    [Fact]
    public void WarnsAboutSubComponentPairs()
    {
        var m = Loaded();
        m.SetChecked(Row(m, "IA Cores Power").Id, true);
        Assert.Equal(["IA Cores Power is part of CPU Package Power. Selecting both would count it twice."], m.Warnings);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void SensorsPickedFromHwinfoAreToldApartByDevice()
    {
        // Picked from HWiNFO's list, a sensor is read by name and device: a second "GPU Power" is not ambiguous.
        var m = Loaded(null, Snapshots.Watts(5, "GPU [#1]: AMD Radeon RX 7600", "GPU Power", 40));
        Assert.Empty(m.Warnings);
        Assert.All(m.Selected, s => Assert.NotNull(s.Device));
    }

    [Fact]
    public void WarnsWhenNothingIsSelected()
    {
        var m = Loaded();
        foreach (var row in m.Selected.ToList())
        {
            m.SetChecked(Row(m, row.Name).Id, false);
        }

        Assert.Equal(["No sensors selected: Other Components will equal PC Power (DC)."], m.Warnings);
        Assert.Equal("PC Power (DC) = PC Other Components", m.CalculationPreview);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void InvalidNamesBlockNextAndAreLeftOutOfTheFormula()
    {
        var m = Loaded(null, Snapshots.Watts(6, "Capture", "Card \"X\" Power", 5));
        m.SetChecked(Row(m, "Card \"X\" Power").Id, true);

        Assert.False(m.IsValid);
        Assert.Contains("\"Card X Power\" contains a double quote and can't be used. Rename it in HWiNFO (right-click the sensor › Rename).", m.Warnings);
        Assert.DoesNotContain("Card", m.CalculationPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyToKeepsARetiredCustomFormula()
    {
        var m = Loaded();
        var s = new AppSettings { CustomFormula = "x" };
        m.ApplyTo(s);
        Assert.Equal("x", s.CustomFormula);
        Assert.Equal(4, s.Sensors.Count);
    }

    [Fact]
    public void ManualFallbackWithoutSharedMemory()
    {
        var m = new SensorStepModel(EnUs);
        m.Load(null, [new SensorSelection { Name = "CPU Package Power", Category = SensorCategory.Cpu, Device = Snapshots.Cpu }]);

        Assert.True(m.IsManual);
        Assert.Empty(m.Groups);
        Assert.Equal("CPU Package Power", Assert.Single(m.ManualRows).Name);

        var id = m.AddManual();
        m.SetManualName(id, "GPU Power");
        Assert.Equal(SensorCategory.Gpu, m.ManualRows[1].Category);
        m.SetManualCategory(id, SensorCategory.OtherMeasured);
        var blank = m.AddManual();
        Assert.Equal(2, m.Selected.Count);

        m.RemoveManual(blank);
        m.RemoveManual(m.ManualRows[0].Id);
        Assert.Equal("GPU Power", Assert.Single(m.Selected).Name);
        Assert.Equal(SensorCategory.OtherMeasured, m.Selected[0].Category);

        Assert.Equal("PC Power (DC) − GPU Power = PC Other Components", m.CalculationPreview);
    }

    [Fact]
    public void ManualDuplicatesAreFlagged()
    {
        var m = new SensorStepModel(EnUs);
        m.Load(null, []);
        m.SetManualName(m.AddManual(), "GPU Power");
        m.SetManualName(m.AddManual(), "gpu power");
        Assert.Contains(m.Warnings, w => w.StartsWith("Two sensors are called \"GPU Power\"", StringComparison.Ordinal));
    }
}
