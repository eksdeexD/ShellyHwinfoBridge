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
/// The settings page, shown inside the main window in place of the dashboard: a title row ("Settings" and the footnote
/// "Changes apply immediately." at the left, "‹ Home" right-aligned), then the cards: Shelly plugs ("Manage Shellys")
/// and Power supply ("Change", input voltage); Sensors across the page (the calculation, a one-line gadget explainer with
/// "How to enable?", "Customize HWiNFO sensor names" and "Edit sensors" in its lower-right corner); Display (display
/// interval and AC smoothing) and Energy;
/// PSU database ("Check for update" in its lower-right corner) and Startup and tray; Maintenance (Open log folder,
/// Re-run setup wizard, Start/Stop diagnostic trace with "Recording to …" under it; "Remove HWiNFO sensors"
/// right-aligned, with a confirmation). Every change goes through
/// <see cref="SettingsPresenter"/> and is applied at once; sliders apply when released, text fields on Enter or when
/// focus leaves. "Change", "Edit sensors" and the sensor names open a modal <see cref="StepDialog"/> owned by the main
/// window; "How to enable?" the <see cref="GadgetHelpDialog"/>; "Manage Shellys" asks the main window to open
/// <see cref="ManageShellysPage"/>. The page scrolls inside the fixed-size window. About has its own window.
/// </summary>
internal sealed class SettingsPage : ScrollPanel
{
    /// <summary>The text of the confirmation of "Remove HWiNFO sensors".</summary>
    public const string RemoveSensorsConfirmText =
        "This deletes the Shelly Power sensor group from HWiNFO's registry key. HWiNFO stops showing the bridge's sensors after its next restart. Your settings are kept; polling pauses until you resume it.";

    /// <summary>The page's back command ("‹ Home"; Manage Shellys has "‹ Settings" in the same place).</summary>
    public const string HomeText = "‹ Home";

    // Layout in DIP, page coordinates (the page sits between the main window's header and footer rows).
    private const float Pad = Theme.WindowPadding;
    private const float Gap = Theme.Gap;
    private const float P = Theme.CardPadding;
    private const float MinContentWidth = 760;
    private const float Line = Theme.LineHeight;
    private const float Field = Theme.FieldHeight;
    private const float Segment = Theme.ButtonHeight;
    private const float CurveNoteHeight = 24;
    private const float SubLine = 24;
    private const float TextLine = 21;
    private const float BottomMargin = 16;
    private const float FormulaWidth = 260;
    private const float FormulaMinHeight = 72;
    private const int FormulaMaxLines = 10;
    private const float FieldLabelWidth = 100;
    private const float BottomPad = 12;

    private static readonly InputVoltageMode[] VoltageModes = [InputVoltageMode.Auto, InputVoltageMode.V115, InputVoltageMode.V230];

    private readonly SettingsPresenter _presenter;
    private readonly IBridgeRuntime _runtime;
    private readonly Func<HwinfoSnapshot?> _readSharedMemory;
    private readonly Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> _discover;

    // Shelly plugs.
    private readonly CardPanel _shellyCard = new() { Title = "Shelly plugs" };
    private readonly TextLabel _devicesText = new() { Role = FontRole.Subtitle };
    private readonly TextLabel _devicesHint = new() { Text = "Add a Shelly for your monitor or another device.", Tone = UiTone.Secondary };
    private readonly TextLink _manage = new() { Text = "Manage Shellys", Semibold = true };

    // Power supply.
    private readonly CardPanel _psuCard = new() { Title = "Power supply" };
    private readonly TextLabel _psuTitle = new() { Role = FontRole.Subtitle };
    private readonly TextLabel _psuSubtitle = new() { Tone = UiTone.Secondary };
    private readonly TextLink _psuLink = new() { Visible = false, AccessibleName = "Open the Cybenetics report" };
    private bool _hasPsuLink;
    private readonly TextLink _change = new() { Text = "Change", Semibold = true, AccessibleName = "Change power supply" };
    private readonly TextLabel _voltageLabel = new() { Text = "Input voltage", Tone = UiTone.Secondary };
    private readonly SegmentedControl _voltage = new() { AccessibleName = "Input voltage" };
    private readonly InfoLine _curveNote = new() { Visible = false };

