using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The title row shared by the sub-pages of the main window (Settings, Manage Shellys), so their back commands sit in
/// exactly the same place: the page title at the top left with the footnote "Changes apply immediately." on its
/// baseline, and the bold back link with a chevron ("‹ Home", "‹ Settings") right-aligned at the top of the page.
/// Metrics in DIP, page coordinates.
/// </summary>
internal static class PageHeader
{
    /// <summary>The top of the title row.</summary>
    public const float Top = 2;

    /// <summary>The height of the title row.</summary>
    public const float Height = 30;

    /// <summary>Where the first card starts.</summary>
    public const float ContentTop = Top + Height + 8;

    /// <summary>The footnote after the title.</summary>
    public const string Footnote = "Changes apply immediately.";

    private const float FootnoteGap = 12;

    /// <summary>
    /// Places the back link right-aligned at the top; returns the left edge of the footnote (or an apply error) in DIP.
    /// </summary>
    public static float Layout(Control page, TextLink back, string title, Point scroll)
    {
        var dpi = page.DeviceDpi;
        var size = back.GetPreferredSize(Size.Empty);
        var top = Theme.Scale(Top, dpi) + ((Theme.Scale(Height, dpi) - size.Height) / 2);
        var right = Math.Max(page.ClientSize.Width, page.AutoScrollMinSizeOrZero().Width) - Theme.Scale(Theme.WindowPadding, dpi);
        back.Bounds = new Rectangle(right - size.Width + scroll.X, top + scroll.Y, size.Width, size.Height);
        var titleWidth = Gfx.Measure(title, ThemeFonts.For(dpi).PageTitle).Width;
        return (Theme.Scale(Theme.WindowPadding, dpi) + titleWidth + Theme.Scale(FootnoteGap, dpi)) * 96f / dpi;
    }

    /// <summary>Returns the width in DIP left for the footnote or an apply error (up to the back link).</summary>
    public static float FootnoteWidth(Control page, TextLink back, float footnoteLeftDip) =>
        Math.Max(0, ((back.Left - page.AutoScrollPositionOrZero().X) * 96f / page.DeviceDpi) - 12 - footnoteLeftDip);

    /// <summary>Paints the title and, unless an error replaces it, the footnote.</summary>
    public static void Paint(Graphics g, Control page, TextLink back, string title, float footnoteLeftDip, bool showFootnote, Point scroll)
    {
        var f = ThemeFonts.For(page.DeviceDpi);
        var top = page.Px(Top) + scroll.Y;
        var height = page.Px(Height);
        var left = page.Px(Theme.WindowPadding) + scroll.X;
        Gfx.Text(g, title, f.PageTitle, new Rectangle(left, top, Math.Max(0, back.Left - left), height), Theme.Ink);
        if (!showFootnote)
        {
            return;
        }

        // On the title's baseline: the title and Small text share the bottom of a vertically centred line.
        var titleText = Gfx.Measure(title, f.PageTitle).Height;
        var smallText = Gfx.Measure("Ag", f.Small).Height;
        var footnoteTop = top + ((height - titleText) / 2) + titleText - smallText - page.Px(3);
        var footnoteLeft = page.Px(footnoteLeftDip) + scroll.X;
        var footnote = new Rectangle(footnoteLeft, footnoteTop, Math.Max(0, back.Left - page.Px(12) - footnoteLeft), smallText);
        Gfx.Text(g, Footnote, f.Small, footnote, Theme.InkSecondary, Gfx.SingleLine);
    }

    private static Size AutoScrollMinSizeOrZero(this Control page) => page is ScrollableControl s ? s.AutoScrollMinSize : Size.Empty;

    private static Point AutoScrollPositionOrZero(this Control page) => page is ScrollableControl s ? s.AutoScrollPosition : Point.Empty;
}
