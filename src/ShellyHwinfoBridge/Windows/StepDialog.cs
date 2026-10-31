using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// A modal dialog that hosts one wizard step view for the settings page ("Change" hosts the PSU step, "Edit" the
/// sensor step, also the device form and the sensor names): page title and subtitle, the step, and Cancel / Save. Fixed
/// size: as wide as the wizard and as tall as the view needs (<see cref="WizardStepView.PreferredHeightDip"/>). Save asks
/// <c>validate</c> first and stays open with the reason when the step is not valid. Esc = Cancel.
/// </summary>
internal sealed class StepDialog : ThemedForm
{
    /// <summary>The client width in DIP (as the wizard window).</summary>
    public const int WidthDip = 900;

    private const int Pad = Theme.WindowPadding;
    private const int TitleTop = 14;
    private const int TitleHeight = 30;
    private const int SubtitleTop = TitleTop + TitleHeight + 4;
    private const int SubtitleHeight = 22;
    private const int HostTop = SubtitleTop + SubtitleHeight + 12;
    private const int FooterHeight = 56;
    private const int ButtonWidth = 84;
    private const int ButtonHeight = Theme.ButtonHeight;

    // Created in field initializers: the base constructor already lays out.
    private readonly PillButton _save = new() { Text = "Save", Variant = PillVariant.Primary };
    private readonly PillButton _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };
    private readonly InfoLine _error = new() { Tone = InfoTone.Error, Visible = false };
    private readonly string _title = string.Empty;
    private readonly string _subtitle = string.Empty;
    private readonly WizardStepView? _view;
    private readonly Func<string?> _validate = () => null;

    /// <summary>Creates the dialog.</summary>
    /// <param name="title">The page title.</param>
    /// <param name="subtitle">The subtitle.</param>
    /// <param name="view">The step view (disposed with the dialog).</param>
    /// <param name="validate">Returns why Save is not possible, or <c>null</c> when it is.</param>
    /// <param name="windowTitle">The brief window title ("Add a Shelly"); <c>null</c> uses <paramref name="title"/>.</param>
    public StepDialog(string title, string subtitle, WizardStepView view, Func<string?> validate, string? windowTitle = null)
    {
        _title = title;
        _subtitle = subtitle;
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        Text = windowTitle ?? title;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterParent;

        SuspendLayout();
        _view.AccessibleName = title;
        _view.TabIndex = 0;
        _error.TabIndex = 1;
        _cancel.TabIndex = 2;
        _save.TabIndex = 3;
        Controls.AddRange([_view, _error, _cancel, _save]);
        CancelButton = _cancel;
        ResumeLayout(false);

        _save.Click += (_, _) => Save();
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        var hostHeight = (int)Math.Ceiling(_view.PreferredHeightDip(WidthDip - (2 * Pad)));
        SetFixedClientSizeDip(new Size(WidthDip, HostTop + hostHeight + FooterHeight));
    }

    /// <summary>
    /// What <c>validate</c> returns to keep the dialog open without an error line (e.g. the user cancelled a
    /// confirmation shown by the validation).
    /// </summary>
    public const string StayOpen = "";

    /// <summary>Gets the hosted step view.</summary>
    public WizardStepView? View => _view;

    /// <summary>Gets or sets whether Save is enabled (e.g. only when something changed).</summary>
    public bool SaveEnabled
    {
        get => _save.Enabled;
        set => _save.Enabled = value;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Ctrl+Enter saves from anywhere (Enter itself belongs to the focused field, list or grid).
        if (keyData == (Keys.Control | Keys.Enter))
        {
            Save();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_view is not null)
        {
            _view.RefreshView();
            _view.OnStepShown();
            SelectNextControl(_view, forward: true, tabStopOnly: true, nested: true, wrap: false);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _view?.OnStepHidden();
        base.OnFormClosed(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (w <= 0 || h <= 0 || _view is null)
        {
            return;
        }

        var pad = this.Px(Pad);
        var footerTop = h - this.Px(FooterHeight);
        _view.Bounds = new Rectangle(pad, this.Px(HostTop), w - (2 * pad), Math.Max(this.Px(120), footerTop - this.Px(HostTop)));

        var buttonTop = h - pad - this.Px(ButtonHeight);
        _save.Bounds = new Rectangle(w - pad - this.Px(ButtonWidth), buttonTop, this.Px(ButtonWidth), this.Px(ButtonHeight));
        _cancel.Bounds = new Rectangle(_save.Left - this.Px(8) - this.Px(ButtonWidth), buttonTop, this.Px(ButtonWidth), this.Px(ButtonHeight));
        var errorWidth = Math.Max(0, _cancel.Left - this.Px(16) - pad);
        _error.Bounds = new Rectangle(pad, buttonTop, errorWidth, this.Px(ButtonHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var width = ClientSize.Width - this.Px(2 * Pad);
        Gfx.Text(e.Graphics, _title, Fonts.PageTitle, new Rectangle(this.Px(Pad + 2), this.Px(TitleTop), width, this.Px(TitleHeight)), Theme.Ink);
        Gfx.Text(e.Graphics, _subtitle, Fonts.Subtitle, new Rectangle(this.Px(Pad + 2), this.Px(SubtitleTop), width, this.Px(SubtitleHeight)), Theme.InkSecondary);
    }

    private void Save()
    {
        if (!_save.Enabled)
        {
            return;
        }

        var problem = _validate();
        if (problem is { Length: 0 })
        {
            _error.Visible = false;
            return;
        }

        if (problem is not null)
        {
            _error.Text = problem;
            _error.Visible = true;
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
