using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The "How to enable?" help of the Other Components card: the title (<see cref="HwinfoTips.GadgetExportTitle"/>), the
/// gadget how-to with the user's selected sensor names (bold markup) on the left, the embedded
/// <c>hwinfo-gadget.png</c> screenshot on the right, and OK. Esc or Enter closes it.
/// </summary>
internal sealed class GadgetHelpDialog : ThemedForm
{
    private const int Pad = 24;
    private const int TextWidth = 360;
    private const int TitleTop = 20;
    private const int ContentTop = 72;

    // Created in field initializers: the base constructor already lays out.
    private readonly PillButton _ok = new() { Text = "OK", Variant = PillVariant.Primary, DialogResult = DialogResult.OK };
    private readonly TextLabel _text = new() { WordWrap = true };
    private readonly ScreenshotFrame _screenshot;

    /// <summary>Creates the dialog.</summary>
    /// <param name="howToText">The how-to (<see cref="OtherCardState.HowToText"/>, <see cref="InlineMarkup"/>).</param>
    /// <param name="screenshot">The screenshot (owned); <c>null</c> loads the embedded one.</param>
    public GadgetHelpDialog(string howToText, Image? screenshot = null)
    {
        _screenshot = new ScreenshotFrame(screenshot ?? AppAssets.TryLoadHwinfoGadgetScreenshot(), "Screenshot: HWiNFO Settings, HWiNFO Gadget tab");
        Text = HwinfoTips.GadgetExportTitle;
        AccessibleName = HwinfoTips.GadgetExportTitle;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        _text.Text = howToText ?? HwinfoTips.GadgetExport;
        _text.TabIndex = 0;
        _screenshot.TabIndex = 1;
        _ok.TabIndex = 2;
        Controls.AddRange([_text, _screenshot, _ok]);
        AcceptButton = _ok;
        CancelButton = _ok;
        SetClientSizeDip(new Size(980, 560), new Size(980, 560));
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    public static void Show(IWin32Window? owner, string howToText)
    {
        using var dialog = new GadgetHelpDialog(howToText);
        dialog.ShowDialog(owner);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ok.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (w <= 0 || h <= 0 || _screenshot is null)
        {
            return;
        }

        var pad = this.Px(Pad);
        var buttonHeight = this.Px(40);
        var buttonTop = h - pad - buttonHeight;
        var okWidth = Math.Max(this.Px(108), _ok.GetPreferredSize(Size.Empty).Width);
        _ok.Bounds = new Rectangle(w - pad - okWidth, buttonTop, okWidth, buttonHeight);

        var top = this.Px(ContentTop);
        var textWidth = this.Px(TextWidth);
        _text.Bounds = new Rectangle(pad + this.Px(4), top, textWidth, Math.Max(0, Math.Min(buttonTop - this.Px(16) - top, _text.GetPreferredHeight(textWidth))));
        var imageLeft = _text.Right + this.Px(24);
        var available = new Size(Math.Max(0, w - pad - imageLeft), Math.Max(0, buttonTop - this.Px(16) - top));
        var size = _screenshot.FitInto(available);
        _screenshot.Visible = _screenshot.HasImage;
        _screenshot.Bounds = new Rectangle(new Point(imageLeft, top), size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var width = ClientSize.Width - this.Px(2 * Pad);
        Gfx.Text(e.Graphics, HwinfoTips.GadgetExportTitle, Fonts.CardTitle, new Rectangle(this.Px(Pad + 4), this.Px(TitleTop), width, this.Px(36)), Theme.Ink);
    }
}
