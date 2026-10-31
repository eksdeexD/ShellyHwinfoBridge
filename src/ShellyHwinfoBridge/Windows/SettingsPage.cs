using System.Diagnostics;
using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The settings page, shown inside the main window in place of the dashboard (layout after design/final/settings.png):
/// a title row ("Settings", the footnote "Changes apply immediately." and "Done") and two columns of cards (Shelly
/// device, Power supply, Sensors and formula, Polling, Energy, PSU database, Startup and tray, Maintenance). Every change goes
/// through <see cref="SettingsPresenter"/> and is applied at once; sliders apply when released, text fields on Enter or
/// when focus leaves. "Change…" and "Edit…" open a modal <see cref="StepDialog"/> owned by the main window. The page
/// scrolls when the window is shorter than its content.
/// </summary>
internal sealed class SettingsPage : ThemedPanel
{
    // Layout in DIP, page coordinates. At the default 1100×740 main window the page starts under the header (y 68).
    private const float Pad = 24;
    private const float Gap = 16;
    private const float RowGap = 10;
    private const float TitleRowTop = 6;
    private const float TitleRowHeight = 34;
    private const float CardsTop = 50;
    private const float MinContentWidth = 880;
    private const float Row1Height = 148;
    private const float Row2MinHeight = 150;
    private const float EnergyHeight = 132;
    private const float SmallCardHeight = 88;
    private const float FootnoteGap = 2;
    private const float FootnoteHeight = 20;
    private const float FormulaMinHeight = 88;
    private const int FormulaMaxLines = 10;

    private static readonly InputVoltageMode[] VoltageModes = [InputVoltageMode.Auto, InputVoltageMode.V115, InputVoltageMode.V230];

    private readonly SettingsPresenter _presenter;
    private readonly IBridgeRuntime _runtime;
    private readonly Func<HwinfoSnapshot?> _readSharedMemory;

    private readonly PillButton _done = new() { Text = "Done", AccessibleName = "Done" };

    // Shelly device.
    private readonly CardPanel _shellyCard = new() { Title = "Shelly device" };
    private readonly TextLabel _hostLabel = new() { Text = "Host", Tone = UiTone.Secondary };
    private readonly TextLabel _passwordLabel = new() { Text = "Password", Tone = UiTone.Secondary };
    private readonly TextLink _clearPassword = new() { Text = "Clear", AccessibleName = "Clear the stored password" };
    private readonly ThemedTextBox _host = new() { AccessibleName = "Host" };
    private readonly ThemedTextBox _password = new() { UseSystemPasswordChar = true, AccessibleName = "Password" };
    private readonly PillButton _test = new() { Text = "Test connection" };
    private readonly InfoLine _testResult = new() { Visible = false };

    // Power supply.
    private readonly CardPanel _psuCard = new() { Title = "Power supply" };
    private readonly TextLabel _psuTitle = new() { Role = FontRole.Subtitle };
    private readonly TextLabel _psuSubtitle = new() { Tone = UiTone.Secondary };
    private readonly TextLink _change = new() { Text = "Change…", Semibold = true, AccessibleName = "Change power supply" };
    private readonly TextLabel _voltageLabel = new() { Text = "Input voltage", Tone = UiTone.Secondary };
    private readonly SegmentedControl _voltage = new() { AccessibleName = "Input voltage" };