    // Sensors.
    private readonly CardPanel _sensorsCard = new() { Title = "Sensors" };
    private readonly ThemedTextBox _formula = new() { Multiline = true, Monospace = true, ReadOnly = true, AccessibleName = "Other Components calculation" };
    private readonly TextLink _edit = new() { Text = "Edit sensors", Semibold = true };
    private readonly TextLabel _gadgetTip = new() { Text = HwinfoTips.GadgetExplainer, Tone = UiTone.Secondary, WordWrap = true };
    private readonly TextLink _howToEnable = new() { Text = MainWindowPresenter.HowToEnableText, Semibold = true, AccessibleName = "How to enable the HWiNFO Gadget export" };
    private readonly TextLink _sensorNames = new() { Text = "Customize HWiNFO sensor names", Semibold = true };
    private readonly InfoLine _retiredNote = new() { Visible = false };

    // Display.
    private readonly CardPanel _displayCard = new() { Title = SettingsPresenter.DisplayCardTitle };
    private readonly TextLabel _intervalLabel = new() { Text = SettingsPresenter.DisplayIntervalLabel, Tone = UiTone.Secondary };
    private readonly ThemedSlider _interval = new() { Minimum = AppSettings.MinDisplayIntervalMs, Maximum = AppSettings.MaxDisplayIntervalMs, SmallChange = AppSettings.DisplayIntervalStepMs, LargeChange = AppSettings.DisplayIntervalStepMs, AccessibleName = SettingsPresenter.DisplayIntervalLabel };
    private readonly TextLabel _intervalValue = new();
    private readonly TextLabel _smoothingLabel = new() { Text = SettingsPresenter.SmoothingLabel, Tone = UiTone.Secondary };
    private readonly ThemedSlider _smoothing = new() { Minimum = 1, Maximum = MovingAverage.MaxSamples, SmallChange = 1, LargeChange = 5, AccessibleName = "Smoothing" };
    private readonly TextLabel _smoothingValue = new();

    // Energy.
    private readonly CardPanel _energyCard = new() { Title = "Energy" };
    private readonly ToggleSwitch _logEnergy = new() { Text = "Log energy use" };
    private readonly TextLink _openEnergy = new() { Text = "Open data folder", Semibold = true };
    private readonly TextLabel _priceLabel = new() { Text = "Price per kWh", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _price = new() { Placeholder = "optional", AccessibleName = "Price per kWh" };
    private readonly TextLabel _currencyLabel = new() { Text = "Currency", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _currency = new() { Placeholder = "e.g. DKK", MaxLength = AppSettings.MaxCurrencyLength, AccessibleName = "Currency" };
    private readonly InfoLine _priceError = new() { Tone = InfoTone.Error, Visible = false };

    // PSU database.
    private readonly CardPanel _databaseCard = new() { Title = "PSU database" };
    private readonly TextLabel _databaseText = new() { Tone = UiTone.Secondary };
    private readonly TextLink _checkUpdate = new() { Text = "Check for update", Semibold = true };
    private readonly InfoLine _databaseResult = new() { Visible = false };

    // Startup and tray.
    private readonly CardPanel _startupCard = new() { Title = "Startup and tray" };
    private readonly ToggleSwitch _startWithWindows = new() { Text = "Start with Windows" };
    private readonly ToggleSwitch _showTrayIcon = new() { Text = "Show tray icon" };

    // Maintenance.
    private readonly CardPanel _maintenanceCard = new() { Title = "Maintenance" };
    private readonly TextLink _openLog = new() { Text = "Open log folder", Semibold = true };
    private readonly TextLink _rerun = new() { Text = "Re-run setup wizard", Semibold = true };
    private readonly TextLink _trace = new() { Text = SettingsPresenter.StartTraceText, Semibold = true };
    private readonly TextLabel _traceCaption = new() { Role = FontRole.Small, Tone = UiTone.Secondary, Visible = false };
    private readonly TextLink _remove = new() { Text = "Remove HWiNFO sensors", Semibold = true };

    private readonly Func<GadgetStepModel> _createGadgetModel;
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
    /// <param name="createGadgetModel">Creates the gadget how-to model for "How to enable?" (loaded with the selected sensors).</param>
    public SettingsPage(SettingsPresenter presenter, IBridgeRuntime runtime, Func<HwinfoSnapshot?> readSharedMemory, Func<CancellationToken, Task<IReadOnlyList<DiscoveredShelly>>> discover, Func<GadgetStepModel> createGadgetModel)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _readSharedMemory = readSharedMemory ?? throw new ArgumentNullException(nameof(readSharedMemory));
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _createGadgetModel = createGadgetModel ?? throw new ArgumentNullException(nameof(createGadgetModel));
        AccessibleName = "Settings";
        AccessibleRole = AccessibleRole.Pane;

        SuspendLayout();
        _voltage.SetItems(["Auto", "115 V", "230 V"], 0);
        AddCard(_shellyCard, _devicesText, _devicesHint, _manage);
        AddCard(_psuCard, _psuTitle, _psuSubtitle, _psuLink, _change, _voltageLabel, _voltage, _curveNote);
        AddCard(_sensorsCard, _formula, _gadgetTip, _howToEnable, _sensorNames, _edit, _retiredNote);
        AddCard(_displayCard, _intervalLabel, _interval, _intervalValue, _smoothingLabel, _smoothing, _smoothingValue);
        AddCard(_energyCard, _logEnergy, _openEnergy, _priceLabel, _price, _currencyLabel, _currency, _priceError);
        AddCard(_databaseCard, _checkUpdate, _databaseText, _databaseResult);
        AddCard(_startupCard, _startWithWindows, _showTrayIcon);
        AddCard(_maintenanceCard, _openLog, _rerun, _trace, _traceCaption, _remove);
        _applyError.TabIndex = Controls.Count;
        Controls.Add(_applyError);
        ResumeLayout(false);

        WireEvents();
        _presenter.Changed += OnPresenterChanged;
        ApplyState();
    }

    /// <summary>Raised by <see cref="RequestBack"/> ("‹ Home" in the main window's footer): the main window shows the dashboard again.</summary>
    public event EventHandler? BackRequested;

    /// <summary>The footer's "‹ Home": raises <see cref="BackRequested"/> after the click has been handled.</summary>
    public void RequestBack() => Defer(() => BackRequested?.Invoke(this, EventArgs.Empty));

    /// <summary>Raised when "Re-run setup wizard" is clicked.</summary>
    public event EventHandler? WizardRequested;

    /// <summary>Raised after "Remove HWiNFO sensors" (the tray and main window should refresh).</summary>
    public event EventHandler? SensorsRemoved;

    /// <summary>Raised when "Manage Shellys" is clicked (the main window opens <see cref="ManageShellysPage"/>).</summary>
    public event EventHandler? ManageShellysRequested;

    /// <summary>Gets whether a dialog (step dialog or confirmation) is open over the page.</summary>
    public bool IsShowingDialog => _modalDepth > 0;

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _committed = true;
            _presenter.Changed -= OnPresenterChanged;
        }

