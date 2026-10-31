using System.Diagnostics;
using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The settings page, shown inside the main window in place of the dashboard (layout after design/final/settings.png):
/// a title row ("Settings", the footnote "Changes apply immediately." and "Done"), two columns of cards (Shelly plugs
/// with "Manage Shellys", Power supply, Sensors and formula with "Customize HWiNFO sensor names", Polling, Energy, PSU
/// database, Startup and tray, Maintenance) and the About card at the bottom. Every change goes through
/// <see cref="SettingsPresenter"/> and is applied at once; sliders apply when released, text fields on Enter or when
/// focus leaves. "Change…", "Edit…" and the sensor names open a modal <see cref="StepDialog"/> owned by the main
/// window; "Manage Shellys" asks the main window to open <see cref="ManageShellysPage"/>. The page scrolls when the
/// window is shorter than its content.
/// </summary>
internal sealed class SettingsPage : ThemedPanel
{
    /// <summary>The project's issues page (About).</summary>
    public const string IssuesUrl = "https://github.com/eksdeexD/ShellyHwinfoBridge/issues";

    /// <summary>The feedback address (About).</summary>
    public const string FeedbackEmail = "zapador@zapador.net";

    // Layout in DIP, page coordinates. At the default 1100×740 main window the page starts under the header (y 68).
    private const float Pad = 24;
    private const float Gap = 16;
    private const float RowGap = 10;
    private const float TitleRowTop = 6;
    private const float TitleRowHeight = 34;
    private const float CardsTop = 50;
    private const float MinContentWidth = 880;
    private const float Row1Height = 148;
    private const float CurveNoteHeight = 28;
    private const float Row2MinHeight = 150;
    private const float EnergyHeight = 132;
    private const float SmallCardHeight = 88;
    private const float BottomMargin = 20;
    private const float FormulaMinHeight = 88;
    private const int FormulaMaxLines = 10;

    private static readonly InputVoltageMode[] VoltageModes = [InputVoltageMode.Auto, InputVoltageMode.V115, InputVoltageMode.V230];

    private readonly SettingsPresenter _presenter;
    private readonly IBridgeRuntime _runtime;
    private readonly Func<HwinfoSnapshot?> _readSharedMemory;
    private readonly Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> _discover;

    private readonly PillButton _done = new() { Text = "Done", AccessibleName = "Done" };

    // Shelly plugs.
    private readonly CardPanel _shellyCard = new() { Title = "Shelly plugs" };
    private readonly TextLabel _devicesText = new() { Role = FontRole.Subtitle };
    private readonly TextLabel _devicesHint = new() { Text = "Add a Shelly for your monitor or another device.", Tone = UiTone.Secondary };
    private readonly PillButton _manage = new() { Text = "Manage Shellys" };

    // Power supply.
    private readonly CardPanel _psuCard = new() { Title = "Power supply" };
    private readonly TextLabel _psuTitle = new() { Role = FontRole.Subtitle };
    private readonly TextLabel _psuSubtitle = new() { Tone = UiTone.Secondary };
    private readonly TextLink _change = new() { Text = "Change…", Semibold = true, AccessibleName = "Change power supply" };
    private readonly TextLabel _voltageLabel = new() { Text = "Input voltage", Tone = UiTone.Secondary };
    private readonly SegmentedControl _voltage = new() { AccessibleName = "Input voltage" };
    private readonly InfoLine _curveNote = new() { Visible = false };

    // Sensors and formula.
    private readonly CardPanel _sensorsCard = new() { Title = "Sensors and formula" };
    private readonly ThemedTextBox _formula = new() { Multiline = true, Monospace = true, ReadOnly = true, AccessibleName = "Formula" };
    private readonly TextLink _edit = new() { Text = "Edit…", Semibold = true, AccessibleName = "Edit sensors and formula" };
    private readonly TextLabel _gadgetTip = new() { Text = HwinfoTips.GadgetExport, Role = FontRole.Small, Tone = UiTone.Secondary, WordWrap = true };
    private readonly PillButton _sensorNames = new() { Text = "Customize HWiNFO sensor names" };
    private readonly InfoLine _retiredNote = new() { Visible = false };

    // Polling.
    private readonly CardPanel _pollingCard = new() { Title = "Polling" };
    private readonly TextLabel _intervalLabel = new() { Text = "Interval", Tone = UiTone.Secondary };
    private readonly ThemedSlider _interval = new() { Minimum = Core.Polling.PollingOptions.MinIntervalMs, Maximum = Core.Polling.PollingOptions.MaxIntervalMs, SmallChange = 100, LargeChange = 500, AccessibleName = "Poll interval" };
    private readonly TextLabel _intervalValue = new();
    private readonly TextLabel _smoothingLabel = new() { Text = "Smoothing", Tone = UiTone.Secondary };
    private readonly ThemedSlider _smoothing = new() { Minimum = 1, Maximum = MovingAverage.MaxSamples, SmallChange = 1, LargeChange = 5, AccessibleName = "Smoothing" };
    private readonly TextLabel _smoothingValue = new();

