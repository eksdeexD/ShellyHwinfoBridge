using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 6 (after Apply): the "HWiNFO" card (status or restart advice; Restart / Start / Retry and Verify; a restart runs
/// in the background with the buttons hidden) and the "Verify" card (four-sensor checklist, AC/DC/Loss/Other tiles,
/// the status line and the gadget export tip). Verify is disabled once all four sensors are confirmed.
/// </summary>
internal sealed class VerifyStepView : WizardStepView
{
    private const float HwinfoHeight = 150;
    private const float ChecksWidth = 250;

    private readonly ApplyStepModel _model;
    private readonly CardPanel _hwinfo = new() { Title = "HWiNFO" };
    private readonly InfoLine _hwinfoText = new();
    private readonly PillButton _primary = new();
    private readonly PillButton _secondary = new();
    private readonly PillButton _verifyButton = new() { Text = "Verify" };
    private readonly TextLabel _restarting = new() { Text = "Restarting HWiNFO…", Tone = UiTone.Secondary };
    private readonly CardPanel _verify = new() { Title = "Verify" };
    private readonly InfoLine[] _checks = [new(), new(), new(), new()];
    private readonly ValueTile[] _tiles = [new() { Caption = "AC" }, new() { Caption = "DC" }, new() { Caption = "Loss" }, new() { Caption = "Other" }];
    private readonly InfoLine _verifyStatus = new();
    private readonly TextLabel _tip = new() { Text = HwinfoTips.GadgetExport, Role = FontRole.Small, Tone = UiTone.Secondary, WordWrap = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)ApplyStepModel.VerifyInterval.TotalMilliseconds };
    private readonly CancellationTokenSource _cts = new();
    private RestartAction? _primaryAction;
    private RestartAction? _secondaryAction;

    public VerifyStepView(WizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _model = session.Apply;
        _hwinfo.Controls.AddRange([_hwinfoText, _primary, _secondary, _verifyButton, _restarting]);
        _verify.Controls.AddRange([.. _checks, .. _tiles, _verifyStatus, _tip]);
        Controls.AddRange([_hwinfo, _verify]);

        _primary.Click += async (_, _) => await RunAsync(_primaryAction);
        _secondary.Click += async (_, _) => await RunAsync(_secondaryAction);
        _verifyButton.Click += (_, _) =>
        {
            if (_model.CanVerify)
            {
                _model.StartVerify();
            }
        };
        _verifyStatus.LinkClicked += (_, _) => _model.StartVerify();
        _timer.Tick += (_, _) =>
        {
            if (_model.IsVerifyRunning)
            {
                _model.VerifyTick();
            }
        };
        _timer.Start();
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    protected override void OnRefreshView()
    {
        // HWiNFO card: the advice of the last attempt, else the status.
        var advice = _model.Advice;
        _hwinfoText.Text = advice?.Text ?? _model.HwinfoText;
        _hwinfoText.Tone = advice is null ? InfoTone.Info : HwinfoStepView.ToInfoTone(advice.Tone);
        var actions = advice?.Actions.Where(a => a != RestartAction.Verify).ToList() ?? _model.PrimaryAction switch
        {
            HwinfoPrimaryAction.Restart => [RestartAction.Retry],
            HwinfoPrimaryAction.Start => [RestartAction.StartHwinfo],
            _ => [],
        };
        _primaryAction = actions.Count > 0 ? actions[0] : null;
        _secondaryAction = actions.Count > 1 ? actions[1] : null;
        _primary.Text = ActionText(_primaryAction, advice is null);
        _secondary.Text = ActionText(_secondaryAction, advice is null);
        _primary.Visible = _primaryAction is not null && !_model.IsRestarting;
        _secondary.Visible = _secondaryAction is not null && !_model.IsRestarting;
        _verifyButton.Visible = !_model.IsRestarting;
        _verifyButton.Enabled = _model.CanVerify;
        _restarting.Visible = _model.IsRestarting;

        // Verify card.
        var result = _model.Verify;
        for (var i = 0; i < _checks.Length; i++)
        {
            var label = VerifyEvaluator.Labels[i];
            var found = result?.Items[i].Found ?? false;
            _checks[i].Text = label;
            _checks[i].Tone = found ? InfoTone.Success : InfoTone.Pending;
            _checks[i].AccessibleDescription = found ? "Found" : "Not found yet";
        }

        for (var i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].Value = result?.Tiles[i].Value ?? UiFormat.Dash;
            _tiles[i].ValueTone = i == 3 && result?.Implausible == true ? UiTone.Warning : i == 1 ? UiTone.Accent : UiTone.Ink;
        }

        var status = _model.VerifyStatus ?? new WizardStatusLine(Tone.Secondary, "Restart HWiNFO, then the four Shelly Power sensors are checked here.");
        _verifyStatus.Text = status.Text;
        _verifyStatus.Tone = HwinfoStepView.ToInfoTone(status.Tone);
        _verifyStatus.LinkText = _model.VerifyPhase == VerifyPhase.TimedOut ? "Verify again" : null;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = 16;

        // HWiNFO card across the top: the text, then the buttons.
        _hwinfo.Bounds = Dip(0, 0, w, HwinfoHeight);
        var inner = _hwinfo.Width - Px(40);
        _hwinfoText.Bounds = new Rectangle(Px(20), Px(50), inner, Math.Min(Px(40), Math.Max(Px(28), _hwinfoText.GetPreferredHeight(inner))));
        var buttonTop = Px(HwinfoHeight - 20 - 40);
        var x = Px(20);
        foreach (var button in new[] { _primary, _secondary, _verifyButton }.Where(b => b.Visible))
        {
            var width = Math.Max(Px(110), button.GetPreferredSize(Size.Empty).Width);
            button.Bounds = new Rectangle(x, buttonTop, width, Px(40));
            x += width + Px(12);
        }

        _restarting.Bounds = new Rectangle(Px(20), buttonTop, inner, Px(40));

        // Verify card: the four sensors as one list (tick when found, neutral ring while pending), the tiles in a row
        // with the status line under them, and the gadget tip at the bottom.
        _verify.Bounds = Dip(0, HwinfoHeight + gap, w, h - HwinfoHeight - gap);
        var verifyInner = _verify.Width - Px(40);
        var listWidth = Px(ChecksWidth);
        for (var i = 0; i < _checks.Length; i++)
        {
            _checks[i].Bounds = new Rectangle(Px(20), Px(50 + (i * 28)), listWidth, Px(26));
        }

        var tilesLeft = Px(20) + listWidth + Px(16);
        var tilesWidth = Px(20) + verifyInner - tilesLeft;
        var tileWidth = (tilesWidth - (3 * Px(10))) / 4;
        for (var i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].Bounds = new Rectangle(tilesLeft + (i * (tileWidth + Px(10))), Px(48), tileWidth, Px(56));
        }

        var tipHeight = _tip.GetPreferredHeight(verifyInner);
        var tipTop = _verify.Height - Px(16) - tipHeight;
        _tip.Bounds = new Rectangle(Px(20), tipTop, verifyInner, tipHeight);

        var statusTop = Px(48 + 56 + 14);
        var statusHeight = Math.Max(Px(28), Math.Min(_verifyStatus.GetPreferredHeight(tilesWidth), tipTop - statusTop - Px(8)));
        _verifyStatus.Bounds = new Rectangle(tilesLeft, statusTop, tilesWidth, statusHeight);
    }

    public override void OnStepHidden()
    {
        // Step 6 is only left by closing the wizard; the form stops the verification.
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
            _timer.Stop();
            _timer.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string ActionText(RestartAction? action, bool initial) => action switch
    {
        RestartAction.Retry => initial ? "Restart HWiNFO" : "Retry",
        RestartAction.StartHwinfo => "Start HWiNFO",
        _ => string.Empty,
    };

    private void OnModelChanged(object? sender, EventArgs e) => RefreshView();

    private async Task RunAsync(RestartAction? action)
    {
        switch (action)
        {
            case RestartAction.Retry:
                try
                {
                    await _model.RestartHwinfoAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // The wizard closed.
                }

                break;
            case RestartAction.StartHwinfo:
                _model.StartHwinfo();
                break;
            case RestartAction.Verify:
                _model.StartVerify();
                break;
        }
    }
}