        base.Dispose(disposing);
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
        var full = contentWidth - (2 * Pad);
        var column = (full - Gap) / 2;
        var left = Pad;
        var right = Pad + column + Gap;

        Rectangle At(float x, float y, float width, float height) =>
            new(this.Px(x) + scroll.X, this.Px(y) + scroll.Y, this.Px(width), this.Px(height));

        // The title row: "Settings" and the footnote (or an apply error) after it, "‹ Home" right-aligned.
        _footnoteLeft = PageHeader.FootnoteLeft(this, "Settings");
        _applyError.Bounds = At(_footnoteLeft, PageHeader.Top + ((PageHeader.Height - Line) / 2), PageHeader.FootnoteWidth(this, _footnoteLeft), Line);

        // Pairs of cards share a row height; Sensors and Maintenance span the page.
        var t = ContentTop();
        var y = PageHeader.ContentTop;
        var row1 = Math.Max(ShellyHeight(t), PsuHeight(t));
        _shellyCard.Bounds = At(left, y, column, row1);
        _psuCard.Bounds = At(right, y, column, row1);
        y += row1 + Gap;
        var sensors = SensorsHeight(t, full);
        _sensorsCard.Bounds = At(left, y, full, sensors);
        y += sensors + Gap;
        var row3 = Math.Max(PollingHeight(t), EnergyHeight(t));
        _displayCard.Bounds = At(left, y, column, row3);
        _energyCard.Bounds = At(right, y, column, row3);
        y += row3 + Gap;
        var row4 = Math.Max(DatabaseHeight(t), StartupHeight(t));
        _databaseCard.Bounds = At(left, y, column, row4);
        _startupCard.Bounds = At(right, y, column, row4);
        y += row4 + Gap;
        var maintenance = t + Line + (_traceCaption.Visible ? TextLine : 0) + BottomPad;
        _maintenanceCard.Bounds = At(left, y, full, maintenance);
        y += maintenance;
        var contentHeight = y + BottomMargin;

