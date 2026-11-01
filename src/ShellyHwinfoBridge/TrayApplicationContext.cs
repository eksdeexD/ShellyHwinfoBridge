using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Startup;
using ShellyHwinfoBridge.Icons;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge;

/// <summary>
/// The app's UI root: tray icon and menu, the main window (with its settings page) and the wizard, a display timer
/// (the settings' display interval) and a one-minute energy timer that pull presenter state, and the exit sequence.
/// </summary>
/// <remarks>
/// Readings and HWiNFO monitor changes only set a dirty flag; <see cref="Refresh"/> (stat cards, status pill, tray
/// icon and tooltip, showing the latest runtime state without averaging) runs on the display timer. Runtime state
/// changes that are not readings (online/offline, pause, trace) refresh at once. The energy history (totals and chart)
/// refreshes once a minute, when the window opens, on a period or mode change and after settings changes. Hooks for
/// the other windows: <see cref="ShowMainWindow"/>, <see cref="ShowSettings"/>,
/// <see cref="ShowWizard"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string BalloonTitle = AppInfo.ProductName;

    /// <summary>How often the energy history (totals and chart) refreshes on its own.</summary>
    private static readonly TimeSpan EnergyRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly AppServices _services;
    private readonly ActivationLatch _activation;
    private readonly ActivationLatch _exitRequest;
    private readonly ILogger _logger;
    private readonly UiDispatcher _dispatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _wizardItem;
    private readonly ToolStripMenuItem _aboutItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _energyTimer;
    private readonly MainWindowPresenter _mainPresenter;
    private IconFactory _icons;
    private MainForm? _mainForm;
    private WizardForm? _wizardForm;
    private AboutWindow? _aboutWindow;
    private TrayIconState? _iconState;
    private bool _hideTrayIcon;
    private volatile bool _dirty = true;
    private Core.Energy.EnergyTotals? _energyTotals;
    private bool _exiting;

    public TrayApplicationContext(AppServices services, ActivationLatch activation, ActivationLatch exitRequest, StartupArguments startup, ILogger logger, IReadOnlyList<string> startupNotices)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _exitRequest = exitRequest ?? throw new ArgumentNullException(nameof(exitRequest));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(startup);
        _dispatcher = new UiDispatcher();
        _mainPresenter = new MainWindowPresenter(services.Runtime);
        ApplyAccent();
        _icons = new IconFactory();
        Theme.AccentChanged += OnAccentChanged;

        _menu = new ContextMenuStrip();
        DarkMenuRenderer.Apply(_menu);
        _menu.Items.Add(new ToolStripMenuItem("Open…", null, (_, _) => ShowMainWindow()) { Font = new Font(_menu.Font, FontStyle.Bold) });
        _settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings());
        _wizardItem = new ToolStripMenuItem("Setup wizard…", null, (_, _) => ShowWizard());
        _pauseItem = new ToolStripMenuItem("Pause polling", null, (_, _) => TogglePause());
        _menu.Items.Add(_settingsItem);
        _menu.Items.Add(_wizardItem);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenFolder(AppPaths.LogDirectory)));
        _aboutItem = new ToolStripMenuItem("About…", null, (_, _) => ShowAbout());
        _menu.Items.Add(_aboutItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, async (_, _) => await ExitAsync()));
        _menu.Opening += (_, _) => UpdateMenu();

        _hideTrayIcon = services.Runtime.Settings?.HideTrayIcon ?? false;
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = BalloonTitle,
            Icon = _icons.Get(TrayIconState.Offline),
            Visible = !_hideTrayIcon,
        };
        _iconState = TrayIconState.Offline;
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };

        // Readings and monitor changes only mark the UI dirty; the display timer pulls the state. Other state changes
        // refresh at once.
        services.Runtime.StateChanged += OnStateChanged;
        services.Runtime.ReadingReceived += OnBackgroundChange;
        services.Runtime.SettingsChanged += OnSettingsChanged;
        services.Runtime.SensorKeysCreated += OnSensorKeysCreated;
        services.Monitor.Changed += OnBackgroundChange;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _timer = new System.Windows.Forms.Timer { Interval = DisplayInterval() };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        _energyTimer = new System.Windows.Forms.Timer { Interval = (int)EnergyRefreshInterval.TotalMilliseconds };
        _energyTimer.Tick += (_, _) => RefreshEnergy(force: false);
        _energyTimer.Start();

        // The first-run / start window rules.
        var configured = services.Runtime.State != RuntimeState.Unconfigured;
        _dispatcher.Post(() =>
        {
            foreach (var notice in startupNotices)
            {
                ShowBalloon(notice);
            }

            if (!configured && !startup.Minimized)
            {
                ShowMainWindow();
                ShowWizard();
            }
            else if (!configured)
            {
                ShowBalloon("Setup needed: double-click the tray icon to run the setup wizard.");
            }
            else if (!startup.Minimized)
            {
                ShowMainWindow();
            }

            Refresh();
        });

        // A second launch (also one that arrived while starting up) opens the main window.
        _activation.Attach(() => _dispatcher.Post(ShowMainWindow));

        // "--exit", "--remove-sensors" and the installer ask for the same clean exit as the Exit menu item.
        _exitRequest.Attach(() => _dispatcher.Post(() => _ = ExitAsync()));
    }

    /// <summary>Gets the services (for the wizard and the settings page).</summary>
    public AppServices Services => _services;

    /// <summary>Shows, restores and activates the main window (created on first use).</summary>
    public void ShowMainWindow()
    {
        if (_exiting)
        {
            return;
        }

        if (_mainForm is null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm();
            _mainForm.SettingsRequested += (_, _) => ShowSettings();
            _mainForm.WizardRequested += (_, _) => ShowWizard();
            _mainForm.PauseToggleRequested += (_, _) => TogglePause();
            _mainForm.AboutRequested += (_, _) => ShowAbout();
            _mainForm.ManageShellysRequested += (_, _) => ShowManageShellys();
            _mainForm.CreateGadgetModel = CreateGadgetModel;
            _mainForm.PeriodChanged += (_, _) => RefreshEnergy(force: true);
            _mainForm.ChartModeChanged += (_, _) => SetChartMode();
            _mainForm.VisibleChanged += (_, _) => UpdateMonitorActivity();
            _mainForm.SettingsClosed += (_, _) =>
            {
                if (!_exiting)
                {
                    RefreshNow();
                }
            };
        }

        _mainForm.SetWindowButtonsEnabled(_wizardForm is null);
        RefreshEnergy(force: true);
        if (!_mainForm.Visible)
        {
            _mainForm.Show();
        }

        if (_mainForm.WindowState == FormWindowState.Minimized)
        {
            _mainForm.WindowState = FormWindowState.Normal;
        }

        _mainForm.Activate();
        _mainForm.BringToFront();
    }

    /// <summary>
    /// Opens the main window on the settings page (or activates it when the page is already open). Leaving the page
    /// disposes it; the next call builds a fresh one with a new presenter.
    /// </summary>
    public void ShowSettings()
    {
        if (_exiting)
        {
            return;
        }

        // The wizard and the settings page are mutually exclusive: while the wizard is open it takes the focus.
        if (_wizardForm is { IsDisposed: false })
        {
            ActivateWizard();
            return;
        }

        ShowMainWindow();
        if (_mainForm is null)
        {
            return;
        }

        if (_mainForm.SettingsPage is { } open)
        {
            open.RefreshState();
            return;
        }

        var runtime = _services.Runtime;
        var page = new SettingsPage(new SettingsPresenter(runtime), runtime, _services.SharedMemory.TryGetSnapshot, ct => _services.Discovery.DiscoverAsync(cancellationToken: ct), CreateGadgetModel);
        page.WizardRequested += (_, _) => ShowWizard();
        page.SensorsRemoved += (_, _) =>
        {
            _logger.LogInformation("HWiNFO sensors removed from the settings page; polling paused.");
            RefreshNow();
        };
        _logger.LogInformation("Settings opened.");
        _mainForm.OpenSettings(page);
    }

    /// <summary>"About" (main window) and "About…" (tray): shows the About window (or activates the open one).</summary>
    public void ShowAbout()
    {
        if (_exiting)
        {
            return;
        }

        if (_aboutWindow is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }

        var owner = VisibleOwner();
        var about = new AboutWindow();
        about.ShowInTaskbar = owner is null;

        // A modeless window disposes itself when closed.
        about.FormClosed += (_, _) => _aboutWindow = null;
        _aboutWindow = about;
        about.Show(owner);
    }

    /// <summary>The status pill on the dashboard: opens the settings page, then Manage Shellys over it.</summary>
    public void ShowManageShellys()
    {
        ShowSettings();
        if (_mainForm is { IsDisposed: false } form && _wizardForm is null && form.SettingsPage is not null && !form.IsShowingDialog)
        {
            form.OpenManageShellysFromSettings();
        }
    }

    /// <summary>Creates the gadget how-to model over the HWiNFO monitor, loaded with the selected component sensors.</summary>
    private Core.Presentation.Wizard.GadgetStepModel CreateGadgetModel()
    {
        var model = new Core.Presentation.Wizard.GadgetStepModel(_services.Monitor.ReadGadgetExport, () => _services.Monitor.Current.SharedMemoryAvailable);
        model.Load(_services.Runtime.Settings?.Sensors ?? []);
        return model;
    }

    /// <summary>Stores the main window's Combined / Per device choice and redraws the chart.</summary>
    private void SetChartMode()
    {
        if (_mainForm is null || _services.Runtime.Settings is not { } settings || settings.ChartMode == _mainForm.SelectedChartMode)
        {
            return;
        }

        new SettingsPresenter(_services.Runtime).SetChartMode(_mainForm.SelectedChartMode);
        RefreshEnergy(force: true);
    }

    /// <summary>
    /// Opens the setup wizard (or activates the open one). While it is open the main window's Settings and Run setup
    /// wizard buttons and the tray's Settings… item are disabled. Closing it before Apply changes nothing; a first-run cancel
    /// leaves the app unconfigured in the tray.
    /// </summary>
    public void ShowWizard()
    {
        if (_exiting)
        {
            return;
        }

        if (_wizardForm is { IsDisposed: false })
        {
            ActivateWizard();
            return;
        }

        // Settings and the wizard are mutually exclusive. While the settings page shows a dialog (PSU, sensors, a
        // confirmation) it keeps the focus; otherwise the page closes (its changes are already applied) and the wizard opens.
        if (_mainForm?.SettingsPage is not null)
        {
            if (_mainForm.IsShowingDialog)
            {
                _mainForm.Activate();
                return;
            }

            _mainForm.CloseSettings();
        }

        var runtime = _services.Runtime;
        var dependencies = WizardDependencies.Create(runtime, _services.Locator, _services.SharedMemory, _services.Discovery, readGadgetExport: _services.Monitor.ReadGadgetExport);
        var session = new WizardSession(runtime.Settings, runtime.Password, runtime.Database, dependencies);
        var form = new WizardForm(session);
        form.Applied += (_, _) => RefreshNow();
        form.FormClosed += (_, _) =>
        {
            _wizardForm = null;
            _mainForm?.SetWindowButtonsEnabled(true);
            if (!_exiting)
            {
                if (session.IsApplied)
                {
                    ShowMainWindow();
                }

                RefreshNow();
            }
        };

        _wizardForm = form;
        _mainForm?.SetWindowButtonsEnabled(false);
        _logger.LogInformation("Setup wizard opened.");
        form.Show();
        ActivateWizard();
    }

    private void ActivateWizard()
    {
        if (_wizardForm is not { IsDisposed: false } form)
        {
            return;
        }

        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Activate();
        form.BringToFront();
    }

    /// <summary>Marks the UI dirty and refreshes it now, the energy history included (call after applying changes from a window).</summary>
    public void RefreshNow()
    {
        _dirty = true;
        Refresh();
        RefreshEnergy(force: false);
    }

    /// <summary>
    /// Exits cleanly: hides the windows, stops polling (sensors go offline, 3 s at most), flushes the energy log,
    /// removes the tray icon and ends the message loop. Re-entrant calls are ignored.
    /// </summary>
    public async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _logger.LogInformation("Exiting.");
        _timer.Stop();
        _energyTimer.Stop();
        _activation.Detach();
        _exitRequest.Detach();
        _wizardForm?.Close();
        _aboutWindow?.Close();
        _mainForm?.Hide();
        Unsubscribe();
        _dispatcher.BeginShutdown();

        var stop = _services.Runtime.StopAsync();
        if (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(3))) != stop)
        {
            _logger.LogWarning("Stopping the poller took longer than 3 seconds.");
        }

        FinishExit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unsubscribe();
            _timer.Dispose();
            _energyTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _mainForm?.Dispose();
            _icons.Dispose();
            _dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    private void FinishExit()
    {
        try
        {
            _services.Energy.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Flushing the energy log failed.");
        }

        _services.Monitor.Stop();
        _notifyIcon.Visible = false;
        if (_mainForm is not null)
        {
            _mainForm.Dispose();
            _mainForm = null;
        }

        ExitThread();
    }

    private void Unsubscribe()
    {
        _services.Runtime.StateChanged -= OnStateChanged;
        _services.Runtime.ReadingReceived -= OnBackgroundChange;
        _services.Runtime.SettingsChanged -= OnSettingsChanged;
        _services.Runtime.SensorKeysCreated -= OnSensorKeysCreated;
        _services.Monitor.Changed -= OnBackgroundChange;
        Theme.AccentChanged -= OnAccentChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnBackgroundChange(object? sender, EventArgs e) => _dirty = true;

    // Not a reading (online/offline, pause, trace, settings applied): show it at once instead of on the next tick.
    private void OnStateChanged(object? sender, EventArgs e)
    {
        _dirty = true;
        _dispatcher.Post(Refresh);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _dirty = true;
        _dispatcher.Post(() =>
        {
            if (_exiting)
            {
                return;
            }

            ApplyAccent();

            // A changed display interval restarts the timer; the first tick of the new cadence is this refresh.
            var interval = DisplayInterval();
            if (_timer.Interval != interval)
            {
                _timer.Interval = interval;
            }

            RefreshNow();
            var hide = _services.Runtime.Settings?.HideTrayIcon ?? false;
            if (hide == _hideTrayIcon)
            {
                return;
            }

            _hideTrayIcon = hide;
            _notifyIcon.Visible = !hide;
            UpdateMonitorActivity();
            if (hide)
            {
                ThemedDialog.Show(VisibleOwner(), "Tray icon hidden", "The app keeps running. Start " + AppInfo.ProductName + " again to open it.");
            }
        });
    }

    private void OnSensorKeysCreated(object? sender, EventArgs e) =>
        _dispatcher.Post(() => ShowBalloon("Restart HWiNFO to show the Shelly Power sensors."));

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        // Windows is shutting down or the user logs off: run the exit sequence synchronously, bounded to 2 s.
        _exiting = true;
        _logger.LogInformation("Session ending ({Reason}); stopping.", e.Reason);
        _timer.Stop();
        _energyTimer.Stop();
        _activation.Detach();
        _exitRequest.Detach();
        Unsubscribe();
        _dispatcher.BeginShutdown();
        try
        {
            _services.Runtime.StopAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex)
        {
            _logger.LogError(ex, "Stopping at session end failed.");
        }

        FinishExit();
    }

    // Icon sizes may have changed: rebuild the tray icons.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _dispatcher.Post(RebuildTrayIcons);

    // The online icon is drawn in the accent colour: rebuild the tray icons (windows redraw their own icon).
    private void OnAccentChanged(object? sender, EventArgs e) => RebuildTrayIcons();

    /// <summary>Builds a new icon factory, swaps the tray icon, then frees the old handles.</summary>
    private void RebuildTrayIcons()
    {
        if (_exiting)
        {
            return;
        }

        var old = _icons;
        _icons = new IconFactory();
        _iconState = null;
        _dirty = true;
        Refresh();
        old.Dispose();
    }

    /// <summary>Makes the settings' accent colour (Manage Shellys "Accent colour") the theme's accent.</summary>
    private void ApplyAccent()
    {
        var text = _services.Runtime.Settings?.BaseColour;
        Theme.SetAccent(DeviceColours.TryParse(text, out var rgb) ? Color.FromArgb(rgb.R, rgb.G, rgb.B) : Theme.DefaultAccent);
    }

    private void Refresh()
    {
        if (_exiting || !_dirty)
        {
            return;
        }

        _dirty = false;
        var runtime = _services.Runtime;
        var hw = _services.Monitor.Current;
        var state = runtime.State;
        var online = runtime.OnlineState;

        var iconState = TrayPresenter.IconState(state, online);
        if (iconState != _iconState)
        {
            _notifyIcon.Icon = _icons.Get(iconState);
            _iconState = iconState;
        }

        var tooltip = TrayPresenter.Tooltip(runtime, hw);
        if (_notifyIcon.Text != tooltip)
        {
            _notifyIcon.Text = tooltip;
        }

        RefreshCards(force: false);
        UpdateMonitorActivity();
    }

    /// <summary>
    /// Refreshes the stat cards and the status pill with the latest runtime state and the cached energy totals.
    /// </summary>
    private void RefreshCards(bool force)
    {
        if (_mainForm is null || _mainForm.IsDisposed || (!force && !_mainForm.Visible))
        {
            return;
        }

        _energyTotals ??= _services.Energy.GetTotals();
        _mainForm.Apply(_mainPresenter.Build(_services.Monitor.Current, _energyTotals));
    }

    /// <summary>Refreshes the energy history (totals and chart), then the cards so the totals show.</summary>
    private void RefreshEnergy(bool force)
    {
        if (_exiting || _mainForm is null || _mainForm.IsDisposed || (!force && !_mainForm.Visible))
        {
            return;
        }

        var energy = _services.Energy;
        _energyTotals = energy.GetTotals();
        var period = _mainForm.SelectedPeriod;
        _mainForm.ApplyChart(EnergyChartModel.Build(energy.GetChartData(period), period, _services.Runtime.Settings ?? new Core.Settings.AppSettings(), CultureInfo.CurrentCulture));
        RefreshCards(force);
    }

    /// <summary>The display timer's interval: the settings' display interval.</summary>
    private int DisplayInterval() =>
        Core.Settings.AppSettings.NormalizeDisplayInterval(_services.Runtime.Settings?.DisplayIntervalMs ?? Core.Settings.AppSettings.DefaultDisplayIntervalMs);

    private void UpdateMonitorActivity() =>
        _services.Monitor.Active = !_hideTrayIcon || _mainForm is { Visible: true };

    private void UpdateMenu()
    {
        var state = _services.Runtime.State;
        _pauseItem.Text = state == RuntimeState.Paused ? "Resume polling" : "Pause polling";
        _pauseItem.Enabled = state is RuntimeState.Running or RuntimeState.Paused;
        _settingsItem.Enabled = _wizardForm is null;
        _wizardItem.Enabled = _mainForm?.IsShowingDialog != true;
        _aboutItem.Enabled = true;
    }

    private void TogglePause()
    {
        var runtime = _services.Runtime;
        if (runtime.State == RuntimeState.Paused)
        {
            runtime.Resume();
        }
        else if (runtime.State == RuntimeState.Running)
        {
            runtime.Pause();
        }

        RefreshNow();
    }

    private void ShowBalloon(string text)
    {
        if (_notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(5000, BalloonTitle, text, ToolTipIcon.Info);
        }
    }

    private IWin32Window? VisibleOwner() => _mainForm is { IsDisposed: false, Visible: true } form ? form : null;

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Opening {Path} failed.", path);
        }
    }
}
