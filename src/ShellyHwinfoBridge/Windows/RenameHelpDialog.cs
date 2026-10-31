using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The "Update the names in HWiNFO" help (<see cref="HwinfoRenameHelp"/>), opened by "Show me how" next to a rename
/// warning: the intro, the renamed sensors (old → new, one row each; the dialog grows with them), fix 1 (Rename in HWiNFO), fix 2
/// (Reset Preferences) and the closing note on the left, the embedded <c>hwinfo-reset-preferences.png</c> screenshot on
/// the right, and OK. Modelled on <see cref="GadgetHelpDialog"/>. Esc or Enter closes it.
/// </summary>
internal sealed class RenameHelpDialog : ThemedForm
{
    private const int Pad = 24;
    private const int TextWidth = 440;
    private const int TitleTop = 20;
    private const int ContentTop = 72;
    private const int RenameRowHeight = 24;

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

        // Taller with more renamed sensors (one row each), so nothing is cut off.
        var height = 600 + (_renames.Count * RenameRowHeight);
        SetClientSizeDip(new Size(1000, height), new Size(1000, height));
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

        var left = pad + this.Px(4);
        var width = this.Px(TextWidth);
        var y = this.Px(ContentTop);
        y = Place(_intro, left, y, width) + this.Px(8);

        // The renamed sensors, one row each.
        var rowHeight = this.Px(RenameRowHeight);
        foreach (var rename in _renames)
        {
            rename.Bounds = new Rectangle(left + this.Px(12), y, width - this.Px(12), rowHeight);
            y += rowHeight;
        }

        y += this.Px(16);
        y = Place(_fix1Title, left, y, width) + this.Px(4);
        y = Place(_fix1, left, y, width) + this.Px(16);
        y = Place(_fix2Title, left, y, width) + this.Px(4);
        y = Place(_fix2, left, y, width) + this.Px(16);
        _followUp.Bounds = new Rectangle(left, y, width, Math.Max(0, Math.Min(buttonTop - this.Px(8) - y, _followUp.GetPreferredHeight(width))));

        var top = this.Px(ContentTop);
        var imageLeft = left + width + this.Px(24);
        var available = new Size(Math.Max(0, w - pad - imageLeft), Math.Max(0, buttonTop - this.Px(16) - top));
        var size = _screenshot.FitInto(available);
        _screenshot.Visible = _screenshot.HasImage;
        _screenshot.Bounds = new Rectangle(new Point(imageLeft, top), size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var width = ClientSize.Width - this.Px(2 * Pad);
        Gfx.Text(e.Graphics, HwinfoRenameHelp.Title, Fonts.CardTitle, new Rectangle(this.Px(Pad + 4), this.Px(TitleTop), width, this.Px(36)), Theme.Ink);
    }

    /// <summary>Places a label at its preferred height for <paramref name="width"/>; returns its bottom.</summary>
    private static int Place(TextLabel label, int left, int top, int width)
    {
        label.Bounds = new Rectangle(left, top, width, label.GetPreferredHeight(width));
        return label.Bottom;
    }
}
