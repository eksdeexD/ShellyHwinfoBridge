using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 6 (after Apply): side by side, the "HWiNFO" card (status or restart advice; Restart / Start / Retry and Verify;
/// a restart runs in the background with the buttons hidden) and the "Verify" card (four-sensor checklist, AC/DC/Loss/
/// Other tiles, the status line); under them the gadget export tip and, when <c>Assets/hwinfo-gadget.png</c> was
/// embedded, its screenshot. Verify is disabled once all four sensors are confirmed.
/// </summary>
internal sealed class VerifyStepView : WizardStepView
{
    private const float TopHeight = 178;
    private const float HwinfoWidth = 300;
    private const float ChecksWidth = 176;

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
    private readonly ScreenshotFrame _gadgetScreenshot = new(GadgetScreenshotSource(), "Screenshot: HWiNFO Settings, HWiNFO Gadget tab");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)ApplyStepModel.VerifyInterval.TotalMilliseconds };
    private readonly CancellationTokenSource _cts = new();
    private RestartAction? _primaryAction;
    private RestartAction? _secondaryAction;

    public VerifyStepView(WizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _model = session.Apply;
        _hwinfo.Controls.AddRange([_hwinfoText, _primary, _secondary, _verifyButton, _restarting]);
        _verify.Controls.AddRange([.. _checks, .. _tiles, _verifyStatus]);
        _gadgetScreenshot.Visible = _gadgetScreenshot.HasImage;
        Controls.AddRange([_hwinfo, _verify, _tip, _gadgetScreenshot]);

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

    /// <summary>Loads the optional gadget screenshot (the screen renderer swaps in a stand-in to preview the layout).</summary>
    internal static Func<Image?> GadgetScreenshotSource { get; set; } = AppAssets.TryLoadHwinfoGadgetScreenshot;

    protected override void OnRefreshView()
    {
        _tip.Text = _model.GadgetTip;

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
        const float gap = Theme.Gap;
        const float p = Theme.CardPadding;

        // HWiNFO card at the left: the text, then the buttons at the bottom of the card.
        _hwinfo.Bounds = Dip(0, 0, HwinfoWidth, TopHeight);
        var inner = _hwinfo.Width - Px(2 * p);
        var contentTop = _hwinfo.ContentBounds.Top;
        var buttonTop = Px(TopHeight - p - Theme.ButtonHeight);
        _hwinfoText.Bounds = new Rectangle(Px(p), contentTop, inner, Math.Min(buttonTop - Px(6) - contentTop, Math.Max(Px(Theme.LineHeight), _hwinfoText.GetPreferredHeight(inner))));
        var x = Px(p);
        foreach (var button in new[] { _primary, _secondary, _verifyButton }.Where(b => b.Visible))
        {
            var width = button.GetPreferredSize(Size.Empty).Width;
            button.Bounds = new Rectangle(x, buttonTop, width, Px(Theme.ButtonHeight));
            x += width + Px(8);
        }

        _restarting.Bounds = new Rectangle(Px(p), buttonTop, inner, Px(Theme.ButtonHeight));

        // Verify card at the right: the four sensors as one list (tick when found, neutral ring while pending), the
        // tiles 2 × 2 and the status line under them.
        _verify.Bounds = Dip(HwinfoWidth + gap, 0, w - HwinfoWidth - gap, TopHeight);
        var verifyInner = _verify.Width - Px(2 * p);
        var listWidth = Px(ChecksWidth);
        var top = _verify.ContentBounds.Top;
        for (var i = 0; i < _checks.Length; i++)
        {
            _checks[i].Bounds = new Rectangle(Px(p), top + Px(i * 22), listWidth, Px(Theme.LineHeight));
        }

        var tilesLeft = Px(p) + listWidth + Px(10);
        var tilesWidth = Px(p) + verifyInner - tilesLeft;
        var tileWidth = (tilesWidth - Px(8)) / 2;
        for (var i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].Bounds = new Rectangle(tilesLeft + ((i % 2) * (tileWidth + Px(8))), top + Px((i / 2) * 50), tileWidth, Px(44));
        }

        var statusTop = top + Px(50 + 44 + 8);
        _verifyStatus.Bounds = new Rectangle(Px(p), statusTop, verifyInner, Math.Max(Px(Theme.LineHeight), Math.Min(_verify.Height - statusTop - Px(6), _verifyStatus.GetPreferredHeight(verifyInner))));

        // Under the cards: the gadget tip, then its optional screenshot fitted to the rest of the page.
        var tipTop = Px(TopHeight + gap);
        var tipHeight = _tip.GetPreferredHeight(Width);
        _tip.Bounds = new Rectangle(0, tipTop, Width, tipHeight);
        var imageTop = tipTop + tipHeight + Px(8);
        var image = _gadgetScreenshot.FitInto(new Size(Width, Math.Max(0, Height - imageTop)));
        _gadgetScreenshot.Bounds = new Rectangle(new Point(0, imageTop), image);
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
