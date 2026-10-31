using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The title row shared by the sub-pages of the main window (Settings, Manage Shellys), so their back commands sit in
/// exactly the same place: the bold back link ("‹ Home", "‹ Settings") at the top left, the page title after it and the
/// footnote "Changes apply immediately." on the title's baseline. Metrics in DIP, page coordinates.
/// </summary>
internal static class PageHeader
{
    /// <summary>The top of the title row.</summary>
    public const float Top = 4;

    /// <summary>The height of the title row.</summary>
    public const float Height = 26;

    /// <summary>Where the first card starts.</summary>
    public const float ContentTop = Top + Height + 6;

    /// <summary>The footnote after the title.</summary>
    public const string Footnote = "Changes apply immediately.";

    private const float TitleGap = 10;
    private const float FootnoteGap = 12;

    /// <summary>Places the back link at the top left; returns the left edge of the footnote (or an apply error) in DIP.</summary>
    public static float Layout(Control page, TextLink back, string title, Point scroll)
    {
        var dpi = page.DeviceDpi;
        var size = back.GetPreferredSize(Size.Empty);
        var top = Theme.Scale(Top, dpi) + ((Theme.Scale(Height, dpi) - size.Height) / 2);
        back.Bounds = new Rectangle(Theme.Scale(Theme.WindowPadding, dpi) + scroll.X, top + scroll.Y, size.Width, size.Height);
        var titleWidth = Gfx.Measure(title, ThemeFonts.For(dpi).CardTitle).Width;
        return (Theme.Scale(Theme.WindowPadding, dpi) + size.Width + Theme.Scale(TitleGap, dpi) + titleWidth + Theme.Scale(FootnoteGap, dpi)) * 96f / dpi;
    }

    /// <summary>Paints the title after the back link and, unless an error replaces it, the footnote.</summary>
    public static void Paint(Graphics g, Control page, TextLink back, string title, float footnoteLeftDip, bool showFootnote, Point scroll)
    {
        var f = ThemeFonts.For(page.DeviceDpi);
        var top = page.Px(Top) + scroll.Y;
        var height = page.Px(Height);
        var left = back.Right + page.Px(TitleGap);
        Gfx.Text(g, title, f.CardTitle, new Rectangle(left, top, Math.Max(0, page.ClientSize.Width - left), height), Theme.Ink);
        if (!showFootnote)
        {
            return;
        }

        // On the title's baseline: CardTitle and Small text share the bottom of a vertically centred line.
        var titleText = Gfx.Measure(title, f.CardTitle).Height;
        var smallText = Gfx.Measure("Ag", f.Small).Height;
        var footnoteTop = top + ((height - titleText) / 2) + titleText - smallText - page.Px(2);
        var footnoteLeft = page.Px(footnoteLeftDip) + scroll.X;
        var footnote = new Rectangle(footnoteLeft, footnoteTop, Math.Max(0, page.ClientSize.Width - footnoteLeft - page.Px(Theme.WindowPadding)), smallText);
        Gfx.Text(g, Footnote, f.Small, footnote, Theme.InkSecondary, Gfx.SingleLine);
    }
}
