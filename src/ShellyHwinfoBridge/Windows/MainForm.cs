using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The main window, fixed at <see cref="Theme.MainWindowWidth"/> × <see cref="Theme.MainWindowHeight"/> DIP: header with
/// logo, title and status pill; the live stat cards (four, or five with "Total Power (AC)" first when additional devices
/// exist; the Other card can show a hint and the "How to enable?" text command); the "Energy history" card (Combined /
/// Per device toggle with more than one device, Hours/Days/Months, bar chart with a hover tooltip, today and this month,
/// and the device legend in per-device mode); the configuration card and two text commands (Settings, Pause/Resume
/// polling). When setup is not done, one "Not set up yet" card (with "Run setup wizard") replaces the stats and the
/// energy card. <see cref="OpenSettings"/> shows a <see cref="SettingsPage"/> in place of everything under the header,
/// and the settings page can open <see cref="ManageShellysPage"/> over itself; "‹ Home", "‹ Settings" or Esc go back.
/// Passive: the tray context pushes <see cref="MainViewState"/> and <see cref="EnergyChartModel"/> in, and the window
/// raises events for its commands. Closing hides it.
/// </summary>
internal sealed class MainForm : ThemedForm
{
    // Layout in DIP (compact scale; spacing from Theme).
    private const int Pad = Theme.WindowPadding;
    private const int Gap = Theme.Gap;
    private const int HeaderTop = 12;
    private const int HeaderHeight = 32;
    private const int LogoHeight = 30;
    private const int StatsTop = HeaderTop + HeaderHeight + Gap;
    private const int StatsHeight = 92;
    private const int StatsHeightWithHowTo = 112;
    private const int HowToHeight = 20;
    private const int BottomHeight = 84;
    private const int BottomMargin = 22;
    private const int CommandsWidth = 120;
    private const int ChartTop = 48;
    private const int TotalsHeight = 58;
    private const int ConfigLabelWidth = 64;
    private const int ConfigLineStep = 20;
    private const int SettingsPageTop = HeaderTop + HeaderHeight;

    private static readonly EnergyPeriod[] Periods = [EnergyPeriod.Hours, EnergyPeriod.Days, EnergyPeriod.Months];
    private static readonly EnergyChartMode[] ChartModes = [EnergyChartMode.Combined, EnergyChartMode.PerDevice];

    private readonly StatusPill _pill = new();

    // Total, AC, DC, Loss, Other: the first is shown only with additional devices.
    private readonly StatCard[] _stats = [new(), new(), new(), new(), new()];
    private readonly TextLink _howToEnable = new() { Text = MainWindowPresenter.HowToEnableText, Semibold = true, Visible = false, AccessibleName = "How to enable Other Components" };
    private readonly CardPanel _energy = new() { Title = "Energy history", AccessibleName = "Energy history" };
    private readonly SegmentedControl _chartMode = new() { AccessibleName = "Chart mode", Visible = false };
    private readonly SegmentedControl _period = new() { AccessibleName = "Energy period" };
    private readonly BarChart _chart = new() { AccessibleName = "Energy chart" };
    private readonly CardPanel _config = new() { AccessibleName = "Configuration" };
    private readonly CardPanel _setup = new() { Title = "Not set up yet", AccessibleName = "Not set up yet" };
    private readonly TextLink _runWizard = new() { Text = "Run setup wizard", Semibold = true };
    private readonly TextLink _settings = new() { Text = "Settings", Semibold = true };
    private readonly TextLink _pause = new() { Text = "Pause polling", Semibold = true };
    private IReadOnlyList<ChartLegendItem> _legend = [];
    private MainViewState? _state;
    private SettingsPage? _settingsPage;
    private ManageShellysPage? _managePage;
    private int _modalDepth;

