namespace ShellyHwinfoBridge.Theming;

/// <summary>Embedded application images.</summary>
internal static class AppAssets
{
    private static readonly Lazy<Image> LogoImage = new(() => Load("ShellyHwinfoBridge.logo.png")
        ?? throw new InvalidOperationException("The embedded logo is missing."));

    /// <summary>Gets the orange plug-and-bolt glyph (transparent PNG, 324×748). Shared; do not dispose.</summary>
    public static Image Logo => LogoImage.Value;

    /// <summary>Loads the optional HWiNFO shared-memory screenshot, or <c>null</c> when it was not embedded. The caller owns the image.</summary>
    public static Image? TryLoadHwinfoScreenshot() => Load("ShellyHwinfoBridge.hwinfo-shared-memory.png");

    /// <summary>Draws the logo fitted to <paramref name="height"/> pixels at <paramref name="location"/>; returns the drawn width.</summary>
    public static int DrawLogo(Graphics g, Point location, int height)
    {
        var logo = Logo;
        var width = (int)Math.Round(height * (double)logo.Width / logo.Height);
        var state = g.Save();
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(logo, new Rectangle(location.X, location.Y, width, height));
        g.Restore(state);
        return width;
    }

    private static Image? Load(string name)
    {
        using var stream = typeof(AppAssets).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        // Copy so the image does not depend on the (disposed) resource stream.
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
