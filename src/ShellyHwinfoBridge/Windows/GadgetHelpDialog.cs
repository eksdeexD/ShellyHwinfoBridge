using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The "How to enable?" help of the Other Components card and of Settings › Sensors (window title "HWiNFO Gadget"): the
/// heading (<see cref="GadgetStepModel.Heading"/>), the gadget how-to as a numbered list with the user's selected sensors
/// and their live markers (<see cref="GadgetHowToView"/>, the same as wizard step 5) on the left, the embedded
/// <c>hwinfo-gadget.png</c> screenshot on the right, and OK. Fixed-size, sized to its content. Esc or Enter closes it.
/// </summary>
internal sealed class GadgetHelpDialog : ThemedForm
{
    /// <summary>The window title.</summary>
    public const string WindowTitle = "HWiNFO Gadget";

    private const int Pad = Theme.WindowPadding;
    private const int WidthDip = Theme.MainWindowWidth;
    private const int TextWidth = 430;
    private const int TitleTop = 14;
    private const int TitleHeight = 30;
    private const int ContentTop = TitleTop + TitleHeight + 10;
    private const int ImageGap = 20;
    private const int MaxImageHeight = 470;

    // Created in field initializers: the base constructor already lays out.
    private readonly PillButton _ok = new() { Text = "OK", Variant = PillVariant.Primary, DialogResult = DialogResult.OK };
    private readonly GadgetHowToView _howTo;
    private readonly ScreenshotFrame _screenshot;

    /// <summary>Creates the dialog.</summary>
    /// <param name="model">The how-to model, loaded with the selected sensors (its markers refresh while shown).</param>
    /// <param name="screenshot">The screenshot (owned); <c>null</c> loads the embedded one.</param>
    public GadgetHelpDialog(GadgetStepModel model, Image? screenshot = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _howTo = new GadgetHowToView(model);
        _screenshot = new ScreenshotFrame(screenshot ?? AppAssets.TryLoadHwinfoGadgetScreenshot(), "Screenshot: HWiNFO Settings, HWiNFO Gadget tab");
        Text = WindowTitle;
        AccessibleName = GadgetStepModel.Heading;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        _howTo.TabIndex = 0;
        _screenshot.TabIndex = 1;
        _ok.TabIndex = 2;
        Controls.AddRange([_howTo, _screenshot, _ok]);
        AcceptButton = _ok;
        CancelButton = _ok;
        SetFixedClientSizeDip(new Size(WidthDip, 560));
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    public static void Show(IWin32Window? owner, GadgetStepModel model)
    {
        using var dialog = new GadgetHelpDialog(model);
        dialog.ShowDialog(owner);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ok.Focus();
    }

    protected override void OnThemeDpiChanged()
    {
        base.OnThemeDpiChanged();

        // As tall as the how-to or the screenshot (at the width left for it), plus the OK row.
        if (_howTo is null || _screenshot is null)
        {
            return;
        }

        var image = _screenshot.FitInto(new Size(ImageWidth(), this.Px(MaxImageHeight)));
        var content = Math.Max(_howTo.GetPreferredHeight(this.Px(TextWidth)), image.Height);
        ClientSize = new Size(this.Px(WidthDip), this.Px(ContentTop) + content + this.Px(Theme.Gap + 6 + Theme.ButtonHeight + Pad));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (w <= 0 || h <= 0 || _screenshot is null || _howTo is null)
        {
            return;
        }

        var pad = this.Px(Pad);
        var buttonHeight = this.Px(Theme.ButtonHeight);
        var buttonTop = h - pad - buttonHeight;
        var okWidth = Math.Max(this.Px(84), _ok.GetPreferredSize(Size.Empty).Width);
        _ok.Bounds = new Rectangle(w - pad - okWidth, buttonTop, okWidth, buttonHeight);

        var top = this.Px(ContentTop);
        var textWidth = this.Px(TextWidth);
        var available = Math.Max(0, buttonTop - this.Px(Theme.Gap) - top);
        _howTo.Bounds = new Rectangle(pad + this.Px(2), top, textWidth, Math.Min(available, _howTo.GetPreferredHeight(textWidth)));
        var size = _screenshot.FitInto(new Size(ImageWidth(), available));
        _screenshot.Visible = _screenshot.HasImage;
        _screenshot.Bounds = new Rectangle(new Point(w - pad - size.Width, top), size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var width = ClientSize.Width - this.Px(2 * Pad);
        Gfx.Text(e.Graphics, GadgetStepModel.Heading, Fonts.PageTitle, new Rectangle(this.Px(Pad + 2), this.Px(TitleTop), width, this.Px(TitleHeight)), Theme.Ink);
    }

    /// <summary>The width left for the screenshot right of the how-to, in device pixels.</summary>
    private int ImageWidth() => this.Px(WidthDip - Pad - 2 - TextWidth - ImageGap - Pad);
}
