using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// An embedded screenshot in a 1 px Border frame, scaled to fit with its aspect ratio (lay the control out with
/// <see cref="AspectRatio"/>). Without an image it draws a bordered placeholder text, or nothing when there is none.
/// Owns and disposes the image.
/// </summary>
internal sealed class ScreenshotFrame : ThemedControl
{
    private readonly Image? _image;
    private readonly string? _placeholder;

    /// <summary>Creates the frame.</summary>
    /// <param name="image">The image (owned), or <c>null</c>.</param>
    /// <param name="description">What the screenshot shows (accessible name).</param>
    /// <param name="placeholder">The text shown when <paramref name="image"/> is <c>null</c>, or <c>null</c> to show nothing.</param>
    public ScreenshotFrame(Image? image, string description, string? placeholder = null)
    {
        _image = image;
        _placeholder = placeholder;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = description;
    }

    /// <summary>Gets whether an image was embedded.</summary>
    public bool HasImage => _image is not null;

    /// <summary>Gets the width/height ratio to lay the frame out with (the image's, else 16:9).</summary>
    public float AspectRatio => _image is { Width: > 0, Height: > 0 } image ? image.Width / (float)image.Height : 16f / 9f;

    /// <summary>Returns the largest size with <see cref="AspectRatio"/> inside <paramref name="available"/>.</summary>
    public Size FitInto(Size available)
    {
        var width = Math.Min(available.Width, (int)(available.Height * AspectRatio));
        return new Size(Math.Max(0, width), Math.Max(0, (int)Math.Round(width / AspectRatio)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var line = Math.Max(1, this.Px(1));
        if (_image is not null)
        {
            var state = g.Save();
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            var inner = new Rectangle(line, line, Math.Max(1, Width - (2 * line)), Math.Max(1, Height - (2 * line)));
            var scale = Math.Min(inner.Width / (float)_image.Width, inner.Height / (float)_image.Height);
            var size = new SizeF(_image.Width * scale, _image.Height * scale);
            g.DrawImage(_image, new RectangleF(inner.X + ((inner.Width - size.Width) / 2), inner.Y + ((inner.Height - size.Height) / 2), size.Width, size.Height));
            g.Restore(state);
            using var border = new SolidBrush(Theme.Border);
            g.FillRectangle(border, 0, 0, Width, line);
            g.FillRectangle(border, 0, Height - line, Width, line);
            g.FillRectangle(border, 0, 0, line, Height);
            g.FillRectangle(border, Width - line, 0, line, Height);
            return;
        }

        if (_placeholder is null)
        {
            return;
        }

        Gfx.FillAndBorder(g, Theme.Field, Theme.Border, Math.Max(1f, this.PxF(1)), new RectangleF(0, 0, Width, Height), this.PxF(Theme.ChipRadius));
        Gfx.Text(g, _placeholder, Fonts.Body, new Rectangle(Point.Empty, Size), Theme.InkDisabled, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
