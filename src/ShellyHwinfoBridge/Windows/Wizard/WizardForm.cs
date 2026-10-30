using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// The setup wizard window (design/final/wizard-step3-psu.png): header with logo, "Setup wizard" and the step indicator;
/// page title and subtitle; the step view; footer with Cancel, Back and Next (Apply, then Finish). Enter = Next,
/// Esc = Cancel. Closing before Apply cancels without changing anything.
/// </summary>
internal sealed class WizardForm : ThemedForm
{
    // Layout in DIP, measured from the 1536×1024 design image (client 1100×740).
    private const int Pad = 24;
    private const int HeaderTop = 24;
    private const int HeaderHeight = 44;
    private const int StepsLeft = 345;
    private const int TitleTop = 104;
    private const int SubtitleTop = 152;
    private const int HostTop = 196;
    private const int FooterHeight = 100;
    private const int ButtonWidth = 108;
    private const int ButtonHeight = 42;

    private readonly WizardSession _session;
    private readonly StepIndicator _steps = new() { AccessibleName = "Setup steps" };
    private readonly ThemedPanel _host = new() { AccessibleName = "Step" };
    private readonly TextLink _cancel = new() { Text = "Cancel", Tone = LinkTone.Ink };
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
        Text = "Setup wizard · ShellyHwinfoBridge";
        KeyPreview = true;
        SuspendLayout();
        _steps.SetSteps(WizardSession.StepLabels);
        Controls.AddRange([_steps, _host, _cancel, _back, _next]);
        AcceptButton = _next;
        ResumeLayout(false);

        _cancel.Click += (_, _) => Close();
        _back.Click += (_, _) => _session.GoBack();
        _next.Click += async (_, _) => await NextAsync();
        _session.Changed += OnSessionChanged;
        SetClientSizeDip(new Size(1100, 740), new Size(1000, 700));
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
        if (keyData == Keys.Escape && _cancel.Visible)
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
        _steps.Bounds = new Rectangle(this.Px(StepsLeft), this.Px(26), stepsSize.Width, Math.Max(stepsSize.Height, this.Px(66)));

        var footerTop = h - this.Px(FooterHeight);
        _host.Bounds = new Rectangle(pad, this.Px(HostTop), w - (2 * pad), Math.Max(this.Px(200), footerTop - this.Px(HostTop)));
        if (_view is not null)
        {
            _view.Bounds = new Rectangle(Point.Empty, _host.Size);
        }

        var buttonTop = h - this.Px(38) - this.Px(ButtonHeight);
        _next.Bounds = new Rectangle(w - pad - this.Px(ButtonWidth), buttonTop, this.Px(ButtonWidth), this.Px(ButtonHeight));
        _back.Bounds = new Rectangle(_next.Left - this.Px(14) - this.Px(ButtonWidth), buttonTop, this.Px(ButtonWidth), this.Px(ButtonHeight));
        var cancelSize = _cancel.GetPreferredSize(Size.Empty);
        _cancel.Bounds = new Rectangle(this.Px(30), buttonTop + ((this.Px(ButtonHeight) - cancelSize.Height) / 2), cancelSize.Width, cancelSize.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var logoHeight = this.Px(46);
        var top = this.Px(HeaderTop) + ((this.Px(HeaderHeight) - logoHeight) / 2);
        var logoWidth = AppAssets.DrawLogo(g, new Point(this.Px(34), top), logoHeight);
        var titleLeft = this.Px(34) + logoWidth + this.Px(18);
        Gfx.Text(g, "Setup wizard", Fonts.AppTitle, new Rectangle(titleLeft, this.Px(HeaderTop), Math.Max(0, _steps.Left - titleLeft - this.Px(16)), this.Px(HeaderHeight)), Theme.Ink);

        var width = ClientSize.Width - this.Px(60);
        Gfx.Text(g, _session.PageTitle, Fonts.PageTitle, new Rectangle(this.Px(30), this.Px(TitleTop), width, this.Px(48)), Theme.Ink);
        Gfx.Text(g, _session.PageSubtitle, Fonts.Subtitle, new Rectangle(this.Px(30), this.Px(SubtitleTop), width, this.Px(30)), Theme.InkSecondary);
    }

    private async Task NextAsync()
    {
        if (_navigating)
        {
            return;
        }

        if (_session.IsApplied && _session.Current == WizardStep.Apply)
        {
            Close();
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
        _cancel.Visible = !_session.IsApplied;

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
            _ => new ApplyStepView(_session),
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