    // Energy.
    private readonly CardPanel _energyCard = new() { Title = "Energy" };
    private readonly ToggleSwitch _logEnergy = new() { Text = "Log energy use" };
    private readonly TextLink _openEnergy = new() { Text = "Open energy folder" };
    private readonly TextLabel _priceLabel = new() { Text = "Price per kWh", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _price = new() { Placeholder = "optional", AccessibleName = "Price per kWh" };
    private readonly TextLabel _currencyLabel = new() { Text = "Currency", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _currency = new() { Placeholder = "e.g. DKK", MaxLength = AppSettings.MaxCurrencyLength, AccessibleName = "Currency" };
    private readonly InfoLine _priceError = new() { Tone = InfoTone.Error, Visible = false };

    // PSU database.
    private readonly CardPanel _databaseCard = new() { Title = "PSU database" };
    private readonly TextLabel _databaseText = new() { Tone = UiTone.Secondary };
    private readonly PillButton _checkUpdate = new() { Text = "Check for update" };
    private readonly InfoLine _databaseResult = new() { Visible = false };

    // Startup and tray.
    private readonly CardPanel _startupCard = new() { Title = "Startup and tray" };
    private readonly ToggleSwitch _startWithWindows = new() { Text = "Start with Windows" };
    private readonly ToggleSwitch _showTrayIcon = new() { Text = "Show tray icon" };

    // Maintenance.
    private readonly CardPanel _maintenanceCard = new() { Title = "Maintenance" };
    private readonly PillButton _openLog = new() { Text = "Open log folder" };
    private readonly PillButton _rerun = new() { Text = "Re-run setup wizard" };
    private readonly TextLink _remove = new() { Text = "Remove HWiNFO sensors", Tone = LinkTone.Danger };
    private readonly TextLink _exit = new() { Text = "Exit ShellyHwinfoBridge", Tone = LinkTone.Danger };

    // About.
    private readonly CardPanel _aboutCard = new() { Title = "About" };
    private readonly TextLabel _aboutName = new() { Text = "ShellyHwinfoBridge " + VersionText, Role = FontRole.BodySemibold };
    private readonly TextLabel _feedbackStart = new() { Text = "Feedback and feature requests are welcome at", Tone = UiTone.Secondary };
    private readonly TextLink _email = new() { Text = FeedbackEmail, AccessibleName = "Send feedback to " + FeedbackEmail };
    private readonly TextLabel _feedbackMiddle = new() { Text = "or as", Tone = UiTone.Secondary };
    private readonly TextLink _issues = new() { Text = "issues on GitHub", AccessibleName = "Open the issues page on GitHub" };
    private readonly TextLabel _licence = new() { Text = "MIT licence", Tone = UiTone.Secondary };
    private readonly TextLabel _attribution = new() { Text = "PSU efficiency data: Cybenetics, used with attribution; each PSU links to its report", Tone = UiTone.Secondary, WordWrap = true };

    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error, Visible = false };

    private bool _applying;
    private int _modalDepth;
    private int _formulaLines = 1;
    private float _footnoteLeft;
    private bool _committed;

    /// <summary>Creates the page.</summary>
    /// <param name="presenter">The presenter that applies every change.</param>
    /// <param name="runtime">The runtime (PSU database and last reading for the PSU dialog).</param>
    /// <param name="readSharedMemory">Reads HWiNFO's shared memory for the sensor dialog (null when it is off).</param>
    /// <param name="discover">Browses the network for plugs (the Manage Shellys device form).</param>
    public SettingsPage(SettingsPresenter presenter, IBridgeRuntime runtime, Func<HwinfoSnapshot?> readSharedMemory, Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> discover)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _readSharedMemory = readSharedMemory ?? throw new ArgumentNullException(nameof(readSharedMemory));
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        AccessibleName = "Settings";
        AccessibleRole = AccessibleRole.Pane;
        AutoScroll = true;

        SuspendLayout();
        _voltage.SetItems(["Auto", "115 V", "230 V"], 0);
        _done.TabIndex = 0;
        Controls.Add(_done);
        AddCard(_shellyCard, _devicesText, _devicesHint, _manage);
        AddCard(_psuCard, _psuTitle, _psuSubtitle, _change, _voltageLabel, _voltage, _curveNote);
        AddCard(_sensorsCard, _formula, _edit, _gadgetTip, _sensorNames, _retiredNote);
        AddCard(_pollingCard, _intervalLabel, _interval, _intervalValue, _smoothingLabel, _smoothing, _smoothingValue);
        AddCard(_energyCard, _logEnergy, _openEnergy, _priceLabel, _price, _currencyLabel, _currency, _priceError);
        AddCard(_databaseCard, _databaseText, _checkUpdate, _databaseResult);
        AddCard(_startupCard, _startWithWindows, _showTrayIcon);
        AddCard(_maintenanceCard, _exit, _openLog, _rerun, _remove);
        AddCard(_aboutCard, _aboutName, _feedbackStart, _email, _feedbackMiddle, _issues, _licence, _attribution);
        _applyError.TabIndex = Controls.Count;
        Controls.Add(_applyError);
        ResumeLayout(false);

        WireEvents();
        _presenter.Changed += OnPresenterChanged;
        ApplyState();
    }

