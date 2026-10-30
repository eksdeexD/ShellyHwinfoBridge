using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>The main window (stub: status and buttons only; the full layout follows).</summary>
internal sealed class MainForm : ThemedForm
{
    private readonly StatusPill _pill = new();
    private readonly PillButton _settings = new() { Text = "Settings" };
    private readonly PillButton _wizard = new() { Text = "Setup wizard" };
    private readonly PillButton _pause = new() { Text = "Pause polling" };
    private MainViewState? _state;

    public MainForm()
    {
        Text = "ShellyHwinfoBridge";
        Controls.AddRange([_pill, _settings, _wizard, _pause]);
        _settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _wizard.Click += (_, _) => WizardRequested?.Invoke(this, EventArgs.Empty);
        _pause.Click += (_, _) => PauseToggleRequested?.Invoke(this, EventArgs.Empty);
        SetClientSizeDip(new Size(1100, 740), new Size(980, 680));
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? WizardRequested;

    public event EventHandler? PauseToggleRequested;

    public event EventHandler? PeriodChanged;

    public EnergyPeriod SelectedPeriod { get; private set; } = EnergyPeriod.Hours;

    public void Apply(MainViewState state)
    {
        _state = state;
        _pill.Apply((UiTone)(int)state.Status.Dot, state.Status.Text);
        _pause.Text = state.PauseButtonText;
        _pause.Enabled = state.PauseEnabled;
        Invalidate();
    }

    public void ApplyChart(EnergyChartModel chart)
    {
        _ = chart;
        _ = PeriodChanged;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        _pill.Bounds = new Rectangle(ClientSize.Width - this.Px(24 + 300), this.Px(26), this.Px(300), this.Px(42));
        var x = ClientSize.Width - this.Px(24 + 276);
        _settings.Bounds = new Rectangle(x, this.Px(120), this.Px(276), this.Px(40));
        _wizard.Bounds = new Rectangle(x, this.Px(175), this.Px(276), this.Px(40));
        _pause.Bounds = new Rectangle(x, this.Px(230), this.Px(276), this.Px(40));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Gfx.Text(e.Graphics, "ShellyHwinfoBridge", Fonts.AppTitle, new Rectangle(this.Px(24), this.Px(24), this.Px(600), this.Px(44)), Theme.Ink);
        if (_state is { } s)
        {
            var y = this.Px(120);
            foreach (var line in new[] { $"AC {s.Ac.Value}", $"DC {s.Dc.Value}", $"PSU {s.PsuLine}", $"HWiNFO {s.HwinfoLine}" })
            {
                Gfx.Text(e.Graphics, line, Fonts.Body, new Rectangle(this.Px(24), y, this.Px(700), this.Px(28)), Theme.InkSecondary);
                y += this.Px(32);
            }
        }
    }
}
