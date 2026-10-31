using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The main window (design/final/main-window.png): header with logo, title and status pill; four live stat cards; the
/// energy card (Hours/Days/Months, bar chart, today and this month); the configuration card and two buttons (Settings,
/// Pause/Resume polling). When setup is not done, one "Not set up yet" card replaces the stats and the energy card.
/// <see cref="OpenSettings"/> shows a <see cref="SettingsPage"/> in place of everything under the header; "Done" or Esc
/// returns to the dashboard. Passive: the tray context pushes <see cref="MainViewState"/> and
/// <see cref="EnergyChartModel"/> in, and the window raises events for its buttons. Closing hides it.
/// </summary>
internal sealed class MainForm : ThemedForm
{
    // Layout in DIP, measured from the 1536×1024 design image (client 1100×740).
    private const int Pad = 24;
    private const int Gap = 16;
    private const int HeaderTop = 24;
    private const int HeaderHeight = 44;
    private const int LogoHeight = 46;
    private const int StatsTop = 92;
    private const int StatsHeight = 128;
    private const int BottomHeight = 150;
    private const int BottomMargin = 38;
    private const int ButtonWidth = 276;
    private const int ButtonHeight = 40;
    private const int ChartTop = 72;
    private const int TotalsHeight = 84;
    private const int ConfigLabelWidth = 102;
    private const int SettingsPageTop = HeaderTop + HeaderHeight;

    private static readonly EnergyPeriod[] Periods = [EnergyPeriod.Hours, EnergyPeriod.Days, EnergyPeriod.Months];

    private readonly StatusPill _pill = new();
    private readonly StatCard[] _stats = [new(), new(), new(), new()];
    private readonly CardPanel _energy = new() { Title = "Energy", AccessibleName = "Energy" };
    private readonly SegmentedControl _period = new() { AccessibleName = "Energy period" };
    private readonly BarChart _chart = new() { AccessibleName = "Energy chart" };
    private readonly CardPanel _config = new() { AccessibleName = "Configuration" };
    private readonly CardPanel _setup = new() { Title = "Not set up yet", AccessibleName = "Not set up yet" };
    private readonly PillButton _runWizard = new() { Text = "Run setup wizard", Variant = PillVariant.Primary };
    private readonly PillButton _settings = new() { Text = "Settings" };
    private readonly PillButton _pause = new() { Text = "Pause polling" };
    private MainViewState? _state;
    private SettingsPage? _settingsPage;

    public MainForm()
    {
        Text = "ShellyHwinfoBridge";
        SuspendLayout();
        Controls.Add(_pill);
        foreach (var card in _stats)
        {
            Controls.Add(card);
        }

        _period.SetItems(["Hours", "Days", "Months"], 0);
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
        _chart.HoveredIndexChanged += (_, _) => _energy.Invalidate(ReadoutBounds());

        SetClientSizeDip(new Size(1100, 740), new Size(980, 680));
    }

    /// <summary>Raised when "Settings" is clicked.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when "Run setup wizard" (on the "Not set up yet" card) is clicked.</summary>
    public event EventHandler? WizardRequested;

    /// <summary>Raised when "Pause polling" / "Resume polling" is clicked.</summary>
    public event EventHandler? PauseToggleRequested;

    /// <summary>Raised when the Hours/Days/Months selection changes; the owner pushes a new chart.</summary>
    public event EventHandler? PeriodChanged;

    /// <summary>Raised after the settings page was closed ("Done", Esc, hiding the window, or <see cref="CloseSettings"/>).</summary>
    public event EventHandler? SettingsClosed;

    /// <summary>Gets the open settings page, or <c>null</c> while the dashboard is shown.</summary>
    public SettingsPage? SettingsPage => _settingsPage;

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
        page.DoneRequested += OnSettingsDone;
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

    /// <summary>Gets the selected chart period.</summary>
    public EnergyPeriod SelectedPeriod => Periods[Math.Clamp(_period.SelectedIndex, 0, Periods.Length - 1)];

    /// <summary>Highlights a chart bar as if hovered (screen renderer).</summary>
    public void PreviewHover(int barIndex) => _chart.SetHoveredIndex(barIndex);

    /// <summary>Selects a chart period without raising <see cref="PeriodChanged"/>.</summary>
    public void SelectPeriod(EnergyPeriod period) => _period.SetItems(["Hours", "Days", "Months"], Array.IndexOf(Periods, period));

    /// <summary>Enables or disables the Settings and Run setup wizard buttons (e.g. while the wizard is open).</summary>
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
        ApplyCard(_stats[0], state.Ac);
        ApplyCard(_stats[1], state.Dc);
        ApplyCard(_stats[2], state.Loss);
        ApplyCard(_stats[3], state.Other);
        _pause.Text = state.PauseButtonText;
        _pause.Enabled = state.PauseEnabled;

