using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 5: the summary card and "Start with Windows" (Apply is the form's Next button). After Apply: the "HWiNFO" card
/// (status or restart advice, Restart / Start / Retry / Verify; the restart runs in the background with the buttons
/// disabled) and the "Verify" card (four-sensor checklist, AC/DC/Loss/Other tiles, plausibility warning).
/// </summary>
internal sealed class ApplyStepView : WizardStepView
{
    private const float LeftWidth = 470;
    private const float LabelWidth = 108;

    private readonly ApplyStepModel _model;
    private readonly CardPanel _summary = new() { Title = "Summary" };
    private readonly List<(TextLabel Label, TextLabel Value)> _lines = [];
    private readonly ToggleSwitch _startWithWindows = new() { Text = "Start with Windows" };
    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error };
    private readonly InfoLine _beforeApply = new()
    {
        Text = "Apply saves the settings, writes the Shelly Power sensors and the formula to HWiNFO's registry key and starts polling. HWiNFO then has to be restarted to show the new sensors.",
    };

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
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)ApplyStepModel.VerifyInterval.TotalMilliseconds };
    private readonly CancellationTokenSource _cts = new();
    private RestartAction? _primaryAction;
    private RestartAction? _secondaryAction;

    public ApplyStepView(WizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _model = session.Apply;
        _hwinfo.Controls.AddRange([_hwinfoText, _primary, _secondary, _verifyButton, _restarting]);
        _verify.Controls.AddRange([.. _checks, .. _tiles, _verifyStatus]);
        Controls.AddRange([_summary, _startWithWindows, _applyError, _beforeApply, _hwinfo, _verify]);

        _startWithWindows.CheckedChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                _model.StartWithWindows = _startWithWindows.Checked;
            }
        };
        _primary.Click += async (_, _) => await RunAsync(_primaryAction);
        _secondary.Click += async (_, _) => await RunAsync(_secondaryAction);
        _verifyButton.Click += (_, _) => _model.StartVerify();
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
        // Summary.
        var lines = _model.SummaryLines;
        while (_lines.Count < lines.Count)
        {
            var label = new TextLabel { Tone = UiTone.Secondary, VerticalCenter = false };
            var value = new TextLabel { WordWrap = true };
            _summary.Controls.Add(label);
            _summary.Controls.Add(value);
            _lines.Add((label, value));
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            var visible = i < lines.Count;
            _lines[i].Label.Visible = visible;
            _lines[i].Value.Visible = visible;
            if (visible)
            {
                _lines[i].Label.Text = lines[i].Label;
                _lines[i].Value.Text = lines[i].Value;
                _lines[i].Value.Role = lines[i].Monospace ? FontRole.Mono : FontRole.Body;
            }
        }

        var applied = _model.IsApplied;
        _startWithWindows.Checked = _model.StartWithWindows;
        _startWithWindows.Enabled = !applied;
        _applyError.Text = _model.ApplyError ?? string.Empty;
        _applyError.Visible = _model.ApplyError is not null;
        _beforeApply.Visible = !applied;
        _hwinfo.Visible = applied;
        _verify.Visible = applied;
        if (!applied)
        {
            return;
        }

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
        _verifyButton.Enabled = !_model.IsVerifyRunning || _model.VerifyPhase == VerifyPhase.Confirmed;
        _restarting.Visible = _model.IsRestarting;

        // Verify card.
        var result = _model.Verify;
        for (var i = 0; i < _checks.Length; i++)
        {
            var label = VerifyEvaluator.Labels[i];
            var found = result?.Items[i].Found ?? false;
            _checks[i].Text = label;
            _checks[i].Tone = found ? InfoTone.Success : InfoTone.Info;
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

        // Left: summary lines (label column, wrapped values), then the toggle and the error.
        var summaryWidth = Px(LeftWidth);
        var valueLeft = Px(20 + LabelWidth);
        var valueWidth = summaryWidth - valueLeft - Px(20);
        var y = Px(60);
        foreach (var (label, value) in _lines.Where(l => l.Label.Visible))
        {
            var height = Math.Max(value.GetPreferredHeight(valueWidth), Px(22));
            label.Bounds = new Rectangle(Px(20), y, valueLeft - Px(28), Px(22));
            value.Bounds = new Rectangle(valueLeft, y, valueWidth, height);
            y += height + Px(10);
        }

        var maxSummary = Px(h - 40 - gap - 40);
        _summary.Bounds = new Rectangle(0, 0, summaryWidth, Math.Min(maxSummary, y + Px(10)));
        _startWithWindows.Bounds = new Rectangle(0, _summary.Bottom + Px(gap), Px(260), Px(32));
        _applyError.Bounds = new Rectangle(0, _startWithWindows.Bottom + Px(10), summaryWidth, Math.Max(Px(28), _applyError.GetPreferredHeight(summaryWidth)));

        // Right column.
        var right = LeftWidth + gap;
        var rightWidth = w - right;
        var rightPx = Px(rightWidth);
        _beforeApply.Bounds = new Rectangle(Px(right), 0, rightPx, _beforeApply.GetPreferredHeight(rightPx));

        const float hwinfoHeight = 186;
        _hwinfo.Bounds = Dip(right, 0, rightWidth, hwinfoHeight);
        var inner = _hwinfo.Width - Px(40);
        _hwinfoText.Bounds = new Rectangle(Px(20), Px(52), inner, Math.Min(Px(70), Math.Max(Px(28), _hwinfoText.GetPreferredHeight(inner))));
        var buttonTop = Px(hwinfoHeight - 20 - 40);
        var x = Px(20);
        foreach (var button in new[] { _primary, _secondary, _verifyButton }.Where(b => b.Visible))
        {
            var width = Math.Max(Px(110), button.GetPreferredSize(Size.Empty).Width);
            button.Bounds = new Rectangle(x, buttonTop, width, Px(40));
            x += width + Px(12);
        }

        _restarting.Bounds = new Rectangle(Px(20), buttonTop, inner, Px(40));

        _verify.Bounds = Dip(right, hwinfoHeight + gap, rightWidth, h - hwinfoHeight - gap);
        var verifyInner = _verify.Width - Px(40);
        var half = (verifyInner - Px(12)) / 2;
        for (var i = 0; i < _checks.Length; i++)
        {
            // Unchecked items have no icon: indent them so the names line up with the ticked ones.
            var indent = _checks[i].Tone == InfoTone.Info ? Px(32) : 0;
            _checks[i].Bounds = new Rectangle(Px(20) + ((i % 2) * (half + Px(12))) + indent, Px(50 + ((i / 2) * 28)), half - indent, Px(26));
        }

        var tileWidth = (verifyInner - Px(3 * 10)) / 4;
        for (var i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].Bounds = new Rectangle(Px(20) + (i * (tileWidth + Px(10))), Px(112), tileWidth, Px(66));
        }

        var statusTop = Px(112 + 66 + 10);
        _verifyStatus.Bounds = new Rectangle(Px(20), statusTop, verifyInner, Math.Max(Px(28), Math.Min(_verify.Height - statusTop - Px(10), _verifyStatus.GetPreferredHeight(verifyInner))));
    }

    public override void OnStepHidden()
    {
        // Leaving step 5 is only possible before Apply; nothing runs then.
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
