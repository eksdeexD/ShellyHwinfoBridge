using System.Drawing.Imaging;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Windows;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>
/// Renders windows to PNG without showing them (<c>--render-screens &lt;dir&gt;</c>), for visual comparison with
/// design/final. The process runs DPI-unaware in this mode, so everything renders at 100 %.
/// </summary>
/// <remarks>
/// Add a window by appending to <see cref="Screens"/>: a file name (without extension) and a factory that builds the
/// form with fake data. Each form is created, laid out and drawn with <see cref="Control.DrawToBitmap"/>, then disposed.
/// </remarks>
internal static class ScreenRenderer
{
    /// <summary>The screens to render, in order.</summary>
    public static readonly List<(string Name, Func<Form> Create)> Screens =
    [
        ("gallery-1", () => new ControlGalleryForm(1)),
        ("gallery-2", () => new ControlGalleryForm(2)),
        ("gallery-3", () => new ControlGalleryForm(3)),
        ("gallery-4", () => new ControlGalleryForm(4)),
        ("gallery-5", () => new ControlGalleryForm(5)),
        ("tray-icons", () => new IconPreviewForm()),
        ("main-window", () => MainWindow(SampleData.MainView(), EnergyPeriod.Hours)),
        ("main-window-days", () => MainWindow(SampleData.MainView(), EnergyPeriod.Days)),
        ("main-window-months-hover", () =>
        {
            var form = MainWindow(SampleData.MainView(), EnergyPeriod.Months);
            form.PreviewHover(10);
            return form;
        }),
        ("main-window-hours-hover", () =>
        {
            var form = MainWindow(SampleData.MainView(), EnergyPeriod.Hours);
            form.PreviewHover(15);
            return form;
        }),
        ("main-window-hours-hover-tallest", () =>
        {
            var form = MainWindow(SampleData.MainView(), EnergyPeriod.Hours);
            form.PreviewHover(23);
            return form;
        }),
        ("main-window-price-offline-no-shm", () => MainWindow(PriceOfflineNoSharedMemory(), EnergyPeriod.Hours)),
        ("dialog-confirm-remove-sensors", () => ThemedDialog.CreateForPreview("Remove HWiNFO sensors?", SettingsPage.RemoveSensorsConfirmText, "Remove", "Cancel")),
        ("main-window-unconfigured",() => MainWindow(SampleData.MainView(RuntimeState.Unconfigured, OnlineState.Unknown, HwinfoMonitorSnapshot.Unknown), EnergyPeriod.Hours)),
        ("wizard-1-hwinfo", () => WizardPreview.At(WizardStep.Hwinfo)),
        ("wizard-1-hwinfo-off", () => WizardPreview.At(WizardStep.Hwinfo, snapshot: WizardPreview.Snapshot.None)),
        ("wizard-2-shelly-question", () => WizardPreview.At(WizardStep.Shelly)),
        ("wizard-2-shelly-guide", () => WizardPreview.At(WizardStep.Shelly, tweak: s => s.Shelly.ShowGuide())),
        ("wizard-2-shelly", () => WizardPreview.At(WizardStep.Shelly, tweak: s =>
        {
            s.Shelly.FindAsync().GetAwaiter().GetResult();
            s.Shelly.SelectDevice(s.Shelly.Devices[0]);
            s.Shelly.TestAsync().GetAwaiter().GetResult();
        })),
        ("wizard-2-shelly-empty", () => WizardPreview.At(WizardStep.Shelly, tweak: s => s.Shelly.FindAsync().GetAwaiter().GetResult())),
        ("wizard-3-psu", () => WizardPreview.At(WizardStep.Psu, tweak: s =>
        {
            s.Psu.SetBrand("Seasonic");
            s.Psu.Select("seasonic-prime-fanless-tx-700");
        })),
        ("wizard-3-psu-generic", () => WizardPreview.At(WizardStep.Psu, tweak: s =>
        {
            s.Psu.Mode = Core.Settings.PsuSelectionMode.Generic;
            s.Psu.SetGenericWattage("850");
            s.Psu.SetGenericRating(Core.Curves.EightyPlusRating.Gold);
        })),
        ("wizard-3-psu-form", () => WizardPreview.At(WizardStep.Psu, tweak: s =>
        {
            s.Psu.Mode = Core.Settings.PsuSelectionMode.Manual;
            s.Psu.ManualEntry = ManualCurveEntry.EfficiencyForm;
        })),
        ("wizard-3-psu-grid", () => WizardPreview.At(WizardStep.Psu, tweak: s =>
        {
            s.Psu.Mode = Core.Settings.PsuSelectionMode.Manual;
            s.Psu.SetManualRows([("77.9", "70"), ("151.9", "141.7"), ("140", "150"), ("741.7", "700")]);
        })),
        ("wizard-3-psu-voltage", () =>
        {
            var form = WizardPreview.At(WizardStep.Psu, tweak: s =>
            {
                s.Psu.Select("seasonic-prime-fanless-tx-700");
                s.Psu.VoltageMode = Core.Curves.InputVoltageMode.V115;
            });
            form.PreviewOpenVoltage();
            return form;
        }),
        ("wizard-3-psu-single-voltage", () => WizardPreview.At(WizardStep.Psu, database: WizardPreview.DatabaseWithSingleVoltage(), tweak: s =>
        {
            s.Psu.SetBrand("Seasonic");
            s.Psu.Select(WizardPreview.SingleVoltageId);
            s.Psu.VoltageMode = Core.Curves.InputVoltageMode.V115;
        })),
        ("wizard-4-sensors", () => WizardPreview.At(WizardStep.Sensors)),
        ("wizard-4-sensors-warning", () => WizardPreview.At(WizardStep.Sensors, tweak: s =>
            s.Sensors.SetChecked(s.Sensors.Groups[0].Rows.Single(r => r.Name == "IA Cores Power").Id, true))),
        ("wizard-4-sensors-advanced", () =>
        {
            var form = WizardPreview.At(WizardStep.Sensors);
            return form;
        }),
        ("wizard-4-sensors-manual", () => WizardPreview.At(WizardStep.Sensors, snapshot: WizardPreview.Snapshot.None, tweak: s =>
        {
            s.Sensors.SetManualName(s.Sensors.AddManual(), "CPU Package Power");
            s.Sensors.SetManualName(s.Sensors.AddManual(), "GPU Power");
        })),
        ("wizard-5-apply", () => WizardPreview.At(WizardStep.Apply)),
        ("wizard-6-gadget-mixed", () => WizardPreview.At(WizardStep.Apply, gadgetExport: WizardPreview.GadgetExport.Partial, tweak: s =>
            s.GoNextAsync().GetAwaiter().GetResult())),
        ("wizard-6-gadget-all-reported", () => WizardPreview.At(WizardStep.Apply, gadgetExport: WizardPreview.GadgetExport.All, tweak: s =>
            s.GoNextAsync().GetAwaiter().GetResult())),
        ("wizard-6-gadget-hwinfo-not-running", () => WizardPreview.At(WizardStep.Apply, gadgetExport: WizardPreview.GadgetExport.NotRunning, tweak: s =>
            s.GoNextAsync().GetAwaiter().GetResult())),
        ("wizard-6-gadget-confirm", () =>
        {
            var model = new GadgetStepModel(() => WizardPreview.Export(WizardPreview.GadgetExport.Partial));
            model.Load(WizardPreview.SelectedSensors());
            return ThemedDialog.CreateForPreview(GadgetStepModel.ConfirmTitle, model.ConfirmText, GadgetStepModel.ContinueText, GadgetStepModel.GoBackText);
        }),
        ("wizard-7-verify-pending", () => WizardPreview.At(WizardStep.Apply, afterApply: WizardPreview.Snapshot.None, tweak: ApplyAndSkipGadget)),
        ("wizard-7-verify-could-not-close", () => WizardPreview.At(WizardStep.Apply, snapshot: WizardPreview.Snapshot.Desktop, tweak: s =>
        {
            ApplyAndSkipGadget(s);
            s.Apply.RestartHwinfoAsync().GetAwaiter().GetResult();
        })),
        ("wizard-7-verify-verified", () => WizardPreview.At(WizardStep.Apply, snapshot: WizardPreview.Snapshot.WithBridge, tweak: ApplyAndSkipGadget)),
        ("main-settings", () => Settings()),
        ("main-settings-single-voltage", () => Settings(out _, SampleData.SingleVoltageSettings(), WizardPreview.DatabaseWithSingleVoltage())),
        ("main-window-single-voltage", () => MainWindow(SampleData.MainView(settings: SampleData.SingleVoltageSettings(), database: WizardPreview.DatabaseWithSingleVoltage()), EnergyPeriod.Hours)),
        ("main-settings-results", () =>
        {
            var form = Settings(out var presenter);
            presenter.TestConnectionAsync("192.168.1.42", null).GetAwaiter().GetResult();
            presenter.CheckDatabaseUpdateAsync().GetAwaiter().GetResult();
            presenter.SetPrice("abc");
            return form;
        }),
        ("main-settings-results-bottom", () =>
        {
            var form = Settings(out var presenter);
            presenter.TestConnectionAsync("192.168.1.42", null).GetAwaiter().GetResult();
            presenter.CheckDatabaseUpdateAsync().GetAwaiter().GetResult();
            presenter.SetPrice("abc");
            form.Tag = new Action(() => form.SettingsPage?.PreviewScrollToEnd());
            return form;
        }),
        ("main-settings-bottom", () =>
        {
            var form = Settings();
            form.Tag = new Action(() => form.SettingsPage?.PreviewScrollToEnd());
            return form;
        }),
        ("main-settings-bottom-after-refresh", () =>
        {
            // The scroll-jump check: Manage Shellys has the focus (as when the page opens), the page is scrolled to the
            // end, then the main window gets the 1 Hz refreshes (the status text changes with the voltage) and a full
            // layout pass. The page must stay at the bottom; a jump fails the render (render-errors.txt).
            var form = Settings();
            form.Tag = new Action(() =>
            {
                var page = form.SettingsPage!;
                page.FocusFirstField();
                page.PreviewScrollToEnd();
                Application.DoEvents();
                var before = page.AutoScrollPosition;
                for (var i = 1; i <= 5; i++)
                {
                    var reading = new PowerReadingEventArgs(
                        new Core.Shelly.ShellyReading(312.4 + i, 226.9 + (i * 0.1), 1.38, 123456 + i),
                        new Core.Curves.PowerReading(312.4 + i, 312.4 + i, 294.8 + i, 17.6, Core.Curves.InputVoltage.V230));
                    form.Apply(MainWindowPresenter.Build(reading, RuntimeState.Running, OnlineState.Online, SampleData.Hwinfo(), SampleData.Totals(), SampleData.Settings(), PsuDatabaseLoader.LoadEmbedded(), SampleData.Culture, SampleData.Other()));
                    Application.DoEvents();
                }

                form.PerformLayout();
                Application.DoEvents();
                if (page.AutoScrollPosition != before)
                {
                    throw new InvalidOperationException($"The settings page jumped from {before} to {page.AutoScrollPosition} while idle.");
                }
            });
            return form;
        }),
        ("step-dialog-psu", () =>
        {
            var model = new PsuStepModel(WizardPreview.Database(), SampleData.Culture);
            model.LoadFrom(SampleData.Settings());
            model.MeasuredVolts = 226.9;
            return new StepDialog("Power supply", "Pick it from the database for the most accurate curve. If it is missing, use a generic or your own curve.", new PsuStepView(model), () => null);
        }),
        ("step-dialog-sensors", () =>
        {
            var settings = SampleData.Settings();
            var model = new SensorStepModel(SampleData.Culture);
            model.Load(WizardPreview.Sensors(withBridge: false).GetPowerSensors(), settings.Sensors);
            return new StepDialog("Sensors and formula", "These HWiNFO power sensors are subtracted from DC power to compute Other Components.", new SensorStepView(model, () => { }), () => null);
        }),

        // Milestone 4: additional devices, the Other card states, Manage Shellys, sensor names, About.
        ("m4-main-2-devices", () =>
        {
            var form = TwoDeviceWindow(EnergyChartMode.Combined);
            form.PreviewHover(20);
            return form;
        }),
        ("m4-main-2-devices-per-device", () =>
        {
            var form = TwoDeviceWindow(EnergyChartMode.PerDevice);
            form.PreviewHover(10, 1);
            return form;
        }),
        ("m4-main-2-devices-per-device-days", () => TwoDeviceWindow(EnergyChartMode.PerDevice, EnergyPeriod.Days)),
        ("m4-main-other-needs-gadget", () => MainWindow(SampleData.MainView(hwinfo: SampleData.HwinfoWithoutComponents(), other: OtherComponentsState.Unknown), EnergyPeriod.Hours)),
        ("m4-main-other-needs-gadget-2-devices", () =>
        {
            var settings = SampleData.TwoDeviceSettings();
            return MainWindow(SampleData.MainView(hwinfo: SampleData.HwinfoWithoutComponents(), settings: settings, other: OtherComponentsState.Unknown), EnergyPeriod.Hours, SampleData.TwoDeviceChart(EnergyPeriod.Hours, settings));
        }),
        ("m4-main-other-hwinfo-not-running", () => MainWindow(SampleData.MainView(hwinfo: new HwinfoMonitorSnapshot(true, false, false, false, null), other: OtherComponentsState.Unknown), EnergyPeriod.Hours)),
        ("m4-gadget-help-dialog", () => new GadgetHelpDialog(GadgetModel())),
        ("gadget-help-dialog-hwinfo-not-running", () => new GadgetHelpDialog(GadgetModel(WizardPreview.GadgetExport.NotRunning))),
        ("m4-rename-help-dialog", () => new RenameHelpDialog(HwinfoRenameHelp.ForDeviceRelabel(SampleData.MonitorId, "Monitor", "Desk monitor"))),
        ("m4-rename-help-dialog-many", () => new RenameHelpDialog(new HwinfoRenameHelp(
        [
            new SensorRename("PC Power (AC)", "Wall Power"),
            new SensorRename("PC Power (DC)", "PC DC"),
            new SensorRename("PC PSU Loss", "PSU Loss W"),
            new SensorRename("PC Other Components", "Rest of PC"),
            new SensorRename("Monitor Power (AC)", "Screen Power"),
            new SensorRename("Total Power (AC)", "Desk Total"),
            new SensorRename("Monitor Online", "Screen Online"),
        ]))),
        ("m4-settings-2-devices", () => Settings(out _, SampleData.TwoDeviceSettings())),
        ("about-window", () => new AboutWindow()),

        // UI pass of 2026-10-31: status pill states, the accent colour, the Include in totals tooltip.
        ("pass2-main-2-devices-some-offline", () =>
        {
            var settings = SampleData.TwoDeviceSettings();
            return MainWindow(SampleData.MainView(settings: settings, additionalState: OnlineState.Offline), EnergyPeriod.Hours, SampleData.TwoDeviceChart(EnergyPeriod.Hours, settings));
        }),
        ("pass2-main-2-devices-offline", () =>
        {
            var settings = SampleData.TwoDeviceSettings();
            return MainWindow(SampleData.MainView(online: OnlineState.Offline, settings: settings, additionalState: OnlineState.Offline), EnergyPeriod.Hours, SampleData.TwoDeviceChart(EnergyPeriod.Hours, settings));
        }),
        ("pass2-main-paused", () => MainWindow(SampleData.MainView(RuntimeState.Paused, OnlineState.Unknown), EnergyPeriod.Hours)),
        ("pass2-accent-teal-main-per-device", () =>
        {
            var settings = TealSettings();
            return MainWindow(SampleData.MainView(settings: settings), EnergyPeriod.Hours, SampleData.TwoDeviceChart(EnergyPeriod.Hours, settings));
        }),
        ("pass2-accent-teal-settings", () => Settings(out _, TealSettings())),
        ("pass2-accent-teal-manage-shellys", () => ManageShellys(TealSettings())),
        ("pass2-accent-teal-wizard-4", () =>
        {
            Theming.Theme.SetAccent(Color.FromArgb(0x1F, 0xA8, 0x9A));
            return WizardPreview.At(WizardStep.Sensors);
        }),
        ("pass2-accent-teal-tray-icons", () =>
        {
            Theming.Theme.SetAccent(Color.FromArgb(0x1F, 0xA8, 0x9A));
            return new IconPreviewForm();
        }),
        ("pass2-device-form-include-tooltip", () =>
        {
            var dialog = DeviceForm(m => m.BeginAdd());
            dialog.Tag = new Action(() => (dialog.View as DeviceFormView)?.PreviewShowIncludeTip());
            return dialog;
        }),
        ("pass2-settings-sensors-dialog-dimms", () =>
        {
            var model = new SensorStepModel(SampleData.Culture);
            model.Load(WizardPreview.Sensors(withBridge: false).GetPowerSensors(), []);
            return new StepDialog("Choose the component sensors", "These HWiNFO power sensors are subtracted from DC power to compute Other Components.", new SensorStepView(model, () => { }), () => null, "Sensors");
        }),
        ("m4-settings-retired-formula", () =>
        {
            var settings = SampleData.Settings();
            settings.CustomFormula = "\"DC Power (Est.)\" - \"CPU Package Power\"";
            return Settings(out _, settings);
        }),
        ("m4-manage-shellys", () => ManageShellys(ThreeDeviceSettings())),
        ("m4-manage-shellys-custom-colours", () => ManageShellys(ThreeDeviceSettings(ColourMode.Custom), model => model.SetBaseColour("orange"))),
        ("m4-manage-shellys-at-cap", () =>
        {
            var settings = ThreeDeviceSettings();
            settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = "9d8c7b6a-5f4e-4d3c-b2a1-0f9e8d7c6b5a", Label = "Lamp", Host = "192.168.1.59", IncludeInTotals = true });
            return ManageShellys(settings);
        }),
        ("m4-manage-shellys-hex-error", () =>
        {
            var form = ManageShellys(ThreeDeviceSettings(ColourMode.Custom));
            form.ManageShellysPage!.PreviewTypeColour(1, "#3FA7D");
            return form;
        }),
        ("m4-device-form-add", () => DeviceForm(m => m.BeginAdd(), f =>
        {
            f.Connection.SelectDevice(f.Connection.Devices[1]);
            f.Connection.TestAsync().GetAwaiter().GetResult();
        })),
        ("m4-device-form-edit-rename", () => DeviceForm(m => m.BeginEdit(SampleData.MonitorId), f => f.Label = "Desk monitor")),
        ("m4-device-form-edit-pc", () => DeviceForm(m => m.BeginEdit(AppSettings.PrimaryDeviceId))),
        ("m4-device-form-label-error", () => DeviceForm(m => m.BeginAdd(), f => f.Label = "PC")),
        ("m4-sensor-names-dialog", () => SensorNames()),
        ("m4-wizard-2-add-device-card", () => WizardAddDevice(formOpen: false)),
        ("m4-wizard-2-device-form", () => WizardAddDevice(formOpen: true)),
    ];

    private static MainForm Settings() => Settings(out _);

    /// <summary>The two-device settings with a teal accent colour (applied to the theme as the running app does).</summary>
    private static AppSettings TealSettings()
    {
        var settings = SampleData.TwoDeviceSettings(EnergyChartMode.PerDevice);
        settings.BaseColour = "#1FA89A";
        Theming.Theme.SetAccent(Color.FromArgb(0x1F, 0xA8, 0x9A));
        return settings;
    }

    /// <summary>Applies on step 5 and moves on past step 6 (the gadget how-to) to step 7.</summary>
    private static void ApplyAndSkipGadget(WizardSession session)
    {
        session.GoNextAsync().GetAwaiter().GetResult();
        session.GoNextAsync().GetAwaiter().GetResult();
    }

    /// <summary>The gadget how-to model over the preview's export, loaded with the sample sensors.</summary>
    private static GadgetStepModel GadgetModel(WizardPreview.GadgetExport export = WizardPreview.GadgetExport.Partial)
    {
        var model = new GadgetStepModel(() => WizardPreview.Export(export));
        model.Load(WizardPreview.SelectedSensors());
        return model;
    }

    /// <summary>The main window on its settings page, over the sample settings (price 0.32 DKK, autostart on, a stored password).</summary>
    private static MainForm Settings(out SettingsPresenter presenter, AppSettings? sample = null, PsuDatabase? database = null)
    {
        var settings = sample ?? SampleData.Settings();
        settings.EnergyPricePerKwh = 0.32m;
        settings.EnergyCurrency = "DKK";
        settings.StartWithWindows = true;
        var runtime = new PreviewRuntime(settings, hasPassword: true, database);
        presenter = new SettingsPresenter(runtime, SampleData.Culture);
        var form = MainWindow(SampleData.MainView(settings: settings, database: database), EnergyPeriod.Hours, settings.HasAdditionalDevices ? SampleData.TwoDeviceChart(EnergyPeriod.Hours, settings) : null);
        form.OpenSettings(new SettingsPage(presenter, runtime, () => WizardPreview.Sensors(withBridge: false), WizardPreview.Discover, () => GadgetModel()));
        return form;
    }

    /// <summary>Builds a main window showing <paramref name="state"/> and the sample chart for <paramref name="period"/>.</summary>
    private static MainForm MainWindow(MainViewState state, EnergyPeriod period, EnergyChartModel? chart = null)
    {
        var form = new MainForm();
        form.SelectPeriod(period);
        form.Apply(state);
        form.ApplyChart(chart ?? SampleData.Chart(period));
        return form;
    }

    /// <summary>The main window with the sample monitor plug, in <paramref name="mode"/>.</summary>
    private static MainForm TwoDeviceWindow(EnergyChartMode mode, EnergyPeriod period = EnergyPeriod.Hours)
    {
        var settings = SampleData.TwoDeviceSettings(mode);
        return MainWindow(SampleData.MainView(settings: settings), period, SampleData.TwoDeviceChart(period, settings));
    }

    /// <summary>The sample settings with the monitor and a third plug that is offline and left out of the totals.</summary>
    private static AppSettings ThreeDeviceSettings(ColourMode colours = ColourMode.AutomaticScheme)
    {
        var settings = SampleData.TwoDeviceSettings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = "0b7e9d2c-3f4a-4e8b-a1c6-7d5e4f3a2b1c", Label = "Speakers", Host = "192.168.1.58", Name = "PlugSG3-2", Model = "S3PL-00112EU", IncludeInTotals = false });
        settings.ColourMode = colours;
        if (colours == ColourMode.Custom)
        {
            settings.Device.Colour = "#FF6600";
            settings.AdditionalDevices[0].Colour = "#3FA7D6";
            settings.AdditionalDevices[1].Colour = "#8BC34A";
        }

        return settings;
    }

    /// <summary>The settings page over <paramref name="settings"/> with Manage Shellys opened on top.</summary>
    private static MainForm ManageShellys(AppSettings settings, Action<ManageShellysModel>? tweak = null)
    {
        var form = Settings(out var presenter, settings);
        var model = presenter.CreateManageShellys(WizardPreview.Discover);
        tweak?.Invoke(model);
        form.OpenManageShellys(new ManageShellysPage(model));
        return form;
    }

    /// <summary>The Manage Shellys device form in its step dialog (as Add / Edit open it).</summary>
    private static StepDialog DeviceForm(Func<ManageShellysModel, DeviceFormModel?> open, Action<DeviceFormModel>? tweak = null)
    {
        var runtime = new PreviewRuntime(SampleData.TwoDeviceSettings(), hasPassword: true);
        var model = new SettingsPresenter(runtime, SampleData.Culture).CreateManageShellys(WizardPreview.Discover);
        var form = open(model) ?? throw new InvalidOperationException("The preview form did not open.");
        form.Connection.DiscoverAsync().GetAwaiter().GetResult();
        tweak?.Invoke(form);
        return new StepDialog(form.Title, "Pick the plug from the list or type its address. It is measured on the AC side only.", new DeviceFormView(form, inline: false), () => null);
    }

    /// <summary>"Customize HWiNFO sensor names" over the two-device settings, with one custom name and one invalid one.</summary>
    private static StepDialog SensorNames()
    {
        var runtime = new PreviewRuntime(SampleData.TwoDeviceSettings(), hasPassword: true);
        var model = new SettingsPresenter(runtime, SampleData.Culture).CreateSensorNames();
        var rows = model.Rows;
        model.SetCustomName(rows[0].KeyId, "Wall Power");
        model.SetCustomName(rows[2].KeyId, "PSU \"Loss\"");
        var dialog = new StepDialog("Customize HWiNFO sensor names", "Leave a name empty to use the default. Names must be unique and contain no double quote.", new SensorNamesView(model), () => null);
        dialog.SaveEnabled = model.IsValid && model.HasChanges;
        return dialog;
    }

    /// <summary>Wizard step 2 after a successful test, with the monitor plug added (or its form still open).</summary>
    private static WizardForm WizardAddDevice(bool formOpen) => WizardPreview.At(WizardStep.Shelly, tweak: s =>
    {
        s.Shelly.FindAsync().GetAwaiter().GetResult();
        s.Shelly.SelectDevice(s.Shelly.Devices[0]);
        s.Shelly.TestAsync().GetAwaiter().GetResult();
        var form = s.Shelly.BeginAddDevice()!;
        form.Connection.DiscoverAsync().GetAwaiter().GetResult();
        form.Connection.SelectDevice(form.Connection.Devices[1]);
        form.Connection.TestAsync().GetAwaiter().GetResult();
        if (!formOpen)
        {
            s.Shelly.CommitDeviceForm();
        }
    });

    private static MainViewState PriceOfflineNoSharedMemory()
    {
        var settings = SampleData.Settings();
        settings.EnergyPricePerKwh = 0.32m;
        settings.EnergyCurrency = "DKK";
        return SampleData.MainView(RuntimeState.Running, OnlineState.Offline, new HwinfoMonitorSnapshot(true, true, false, false, null), settings, other: OtherComponentsState.Unknown);
    }

    /// <summary>Renders every screen into <paramref name="directory"/>; returns the process exit code.</summary>
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var errors = new List<string>();
        foreach (var (name, create) in Screens)
        {
            try
            {
                using var form = create();
                var path = Path.Combine(directory, name + ".png");
                using var bitmap = Render(form);
                bitmap.Save(path, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex}");
            }
            finally
            {
                // A screen may preview another accent colour.
                Theming.Theme.SetAccent(Theming.Theme.DefaultAccent);
            }
        }

        // A WinExe has no console: failures go to a file next to the PNGs.
        var errorFile = Path.Combine(directory, "render-errors.txt");
        if (errors.Count > 0)
        {
            File.WriteAllLines(errorFile, errors);
            return 1;
        }

        File.Delete(errorFile);
        return 0;
    }

    /// <summary>
    /// Renders a form's client area. The form is shown off-screen and fully transparent for the capture, because
    /// WM_PRINT skips the children of a window that was never shown.
    /// </summary>
    public static Bitmap Render(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.StartPosition = FormStartPosition.Manual;
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.CreateControl();
        form.Location = new Point(-30000, -30000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        if (form.Tag is Action afterShow)
        {
            // Screens that need a shown window (scrolling) set their last step as the form's Tag.
            afterShow();
            Application.DoEvents();
        }

        var client = form.ClientSize;
        using var whole = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
        var origin = form.PointToScreen(Point.Empty);
        var offset = new Point(origin.X - form.Left, origin.Y - form.Top);
        var result = new Bitmap(client.Width, client.Height);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(whole, new Rectangle(Point.Empty, client), new Rectangle(offset, client), GraphicsUnit.Pixel);
        }

        form.Hide();
        return result;
    }
}