        if (previous is null || previous.IsConfigured != state.IsConfigured || previous.Status.Text != state.Status.Text)
        {
            UpdateVisibility();
            PerformLayout();
        }

        if (previous is null || previous.Energy != state.Energy)
        {
            _energy.Invalidate(TotalsBounds());
        }

        if (previous is null || previous.PsuLine != state.PsuLine || previous.SensorsLine != state.SensorsLine || previous.HwinfoLine != state.HwinfoLine)
        {
            _config.Invalidate();
        }
    }

    /// <summary>Shows a new chart.</summary>
    public void ApplyChart(EnergyChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        _chart.SetData(chart.Bars.Select(b => new ChartBar(b.Value, b.Label, b.Tooltip)).ToList(), chart.YMax, chart.YLabels);
        _energy.Invalidate(ReadoutBounds());
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Esc leaves the settings page; on the dashboard it hides the window like the close button.
        if (keyData == Keys.Escape)
        {
            if (_settingsPage is not null)
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
        if (!Visible && _settingsPage is not null && !_settingsPage.IsShowingDialog)
        {
            CloseSettings();
        }

        base.OnVisibleChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _settingsPage is { } page)
        {
            _settingsPage = null;
            page.DoneRequested -= OnSettingsDone;
            page.Dispose();
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

        // Header: the pill is right-aligned and sized to its text (at least 270 DIP).
        var pillSize = _pill.GetPreferredSize(Size.Empty);
        var pillWidth = Math.Max(this.Px(270), pillSize.Width);
        _pill.Bounds = new Rectangle(right - pillWidth, this.Px(HeaderTop + 2), pillWidth, this.Px(42));

        // Stats row: four equal cards.
        var statsTop = this.Px(StatsTop);
        var statsHeight = this.Px(StatsHeight);
        var cardWidth = (w - (2 * pad) - (3 * gap)) / 4;
        for (var i = 0; i < _stats.Length; i++)
        {
            var x = pad + (i * (cardWidth + gap));
            var width = i == _stats.Length - 1 ? right - x : cardWidth;
            _stats[i].Bounds = new Rectangle(x, statsTop, width, statsHeight);
        }

        // Bottom row: configuration card and the button stack.
        var bottomTop = h - this.Px(BottomMargin) - this.Px(BottomHeight);
        var buttonWidth = this.Px(ButtonWidth);
        var buttonLeft = right - buttonWidth;
        _config.Bounds = new Rectangle(pad, bottomTop, buttonLeft - this.Px(20) - pad, this.Px(BottomHeight));
        var buttonHeight = this.Px(ButtonHeight);
        var buttonStep = (this.Px(BottomHeight) - buttonHeight) / 2;
        _settings.Bounds = new Rectangle(buttonLeft, bottomTop, buttonWidth, buttonHeight);
        _pause.Bounds = new Rectangle(buttonLeft, bottomTop + buttonStep, buttonWidth, buttonHeight);

        // Energy card fills the space between the stats and the bottom row.
        var energyTop = statsTop + statsHeight + gap;
        var energy = new Rectangle(pad, energyTop, right - pad, Math.Max(this.Px(220), bottomTop - gap - energyTop));
        _energy.Bounds = energy;
        var cardPad = this.Px(Theme.CardPadding);
        _period.Bounds = new Rectangle(energy.Width - cardPad - this.Px(290), this.Px(14), this.Px(290), this.Px(40));
        var chartTop = this.Px(ChartTop);
        _chart.Bounds = new Rectangle(cardPad, chartTop, energy.Width - (2 * cardPad), Math.Max(this.Px(80), energy.Height - chartTop - this.Px(TotalsHeight)));

        // Settings page: everything under the header.
        if (_settingsPage is not null)
        {
            var pageTop = this.Px(SettingsPageTop);
            _settingsPage.Bounds = new Rectangle(0, pageTop, w, Math.Max(0, h - pageTop));
        }

        // Unconfigured: one card over the stats and energy area.
        _setup.Bounds = new Rectangle(pad, statsTop, right - pad, energy.Bottom - statsTop);
        var runSize = _runWizard.GetPreferredSize(Size.Empty);
        _runWizard.Bounds = new Rectangle(cardPad, cardPad + _setup.TitleHeight + this.Px(12 + 64), Math.Max(this.Px(200), runSize.Width), this.Px(42));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var logoHeight = this.Px(LogoHeight);
        var top = this.Px(HeaderTop) + ((this.Px(HeaderHeight) - logoHeight) / 2);
        var logoWidth = AppAssets.DrawLogo(g, new Point(this.Px(34), top), logoHeight);
        var titleLeft = this.Px(34) + logoWidth + this.Px(18);
        Gfx.Text(g, "ShellyHwinfoBridge", Fonts.AppTitle, new Rectangle(titleLeft, this.Px(HeaderTop), Math.Max(0, _pill.Left - titleLeft - this.Px(16)), this.Px(HeaderHeight)), Theme.Ink);
    }

    private void CloseSettings(bool raise)
    {
        if (_settingsPage is not { } page)
        {
            return;
        }

        page.CommitPendingEdits();
        _settingsPage = null;
        page.DoneRequested -= OnSettingsDone;
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

    private void OnSettingsDone(object? sender, EventArgs e) => CloseSettings();

    /// <summary>Shows either the settings page or the dashboard (stats and energy when configured, else the setup card).</summary>
    private void UpdateVisibility()
    {
        var dashboard = _settingsPage is null;
        var configured = _state?.IsConfigured ?? true;
        foreach (var card in _stats)
        {
            card.Visible = dashboard && configured;
        }

        _energy.Visible = dashboard && configured;
        _setup.Visible = dashboard && !configured;
        _config.Visible = dashboard;
        _settings.Visible = dashboard;
        _pause.Visible = dashboard;
    }

    private static void ApplyCard(StatCard card, StatCardState state) =>
        card.Apply(state.Caption, state.Value, state.Sub, state.ValueTone.ToUi(), state.Placeholder);

    private Rectangle TotalsBounds() =>
        new(0, _energy.Height - this.Px(TotalsHeight), _energy.Width, this.Px(TotalsHeight));

    private Rectangle ReadoutBounds() =>
        new(this.Px(140), 0, Math.Max(0, _period.Left - this.Px(140)), this.Px(ChartTop));

    private void PaintEnergy(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var f = Fonts;

        // Hover readout between the title and the period selector.
        if (_chart.HoveredTooltip is { } readout)
        {
            var area = ReadoutBounds();
            area.Width -= this.Px(20);
            area.Y = this.Px(14);
            area.Height = this.Px(40);
            Gfx.Text(g, readout, f.Body, area, Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        if (_state is not { } state)
        {
            return;
        }

        // Two totals: value (TotalValue) over a tracked caption; a 1 px divider between them.
        var tracking = this.PxF(Theme.CaptionTracking);
        var valueHeight = Gfx.Measure("0", f.TotalValue).Height;
        var y = _energy.Height - this.Px(TotalsHeight) + this.Px(14);
        var captionY = y + valueHeight + this.Px(2);
        var x = this.Px(24);
        var todayWidth = Math.Max(Gfx.Measure(state.Energy.TodayValue, f.TotalValue).Width, Gfx.MeasureTracked(state.Energy.TodayCaption, f.Caption, tracking).Width);
        Gfx.Text(g, state.Energy.TodayValue, f.TotalValue, new Rectangle(x, y, todayWidth + this.Px(8), valueHeight), Theme.Ink);
        Gfx.DrawTracked(g, state.Energy.TodayCaption, f.Caption, new Point(x, captionY), Theme.InkSecondary, tracking);

        var divider = Math.Max(x + this.Px(177), x + todayWidth + this.Px(32));
        using (var pen = new Pen(Theme.Border, Math.Max(1, this.Px(1))))
        {
            g.DrawLine(pen, divider, y + this.Px(2), divider, captionY + this.Px(18));
        }

        x = divider + this.Px(44);
        var monthWidth = Math.Max(Gfx.Measure(state.Energy.MonthValue, f.TotalValue).Width, Gfx.MeasureTracked(state.Energy.MonthCaption, f.Caption, tracking).Width);
        Gfx.Text(g, state.Energy.MonthValue, f.TotalValue, new Rectangle(x, y, monthWidth + this.Px(8), valueHeight), Theme.Ink);
        Gfx.DrawTracked(g, state.Energy.MonthCaption, f.Caption, new Point(x, captionY), Theme.InkSecondary, tracking);
    }

    private void PaintConfig(object? sender, PaintEventArgs e)
    {
        if (_state is not { } state)
        {
            return;
        }

        var g = e.Graphics;
        var f = Fonts;
        var labelLeft = this.Px(24);
        var valueLeft = labelLeft + this.Px(ConfigLabelWidth);
        var valueWidth = Math.Max(0, _config.Width - valueLeft - this.Px(20));
        var lineHeight = this.Px(30);
        var lines = new[] { ("PSU", state.PsuLine), ("Sensors", state.SensorsLine), ("HWiNFO", state.HwinfoLine) };
        var step = this.Px(38);
        var y = ((_config.Height - (2 * step) - lineHeight) / 2) + this.Px(2);
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
        var top = p + _setup.TitleHeight + this.Px(12);
        var text = "Run the setup wizard to connect your Shelly plug, choose your power supply and pick the HWiNFO sensors. Until then nothing is polled or written to HWiNFO.";
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Fonts.Body,
            new Rectangle(p, top, Math.Min(_setup.Width - (2 * p), this.Px(720)), this.Px(80)),
            Theme.InkSecondary,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