    // Sensors and formula.
    private readonly CardPanel _sensorsCard = new() { Title = "Sensors and formula" };
    private readonly ThemedTextBox _formula = new() { Multiline = true, Monospace = true, ReadOnly = true, AccessibleName = "Formula" };
    private readonly TextLink _edit = new() { Text = "Edit…", Semibold = true, AccessibleName = "Edit sensors and formula" };
    private readonly TextLabel _gadgetTip = new() { Text = HwinfoTips.GadgetExport, Role = FontRole.Small, Tone = UiTone.Secondary, WordWrap = true };

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

    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error, Visible = false };

    private bool _applying;
    private int _modalDepth;
    private int _formulaLines = 1;
    private float _footnoteLeft;
    private float _bottomLineTop;
    private bool _committed;

    /// <summary>Creates the page.</summary>
    /// <param name="presenter">The presenter that applies every change.</param>
    /// <param name="runtime">The runtime (PSU database and last reading for the PSU dialog).</param>
    /// <param name="readSharedMemory">Reads HWiNFO's shared memory for the sensor dialog (null when it is off).</param>
    public SettingsPage(SettingsPresenter presenter, IBridgeRuntime runtime, Func<HwinfoSnapshot?> readSharedMemory)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _readSharedMemory = readSharedMemory ?? throw new ArgumentNullException(nameof(readSharedMemory));
        AccessibleName = "Settings";
        AccessibleRole = AccessibleRole.Pane;
        AutoScroll = true;

        SuspendLayout();
        _voltage.SetItems(["Auto", "115 V", "230 V"], 0);
        _done.TabIndex = 0;
        Controls.Add(_done);
        AddCard(_shellyCard, _hostLabel, _host, _passwordLabel, _password, _clearPassword, _test, _testResult);
        AddCard(_psuCard, _psuTitle, _psuSubtitle, _change, _voltageLabel, _voltage);
        AddCard(_sensorsCard, _formula, _edit, _gadgetTip);
        AddCard(_pollingCard, _intervalLabel, _interval, _intervalValue, _smoothingLabel, _smoothing, _smoothingValue);
        AddCard(_energyCard, _logEnergy, _openEnergy, _priceLabel, _price, _currencyLabel, _currency, _priceError);
        AddCard(_databaseCard, _databaseText, _checkUpdate, _databaseResult);
        AddCard(_startupCard, _startWithWindows, _showTrayIcon);
        AddCard(_maintenanceCard, _exit, _openLog, _rerun, _remove);
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

    /// <summary>Gets whether a dialog (step dialog or confirmation) is open over the page.</summary>
    public bool IsShowingDialog => _modalDepth > 0;

    /// <summary>Re-reads the presenter state into the controls (fields being edited keep their text).</summary>
    public void RefreshState() => ApplyState();

    /// <summary>Puts the keyboard focus into the host field (when the page is opened).</summary>
    public void FocusFirstField()
    {
        _host.Focus();
        _host.InnerTextBox.Select(_host.Text.Length, 0);
    }

    /// <summary>Applies a field that is still being edited, so leaving the page never loses a typed change.</summary>
    public void CommitPendingEdits()
    {
        if (_committed)
        {
            return;
        }

        foreach (var box in new[] { _host, _password, _price, _currency })
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
        // Maintenance, so both columns end at the same height.
        var sensorsWidth = (float)Math.Round(2 * column * SensorsShare);
        var pollingWidth = (2 * column) - sensorsWidth;
        var row2 = Math.Max(Row2MinHeight, 50 + Math.Max(FormulaHeight(), TipHeight(sensorsWidth)) + 16);
        var energy = EnergyHeight + (_priceError.Visible ? 30 : 0);
        var y = CardsTop;
        _shellyCard.Bounds = At(left, y, column, Row1Height);
        _psuCard.Bounds = At(right, y, column, Row1Height);
        y += Row1Height + RowGap;
        _sensorsCard.Bounds = At(left, y, sensorsWidth, row2);
        _pollingCard.Bounds = At(left + sensorsWidth + Gap, y, pollingWidth, row2);
        y += row2 + RowGap;
        _energyCard.Bounds = At(left, y, column, energy);
        _databaseCard.Bounds = At(left, y + energy + RowGap, column, SmallCardHeight);
        _startupCard.Bounds = At(right, y, column, SmallCardHeight);
        _maintenanceCard.Bounds = At(right, y + SmallCardHeight + RowGap, column, energy);
        y += energy + RowGap + SmallCardHeight;

        // The footnote (or an apply error) sits in the title row, after "Settings"; the bottom line is kept free.
        _footnoteLeft = Pad + 2 + (Gfx.Measure("Settings", ThemeFonts.For(dpi).CardTitle).Width * 96f / dpi) + 16;
        var footnoteWidth = Math.Max(0, contentWidth - Pad - doneWidth - 16 - _footnoteLeft);
        _applyError.Bounds = At(_footnoteLeft, TitleRowTop + ((TitleRowHeight - 28) / 2), footnoteWidth, 28);
        _bottomLineTop = y + FootnoteGap;
        var contentHeight = _bottomLineTop + FootnoteHeight;

        LayoutCards(column, sensorsWidth, pollingWidth);

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

        // The version at the bottom right, under the last cards.
        var contentRight = Math.Max(ClientSize.Width, this.Px(MinContentWidth)) - this.Px(Pad);
        var version = new Rectangle(scroll.X, this.Px(_bottomLineTop) + scroll.Y, contentRight, this.Px(FootnoteHeight));
        Gfx.Text(g, VersionText, f.Small, version, Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    /// <summary>Gets the app version for the bottom line ("v0.1.0-beta.2"; build metadata after '+' dropped).</summary>
    internal static string VersionText { get; } = "v" + (typeof(SettingsPage).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0");

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

        _host.TextCommitted += (_, _) => CommitField(_host);
        _password.TextCommitted += (_, _) => CommitField(_password);
        _clearPassword.Click += (_, _) =>
        {
            _password.SetText(string.Empty);
            _presenter.ClearPassword();
        };
        _test.Click += async (_, _) => await _presenter.TestConnectionAsync(_host.Text, _password.Text);

        _change.Click += (_, _) => ChangePsu();
        _voltage.SelectedIndexChanged += (_, _) => Guard(() => _presenter.SetVoltageMode(VoltageModes[Math.Max(0, _voltage.SelectedIndex)]));
        _edit.Click += (_, _) => EditSensors();

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
        if (ReferenceEquals(box, _host))
        {
            _presenter.SetHost(_host.Text);
        }
        else if (ReferenceEquals(box, _password))
        {
            // The field stays empty ("unchanged"): only typed text replaces the stored password.
            var typed = _password.Text;
            _password.SetText(string.Empty);
            _presenter.SetPassword(typed);
        }
        else if (ReferenceEquals(box, _price))
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
            // Shelly device.
            SetField(_host, s.Host);
            _host.HasError = s.HostError is not null;
            _password.Placeholder = s.PasswordPlaceholder;
            _clearPassword.Visible = s.HasPassword;
            ShowResult(_testResult, s.HostError is { } hostError ? new ResultLine(Tone.Error, hostError) : s.TestResult);
            _test.Enabled = !s.IsBusy;

            // Power supply.
            _psuTitle.Text = s.PsuTitle;
            _psuSubtitle.Text = s.PsuSubtitle;
            _voltage.SetItems(["Auto", "115 V", "230 V"], Math.Max(0, Array.IndexOf(VoltageModes, s.VoltageMode)));

            // Sensors and formula.
            if (_formula.Text != s.FormulaText)
            {
                _formula.SetText(s.FormulaText);
            }

            var lines = s.FormulaText.Split('\n').Length;
            _formulaLines = Math.Clamp(lines, 1, FormulaMaxLines);
            _formula.InnerTextBox.ScrollBars = lines > FormulaMaxLines ? ScrollBars.Vertical : ScrollBars.None;

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

    private float TipHeight(float cardWidth)
    {
        var width = this.Px(cardWidth - 40 - FormulaWidth - 16);
        return _gadgetTip.GetPreferredHeight(width) * 96f / DeviceDpi;
    }

    private void LayoutCards(float column, float sensorsWidth, float pollingWidth)
    {
        var inner = column - 40;
        Rectangle R(float x, float y, float width, float height) => new(this.Px(x), this.Px(y), this.Px(width), this.Px(height));
        float PrefWidth(Control c) => c.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi;

        // Shelly device: Host | Password, then Test connection with the result to its right.
        var half = (inner - 16) / 2;
        _hostLabel.Bounds = R(20, 48, half, 22);
        _passwordLabel.Bounds = R(36 + half, 48, half - 60, 22);
        var clearWidth = PrefWidth(_clearPassword);
        _clearPassword.Bounds = R(20 + inner - clearWidth, 46, clearWidth, 26);
        _host.Bounds = R(20, 70, half, 34);
        _password.Bounds = R(36 + half, 70, half, 34);
        _test.Bounds = R(20, 110, 174, 34);
        var resultLeft = 20 + 174 + 16;
        var resultWidth = this.Px(inner - 174 - 16);
        // Centred on the button; two wrapped lines may extend a little above it.
        var resultHeight = Math.Min(this.Px(46), _testResult.GetPreferredHeight(resultWidth));
        _testResult.Bounds = new Rectangle(this.Px(resultLeft), this.Px(110) + ((this.Px(34) - resultHeight) / 2), resultWidth, resultHeight);

        // Power supply: model and details with Change…, then the input voltage segments.
        var changeWidth = PrefWidth(_change);
        _change.Bounds = R(20 + inner - changeWidth, 48, changeWidth, 26);
        _psuTitle.Bounds = R(20, 48, inner - changeWidth - 16, 26);
        _psuSubtitle.Bounds = R(20, 74, inner, 22);
        _voltageLabel.Bounds = R(20, 104, 110, 36);
        _voltage.Bounds = R(134, 104, inner - 114, 36);

        // Sensors and formula: Edit… in the title row, the read-only formula box and the gadget tip beside it.
        var sensorsInner = sensorsWidth - 40;
        var editWidth = PrefWidth(_edit);
        _edit.Bounds = R(20 + sensorsInner - editWidth, 21, editWidth, 26);
        _formula.Bounds = R(20, 50, FormulaWidth, FormulaHeight());
        _gadgetTip.Bounds = R(20 + FormulaWidth + 16, 50, sensorsInner - FormulaWidth - 16, TipHeight(sensorsWidth));

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
        void Load() => model.Load(_readSharedMemory()?.GetPowerSensors(), current.Sensors, current.CustomFormula);
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