    public MainForm()
    {
        Text = "ShellyHwinfoBridge";
        SuspendLayout();
        Controls.Add(_pill);
        foreach (var card in _stats)
        {
            Controls.Add(card);
        }

        _stats[0].Visible = false;
        _stats[4].Controls.Add(_howToEnable);

        _chartMode.SetItems(["Combined", "Per device"], 0);
        _period.SetItems(["Hours", "Days", "Months"], 0);
        _energy.Controls.Add(_chartMode);
        _energy.Controls.Add(_period);
        _energy.Controls.Add(_chart);
        _energy.Paint += PaintEnergy;
        Controls.Add(_energy);

        _setup.Controls.Add(_runWizard);
        _setup.Paint += PaintSetup;
        _setup.Visible = false;
        Controls.Add(_setup);

        _config.Paint += PaintConfig;
        Controls.Add(_config);
        Controls.Add(_settings);
        Controls.Add(_pause);
        ResumeLayout(false);

        _settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _runWizard.Click += (_, _) => WizardRequested?.Invoke(this, EventArgs.Empty);
        _pause.Click += (_, _) => PauseToggleRequested?.Invoke(this, EventArgs.Empty);
        _period.SelectedIndexChanged += (_, _) => PeriodChanged?.Invoke(this, EventArgs.Empty);
        _chartMode.SelectedIndexChanged += (_, _) => ChartModeChanged?.Invoke(this, EventArgs.Empty);
        _howToEnable.Click += (_, _) => ShowHowToEnable();

        SetFixedClientSizeDip(new Size(Theme.MainWindowWidth, Theme.MainWindowHeight));
    }

    /// <summary>Raised when "Settings" is clicked.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when "Run setup wizard" (on the "Not set up yet" card) is clicked.</summary>
    public event EventHandler? WizardRequested;

    /// <summary>Raised when "Pause polling" / "Resume polling" is clicked.</summary>
    public event EventHandler? PauseToggleRequested;

    /// <summary>Raised when the Hours/Days/Months selection changes; the owner pushes a new chart.</summary>
    public event EventHandler? PeriodChanged;

    /// <summary>Raised when the Combined / Per device selection changes; the owner stores <see cref="SelectedChartMode"/>.</summary>
    public event EventHandler? ChartModeChanged;

    /// <summary>Raised after the settings page was closed ("‹ Home", Esc, hiding the window, or <see cref="CloseSettings"/>).</summary>
    public event EventHandler? SettingsClosed;

    /// <summary>Gets the open settings page, or <c>null</c> while the dashboard is shown.</summary>
    public SettingsPage? SettingsPage => _settingsPage;

    /// <summary>Gets the open Manage Shellys page, or <c>null</c>.</summary>
    public ManageShellysPage? ManageShellysPage => _managePage;

    /// <summary>Gets whether a modal dialog is open over the window (settings dialogs, Manage Shellys dialogs, the gadget help).</summary>
    public bool IsShowingDialog => _modalDepth > 0 || _settingsPage?.IsShowingDialog == true || _managePage?.IsShowingDialog == true;

    /// <summary>
    /// Shows <paramref name="page"/> in place of the dashboard (the header stays). The window owns the page from now on
    /// and disposes it when it is closed.
    /// </summary>
    public void OpenSettings(SettingsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (ReferenceEquals(page, _settingsPage))
        {
            return;
        }

        CloseSettings(raise: false);
        _settingsPage = page;
        page.BackRequested += OnSettingsBack;
        page.ManageShellysRequested += OnManageShellysRequested;
        SuspendLayout();
        Controls.Add(page);
        UpdateVisibility();
        ResumeLayout(true);
        Text = "Settings · ShellyHwinfoBridge";
        if (Visible)
        {
            page.FocusFirstField();
        }
    }

    /// <summary>Closes the settings page (applying a field still being edited) and shows the dashboard again.</summary>
    public void CloseSettings() => CloseSettings(raise: true);

    /// <summary>Opens the Manage Shellys page over the settings page (which stays open underneath).</summary>
    public void OpenManageShellys(ManageShellysPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_settingsPage is null)
        {
            page.Dispose();
            return;
        }