        LayoutCards(t, column, full, row4);

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
        PageHeader.Paint(e.Graphics, this, "Settings", _footnoteLeft, !_applyError.Visible, AutoScrollPosition);
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
        _rerun.Click += (_, _) => Defer(() => WizardRequested?.Invoke(this, EventArgs.Empty));
        _manage.Click += (_, _) => Defer(() => ManageShellysRequested?.Invoke(this, EventArgs.Empty));

        _change.Click += (_, _) => ChangePsu();
        _psuLink.Click += (_, _) => OpenPsuReport();
        _voltage.SelectedIndexChanged += (_, _) => Guard(() => _presenter.SetVoltageMode(VoltageModes[Math.Max(0, _voltage.SelectedIndex)]));
        _edit.Click += (_, _) => EditSensors();
        _sensorNames.Click += (_, _) => CustomizeSensorNames();

        _interval.ValueChanged += (_, _) => _intervalValue.Text = SettingsPresenter.FormatDisplayInterval(AppSettings.NormalizeDisplayInterval(_interval.Value));
        _interval.ValueCommitted += (_, _) => Guard(() => _presenter.SetDisplayIntervalMs(_interval.Value));
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
        _trace.Click += (_, _) => _presenter.ToggleDiagnosticTrace();
        _remove.Click += (_, _) => RemoveSensors();
        _howToEnable.Click += (_, _) => ShowModal(() =>
        {
            GadgetHelpDialog.Show(FindForm(), _createGadgetModel());
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
            _psuLink.Text = s.PsuLinkText ?? string.Empty;
            _hasPsuLink = s.PsuLinkText is not null;
            _psuLink.Visible = _hasPsuLink;
            _voltage.SetItems(["Auto", "115 V", "230 V"], Math.Max(0, Array.IndexOf(VoltageModes, s.VoltageMode)));
            _curveNote.Visible = s.CurveNote is not null;
            _curveNote.Text = s.CurveNote ?? string.Empty;

            // Sensors: the calculation and the retired formula note.
            if (_formula.Text != s.FormulaText)
            {
                _formula.SetText(s.FormulaText);
            }

            var lines = s.FormulaText.Split('\n').Length;
            _formulaLines = Math.Clamp(lines, 1, FormulaMaxLines);
            _formula.InnerTextBox.ScrollBars = lines > FormulaMaxLines ? ScrollBars.Vertical : ScrollBars.None;
            _retiredNote.Visible = s.RetiredFormulaNote is not null;
            _retiredNote.Text = s.RetiredFormulaNote ?? string.Empty;

            // Display.
            _interval.Value = s.DisplayIntervalMs;
            _intervalValue.Text = s.DisplayIntervalText;
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

            // Maintenance: the diagnostic trace's text action and its caption.
            _trace.Text = s.DiagnosticTraceText;
            _traceCaption.Visible = s.DiagnosticTraceCaption is not null;
            _traceCaption.Text = s.DiagnosticTraceCaption ?? string.Empty;

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

    /// <summary>The top of a card's content in DIP: padding, the title line and the gap under it.</summary>
    private float ContentTop() => P + TitleHeight() + Theme.TitleGap;

    /// <summary>The height of a card's title line in DIP.</summary>
    private float TitleHeight() => Gfx.Measure("Ag", ThemeFonts.For(DeviceDpi).CardTitle).Height * 96f / DeviceDpi;

    private float FormulaHeight()
    {
        var lineHeight = Gfx.Measure("Ag", ThemeFonts.For(DeviceDpi).Mono).Height * 96f / DeviceDpi;
        return Math.Max(FormulaMinHeight, (_formulaLines * lineHeight) + 14);
    }

    /// <summary>The report link line of the Power supply card (none without a link).</summary>
    private float PsuLinkLine => _hasPsuLink ? TextLine : 0;

    private float TipWidth(float cardWidth) => cardWidth - (2 * P) - FormulaWidth - Gap;

    private float TipHeight(float cardWidth) => _gadgetTip.GetPreferredHeight(this.Px(TipWidth(cardWidth))) * 96f / DeviceDpi;

    private float NoteHeight(float cardWidth) =>
        _retiredNote.Visible ? _retiredNote.GetPreferredHeight(this.Px(cardWidth - (2 * P))) * 96f / DeviceDpi : 0;

    /// <summary>Shelly plugs: the devices summary and the hint, then Manage Shellys.</summary>
    private static float ShellyHeight(float t) => t + SubLine + TextLine + 6 + Line + BottomPad;

    /// <summary>
    /// Power supply: model, details and (database PSU) the report link, the input voltage segments and (single-voltage
    /// PSU) the curve note.
    /// </summary>
    private float PsuHeight(float t) => t + SubLine + TextLine + PsuLinkLine + 8 + Segment + (_curveNote.Visible ? 4 + CurveNoteHeight : 0) + BottomPad;

    /// <summary>
    /// Sensors: the calculation box and, beside it, the gadget explainer with "How to enable?"; the retired formula
    /// note; then the commands row.
    /// </summary>
    private float SensorsHeight(float t, float cardWidth)
    {
        var note = NoteHeight(cardWidth);
        return t + Math.Max(FormulaHeight(), TipHeight(cardWidth) + 4 + Line) + 8 + (note > 0 ? note + 6 : 0) + Line + BottomPad;
    }

    private static float PollingHeight(float t) => t + Line + 8 + Line + BottomPad;

    private float EnergyHeight(float t) => t + Line + 8 + Field + (_priceError.Visible ? 6 + Line : 0) + BottomPad;

    /// <summary>PSU database: the database text, the result of a check, then "Check for update" in the lower-right corner.</summary>
    private float DatabaseHeight(float t) => t + TextLine + (_databaseResult.Visible ? 2 + Line : 0) + 6 + Line + BottomPad;

    private static float StartupHeight(float t) => t + Line + 6 + Line + BottomPad;

    private void LayoutCards(float t, float column, float full, float row4)
    {
        var inner = column - (2 * P);
        var fullInner = full - (2 * P);
        var titleHeight = TitleHeight();
        Rectangle R(float x, float y, float width, float height) => new(this.Px(x), this.Px(y), this.Px(width), this.Px(height));
        float PrefWidth(Control c) => c.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi;
        float PrefHeight(Control c) => c.GetPreferredSize(Size.Empty).Height * 96f / DeviceDpi;

        // A text command at its preferred size: left-aligned at x, or right-aligned when x is negative (from the right
        // edge of a card of width w), centred on a line of the given height.
        void Command(TextLink link, float x, float y, float lineHeight, float w = 0)
        {
            var width = PrefWidth(link);
            var height = PrefHeight(link);
            var left = x < 0 ? w - P - width : x;
            link.Bounds = R(left, y + ((lineHeight - height) / 2), width, height);
        }

        // Shelly plugs: the devices summary and the hint, then Manage Shellys.
        _devicesText.Bounds = R(P, t, inner, SubLine);
        _devicesHint.Bounds = R(P, t + SubLine, inner, TextLine);
        Command(_manage, P, t + SubLine + TextLine + 6, Line);

        // Power supply: Change on the title line; model, details and the report link; the input voltage segments; the
        // curve note.
        Command(_change, -1, P, titleHeight, column);
        _psuTitle.Bounds = R(P, t, inner, SubLine);
        _psuSubtitle.Bounds = R(P, t + SubLine, inner, TextLine);
        Command(_psuLink, P, t + SubLine + TextLine, TextLine);
        var voltageTop = t + SubLine + TextLine + PsuLinkLine + 8;
        _voltageLabel.Bounds = R(P, voltageTop, FieldLabelWidth, Segment);
        _voltage.Bounds = R(P + FieldLabelWidth, voltageTop, Math.Min(inner - FieldLabelWidth, 230), Segment);
        _curveNote.Bounds = R(P, voltageTop + Segment + 4, inner, CurveNoteHeight);

        // Sensors: the read-only calculation and, beside it, the one-line gadget explainer with "How to enable?" under
        // it; the retired formula note; then "Customize HWiNFO sensor names" on the left and "Edit sensors" in the
        // lower-right corner.
        var formulaHeight = FormulaHeight();
        var tipHeight = TipHeight(full);
        _formula.Bounds = R(P, t, FormulaWidth, formulaHeight);
        _gadgetTip.Bounds = R(P + FormulaWidth + Gap, t, TipWidth(full), tipHeight);
        Command(_howToEnable, P + FormulaWidth + Gap, t + tipHeight + 4, Line);
        var y = t + Math.Max(formulaHeight, tipHeight + 4 + Line) + 8;
        var note = NoteHeight(full);
        _retiredNote.Bounds = R(P, y, fullInner, note);
        y += note > 0 ? note + 6 : 0;
        Command(_sensorNames, P, y, Line);
        Command(_edit, -1, y, Line, full);

        // Display: label, slider, value.
        const float labelWidth = 124;
        const float valueWidth = 80;
        var sliderWidth = inner - labelWidth - valueWidth - 8;
        foreach (var (label, slider, value, top) in new[] { (_intervalLabel, _interval, _intervalValue, t), (_smoothingLabel, _smoothing, _smoothingValue, t + Line + 8) })
        {
            label.Bounds = R(P, top, labelWidth, Line);
            slider.Bounds = R(P + labelWidth, top, sliderWidth, Line);
            value.Bounds = R(P + labelWidth + sliderWidth + 8, top, valueWidth, Line);
        }

        // Energy: the switch and "Open data folder", then price and currency (and a price error).
        Command(_openEnergy, -1, t, Line, column);
        _logEnergy.Bounds = R(P, t, Math.Min(inner - PrefWidth(_openEnergy) - 8, PrefWidth(_logEnergy)), Line);
        var priceTop = t + Line + 8;
        const float currencyLabelWidth = 68;
        const float currencyWidth = 70;
        var priceWidth = Math.Max(50, inner - FieldLabelWidth - 8 - currencyLabelWidth - currencyWidth);
        _priceLabel.Bounds = R(P, priceTop, FieldLabelWidth, Field);
        _price.Bounds = R(P + FieldLabelWidth, priceTop, priceWidth, Field);
        var currencyLabelLeft = P + FieldLabelWidth + priceWidth + 8;
        _currencyLabel.Bounds = R(currencyLabelLeft, priceTop, currencyLabelWidth, Field);
        _currency.Bounds = R(currencyLabelLeft + currencyLabelWidth, priceTop, P + inner - currencyLabelLeft - currencyLabelWidth, Field);
        _priceError.Bounds = R(P, priceTop + Field + 6, inner, Line);

        // PSU database: the database text and the check's result; "Check for update" in the lower-right corner.
        _databaseText.Bounds = R(P, t, inner, TextLine);
        _databaseResult.Bounds = R(P, t + TextLine + 2, inner, Line);
        Command(_checkUpdate, -1, row4 - BottomPad - Line, Line, column);

        // Startup and tray: the two switches, one per line.
        _startWithWindows.Bounds = R(P, t, Math.Min(inner, PrefWidth(_startWithWindows)), Line);
        _showTrayIcon.Bounds = R(P, t + Line + 6, Math.Min(inner, PrefWidth(_showTrayIcon)), Line);

        // Maintenance: Open log folder, Re-run setup wizard and the diagnostic trace on the left (its caption on the
        // line under it while recording); Remove HWiNFO sensors right-aligned.
        const float commandGap = 20;
        Command(_openLog, P, t, Line);
        var rerunLeft = P + PrefWidth(_openLog) + commandGap;
        Command(_rerun, rerunLeft, t, Line);
        var traceLeft = rerunLeft + PrefWidth(_rerun) + commandGap;
        Command(_trace, traceLeft, t, Line);
        _traceCaption.Bounds = R(traceLeft, t + Line, Math.Max(0, full - P - traceLeft), TextLine);
        Command(_remove, -1, t, Line, full);
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

    /// <summary>Opens the selected PSU's Cybenetics report (http or https only) in the browser.</summary>
    private void OpenPsuReport()
    {
        if (_presenter.State.PsuReportUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            AboutWindow.OpenWithShell(FindForm(), uri.AbsoluteUri);
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
            "Pick it from the database for the most accurate curve. If it is missing, use a generic or your own curve.",
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
        model.UseSensorNames(current);
        void Load() => model.Load(_readSharedMemory()?.GetPowerSensors(), current.Sensors);
        Load();
        var view = new SensorStepView(model, () =>
        {
            model.ApplyTo(current);
            Load();
        });
        using var dialog = new StepDialog(
            "Choose the component sensors",
            "These HWiNFO power sensors are subtracted from DC power to compute Other Components.",
            view,
            () => model.IsValid ? null
                : model.InvalidNames.Count > 0 ? "A selected sensor name contains a double quote. Rename it in HWiNFO or untick it."
                : "Select at least one sensor or enter a formula.",
            "Sensors");
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
            () => SaveSensorNames(model, dialog),
            "Sensor names");
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
            RemoveSensorsConfirmText,
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
