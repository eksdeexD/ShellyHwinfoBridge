using System.Drawing.Drawing2D;

namespace ShellyHwinfoBridge.Theming;

/// <summary>A container that paints what lies behind its children ("fake transparency").</summary>
internal interface IThemedSurface
{
    /// <summary>Paints this surface's background for <paramref name="area"/>, given in this container's client coordinates.</summary>
    void PaintSurface(Graphics g, Rectangle area);
}

/// <summary>Drawing helpers: rounded shapes (GDI+ antialiased) and GDI text (TextRenderer).</summary>
internal static class Gfx
{
    /// <summary>Default flags for single-line text.</summary>
    public const TextFormatFlags SingleLine = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    /// <summary>Builds a rounded rectangle path; a radius of at least half the height gives a pill.</summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
        if (radius < 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Switches a graphics object to antialiased shapes.</summary>
    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    /// <summary>Fills a rounded rectangle.</summary>
    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        Smooth(g);
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>Draws a rounded outline of <paramref name="width"/> pixels fully inside <paramref name="r"/>.</summary>
    public static void DrawRounded(Graphics g, Color color, float width, RectangleF r, float radius)
    {
        Smooth(g);
        var inset = width / 2f;
        var inner = RectangleF.Inflate(r, -inset, -inset);
        using var path = RoundedRect(inner, Math.Max(0, radius - inset));
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Fills a rounded rectangle and draws its border inside it.</summary>
    public static void FillAndBorder(Graphics g, Color fill, Color border, float borderWidth, RectangleF r, float radius)
    {
        FillRounded(g, fill, r, radius);
        if (borderWidth > 0)
        {
            DrawRounded(g, border, borderWidth, r, radius);
        }
    }

    /// <summary>Draws text with GDI.</summary>
    public static void Text(Graphics g, string? text, Font font, Rectangle bounds, Color color, TextFormatFlags flags = SingleLine | TextFormatFlags.VerticalCenter)
    {
        if (!string.IsNullOrEmpty(text))
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags);
        }
    }

    /// <summary>Measures single-line text without padding.</summary>
    public static Size Measure(string? text, Font font, TextFormatFlags flags = SingleLine) =>
        string.IsNullOrEmpty(text) ? new Size(0, TextRenderer.MeasureText("Ag", font, new Size(int.MaxValue, int.MaxValue), flags).Height)
            : TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), flags);

    /// <summary>Measures wrapped text for a width.</summary>
    public static Size MeasureWrapped(string? text, Font font, int width) =>
        TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? " " : text, font, new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

    /// <summary>Draws uppercase letter-spaced text (captions) starting at <paramref name="origin"/>; returns the drawn width.</summary>
    public static int DrawTracked(Graphics g, string? text, Font font, Point origin, Color color, float trackingPx)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var x = (float)origin.X;
        foreach (var ch in text.ToUpperInvariant())
        {
            var s = ch.ToString();
            var size = TextRenderer.MeasureText(g, s, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (!char.IsWhiteSpace(ch))
            {
                TextRenderer.DrawText(g, s, font, new Point((int)Math.Round(x), origin.Y), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            x += size.Width + trackingPx;
        }

        return (int)Math.Round(x - origin.X - trackingPx);
    }

    /// <summary>Measures text drawn by <see cref="DrawTracked"/>.</summary>
    public static Size MeasureTracked(string? text, Font font, float trackingPx)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new Size(0, Measure("A", font).Height);
        }

        var width = 0f;
        var height = 0;
        foreach (var ch in text.ToUpperInvariant())
        {
            var size = TextRenderer.MeasureText(ch.ToString(), font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            width += size.Width + trackingPx;
            height = Math.Max(height, size.Height);
        }

        return new Size((int)Math.Round(width - trackingPx), height);
    }

    /// <summary>Draws a check mark inside <paramref name="box"/>.</summary>
    public static void DrawCheck(Graphics g, Color color, RectangleF box, float width)
    {
        Smooth(g);
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(box.Left + (box.Width * 0.22f), box.Top + (box.Height * 0.52f)),
            new PointF(box.Left + (box.Width * 0.42f), box.Top + (box.Height * 0.72f)),
            new PointF(box.Left + (box.Width * 0.78f), box.Top + (box.Height * 0.30f)),
        });
    }

    /// <summary>Draws a downward chevron centred in <paramref name="box"/>.</summary>
    public static void DrawChevronDown(Graphics g, Color color, RectangleF box, float width)
    {
        Smooth(g);
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var cx = box.Left + (box.Width / 2f);
        var cy = box.Top + (box.Height / 2f);
        var w = box.Width / 2f;
        var h = w / 2f;
        g.DrawLines(pen, new[] { new PointF(cx - w, cy - (h / 2)), new PointF(cx, cy + (h / 2)), new PointF(cx + w, cy - (h / 2)) });
    }

    /// <summary>
    /// Paints what lies behind <paramref name="control"/>: the nearest <see cref="IThemedSurface"/> ancestor's
    /// background, or the first ancestor with an opaque BackColor.
    /// </summary>
    public static void PaintBackdrop(Control control, Graphics g)
    {
        var offset = control.Location;
        var parent = control.Parent;
        while (parent is not null)
        {
            if (parent is IThemedSurface surface)
            {
                var state = g.Save();
                g.TranslateTransform(-offset.X, -offset.Y);
                surface.PaintSurface(g, new Rectangle(offset, control.Size));
                g.Restore(state);
                return;
            }

            if (parent.BackColor.A == 255)
            {
                g.Clear(parent.BackColor);
                return;
            }

            if (parent is Form)
            {
                break;
            }

            offset.Offset(parent.Left, parent.Top);
            parent = parent.Parent;
        }

        g.Clear(Theme.BgBottom);
    }
}