        CloseManageShellys();
        _settingsPage.CommitPendingEdits();
        _managePage = page;
        page.BackRequested += OnManageBack;
        SuspendLayout();
        Controls.Add(page);
        UpdateVisibility();
        ResumeLayout(true);
        Text = "Manage Shellys · ShellyHwinfoBridge";
        if (Visible)
        {
            page.FocusFirstField();
        }
    }

    /// <summary>Closes the Manage Shellys page and shows the settings page again.</summary>
    public void CloseManageShellys()
    {
        if (_managePage is not { } page)
        {
            return;
        }

        _managePage = null;
        page.BackRequested -= OnManageBack;
        SuspendLayout();
        Controls.Remove(page);
        UpdateVisibility();
        ResumeLayout(true);
        page.Dispose();
        if (_settingsPage is { } settings)
        {
            Text = "Settings · ShellyHwinfoBridge";
            settings.RefreshState();
            settings.FocusManageShellys();
        }

        Invalidate();
    }

    /// <summary>Shows the settings page (closing Manage Shellys) and scrolls it to the About section.</summary>
    public void ScrollSettingsToAbout()
    {
        CloseManageShellys();
        _settingsPage?.ScrollToAbout();
    }

    /// <summary>Gets the selected chart period.</summary>
    public EnergyPeriod SelectedPeriod => Periods[Math.Clamp(_period.SelectedIndex, 0, Periods.Length - 1)];

    /// <summary>Gets the selected chart mode (Combined / Per device).</summary>
    public EnergyChartMode SelectedChartMode => ChartModes[Math.Clamp(_chartMode.SelectedIndex, 0, ChartModes.Length - 1)];

    /// <summary>Highlights a chart bar as if hovered (screen renderer).</summary>
    public void PreviewHover(int barIndex) => _chart.SetHoveredIndex(barIndex);

    /// <summary>Highlights a bar of a per-device group as if hovered (screen renderer).</summary>
    public void PreviewHover(int group, int bar) => _chart.SetHoveredBar(group, bar);

    /// <summary>Selects a chart period without raising <see cref="PeriodChanged"/>.</summary>
    public void SelectPeriod(EnergyPeriod period) => _period.SetItems(["Hours", "Days", "Months"], Array.IndexOf(Periods, period));

    /// <summary>Enables or disables the Settings and Run setup wizard commands (e.g. while the wizard is open).</summary>
    public void SetWindowButtonsEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _runWizard.Enabled = enabled;
    }

    /// <summary>Shows a new state; repaints only what changed.</summary>
    public void Apply(MainViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = _state;
        _state = state;
        _pill.Apply(state.Status.Dot.ToUi(), state.Status.Text);

        // The presenter's display order: Total first when present, then AC, DC, Loss, Other.
        var cards = state.Cards;
        var targets = state.Total is null ? _stats[1..] : _stats;
        for (var i = 0; i < cards.Count && i < targets.Length; i++)
        {
            ApplyCard(targets[i], cards[i]);
        }

        UpdateCompactCards();

        var showHowTo = state.Other.OtherState?.ShowHowToEnable == true;
        _howToEnable.Visible = showHowTo;
        _stats[4].ReservedBottomDip = showHowTo ? HowToHeight + 6 : 0;

        var modeIndex = Math.Max(0, Array.IndexOf(ChartModes, state.ChartMode));
        if (_chartMode.SelectedIndex != modeIndex)
        {
            _chartMode.SetItems(["Combined", "Per device"], modeIndex);
        }

        _pause.Text = state.PauseButtonText;
        _pause.Enabled = state.PauseEnabled;

        if (previous is null || previous.IsConfigured != state.IsConfigured ||
            (previous.Total is null) != (state.Total is null) || previous.ShowChartModeToggle != state.ShowChartModeToggle ||
            (previous.Other.OtherState?.ShowHowToEnable == true) != showHowTo)
        {
            UpdateVisibility();
            PerformLayout();
        }
        else if (previous.Status.Text != state.Status.Text)
        {
            // The status text (with the live voltage) changes on nearly every poll: re-place only the pill. A layout pass
            // of the whole window would also make WinForms scroll the open settings page back to its focused control.
            LayoutHeader();
            Invalidate(new Rectangle(0, 0, ClientSize.Width, this.Px(HeaderTop + HeaderHeight)));
        }

        if (previous is null || previous.Energy != state.Energy)
        {
            _energy.Invalidate(TotalsBounds());
        }

        if (previous is null || previous.PsuLine != state.PsuLine || previous.SensorsLine != state.SensorsLine || previous.HwinfoLine != state.HwinfoLine)
        {
            _config.Invalidate();
        }

        // Manage Shellys shows each device's online status: let it re-read the runtime.
        _managePage?.RefreshState();
    }

    /// <summary>Shows a new chart (grouped bars and the legend in per-device mode).</summary>
    public void ApplyChart(EnergyChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var groups = chart.Groups
            .Select(g => new ChartGroup(g.Label, g.Bars.Select(b => new ChartBar(b.Value, b.Label, b.TooltipLines, ColourSwatch.Parse(b.Colour, Theme.Accent))).ToList()))
            .ToList();
        _chart.SetData(chart.Bars.Select(b => new ChartBar(b.Value, b.Label, b.TooltipLines)).ToList(), chart.YMax, chart.YLabels, groups);
        if (!_legend.SequenceEqual(chart.Legend))
        {
            _legend = chart.Legend;
            _energy.AccessibleDescription = _legend.Count == 0 ? null : "Legend: " + string.Join(", ", _legend.Select(l => l.Label));
            _energy.Invalidate(TotalsBounds());
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Esc leaves Manage Shellys, then the settings page; on the dashboard it hides the window like the close button.
        if (keyData == Keys.Escape)
        {
            if (_managePage is not null)
            {
                BeginInvoke(new Action(CloseManageShellys));
            }
            else if (_settingsPage is not null)
            {
                // Closing disposes the focused field: do it after this key has been handled.
                BeginInvoke(new Action(CloseSettings));
            }
            else
            {
                Hide();
            }

            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing hides the window; the app keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        // A hidden window shows the dashboard when it comes back.
        if (!Visible && _settingsPage is not null && !IsShowingDialog)
        {
            CloseSettings();
        }

        base.OnVisibleChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_managePage is { } manage)
            {
                _managePage = null;
                manage.BackRequested -= OnManageBack;
                manage.Dispose();
            }

            if (_settingsPage is { } page)
            {
                _settingsPage = null;
                page.BackRequested -= OnSettingsBack;
                page.ManageShellysRequested -= OnManageShellysRequested;
                page.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var pad = this.Px(Pad);
        var gap = this.Px(Gap);
        var right = w - pad;
        LayoutHeader();

        // Stats row: four (or five, with Total first) equal cards; taller while the Other card shows "How to enable?".
        var statsTop = this.Px(StatsTop);
        var statsHeight = this.Px(_howToEnable.Visible ? StatsHeightWithHowTo : StatsHeight);
        var shown = _stats.Where((_, i) => i > 0 || _state?.Total is not null).ToList();
        var cardWidth = (w - (2 * pad) - ((shown.Count - 1) * gap)) / shown.Count;
        for (var i = 0; i < shown.Count; i++)
        {
            var x = pad + (i * (cardWidth + gap));
            var width = i == shown.Count - 1 ? right - x : cardWidth;
            shown[i].Bounds = new Rectangle(x, statsTop, width, statsHeight);
        }

        UpdateCompactCards();
        var other = _stats[4];
        var howToLeft = other.TextLeft;
        var howToSize = _howToEnable.GetPreferredSize(Size.Empty);
        var howToWidth = Math.Min(other.Width - howToLeft - this.Px(6), howToSize.Width);
        _howToEnable.Bounds = new Rectangle(howToLeft, other.Height - this.Px(8) - this.Px(HowToHeight), Math.Max(0, howToWidth), this.Px(HowToHeight));

        // Bottom row: the configuration card and, right of it, the Settings and Pause polling commands (one per config line).
        var bottomTop = h - this.Px(BottomMargin) - this.Px(BottomHeight);
        var commandsLeft = right - this.Px(CommandsWidth);
        _config.Bounds = new Rectangle(pad, bottomTop, commandsLeft - gap - pad, this.Px(BottomHeight));
        var firstLine = bottomTop + ConfigFirstLineTop();
        foreach (var (command, line) in new[] { (_settings, 0), (_pause, 1) })
        {
            var size = command.GetPreferredSize(Size.Empty);
            var lineTop = firstLine + this.Px(line * ConfigLineStep);
            command.Bounds = new Rectangle(commandsLeft, lineTop + ((this.Px(ConfigLineStep) - size.Height) / 2), Math.Min(size.Width, right - commandsLeft), size.Height);
        }

        // Energy card fills the space between the stats and the bottom row. The period selector sits at the top right,
        // the Combined / Per device toggle to its left, both centred on the title line.
        var energyTop = statsTop + statsHeight + gap;
        var energy = new Rectangle(pad, energyTop, right - pad, Math.Max(this.Px(180), bottomTop - gap - energyTop));
        _energy.Bounds = energy;
        var cardPad = this.Px(Theme.CardPadding);
        var segmentHeight = this.Px(Theme.ButtonHeight);
        var segmentTop = cardPad + ((_energy.TitleHeight - segmentHeight) / 2);
        var periodWidth = _period.GetPreferredSize(Size.Empty).Width;
        _period.Bounds = new Rectangle(energy.Width - cardPad - periodWidth, segmentTop, periodWidth, segmentHeight);
        var modeWidth = _chartMode.GetPreferredSize(Size.Empty).Width;
        _chartMode.Bounds = new Rectangle(_period.Left - this.Px(8) - modeWidth, segmentTop, modeWidth, segmentHeight);
        var chartTop = this.Px(ChartTop);
        _chart.Bounds = new Rectangle(cardPad, chartTop, energy.Width - (2 * cardPad), Math.Max(this.Px(60), energy.Height - chartTop - this.Px(TotalsHeight)));

        // Settings page (and Manage Shellys over it): everything under the header.
        var pageTop = this.Px(SettingsPageTop);
        var pageBounds = new Rectangle(0, pageTop, w, Math.Max(0, h - pageTop));
        if (_settingsPage is not null)
        {
            _settingsPage.Bounds = pageBounds;
        }

        if (_managePage is not null)
        {
            _managePage.Bounds = pageBounds;
        }

        // Unconfigured: one card over the stats and energy area.
        _setup.Bounds = new Rectangle(pad, statsTop, right - pad, energy.Bottom - statsTop);
        var runSize = _runWizard.GetPreferredSize(Size.Empty);
        _runWizard.Bounds = new Rectangle(cardPad, cardPad + _setup.TitleHeight + this.Px(Theme.TitleGap) + SetupTextHeight() + this.Px(Theme.RowGap), runSize.Width, runSize.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var logoHeight = this.Px(LogoHeight);
        var top = this.Px(HeaderTop) + ((this.Px(HeaderHeight) - logoHeight) / 2);
        var logoLeft = this.Px(Pad + 4);
        var logoWidth = AppAssets.DrawLogo(g, new Point(logoLeft, top), logoHeight);
        var titleLeft = logoLeft + logoWidth + this.Px(10);
        Gfx.Text(g, "ShellyHwinfoBridge", Fonts.AppTitle, new Rectangle(titleLeft, this.Px(HeaderTop), Math.Max(0, _pill.Left - titleLeft - this.Px(10)), this.Px(HeaderHeight)), Theme.Ink);

        // The app version, very small, in the bottom margin under the command column (clear of the cards and commands).
        var tiny = Fonts.Get(FontRole.Tiny);
        var versionHeight = Gfx.Measure("Ag", tiny).Height;
        var version = new Rectangle(0, ClientSize.Height - this.Px(BottomMargin / 2) - (versionHeight / 2), ClientSize.Width - this.Px(Pad), versionHeight);
        Gfx.Text(g, SettingsPage.VersionText, tiny, version, Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.Right);
    }

    /// <summary>Places the status pill: right-aligned, sized to its text (at least 180 DIP), centred on the header line.</summary>
    private void LayoutHeader()
    {
        var right = ClientSize.Width - this.Px(Pad);
        var pillWidth = Math.Max(this.Px(180), _pill.GetPreferredSize(Size.Empty).Width);
        var pillHeight = this.Px(28);
        _pill.Bounds = new Rectangle(right - pillWidth, this.Px(HeaderTop) + ((this.Px(HeaderHeight) - pillHeight) / 2), pillWidth, pillHeight);
    }

    /// <summary>The top of the first configuration line inside the card (the three lines are centred vertically).</summary>
    private int ConfigFirstLineTop() => (this.Px(BottomHeight) - this.Px(3 * ConfigLineStep)) / 2;

    private const string SetupText = "Run the setup wizard to connect your Shelly plug, choose your power supply and pick the HWiNFO sensors. Until then nothing is polled or written to HWiNFO.";

    private int SetupTextWidth() => Math.Max(0, Math.Min(_setup.Width - (2 * this.Px(Theme.CardPadding)), this.Px(560)));

    private int SetupTextHeight() => Gfx.MeasureWrapped(SetupText, Fonts.Body, SetupTextWidth()).Height;

    private void CloseSettings(bool raise)
    {
        if (_settingsPage is not { } page)
        {
            return;
        }

        CloseManageShellys();
        page.CommitPendingEdits();
        _settingsPage = null;
        page.BackRequested -= OnSettingsBack;
        page.ManageShellysRequested -= OnManageShellysRequested;
        SuspendLayout();
        Controls.Remove(page);
        UpdateVisibility();
        ResumeLayout(true);
        page.Dispose();
        Text = "ShellyHwinfoBridge";
        Invalidate();
        if (raise)
        {
            SettingsClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSettingsBack(object? sender, EventArgs e) => CloseSettings();

    private void OnManageBack(object? sender, EventArgs e) => CloseManageShellys();

    private void OnManageShellysRequested(object? sender, EventArgs e)
    {
        if (_settingsPage is { } page)
        {
            OpenManageShellys(new ManageShellysPage(page.CreateManageShellysModel()));
        }
    }

    private void ShowHowToEnable()
    {
        if (_state?.Other.OtherState is not { } other)
        {
            return;
        }

        _modalDepth++;
        try
        {
            GadgetHelpDialog.Show(this, other.HowToText);
        }
        finally
        {
            _modalDepth--;
        }
    }

    /// <summary>
    /// Shows Manage Shellys, the settings page or the dashboard (stats and energy when configured, else the setup card).
    /// </summary>
    private void UpdateVisibility()
    {
        var dashboard = _settingsPage is null;
        var configured = _state?.IsConfigured ?? true;
        for (var i = 0; i < _stats.Length; i++)
        {
            _stats[i].Visible = dashboard && configured && (i > 0 || _state?.Total is not null);
        }

        _energy.Visible = dashboard && configured;
        _chartMode.Visible = _state?.ShowChartModeToggle == true;
        _setup.Visible = dashboard && !configured;
        _config.Visible = dashboard;
        _settings.Visible = dashboard;
        _pause.Visible = dashboard;
        if (_settingsPage is not null)
        {
            _settingsPage.Visible = _managePage is null;
        }
    }

    /// <summary>When any shown card's value does not fit its width, every card draws compactly, so the row stays even.</summary>
    private void UpdateCompactCards()
    {
        var shown = _stats.Where((c, i) => (i > 0 || _state?.Total is not null) && c.Width > 0).ToList();
        var compact = shown.Any(c => !c.FitsFullSize);
        foreach (var card in shown)
        {
            card.Compact = compact;
        }
    }

    private static void ApplyCard(StatCard card, StatCardState state) =>
        card.Apply(state.Caption, state.Value, state.Sub, state.ValueTone.ToUi(), state.Placeholder);

    private Rectangle TotalsBounds() =>
        new(0, _energy.Height - this.Px(TotalsHeight), _energy.Width, this.Px(TotalsHeight));

    private void PaintEnergy(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var f = Fonts;
        if (_state is not { } state)
        {
            return;
        }

        // Two totals: value (TotalValue) over a tracked caption; a 1 px divider between them.
        var tracking = this.PxF(Theme.CaptionTracking);
        var valueHeight = Gfx.Measure("0", f.TotalValue).Height;
        var y = _energy.Height - this.Px(TotalsHeight) + this.Px(6);
        var captionY = y + valueHeight + this.Px(1);
        var captionHeight = Gfx.MeasureTracked("A", f.Caption, 0).Height;
        var x = this.Px(Theme.CardPadding + 2);
        var todayWidth = Math.Max(Gfx.Measure(state.Energy.TodayValue, f.TotalValue).Width, Gfx.MeasureTracked(state.Energy.TodayCaption, f.Caption, tracking).Width);
        Gfx.Text(g, state.Energy.TodayValue, f.TotalValue, new Rectangle(x, y, todayWidth + this.Px(8), valueHeight), Theme.Ink);
        Gfx.DrawTracked(g, state.Energy.TodayCaption, f.Caption, new Point(x, captionY), Theme.InkSecondary, tracking);

        var divider = Math.Max(x + this.Px(110), x + todayWidth + this.Px(20));
        using (var pen = new Pen(Theme.Border, Math.Max(1, this.Px(1))))
        {
            g.DrawLine(pen, divider, y + this.Px(2), divider, captionY + captionHeight);
        }

        x = divider + this.Px(24);
        var monthWidth = Math.Max(Gfx.Measure(state.Energy.MonthValue, f.TotalValue).Width, Gfx.MeasureTracked(state.Energy.MonthCaption, f.Caption, tracking).Width);
        Gfx.Text(g, state.Energy.MonthValue, f.TotalValue, new Rectangle(x, y, monthWidth + this.Px(8), valueHeight), Theme.Ink);
        Gfx.DrawTracked(g, state.Energy.MonthCaption, f.Caption, new Point(x, captionY), Theme.InkSecondary, tracking);

        PaintLegend(g, x + monthWidth + this.Px(24), y, captionY + captionHeight);
    }

    /// <summary>The per-device legend (swatch and label per device), right-aligned in the totals row.</summary>
    private void PaintLegend(Graphics g, int minLeft, int top, int bottom)
    {
        if (_legend.Count == 0)
        {
            return;
        }

        var f = Fonts;
        var swatch = this.Px(10);
        var swatchGap = this.Px(6);
        var itemGap = this.Px(14);
        var widths = _legend.Select(l => swatch + swatchGap + Gfx.Measure(l.Label, f.Body).Width).ToList();
        var total = widths.Sum() + (itemGap * (widths.Count - 1));
        var x = Math.Max(minLeft, _energy.Width - this.Px(Theme.CardPadding) - total);
        var lineHeight = Gfx.Measure("Ag", f.Body).Height;
        var y = top + ((bottom - top - lineHeight) / 2);
        for (var i = 0; i < _legend.Count; i++)
        {
            var item = _legend[i];
            var colour = ColourSwatch.Parse(item.Colour, Theme.Accent);
            Gfx.FillRounded(g, colour, new RectangleF(x, y + ((lineHeight - swatch) / 2f), swatch, swatch), this.PxF(2));
            var textLeft = x + swatch + swatchGap;
            Gfx.Text(g, item.Label, f.Body, new Rectangle(textLeft, y, Math.Max(0, _energy.Width - textLeft), lineHeight), Theme.InkSecondary);
            x += widths[i] + itemGap;
        }
    }

    private void PaintConfig(object? sender, PaintEventArgs e)
    {
        if (_state is not { } state)
        {
            return;
        }

        var g = e.Graphics;
        var f = Fonts;
        var labelLeft = this.Px(Theme.CardPadding);
        var valueLeft = labelLeft + this.Px(ConfigLabelWidth);
        var valueWidth = Math.Max(0, _config.Width - valueLeft - this.Px(Theme.CardPadding));
        var lineHeight = this.Px(ConfigLineStep);
        var lines = new[] { ("PSU", state.PsuLine), ("Sensors", state.SensorsLine), ("HWiNFO", state.HwinfoLine) };
        var step = lineHeight;
        var y = ConfigFirstLineTop();
        foreach (var (label, value) in lines)
        {
            Gfx.Text(g, label, f.Body, new Rectangle(labelLeft, y, valueLeft - labelLeft, lineHeight), Theme.InkSecondary);
            Gfx.Text(g, value, f.Body, new Rectangle(valueLeft, y, valueWidth, lineHeight), Theme.Ink);
            y += step;
        }
    }

    private void PaintSetup(object? sender, PaintEventArgs e)
    {
        var p = this.Px(Theme.CardPadding);
        var top = p + _setup.TitleHeight + this.Px(Theme.TitleGap);
        TextRenderer.DrawText(
            e.Graphics,
            SetupText,
            Fonts.Body,
            new Rectangle(p, top, SetupTextWidth(), SetupTextHeight() + this.Px(2)),
            Theme.InkSecondary,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
