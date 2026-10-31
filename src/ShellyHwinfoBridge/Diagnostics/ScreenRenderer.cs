using System.Drawing.Imaging;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
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
            form.PreviewHover(22);
            return form;
        }),
        ("main-window-hours-hover", () =>
        {
            var form = MainWindow(SampleData.MainView(), EnergyPeriod.Hours);
            form.PreviewHover(30);
            return form;
        }),
        ("main-window-hours-hover-tallest", () =>
        {
            var form = MainWindow(SampleData.MainView(), EnergyPeriod.Hours);
            form.PreviewHover(47);
            return form;
        }),
        ("main-window-price-offline-no-shm", () => MainWindow(PriceOfflineNoSharedMemory(), EnergyPeriod.Hours)),
        ("dialog-confirm", () => ThemedDialog.CreateForPreview("Remove HWiNFO sensors?", "This deletes the Shelly Power sensors from HWiNFO and pauses polling. Resume polling to create them again.", "Remove", "Cancel")),
        ("main-window-unconfigured",() => MainWindow(SampleData.MainView(RuntimeState.Unconfigured, OnlineState.Unknown, HwinfoMonitorSnapshot.Unknown), EnergyPeriod.Hours)),
        ("wizard-1-hwinfo", () => WizardPreview.At(WizardStep.Hwinfo)),
        ("wizard-1-hwinfo-off", () => WizardPreview.At(WizardStep.Hwinfo, snapshot: WizardPreview.Snapshot.None)),
        ("wizard-2-shelly", () => WizardPreview.At(WizardStep.Shelly, tweak: s =>
        {
            s.Shelly.SelectDevice(s.Shelly.Devices[0]);
            s.Shelly.TestAsync().GetAwaiter().GetResult();
        })),
        ("wizard-2-shelly-empty", () => WizardPreview.At(WizardStep.Shelly)),
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
        ("wizard-4-sensors", () => WizardPreview.At(WizardStep.Sensors)),
        ("wizard-4-sensors-warning", () => WizardPreview.At(WizardStep.Sensors, tweak: s =>
            s.Sensors.SetChecked(s.Sensors.Groups[0].Rows.Single(r => r.Name == "IA Cores Power").Id, true))),
        ("wizard-4-sensors-advanced", () =>
        {
            var form = WizardPreview.At(WizardStep.Sensors, tweak: s => s.Sensors.SetCustomFormula("\"DC Power (Est.)\" - \"CPU Package Power\" - \"GPU Power\" - 15"));
            return form;
        }),
        ("wizard-4-sensors-manual", () => WizardPreview.At(WizardStep.Sensors, snapshot: WizardPreview.Snapshot.None, tweak: s =>
        {
            s.Sensors.SetManualName(s.Sensors.AddManual(), "CPU Package Power");
            s.Sensors.SetManualName(s.Sensors.AddManual(), "GPU Power");
        })),
        ("wizard-5-apply", () => WizardPreview.At(WizardStep.Apply)),
        ("wizard-5-applied-could-not-close", () => WizardPreview.At(WizardStep.Apply, snapshot: WizardPreview.Snapshot.Desktop, tweak: s =>
        {
            s.GoNextAsync().GetAwaiter().GetResult();
            s.Apply.RestartHwinfoAsync().GetAwaiter().GetResult();
        })),
        ("wizard-5-applied-verified", () => WizardPreview.At(WizardStep.Apply, snapshot: WizardPreview.Snapshot.WithBridge, tweak: s =>
        {
            s.GoNextAsync().GetAwaiter().GetResult();
            s.Apply.StartVerify();
        })),
        ("main-settings", () => Settings()),
        ("main-settings-results", () =>
        {
            var form = Settings(out var presenter);
            presenter.TestConnectionAsync("192.168.1.42", null).GetAwaiter().GetResult();
            presenter.CheckDatabaseUpdateAsync().GetAwaiter().GetResult();
            presenter.SetPrice("abc");
            return form;
        }),
        ("main-settings-short", () =>
        {
            var form = Settings();
            form.SetClientSizeDip(new Size(980, 600), new Size(980, 600));
            return form;
        }),
        ("main-settings-short-scrolled", () =>
        {
            var form = Settings();
            form.SetClientSizeDip(new Size(980, 600), new Size(980, 600));
            form.Tag = new Action(() => form.SettingsPage?.PreviewScrollToEnd());
            return form;
        }),
        ("step-dialog-psu", () =>
        {
            var model = new PsuStepModel(WizardPreview.Database(), SampleData.Culture);
            model.LoadFrom(SampleData.Settings());
            model.MeasuredVolts = 226.9;
            return new StepDialog("Power supply", "Pick it from the database for the most accurate curve. If it is missing, use a generic curve by rating or enter your own.", new PsuStepView(model), () => null);
        }),
        ("step-dialog-sensors", () =>
        {
            var settings = SampleData.Settings();
            var model = new SensorStepModel(SampleData.Culture);
            model.Load(WizardPreview.Sensors(withBridge: false).GetPowerSensors(), settings.Sensors, settings.CustomFormula);
            return new StepDialog("Sensors and formula", "These HWiNFO power sensors are subtracted from DC power to compute Other Components.", new SensorStepView(model, () => { }), () => null);
        }),
    ];

    private static MainForm Settings() => Settings(out _);

    /// <summary>The main window on its settings page, over the sample settings (price 0.32 DKK, autostart on, a stored password).</summary>
    private static MainForm Settings(out SettingsPresenter presenter)
    {
        var settings = SampleData.Settings();
        settings.EnergyPricePerKwh = 0.32m;
        settings.EnergyCurrency = "DKK";
        settings.StartWithWindows = true;
        var runtime = new PreviewRuntime(settings, hasPassword: true);
        presenter = new SettingsPresenter(runtime, SampleData.Culture);
        var form = MainWindow(SampleData.MainView(settings: settings), EnergyPeriod.Hours);
        form.OpenSettings(new SettingsPage(presenter, runtime, () => WizardPreview.Sensors(withBridge: false)));
        return form;
    }

    /// <summary>Builds a main window showing <paramref name="state"/> and the sample chart for <paramref name="period"/>.</summary>
    private static MainForm MainWindow(MainViewState state, EnergyPeriod period)
    {
        var form = new MainForm();
        form.SelectPeriod(period);
        form.Apply(state);
        form.ApplyChart(SampleData.Chart(period));
        return form;
    }

    private static MainViewState PriceOfflineNoSharedMemory()
    {
        var settings = SampleData.Settings();
        settings.EnergyPricePerKwh = 0.32m;
        settings.EnergyCurrency = "DKK";
        return SampleData.MainView(RuntimeState.Running, OnlineState.Offline, new HwinfoMonitorSnapshot(true, true, false, false, null, null), settings);
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
