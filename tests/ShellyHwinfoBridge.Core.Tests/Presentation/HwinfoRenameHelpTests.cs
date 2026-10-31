using System.Globalization;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class HwinfoRenameHelpTests
{
    private const string MonitorId = "11111111-1111-1111-1111-111111111111";
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private readonly FakeRuntime _runtime = new();

    private static Task<IReadOnlyList<DiscoveredShelly>> Discover(CancellationToken _) =>
        Task.FromResult<IReadOnlyList<DiscoveredShelly>>([WizardFixture.Plug("PlugSG3-Monitor", "192.168.1.50")]);

    private void AddMonitor(Dictionary<string, string>? names = null)
    {
        var settings = _runtime.Settings!.Clone();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor", Host = "192.168.1.50", IncludeInTotals = true });
        if (names is not null)
        {
            settings.SensorNames = names;
        }

        _runtime.Settings = settings;
    }

    [Fact]
    public void DeviceRelabelListsOnlyTheAcSensor()
    {
        var help = HwinfoRenameHelp.ForDeviceRelabel(MonitorId, "Monitor", " Desk ");

        Assert.Equal(
            [new SensorRename("Monitor Power (AC)", "Desk Power (AC)")],
            help.Renames);
        Assert.Equal(["Monitor Power (AC) → Desk Power (AC)"], help.RenameLines);
    }

    [Fact]
    public void DeviceRelabelSkipsSensorsWithACustomName()
    {
        var names = new Dictionary<string, string> { ["DeviceAc:" + MonitorId] = "Screen Watts" };

        var help = HwinfoRenameHelp.ForDeviceRelabel(MonitorId, "Monitor", "Desk", names);

        Assert.Empty(help.Renames);
    }

    [Fact]
    public void UnchangedBlankAndDuplicatePairsAreSkipped()
    {
        var help = new HwinfoRenameHelp(
        [
            new SensorRename("A", "A"),
            new SensorRename(" ", "B"),
            new SensorRename("C", "D"),
            new SensorRename("C ", " D"),
        ]);

        Assert.Equal([new SensorRename("C", "D")], help.Renames);
        Assert.Empty(HwinfoRenameHelp.ForDeviceRelabel(MonitorId, "Monitor", "Monitor").Renames);
    }

    [Fact]
    public void SensorNamesListsOnlyChangedEffectiveNamesInOrder()
    {
        var before = new Dictionary<string, string> { ["PcAc"] = "PC Power (AC)", ["PcDc"] = "PC Power (DC)", ["PcLoss"] = "PC PSU Loss" };
        var after = new[]
        {
            new KeyValuePair<string, string>("PcAc", "Wall Power"),
            new KeyValuePair<string, string>("PcDc", "PC Power (DC)"),
            new KeyValuePair<string, string>("PcLoss", "Loss"),
            new KeyValuePair<string, string>("Unknown", "X"),
        };

        var help = HwinfoRenameHelp.ForSensorNames(before, after);

        Assert.Equal([new SensorRename("PC Power (AC)", "Wall Power"), new SensorRename("PC PSU Loss", "Loss")], help.Renames);
    }

    [Fact]
    public void DeviceFormBuildsTheHelpForAPendingRelabel()
    {
        AddMonitor(new Dictionary<string, string> { ["PcDc"] = "DC Est" });
        var form = new ManageShellysModel(_runtime, Discover, EnUs).BeginEdit(MonitorId)!;
        Assert.Empty(form.RenameHelp.Renames);

        form.Label = "Desk";

        Assert.True(form.ShowRenameWarning);
        Assert.Equal([new SensorRename("Monitor Power (AC)", "Desk Power (AC)")], form.RenameHelp.Renames);
    }

    [Fact]
    public void SensorNamesModelBuildsTheHelpFromTheChangedRows()
    {
        AddMonitor(new Dictionary<string, string> { ["PcDc"] = "DC Est" });
        var model = new SensorNamesModel(_runtime);
        Assert.Empty(model.RenameHelp.Renames);

        model.SetCustomName("PcAc", "Wall Power");
        model.SetCustomName("PcDc", string.Empty);

        Assert.Equal(
            [new SensorRename("PC Power (AC)", "Wall Power"), new SensorRename("DC Est", "PC Power (DC)")],
            model.RenameHelp.Renames);
    }

    [Fact]
    public void TextsStateThatHwinfoKeepsTheOldNames()
    {
        Assert.Contains("HWiNFO keeps showing the old names", DeviceFormModel.RenameWarningText, StringComparison.Ordinal);
        Assert.Contains("Rename", DeviceFormModel.RenameWarningText, StringComparison.Ordinal);
        Assert.Contains("reset its preferences", DeviceFormModel.RenameWarningText, StringComparison.Ordinal);
        Assert.Contains("**Rename**", HwinfoRenameHelp.Fix1Text, StringComparison.Ordinal);
        Assert.Contains("**Reset Preferences**", HwinfoRenameHelp.Fix2Text, StringComparison.Ordinal);
        Assert.Equal("hwinfo-reset-preferences.png", HwinfoRenameHelp.Fix2Screenshot);
    }
}
