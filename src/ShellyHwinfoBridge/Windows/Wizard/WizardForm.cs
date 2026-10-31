using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// The setup wizard window (title "Setup wizard"): the step indicator centred at the top (no logo or app name: the title
/// bar carries them), page title and subtitle, the step view, and the footer with Back and Next ("Apply" on step 6;
/// "Finish" on step 7; Back is hidden after Apply). There is no Cancel: the title bar's X closes the window, and so does
/// Esc before Apply. Enter = Next. Closing before Apply cancels without changing anything. Next on step 5 asks first while
/// a selected sensor is not reported to the HWiNFO Gadget yet.
/// </summary>
internal sealed class WizardForm : ThemedForm
{
    /// <summary>The fixed client size in DIP (fits the tallest step).</summary>
    public static readonly Size ClientSizeDip = new(StepDialog.WidthDip, HostTop + (int)WizardStepView.StepHeightDip + FooterHeight);

    // Layout in DIP (compact scale; spacing from Theme).
    private const int Pad = Theme.WindowPadding;
    private const int StepsTop = 12;
    private const int TitleTop = 70;
    private const int TitleHeight = 30;
    private const int SubtitleTop = TitleTop + TitleHeight + 4;
    private const int SubtitleHeight = 22;
    private const int HostTop = SubtitleTop + SubtitleHeight + 12;
    private const int FooterHeight = 52;
    private const int ButtonWidth = 84;
    private const int ButtonHeight = Theme.ButtonHeight;

    private readonly WizardSession _session;
    private readonly StepIndicator _steps = new() { AccessibleName = "Setup steps" };
    private readonly ThemedPanel _host = new() { AccessibleName = "Step" };
    private readonly PillButton _back = new() { Text = "Back" };
    private readonly PillButton _next = new() { Text = "Next", Variant = PillVariant.Primary };
    private readonly Dictionary<WizardStep, WizardStepView> _views = [];
    private readonly CancellationTokenSource _closing = new();
    private WizardStepView? _view;
    private WizardStep? _shownStep;
    private bool _navigating;

    public WizardForm(WizardSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Text = "Setup wizard";
        KeyPreview = true;
        SuspendLayout();
        _steps.SetSteps(WizardSession.StepLabels);
        Controls.AddRange([_steps, _host, _back, _next]);
        AcceptButton = _next;
        ResumeLayout(false);

        _back.Click += (_, _) => _session.GoBack();
        _next.Click += async (_, _) => await NextAsync();
        _session.Changed += OnSessionChanged;
        SetFixedClientSizeDip(ClientSizeDip);
        SyncChrome();
    }

    /// <summary>Raised once when the settings were applied.</summary>
    public event EventHandler? Applied;

    /// <summary>Gets the session.</summary>
    public WizardSession Session => _session;

    /// <summary>Gets the view of the current step (screen renderer).</summary>
    internal WizardStepView? CurrentView => _view;

