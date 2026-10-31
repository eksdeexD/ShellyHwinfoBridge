using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The "Update the names in HWiNFO" help (<see cref="HwinfoRenameHelp"/>), opened by "Show me how" next to a rename
/// warning: the intro, the renamed sensors (old → new, one row each; the dialog grows with them), fix 1 (Rename in HWiNFO), fix 2
/// (Reset Preferences) and the closing note on the left, the embedded <c>hwinfo-reset-preferences.png</c> screenshot on
/// the right, and OK. Modelled on <see cref="GadgetHelpDialog"/>; fixed-size, sized to its content. Esc or Enter closes it.
/// </summary>
internal sealed class RenameHelpDialog : ThemedForm
{
    private const int Pad = Theme.WindowPadding;
    private const int WidthDip = 760;
    private const int TextWidth = 340;
    private const int TitleTop = 12;
    private const int TitleHeight = 28;
    private const int ContentTop = 48;
    private const int RenameRowHeight = 19;
    private const int MaxImageHeight = 400;

    // Created in field initializers: the base constructor already lays out.
    private readonly PillButton _ok = new() { Text = "OK", Variant = PillVariant.Primary, DialogResult = DialogResult.OK };
    private readonly TextLabel _intro = new() { Text = HwinfoRenameHelp.Intro, WordWrap = true };
    private readonly List<TextLabel> _renames = [];
    private readonly TextLabel _fix1Title = new() { Text = HwinfoRenameHelp.Fix1Title, Role = FontRole.BodySemibold };
    private readonly TextLabel _fix1 = new() { Text = HwinfoRenameHelp.Fix1Text, WordWrap = true };
    private readonly TextLabel _fix2Title = new() { Text = HwinfoRenameHelp.Fix2Title, Role = FontRole.BodySemibold };
    private readonly TextLabel _fix2 = new() { Text = HwinfoRenameHelp.Fix2Text, WordWrap = true };
    private readonly TextLabel _followUp = new() { Text = HwinfoRenameHelp.FollowUpText, WordWrap = true, Role = FontRole.Small, Tone = UiTone.Secondary };
    private readonly ScreenshotFrame _screenshot;

    /// <summary>Creates the dialog.</summary>
    /// <param name="help">The help with the renamed sensors.</param>
    /// <param name="screenshot">The screenshot (owned); <c>null</c> loads the embedded one.</param>
    public RenameHelpDialog(HwinfoRenameHelp help, Image? screenshot = null)
    {
        ArgumentNullException.ThrowIfNull(help);
        _screenshot = new ScreenshotFrame(screenshot ?? AppAssets.TryLoadHwinfoResetPreferencesScreenshot(), "Screenshot: HWiNFO Settings, Reset Preferences");
        Text = HwinfoRenameHelp.Title;
        AccessibleName = HwinfoRenameHelp.Title;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var lines = help.RenameLines.Count > 0 ? help.RenameLines : [HwinfoRenameHelp.NoRenamesText];
        foreach (var line in lines)
        {
            _renames.Add(new TextLabel { Text = line, Role = FontRole.BodySemibold, AccessibleName = line });
        }

        var index = 0;
        foreach (var control in new Control[] { _intro }.Concat(_renames).Concat([_fix1Title, _fix1, _fix2Title, _fix2, _followUp, _screenshot, _ok]))
        {
            control.TabIndex = index++;
            Controls.Add(control);
        }

        AcceptButton = _ok;
        CancelButton = _ok;

        // The height follows the content (taller with more renamed sensors): see OnThemeDpiChanged.
        SetFixedClientSizeDip(new Size(WidthDip, 520));
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    public static void Show(IWin32Window? owner, HwinfoRenameHelp help)
    {
        using var dialog = new RenameHelpDialog(help);
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

        // As tall as the text column or the screenshot (at the width left for it), plus the OK row.
        var image = _screenshot.FitInto(new Size(ImageWidth(), this.Px(MaxImageHeight)));
        var content = Math.Max(LayoutText(apply: false) - this.Px(ContentTop), image.Height);
        ClientSize = new Size(this.Px(WidthDip), this.Px(ContentTop) + content + this.Px(Theme.Gap + Theme.ButtonHeight + Pad));
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
        var buttonHeight = this.Px(Theme.ButtonHeight);
        var buttonTop = h - pad - buttonHeight;
        var okWidth = Math.Max(this.Px(84), _ok.GetPreferredSize(Size.Empty).Width);
        _ok.Bounds = new Rectangle(w - pad - okWidth, buttonTop, okWidth, buttonHeight);
        LayoutText(apply: true);

        var top = this.Px(ContentTop);
        var size = _screenshot.FitInto(new Size(ImageWidth(), Math.Max(0, buttonTop - this.Px(Theme.Gap) - top)));
        _screenshot.Visible = _screenshot.HasImage;
        _screenshot.Bounds = new Rectangle(new Point(this.Px(WidthDip - Pad) - size.Width, top), size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var width = ClientSize.Width - this.Px(2 * Pad);
        Gfx.Text(e.Graphics, HwinfoRenameHelp.Title, Fonts.CardTitle, new Rectangle(this.Px(Pad + 2), this.Px(TitleTop), width, this.Px(TitleHeight)), Theme.Ink);
    }

    /// <summary>The width left for the screenshot right of the text, in device pixels.</summary>
    private int ImageWidth() => this.Px(WidthDip - Pad - 2 - TextWidth - 20 - Pad);

    /// <summary>Stacks the text column (intro, renamed sensors, the two fixes, the note); returns its bottom in pixels.</summary>
    private int LayoutText(bool apply)
    {
        var left = this.Px(Pad + 2);
        var width = this.Px(TextWidth);
        var y = this.Px(ContentTop);
        int Place(TextLabel label, int top, int indent = 0)
        {
            var height = label.GetPreferredHeight(width - indent);
            if (apply)
            {
                label.Bounds = new Rectangle(left + indent, top, width - indent, height);
            }

            return top + height;
        }

        y = Place(_intro, y) + this.Px(6);

        // The renamed sensors, one row each.
        var rowHeight = this.Px(RenameRowHeight);
        foreach (var rename in _renames)
        {
            if (apply)
            {
                rename.Bounds = new Rectangle(left + this.Px(10), y, width - this.Px(10), rowHeight);
            }

            y += rowHeight;
        }

        y += this.Px(10);
        y = Place(_fix1Title, y) + this.Px(2);
        y = Place(_fix1, y) + this.Px(10);
        y = Place(_fix2Title, y) + this.Px(2);
        y = Place(_fix2, y) + this.Px(10);
        return Place(_followUp, y);
    }
}