    /// <summary>Raised when "Done" is clicked.</summary>
    public event EventHandler? DoneRequested;

    /// <summary>Raised when "Re-run setup wizard" is clicked.</summary>
    public event EventHandler? WizardRequested;

    /// <summary>Raised when "Exit ShellyHwinfoBridge" is confirmed.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised after "Remove HWiNFO sensors" (the tray and main window should refresh).</summary>
    public event EventHandler? SensorsRemoved;

    /// <summary>Raised when "Manage Shellys" is clicked (the main window opens <see cref="ManageShellysPage"/>).</summary>
    public event EventHandler? ManageShellysRequested;

    /// <summary>Gets whether a dialog (step dialog or confirmation) is open over the page.</summary>
    public bool IsShowingDialog => _modalDepth > 0;

    /// <summary>Gets the app version for the About card ("v0.1.0-beta.2"; build metadata after '+' dropped).</summary>
    internal static string VersionText { get; } = "v" + (typeof(SettingsPage).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    /// <summary>Re-reads the presenter state into the controls (fields being edited keep their text).</summary>
    public void RefreshState() => ApplyState();

    /// <summary>Creates the Manage Shellys model over the same runtime.</summary>
    public ManageShellysModel CreateManageShellysModel() => _presenter.CreateManageShellys(_discover);

    /// <summary>Puts the keyboard focus on "Manage Shellys" (when the page is opened).</summary>
    public void FocusFirstField() => FocusManageShellys();

    /// <summary>Puts the keyboard focus on "Manage Shellys" (also after coming back from that page).</summary>
    public void FocusManageShellys()
    {
        if (_manage.CanFocus)
        {
            _manage.Focus();
        }
    }

    /// <summary>Applies a field that is still being edited, so leaving the page never loses a typed change.</summary>
    public void CommitPendingEdits()
    {
        if (_committed)
        {
            return;
        }

        foreach (var box in new[] { _price, _currency })
        {
            if (box.ContainsFocus && box.IsModified)
            {
                CommitField(box);
            }
        }
    }

    /// <summary>Scrolls the page to its end (screen renderer).</summary>
    public void PreviewScrollToEnd()
    {
        PerformLayout();
        AutoScrollPosition = new Point(0, short.MaxValue);
        PerformLayout();
        Invalidate(true);
    }

    /// <summary>Scrolls the About card into view (tray "About…").</summary>
    public void ScrollToAbout()
    {
        PerformLayout();
        var top = _aboutCard.Top - AutoScrollPosition.Y - this.Px(Pad);
        AutoScrollPosition = new Point(-AutoScrollPosition.X, Math.Max(0, top));
        PerformLayout();
        Invalidate(true);
        if (_email.CanFocus)
        {
            _email.Focus();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkExplorerTheme(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _committed = true;
            _presenter.Changed -= OnPresenterChanged;
        }

        base.Dispose(disposing);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);

        // The backdrop is the window's gradient, fixed to the window: repaint what the scroll blitted.
        Invalidate(true);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate(true);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            base.OnLayout(levent);
            return;
        }

        var dpi = DeviceDpi;
        float Dip(int px) => px * 96f / dpi;
        var scroll = AutoScrollPosition;
        var contentWidth = Math.Max(Dip(ClientSize.Width), MinContentWidth);
        var column = (contentWidth - (2 * Pad) - Gap) / 2;
        var left = Pad;
        var right = Pad + column + Gap;

        Rectangle At(float x, float y, float width, float height) =>
            new(this.Px(x) + scroll.X, this.Px(y) + scroll.Y, this.Px(width), this.Px(height));

        var doneWidth = Math.Max(120, _done.GetPreferredSize(Size.Empty).Width * 96f / dpi);
        _done.Bounds = At(contentWidth - Pad - doneWidth, TitleRowTop, doneWidth, TitleRowHeight);

        // Rows 1 and 2 pair the cards (row 2 gives Sensors and formula more room for the formula and the gadget tip);
        // below them the left column has Energy over PSU database and the right column Startup and tray over
        // Maintenance, so both columns end at the same height. The About card spans both columns at the bottom.
        var sensorsWidth = (float)Math.Round(2 * column * SensorsShare);
        var pollingWidth = (2 * column) - sensorsWidth;
        var row2 = Math.Max(Row2MinHeight, SensorsHeight(sensorsWidth));
        var energy = EnergyHeight + (_priceError.Visible ? 30 : 0);
        var y = CardsTop;
        // A single-voltage PSU adds its curve note under the voltage segments; both cards of the row grow with it.
        var row1 = Row1Height + (_curveNote.Visible ? CurveNoteHeight : 0);
        _shellyCard.Bounds = At(left, y, column, row1);
        _psuCard.Bounds = At(right, y, column, row1);
        y += row1 + RowGap;
        _sensorsCard.Bounds = At(left, y, sensorsWidth, row2);
        _pollingCard.Bounds = At(left + sensorsWidth + Gap, y, pollingWidth, row2);
        y += row2 + RowGap;
        _energyCard.Bounds = At(left, y, column, energy);
        _databaseCard.Bounds = At(left, y + energy + RowGap, column, SmallCardHeight);
        _startupCard.Bounds = At(right, y, column, SmallCardHeight);
        _maintenanceCard.Bounds = At(right, y + SmallCardHeight + RowGap, column, energy);
        y += energy + RowGap + SmallCardHeight + RowGap;
        var aboutWidth = (2 * column) + Gap;
        var aboutHeight = AboutHeight(aboutWidth);
        _aboutCard.Bounds = At(left, y, aboutWidth, aboutHeight);
        y += aboutHeight;

        // The footnote (or an apply error) sits in the title row, after "Settings".
        _footnoteLeft = Pad + 2 + (Gfx.Measure("Settings", ThemeFonts.For(dpi).CardTitle).Width * 96f / dpi) + 16;
        var footnoteWidth = Math.Max(0, contentWidth - Pad - doneWidth - 16 - _footnoteLeft);
        _applyError.Bounds = At(_footnoteLeft, TitleRowTop + ((TitleRowHeight - 28) / 2), footnoteWidth, 28);
        var contentHeight = y + BottomMargin;

        LayoutCards(column, sensorsWidth, pollingWidth, aboutWidth);

        var minSize = new Size(contentWidth > Dip(ClientSize.Width) + 0.5f ? this.Px(contentWidth) : 0, this.Px(contentHeight));
        if (AutoScrollMinSize != minSize)
        {
            AutoScrollMinSize = minSize;
        }

        base.OnLayout(levent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var scroll = AutoScrollPosition;
        var f = ThemeFonts.For(DeviceDpi);
        var titleTop = this.Px(TitleRowTop) + scroll.Y;
        var titleHeight = this.Px(TitleRowHeight);
        Gfx.Text(g, "Settings", f.CardTitle, new Rectangle(this.Px(Pad + 2) + scroll.X, titleTop, Math.Max(0, _done.Left - this.Px(Pad + 18)), titleHeight), Theme.Ink);

        if (!_applyError.Visible)
        {
            // On the title's baseline: CardTitle and Small text share the bottom of a vertically centred line.
            var titleText = Gfx.Measure("Settings", f.CardTitle).Height;
            var smallText = Gfx.Measure("Ag", f.Small).Height;
            var top = titleTop + ((titleHeight - titleText) / 2) + titleText - smallText - this.Px(3);
            var footnote = new Rectangle(this.Px(_footnoteLeft) + scroll.X, top, Math.Max(0, _done.Left - this.Px(_footnoteLeft + 16) - scroll.X), smallText);
            Gfx.Text(g, "Changes apply immediately.", f.Small, footnote, Theme.InkSecondary, Gfx.SingleLine);
        }
    }

    /// <summary>Opens a link or a mailto: address with the default handler.</summary>
    internal static void OpenWithShell(IWin32Window? owner, string target)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ThemedDialog.Show(owner, "Could not open the link", target + Environment.NewLine + ex.Message);
        }
    }

    private static void SetField(ThemedTextBox box, string text)
    {
        // Never overwrite what the user is typing.
        if (box.ContainsFocus && box.IsModified)
        {
            return;
        }

        if (box.Text != text)
        {
            box.SetText(text);
        }
    }

    private static void ShowResult(InfoLine line, ResultLine? result)
    {
        line.Visible = result is not null;
        if (result is not null)
        {
            line.Text = result.Text;
            line.Tone = HwinfoStepView.ToInfoTone(result.Tone);
        }
    }

    private void AddCard(CardPanel card, params Control[] children)
    {
        card.TabIndex = Controls.Count;
        for (var i = 0; i < children.Length; i++)
        {
            children[i].TabIndex = i;
        }

        card.Controls.AddRange(children);
        Controls.Add(card);
    }

    private void WireEvents()
    {
        // Both close (and dispose) the page: raise them after the click has been handled.
        _done.Click += (_, _) => Defer(() => DoneRequested?.Invoke(this, EventArgs.Empty));
        _rerun.Click += (_, _) => Defer(() => WizardRequested?.Invoke(this, EventArgs.Empty));
        _manage.Click += (_, _) => Defer(() => ManageShellysRequested?.Invoke(this, EventArgs.Empty));

        _change.Click += (_, _) => ChangePsu();
        _voltage.SelectedIndexChanged += (_, _) => Guard(() => _presenter.SetVoltageMode(VoltageModes[Math.Max(0, _voltage.SelectedIndex)]));
        _edit.Click += (_, _) => EditSensors();
        _sensorNames.Click += (_, _) => CustomizeSensorNames();

        _interval.ValueChanged += (_, _) => _intervalValue.Text = _interval.Value.ToString(System.Globalization.CultureInfo.CurrentCulture) + " ms";
        _interval.ValueCommitted += (_, _) => Guard(() => _presenter.SetIntervalMs(_interval.Value));
        _smoothing.ValueChanged += (_, _) => _smoothingValue.Text = _smoothing.Value == 1 ? "1 sample" : _smoothing.Value.ToString(System.Globalization.CultureInfo.CurrentCulture) + " samples";
        _smoothing.ValueCommitted += (_, _) => Guard(() => _presenter.SetSmoothing(_smoothing.Value));

        _logEnergy.CheckedChanged += (_, _) => Guard(() => _presenter.SetEnergyEnabled(_logEnergy.Checked));
        _openEnergy.Click += (_, _) => OpenFolder(AppPaths.EnergyDirectory);
        _price.TextCommitted += (_, _) => CommitField(_price);
        _currency.TextCommitted += (_, _) => CommitField(_currency);

        _startWithWindows.CheckedChanged += (_, _) => Guard(() => _presenter.SetStartWithWindows(_startWithWindows.Checked));
        _showTrayIcon.CheckedChanged += (_, _) => Guard(() => _presenter.SetShowTrayIcon(_showTrayIcon.Checked));

        _checkUpdate.Click += async (_, _) => await _presenter.CheckDatabaseUpdateAsync();

        _openLog.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);
        _remove.Click += (_, _) => RemoveSensors();
        _exit.Click += (_, _) =>
        {
            if (ShowModal(() => ThemedDialog.Show(FindForm(), "Exit ShellyHwinfoBridge?", "Polling stops and the Shelly Power sensors in HWiNFO go offline until the app is started again.", "Exit", "Cancel")) == DialogResult.OK)
            {
                Defer(() => ExitRequested?.Invoke(this, EventArgs.Empty));
            }
        };

        _email.Click += (_, _) => ShowModal(() =>
        {
            OpenWithShell(FindForm(), "mailto:" + FeedbackEmail);
            return DialogResult.OK;
        });
        _issues.Click += (_, _) => ShowModal(() =>
        {
            OpenWithShell(FindForm(), IssuesUrl);
            return DialogResult.OK;
        });
    }

    /// <summary>Runs <paramref name="action"/> once the current message is handled (on the window, which outlives the page).</summary>
    private void Defer(Action action)
    {
        if (FindForm() is { IsHandleCreated: true, IsDisposed: false } form)
        {
            form.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void Guard(Action action)
    {
        if (!_applying)
        {
            action();
        }
    }

    private void CommitField(ThemedTextBox box)
    {
        if (_applying || !box.IsModified)
        {
            return;
        }

        box.IsModified = false;
        if (ReferenceEquals(box, _price))
        {
            _presenter.SetPrice(_price.Text);
        }
        else if (ReferenceEquals(box, _currency))
        {
            _presenter.SetCurrency(_currency.Text);
        }
    }

    private void OnPresenterChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        var s = _presenter.State;
        _applying = true;
        try
        {
            // Shelly plugs.
            _devicesText.Text = s.DevicesText;

            // Power supply.
            _psuTitle.Text = s.PsuTitle;
            _psuSubtitle.Text = s.PsuSubtitle;
            _voltage.SetItems(["Auto", "115 V", "230 V"], Math.Max(0, Array.IndexOf(VoltageModes, s.VoltageMode)));
            _curveNote.Visible = s.CurveNote is not null;
            _curveNote.Text = s.CurveNote ?? string.Empty;

            // Sensors and formula: the calculation, the gadget how-to with the selected names, the retired formula note.
            if (_formula.Text != s.FormulaText)
            {
                _formula.SetText(s.FormulaText);
            }

            var lines = s.FormulaText.Split('\n').Length;
            _formulaLines = Math.Clamp(lines, 1, FormulaMaxLines);
            _formula.InnerTextBox.ScrollBars = lines > FormulaMaxLines ? ScrollBars.Vertical : ScrollBars.None;
            _gadgetTip.Text = s.GadgetTip.Length > 0 ? s.GadgetTip : HwinfoTips.GadgetExport;
            _retiredNote.Visible = s.RetiredFormulaNote is not null;
            _retiredNote.Text = s.RetiredFormulaNote ?? string.Empty;

            // Polling.
            _interval.Value = s.IntervalMs;
            _intervalValue.Text = s.IntervalText;
            _smoothing.Value = s.Smoothing;
            _smoothingValue.Text = s.SmoothingText;

            // Energy.
            _logEnergy.Checked = s.EnergyEnabled;
            SetField(_price, s.PriceText);
            _price.HasError = s.PriceError is not null;
            _priceError.Visible = s.PriceError is not null;
            _priceError.Text = s.PriceError ?? string.Empty;
            SetField(_currency, s.Currency);

            // Startup and tray.
            _startWithWindows.Checked = s.StartWithWindows;
            _showTrayIcon.Checked = s.ShowTrayIcon;

            // PSU database.
            _databaseText.Text = s.DatabaseText;
            ShowResult(_databaseResult, s.DatabaseResult);
            _checkUpdate.Enabled = !s.IsBusy;

            _applyError.Visible = s.ApplyError is not null;
            _applyError.Text = s.ApplyError is null ? string.Empty : "Could not apply the change: " + s.ApplyError;
        }
        finally
        {
            _applying = false;
        }

        PerformLayout();
        Invalidate();
    }

    private float FormulaHeight()
    {
        var lineHeight = Gfx.Measure("Ag", ThemeFonts.For(DeviceDpi).Mono).Height * 96f / DeviceDpi;
        return Math.Max(FormulaMinHeight, (_formulaLines * lineHeight) + 20);
    }

    private const float FormulaWidth = 240;
    private const float SensorsShare = 0.62f;
    private const float SensorNamesTop = 12;
    private const float SensorNamesHeight = 36;

    private float TipHeight(float cardWidth)
    {
        var width = this.Px(cardWidth - 40 - FormulaWidth - 16);
        return _gadgetTip.GetPreferredHeight(width) * 96f / DeviceDpi;
    }

    private float NoteHeight(float cardWidth) =>
        _retiredNote.Visible ? _retiredNote.GetPreferredHeight(this.Px(cardWidth - 40)) * 96f / DeviceDpi : 0;

    /// <summary>The Sensors card: formula and tip, then "Customize HWiNFO sensor names" and the retired formula note.</summary>
    private float SensorsHeight(float cardWidth)
    {
        var note = NoteHeight(cardWidth);
        return 50 + Math.Max(FormulaHeight(), TipHeight(cardWidth)) + SensorNamesTop + SensorNamesHeight + (note > 0 ? 8 + note : 0) + 16;
    }

    /// <summary>The About card: name and version, the feedback line (wraps as a flow), the licence and attribution lines.</summary>
    private float AboutHeight(float cardWidth)
    {
        var flow = LayoutFeedback(cardWidth - 40, apply: false);
        var attribution = _attribution.GetPreferredHeight(this.Px(cardWidth - 40)) * 96f / DeviceDpi;
        return 50 + 26 + 4 + flow + 4 + 24 + 2 + Math.Max(24, attribution) + 18;
    }

    /// <summary>Places the feedback sentence's labels and links left to right, wrapping between them; returns its height in DIP.</summary>
    private float LayoutFeedback(float innerWidth, bool apply)
    {
        var parts = new Control[] { _feedbackStart, _email, _feedbackMiddle, _issues };
        const float lineHeight = 26;
        const float space = 5;
        var x = 0f;
        var y = 0f;
        foreach (var part in parts)
        {
            var width = part.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi;
            if (x > 0 && x + width > innerWidth)
            {
                x = 0;
                y += lineHeight;
            }

            if (apply)
            {
                part.Bounds = new Rectangle(this.Px(20 + x), this.Px(50 + 26 + 4 + y), this.Px(width), this.Px(lineHeight));
            }

            x += width + space;
        }

        return y + lineHeight;
    }

    private void LayoutCards(float column, float sensorsWidth, float pollingWidth, float aboutWidth)
    {
        var inner = column - 40;
        Rectangle R(float x, float y, float width, float height) => new(this.Px(x), this.Px(y), this.Px(width), this.Px(height));
        float PrefWidth(Control c) => c.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi;

        // Shelly plugs: the devices summary and the hint, then Manage Shellys.
        _devicesText.Bounds = R(20, 48, inner, 26);
        _devicesHint.Bounds = R(20, 74, inner, 22);
        _manage.Bounds = R(20, 104, Math.Max(170, PrefWidth(_manage) - 16), 36);

        // Power supply: model and details with Change…, then the input voltage segments.
        var changeWidth = PrefWidth(_change);
        _change.Bounds = R(20 + inner - changeWidth, 48, changeWidth, 26);
        _psuTitle.Bounds = R(20, 48, inner - changeWidth - 16, 26);
        _psuSubtitle.Bounds = R(20, 74, inner, 22);
        _voltageLabel.Bounds = R(20, 104, 110, 36);
        _voltage.Bounds = R(134, 104, inner - 114, 36);
        _curveNote.Bounds = R(20, 146, inner, CurveNoteHeight - 2);

        // Sensors and formula: Edit… in the title row, the read-only formula box and the gadget tip beside it; under them
        // "Customize HWiNFO sensor names" and, when a retired custom formula is stored, its note.
        var sensorsInner = sensorsWidth - 40;
        var editWidth = PrefWidth(_edit);
        _edit.Bounds = R(20 + sensorsInner - editWidth, 21, editWidth, 26);
        var formulaHeight = FormulaHeight();
        var tipHeight = TipHeight(sensorsWidth);
        _formula.Bounds = R(20, 50, FormulaWidth, formulaHeight);
        _gadgetTip.Bounds = R(20 + FormulaWidth + 16, 50, sensorsInner - FormulaWidth - 16, tipHeight);
        var namesTop = 50 + Math.Max(formulaHeight, tipHeight) + SensorNamesTop;
        _sensorNames.Bounds = R(20, namesTop, Math.Min(sensorsInner, PrefWidth(_sensorNames) - 16), SensorNamesHeight);
        _retiredNote.Bounds = R(20, namesTop + SensorNamesHeight + 8, sensorsInner, NoteHeight(sensorsWidth));

        // Polling: label, slider, value.
        var sliderWidth = pollingWidth - 40 - 92 - 104;
        _intervalLabel.Bounds = R(20, 60, 90, 28);
        _interval.Bounds = R(112, 60, sliderWidth, 28);
        _intervalValue.Bounds = R(112 + sliderWidth + 16, 60, 88, 28);
        _smoothingLabel.Bounds = R(20, 104, 90, 28);
        _smoothing.Bounds = R(112, 104, sliderWidth, 28);
        _smoothingValue.Bounds = R(112 + sliderWidth + 16, 104, 88, 28);

        // Energy: toggle and folder link, then price and currency (and a price error).
        _logEnergy.Bounds = R(20, 50, Math.Min(inner - 160, PrefWidth(_logEnergy)), 28);
        var openWidth = PrefWidth(_openEnergy);
        _openEnergy.Bounds = R(20 + inner - openWidth, 51, openWidth, 26);
        var priceWidth = Math.Max(110, (inner - 114 - 16 - 70) * 0.6f);
        _priceLabel.Bounds = R(20, 86, 110, 36);
        _price.Bounds = R(134, 86, priceWidth, 36);
        var currencyLabelLeft = 134 + priceWidth + 16;
        _currencyLabel.Bounds = R(currencyLabelLeft, 86, 70, 36);
        _currency.Bounds = R(currencyLabelLeft + 70, 86, 20 + inner - currencyLabelLeft - 70, 36);
        _priceError.Bounds = R(20, 130, inner, 28);

        // PSU database: text and result on the left, Check for update on the right.
        _checkUpdate.Bounds = R(20 + inner - 180, 26, 180, 38);
        _databaseText.Bounds = R(20, 46, inner - 196, 22);
        _databaseResult.Bounds = R(20, 66, inner - 196, 22);

        // Startup and tray: the two switches side by side.
        var startWidth = Math.Min(inner / 2, PrefWidth(_startWithWindows));
        _startWithWindows.Bounds = R(20, 50, startWidth, 28);
        _showTrayIcon.Bounds = R(20 + Math.Max(startWidth + 32, inner / 2), 50, Math.Min(inner / 2, PrefWidth(_showTrayIcon)), 28);

        // Maintenance: Exit in the title row (right); Open log folder and Re-run setup wizard; Remove HWiNFO sensors.
        var exitWidth = PrefWidth(_exit);
        _exit.Bounds = R(20 + inner - exitWidth, 21, exitWidth, 26);
        var openLogWidth = Math.Max(150, PrefWidth(_openLog) - 16);
        _openLog.Bounds = R(20, 50, openLogWidth, 36);
        _rerun.Bounds = R(20 + openLogWidth + 12, 50, Math.Max(180, PrefWidth(_rerun) - 16), 36);
        _remove.Bounds = R(20, 96, PrefWidth(_remove), 26);

        // About: name and version; the feedback sentence with its two links; licence; Cybenetics attribution.
        var aboutInner = aboutWidth - 40;
        _aboutName.Bounds = R(20, 50, aboutInner, 26);
        var flow = LayoutFeedback(aboutInner, apply: true);
        var licenceTop = 50 + 26 + 4 + flow + 4;
        _licence.Bounds = R(20, licenceTop, aboutInner, 24);
        var attribution = Math.Max(24, _attribution.GetPreferredHeight(this.Px(aboutInner)) * 96f / DeviceDpi);
        _attribution.Bounds = R(20, licenceTop + 24 + 2, aboutInner, attribution);
    }

    private DialogResult ShowModal(Func<DialogResult> show)
    {
        _modalDepth++;
        try
        {
            return show();
        }
        finally
        {
            _modalDepth--;
        }
    }

    private void ChangePsu()
    {
        var current = _runtime.Settings?.Clone() ?? new AppSettings();
        var model = new PsuStepModel(_runtime.Database);
        model.LoadFrom(current);
        model.MeasuredVolts = _runtime.LastReading?.Raw.Volts;
        using var dialog = new StepDialog(
            "Power supply",
            "Pick it from the database for the most accurate curve. If it is missing, use a generic curve by rating or enter your own.",
            new PsuStepView(model),
            () => model.IsValid ? null : model.ValidationMessage);
        if (ShowModal(() => dialog.ShowDialog(FindForm())) == DialogResult.OK)
        {
            _presenter.Update(model.ApplyTo);
        }
    }

    private void EditSensors()
    {
        var current = _runtime.Settings?.Clone() ?? new AppSettings();
        var model = new SensorStepModel();
        void Load() => model.Load(_readSharedMemory()?.GetPowerSensors(), current.Sensors);
        Load();
        var view = new SensorStepView(model, () =>
        {
            model.ApplyTo(current);
            Load();
        });
        using var dialog = new StepDialog(
            "Sensors and formula",
            "These HWiNFO power sensors are subtracted from DC power to compute Other Components.",
            view,
            () => model.IsValid ? null
                : model.InvalidNames.Count > 0 ? "A selected sensor name contains a double quote. Rename it in HWiNFO or untick it."
                : "Select at least one sensor or enter a formula.");
        if (ShowModal(() => dialog.ShowDialog(FindForm())) == DialogResult.OK)
        {
            _presenter.Update(model.ApplyTo);
        }
    }

    /// <summary>"Customize HWiNFO sensor names": the names dialog; Save shows the rename warning first, then applies.</summary>
    private void CustomizeSensorNames()
    {
        var model = _presenter.CreateSensorNames();
        StepDialog? dialog = null;
        using var created = new StepDialog(
            "Customize HWiNFO sensor names",
            "Leave a name empty to use the default. Names must be unique and contain no double quote.",
            new SensorNamesView(model),
            () => SaveSensorNames(model, dialog));
        dialog = created;
        void UpdateSave() => created.SaveEnabled = model.IsValid && model.HasChanges;
        model.Changed += (_, _) => UpdateSave();
        UpdateSave();
        ShowModal(() => created.ShowDialog(FindForm()));

        // The gadget tip and the summaries use the (possibly renamed) settings.
        ApplyState();
    }

    private static string? SaveSensorNames(SensorNamesModel model, IWin32Window? owner)
    {
        if (!model.IsValid)
        {
            return "Fix the names marked in red first.";
        }

        if (!model.HasChanges)
        {
            return StepDialog.StayOpen;
        }

        if (model.ShowRenameWarning &&
            ThemedDialog.Show(owner, "Rename the HWiNFO sensors?", DeviceFormModel.RenameWarningText, "Rename", "Cancel") != DialogResult.OK)
        {
            return StepDialog.StayOpen;
        }

        var outcome = model.Save();
        return outcome is { Success: true } ? null : "Could not apply the names: " + (outcome?.Error ?? model.ApplyError ?? "unknown error");
    }

    private void RemoveSensors()
    {
        var answer = ShowModal(() => ThemedDialog.Show(
            FindForm(),
            "Remove HWiNFO sensors?",
            "This deletes the Shelly Power sensors from HWiNFO and pauses polling, because the next poll would create them again. Use this before uninstalling. Resume polling (main window or tray) to create them again.",
            "Remove",
            "Cancel"));
        if (answer != DialogResult.OK)
        {
            return;
        }

        _presenter.RemoveSensors();
        SensorsRemoved?.Invoke(this, EventArgs.Empty);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowModal(() => ThemedDialog.Show(FindForm(), "Could not open the folder", ex.Message));
        }
    }
}