    /// <summary>Opens the voltage override on the PSU step (screen renderer).</summary>
    internal void PreviewOpenVoltage() => (_view as PsuStepView)?.ShowVoltageOverride();

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape && !_session.IsApplied)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Start on Next, so Enter moves on at once (a focused button would otherwise take the Enter key).
        ActiveControl = _next;
        _view?.OnStepShown();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        _session.Changed -= OnSessionChanged;
        _session.Apply.StopVerify();
        _view?.OnStepHidden();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Changed -= OnSessionChanged;
            _closing.Dispose();
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
        var stepsSize = _steps.GetPreferredSize(Size.Empty);
        _steps.Bounds = new Rectangle((w - stepsSize.Width) / 2, this.Px(StepsTop), stepsSize.Width, stepsSize.Height);

        var footerTop = h - this.Px(FooterHeight);
        _host.Bounds = new Rectangle(pad, this.Px(HostTop), w - (2 * pad), Math.Max(this.Px(160), footerTop - this.Px(HostTop)));
        if (_view is not null)
        {
            _view.Bounds = new Rectangle(Point.Empty, _host.Size);
        }

        var buttonTop = h - this.Px(Pad) - this.Px(ButtonHeight);
        var nextWidth = Math.Max(this.Px(ButtonWidth), _next.GetPreferredSize(Size.Empty).Width);
        _next.Bounds = new Rectangle(w - pad - nextWidth, buttonTop, nextWidth, this.Px(ButtonHeight));
        _back.Bounds = new Rectangle(_next.Left - this.Px(8) - this.Px(ButtonWidth), buttonTop, this.Px(ButtonWidth), this.Px(ButtonHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var width = ClientSize.Width - this.Px(2 * (Pad + 2));
        Gfx.Text(g, _session.PageTitle, Fonts.PageTitle, new Rectangle(this.Px(Pad + 2), this.Px(TitleTop), width, this.Px(TitleHeight)), Theme.Ink);
        Gfx.Text(g, _session.PageSubtitle, Fonts.Subtitle, new Rectangle(this.Px(Pad + 2), this.Px(SubtitleTop), width, this.Px(SubtitleHeight)), Theme.InkSecondary);
    }

    private async Task NextAsync()
    {
        if (_navigating)
        {
            return;
        }

        if (_session.Current == WizardStep.Verify)
        {
            Close();
            return;
        }

        // Step 5: while a selected sensor is not reported to the HWiNFO Gadget yet, ask first.
        if (_session.NeedsNextConfirmation &&
            ThemedDialog.Show(this, GadgetStepModel.ConfirmTitle, _session.Gadget.ConfirmText, GadgetStepModel.ContinueText, GadgetStepModel.GoBackText) != DialogResult.OK)
        {
            return;
        }

        _navigating = true;
        var wasApplied = _session.IsApplied;
        try
        {
            await _session.GoNextAsync(_closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _navigating = false;
        }

        if (IsDisposed)
        {
            return;
        }

        SyncChrome();
        if (!wasApplied && _session.IsApplied)
        {
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Continues past a failed connection test.</summary>
    private async Task ContinueAnywayAsync()
    {
        if (_navigating)
        {
            return;
        }

        _navigating = true;
        try
        {
            await _session.ContinueAnywayAsync(_closing.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _navigating = false;
        }

        if (!IsDisposed)
        {
            SyncChrome();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            SyncChrome();
        }
    }

    /// <summary>Brings the step indicator, the titles, the buttons and the visible step view in line with the session.</summary>
    private void SyncChrome()
    {
        var current = _session.Current;
        _steps.SetState((int)current, _session.Completed.Select(s => (int)s));
        _next.Text = _session.NextText;
        _next.Enabled = _session.CanGoNext && !_navigating;
        _back.Visible = !_session.IsApplied;
        _back.Enabled = _session.CanGoBack;

        if (_shownStep != current)
        {
            _view?.OnStepHidden();
            if (_view is not null)
            {
                _view.Visible = false;
            }

            _view = GetView(current);
            _view.Bounds = new Rectangle(Point.Empty, _host.Size);
            _view.Visible = true;
            _view.RefreshView();
            _shownStep = current;
            if (Visible)
            {
                _view.OnStepShown();
            }
        }

        Invalidate(new Rectangle(0, this.Px(TitleTop), ClientSize.Width, this.Px(HostTop - TitleTop)));
    }

    private WizardStepView GetView(WizardStep step)
    {
        if (_views.TryGetValue(step, out var view))
        {
            return view;
        }

        view = step switch
        {
            WizardStep.Hwinfo => new HwinfoStepView(_session.Hwinfo),
            WizardStep.Shelly => CreateShellyView(),
            WizardStep.Psu => new PsuStepView(_session.Psu),
            WizardStep.Sensors => new SensorStepView(_session.Sensors, _session.ReloadSensors),
            WizardStep.Apply => new ApplyStepView(_session),
            WizardStep.Gadget => new GadgetStepView(_session.Gadget),
            _ => new VerifyStepView(_session),
        };
        view.Visible = false;
        _host.Controls.Add(view);
        _views[step] = view;
        return view;
    }

    private ShellyStepView CreateShellyView()
    {
        var view = new ShellyStepView(_session.Shelly);
        view.ContinueAnywayRequested += async (_, _) => await ContinueAnywayAsync();
        return view;
    }
}
