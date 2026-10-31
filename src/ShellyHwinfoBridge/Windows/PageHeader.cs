using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The title row shared by the sub-pages of the main window (Settings, Manage Shellys): the page title at the top left
/// with the footnote "Changes apply immediately." on its baseline. The back link with a chevron ("‹ Home",
/// "‹ Settings") is not part of the page: it sits in the main window's non-scrolling header, on one row with the status
/// pill. Metrics in DIP, page coordinates.
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

    /// <summary>Returns the left edge of the footnote (or an apply error) after the title, in DIP.</summary>
    public static float FootnoteLeft(Control page, string title)
    {
        var dpi = page.DeviceDpi;
        var titleWidth = Gfx.Measure(title, ThemeFonts.For(dpi).PageTitle).Width;
        return (Theme.Scale(Theme.WindowPadding, dpi) + titleWidth + Theme.Scale(FootnoteGap, dpi)) * 96f / dpi;
    }

    /// <summary>Returns the width in DIP left for the footnote or an apply error (up to the page's right padding).</summary>
    public static float FootnoteWidth(Control page, float footnoteLeftDip) =>
        Math.Max(0, (Math.Max(page.ClientSize.Width, page.AutoScrollMinSizeOrZero().Width) * 96f / page.DeviceDpi) - Theme.WindowPadding - footnoteLeftDip);

    /// <summary>Paints the title and, unless an error replaces it, the footnote.</summary>
    public static void Paint(Graphics g, Control page, string title, float footnoteLeftDip, bool showFootnote, Point scroll)
    {
        var f = ThemeFonts.For(page.DeviceDpi);
        var top = page.Px(Top) + scroll.Y;
        var height = page.Px(Height);
        var left = page.Px(Theme.WindowPadding) + scroll.X;
        var right = Math.Max(page.ClientSize.Width, page.AutoScrollMinSizeOrZero().Width) - page.Px(Theme.WindowPadding) + scroll.X;
        Gfx.Text(g, title, f.PageTitle, new Rectangle(left, top, Math.Max(0, right - left), height), Theme.Ink);
        if (!showFootnote)
        {
            return;
        }

        // On the title's baseline: the title and Small text share the bottom of a vertically centred line.
        var titleText = Gfx.Measure(title, f.PageTitle).Height;
        var smallText = Gfx.Measure("Ag", f.Small).Height;
        var footnoteTop = top + ((height - titleText) / 2) + titleText - smallText - page.Px(3);
        var footnoteLeft = page.Px(footnoteLeftDip) + scroll.X;
        var footnote = new Rectangle(footnoteLeft, footnoteTop, Math.Max(0, right - footnoteLeft), smallText);
        Gfx.Text(g, Footnote, f.Small, footnote, Theme.InkSecondary, Gfx.SingleLine);
    }

    private static Size AutoScrollMinSizeOrZero(this Control page) => page is ScrollableControl s ? s.AutoScrollMinSize : Size.Empty;
}
