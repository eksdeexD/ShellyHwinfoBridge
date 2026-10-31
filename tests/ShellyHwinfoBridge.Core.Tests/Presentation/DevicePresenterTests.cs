using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class DevicePresenterTests
{
    private const string MonitorId = "11111111-1111-1111-1111-111111111111";
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private readonly FakeRuntime _runtime = new();

    private static Task<IReadOnlyList<DiscoveredShelly>> Discover(CancellationToken _) =>
        Task.FromResult<IReadOnlyList<DiscoveredShelly>>([WizardFixture.Plug("PlugSG3-Monitor", "192.168.1.50")]);

    private ManageShellysModel Manage() => new(_runtime, Discover, EnUs);

    private void AddMonitor(bool include = true)
    {
        var settings = _runtime.Settings!.Clone();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor", Host = "192.168.1.50", Model = "S3PL-00112EU", IncludeInTotals = include });
        _runtime.Settings = settings;
    }

    [Fact]
    public void RowsListThePcFirstWithStatusAndColour()
    {
        AddMonitor();
        var rows = Manage().Rows;

        Assert.Equal(["PC", "Monitor"], rows.Select(r => r.Label));
        var pc = rows[0];
        Assert.True(pc.IsPrimary);
        Assert.False(pc.CanRemove);
        Assert.False(pc.CanToggleInclude);
        Assert.Equal("192.168.1.42", pc.Host);
        Assert.Equal("S3PL-10112EU", pc.Model);
        Assert.Equal("Online · 312.4 W", pc.StatusText);
        Assert.Equal(Tone.Success, pc.StatusTone);
        Assert.Equal("#FF6600", pc.Colour);

        var monitor = rows[1];
        Assert.True(monitor.CanRemove);
        Assert.True(monitor.IncludeInTotals);
        Assert.Equal("Online · 30.0 W", monitor.StatusText);
        Assert.Equal("#0099FF", monitor.Colour);
    }

    [Fact]
    public void OfflineAndPausedStatuses()
    {
        AddMonitor();
        _runtime.DeviceList =
        [
            new(AppSettings.PrimaryDeviceId, "PC", "192.168.1.42", null, null, OnlineState.Online, 300, true),
            new(MonitorId, "Monitor", "192.168.1.50", null, null, OnlineState.Offline, null, true),
        ];
        Assert.Equal(("Offline", Tone.Error), (Manage().Rows[1].StatusText, Manage().Rows[1].StatusTone));

        _runtime.State = RuntimeState.Paused;
        Assert.Equal("Paused", Manage().Rows[1].StatusText);
    }

    [Fact]
    public async Task AddingADeviceUsesTheFormAndAppliesItWithItsPassword()
    {
        var model = Manage();
        var form = model.BeginAdd()!;

        Assert.Equal(DeviceFormMode.Add, form.Mode);
        Assert.Equal("Monitor", form.Label);
        Assert.True(form.IncludeInTotals);
        Assert.True(form.Connection.IsReady);
        Assert.False(form.ShowRenameWarning);
        Assert.False(model.CanAdd);

        await form.Connection.DiscoverAsync();
        form.Connection.SelectDevice(form.Connection.Devices[0]);
        form.Connection.Password = "mon";
        Assert.True(await form.Connection.TestAsync());
        Assert.Equal(("192.168.1.50", "mon"), _runtime.LastTest);

        var outcome = model.SaveForm();

        Assert.True(outcome!.Success);
        Assert.Null(model.Form);
        var device = Assert.Single(_runtime.Settings!.AdditionalDevices);
        Assert.Equal(form.DeviceId, device.Id);
        Assert.Equal("Monitor", device.Label);
        Assert.Equal("192.168.1.50", device.Host);
        Assert.Equal("mon", _runtime.GetDevicePassword(device.Id));
    }

    [Fact]
    public void FormValidatesLabelAndHost()
    {
        AddMonitor();
        var form = Manage().BeginAdd()!;
        Assert.Equal("Monitor 2", form.Label);

        form.Label = "monitor";
        Assert.Equal("Another device already has this label.", form.LabelError);
        form.Label = "PC";
        Assert.NotNull(form.LabelError);
        form.Label = "TV";
        Assert.Null(form.LabelError);
        Assert.False(form.IsValid);

        form.Connection.Host = "192.168.1.60";
        Assert.True(form.IsValid);
    }

    [Fact]
    public void AddIsDisabledAtTheCap()
    {
        var settings = _runtime.Settings!.Clone();
        foreach (var label in new[] { "Monitor", "Lamp", "TV" })
        {
            settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = label, Host = "10.0.0.9" });
        }

        _runtime.Settings = settings;
        var model = Manage();

        Assert.False(model.CanAdd);
        Assert.Equal(ShellyStepModel.DeviceCapHint, model.AddHint);
        Assert.Null(model.BeginAdd());
    }

    [Fact]
    public void EditingALabelShowsTheRenameWarningAndKeepsTheId()
    {
        AddMonitor();
        _runtime.DevicePasswords[MonitorId] = "mon";
        var model = Manage();
        var form = model.BeginEdit(MonitorId)!;

        Assert.Equal(DeviceFormMode.Edit, form.Mode);
        Assert.Equal("Edit Monitor", form.Title);
        Assert.Equal("192.168.1.50", form.Connection.Host);
        Assert.Equal("mon", form.Connection.Password);
        Assert.False(form.ShowRenameWarning);
        Assert.Null(form.PasswordChange);

        form.Label = "Screen";
        Assert.True(form.ShowRenameWarning);
        Assert.Contains("HWiNFO sensor names", DeviceFormModel.RenameWarningText, StringComparison.Ordinal);

        Assert.True(model.SaveForm()!.Success);
        var device = Assert.Single(_runtime.Settings!.AdditionalDevices);
        Assert.Equal(MonitorId, device.Id);
        Assert.Equal("Screen", device.Label);
        Assert.Null(_runtime.AppliedDevicePasswords[^1]);
    }

    [Fact]
    public void ThePcCanBeEditedButNotRemoved()
    {
        _runtime.Password = "secret";
        var model = Manage();
        var form = model.BeginEdit(AppSettings.PrimaryDeviceId)!;

        Assert.True(form.IsPrimary);
        Assert.Equal("PC", form.Label);
        Assert.False(form.CanEditLabel);
        Assert.False(form.ShowIncludeInTotals);
        form.Label = "Other";
        Assert.Equal("PC", form.Label);

        form.Connection.Host = "192.168.1.43";
        form.Connection.Password = "new";
        Assert.True(model.SaveForm()!.Success);
        Assert.Equal("192.168.1.43", _runtime.Settings!.Device.Host);
        Assert.Equal("new", _runtime.Applied[^1].Password);

        Assert.False(model.Remove(AppSettings.PrimaryDeviceId));
    }

    [Fact]
    public void RemoveIncludeAndColours()
    {
        AddMonitor();
        var model = Manage();

        model.SetIncludeInTotals(MonitorId, false);
        Assert.False(_runtime.Settings!.AdditionalDevices[0].IncludeInTotals);

        Assert.False(model.CanPickDeviceColours);
        model.SetColourMode(ColourMode.Custom);
        Assert.True(model.CanPickDeviceColours);
        Assert.True(model.SetDeviceColour(MonitorId, "#00ff00"));
        Assert.Equal("#00FF00", _runtime.Settings!.AdditionalDevices[0].Colour);
        Assert.Equal("#00FF00", model.Rows[1].Colour);
        Assert.True(model.SetDeviceColour(AppSettings.PrimaryDeviceId, "#111111"));
        Assert.Equal("#111111", _runtime.Settings!.Device.Colour);

        Assert.False(model.SetBaseColour("orange"));
        Assert.Equal(ManageShellysModel.ColourFormatError, model.ColourError);
        Assert.True(model.SetBaseColour("#3366ff"));
        Assert.Null(model.ColourError);
        Assert.Equal("#3366FF", model.BaseColour);

        Assert.True(model.Remove(MonitorId));
        Assert.Empty(_runtime.Settings!.AdditionalDevices);
    }

    [Fact]
    public void SensorNamesDialogValidatesSavesAndResets()
    {
        AddMonitor();
        var model = new SensorNamesModel(_runtime);

        Assert.Equal(10, model.Rows.Count);
        Assert.Equal("PC Power (AC)", model.Rows[0].DefaultName);
        Assert.Equal("Power0", model.Rows[0].Subkey);
        Assert.Contains(model.Rows, r => r.DefaultName == "Monitor Power (AC)");
        Assert.Contains(model.Rows, r => r.DefaultName == "Total Power (AC)");
        Assert.Contains(model.Rows, r => r.DefaultName == "Monitor Online");
        Assert.False(model.HasChanges);
        Assert.False(model.ShowRenameWarning);

        model.SetCustomName("PcAc", "Bad \"name\"");
        Assert.Equal("The name can't contain a double quote.", model.Rows[0].Error);
        Assert.False(model.IsValid);
        Assert.Null(model.Save());

        model.SetCustomName("PcAc", "Total Power (AC)");
        Assert.Equal("Another sensor already has this name.", model.Rows[0].Error);
        Assert.Equal("Another sensor already has this name.", model.Rows.Single(r => r.KeyId == "Total").Error);

        model.SetCustomName("PcAc", "Wall Power");
        model.SetCustomName("PcDc", "PC Power (DC)");
        Assert.True(model.IsValid);
        Assert.True(model.ShowRenameWarning);
        Assert.Equal("Wall Power", model.Rows[0].EffectiveName);

        Assert.True(model.Save()!.Success);
        Assert.Equal(new Dictionary<string, string> { ["PcAc"] = "Wall Power" }, _runtime.Settings!.SensorNames);

        var reopened = new SensorNamesModel(_runtime);
        Assert.Equal("Wall Power", reopened.Rows[0].CustomName);
        Assert.False(reopened.HasChanges);
        reopened.ResetToDefaults();
        Assert.True(reopened.HasChanges);
        reopened.Save();
        Assert.Empty(_runtime.Settings!.SensorNames);
    }

    [Fact]
    public void EmptyCustomNameMeansDefault()
    {
        var model = new SensorNamesModel(_runtime);
        model.SetCustomName("PcAc", "   ");
        Assert.Equal("PC Power (AC)", model.Rows[0].EffectiveName);
        Assert.False(model.HasChanges);
    }

    [Fact]
    public async Task WizardStepTwoAddsAndRemovesAdditionalDevices()
    {
        var f = new WizardFixture();
        var session = f.Session();
        Assert.True(await session.GoNextAsync());
        await session.Shelly.FindAsync();
        session.Shelly.SelectDevice(f.Devices[0]);
        Assert.False(session.Shelly.ShowAddDeviceCard);

        Assert.True(await session.Shelly.TestAsync());
        Assert.True(session.Shelly.ShowAddDeviceCard);
        Assert.True(session.Shelly.CanAddDevice);

        var form = session.Shelly.BeginAddDevice()!;
        Assert.False(session.CanGoNext);
        form.Label = "Monitor";
        form.Connection.Host = "192.168.1.50";
        form.Connection.Password = "mon";
        form.IncludeInTotals = false;
        Assert.True(session.Shelly.CommitDeviceForm());
        Assert.Null(session.Shelly.DeviceForm);

        var second = session.Shelly.BeginAddDevice()!;
        Assert.Equal("Monitor 2", second.Label);
        second.Connection.Host = "192.168.1.51";
        Assert.True(session.Shelly.CommitDeviceForm());
        session.Shelly.RemoveAdditionalDevice(session.Shelly.AdditionalDevices[1].Id);
        Assert.Single(session.Shelly.AdditionalDevices);

        Assert.True(await session.GoNextAsync());
        var device = Assert.Single(session.Draft.AdditionalDevices);
        Assert.Equal("Monitor", device.Label);
        Assert.False(device.IncludeInTotals);
        Assert.Equal("mon", session.Shelly.AdditionalPasswordChanges[device.Id]);

        // Through to Apply: the passwords reach the runtime.
        session.Psu.Select("seasonic-prime-fanless-tx-700");
        Assert.True(await session.GoNextAsync());
        Assert.True(await session.GoNextAsync());
        var lines = session.Apply.SummaryLines.ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("Monitor · 192.168.1.50 (not in totals)", lines["Other devices"]);
        Assert.True(await session.GoNextAsync());
        Assert.Equal("mon", f.Runtime.GetDevicePassword(device.Id));
    }

    [Fact]
    public void WizardRerunLoadsExistingDevicesAndCapsAdding()
    {
        var f = new WizardFixture();
        var settings = BriefExample.Settings();
        foreach (var label in new[] { "Monitor", "Lamp", "TV" })
        {
            settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Label = label, Host = "10.0.0.9" });
        }

        var session = new WizardSession(settings, "secret", BriefExample.Database(), f.Dependencies);

        Assert.Equal(3, session.Shelly.AdditionalDevices.Count);
        Assert.True(session.Shelly.ShowAddDeviceCard);
        Assert.False(session.Shelly.CanAddDevice);
        Assert.Equal(ShellyStepModel.DeviceCapHint, session.Shelly.AddDeviceHint);
        Assert.Null(session.Shelly.BeginAddDevice());
    }

    [Fact]
    public void VerifyChecksTheCurrentNamesIncludingDevicesAndTotal()
    {
        var settings = BriefExample.Settings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor", Host = "10.0.0.9" });
        settings.SensorNames["PcAc"] = "Wall Power";
        var layout = SensorLayout.Build(settings);
        var snapshot = Snapshots.Desktop(
            Snapshots.Watts(9, "Shelly Power", "Wall Power", 312.4),
            Snapshots.Watts(9, "Shelly Power", "PC Power (DC)", 294.8),
            Snapshots.Watts(9, "Shelly Power", "PC PSU Loss", 17.6),
            Snapshots.Watts(9, "Shelly Power", "PC Other Components", 48.1),
            Snapshots.Watts(9, "Shelly Power", "Monitor Power (AC)", 30));

        var result = VerifyEvaluator.Evaluate(snapshot, null, EnUs, layout);

        Assert.Equal(["Wall Power", "PC Power (DC)", "PC PSU Loss", "PC Other Components", "Monitor Power (AC)", "Total Power (AC)"], result.Items.Select(i => i.Label));
        Assert.Equal([true, true, true, true, true, false], result.Items.Select(i => i.Found));
        Assert.Equal("312.4 W", result.Tiles[0].Value);
        Assert.False(result.AllFound);
    }
}
