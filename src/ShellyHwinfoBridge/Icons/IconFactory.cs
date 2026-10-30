using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Icons;

/// <summary>
/// Builds the tray icons from the embedded logo: orange (online), grey #5F6973 (offline) and grey with a pause mark
/// (paused). Each icon is a recoloured bitmap turned into an HICON with <see cref="Bitmap.GetHicon"/>; the handles are
/// destroyed when the factory is disposed, so dispose it only after its icons are no longer shown.
/// </summary>
internal sealed class IconFactory : IDisposable
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    private readonly Dictionary<TrayIconState, (Icon Icon, IntPtr Handle)> _cache = [];

    public IconFactory()
        : this(SystemInformation.SmallIconSize.Width)
    {
    }

    public IconFactory(int requestedSize)
    {
        Size = Sizes.FirstOrDefault(s => s >= requestedSize, Sizes[^1]);
    }

    /// <summary>Gets the icon size in pixels.</summary>
    public int Size { get; }

    /// <summary>Returns the (cached) icon for a state. Do not dispose it; the factory owns it.</summary>
    public Icon Get(TrayIconState state)
    {
        if (_cache.TryGetValue(state, out var cached))
        {
            return cached.Icon;
        }

        using var bitmap = Render(state, Size);
        var handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);
        _cache[state] = (icon, handle);
        return icon;
    }

    /// <summary>Renders the glyph for a state at <paramref name="size"/> × <paramref name="size"/> pixels.</summary>
    public static Bitmap Render(TrayIconState state, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var logo = AppAssets.Logo;
        var width = Math.Max(1, (int)Math.Round(size * (double)logo.Width / logo.Height));
        var target = state == TrayIconState.Online ? Theme.Accent : Theme.InkDisabled;
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(Recolour(target));
            g.DrawImage(logo, new Rectangle((size - width) / 2, 0, width, size), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
        }

        if (state == TrayIconState.Paused)
        {
            DrawPauseMark(g, size);
        }

        return bitmap;
    }

    public void Dispose()
    {
        foreach (var (icon, handle) in _cache.Values)
        {
            icon.Dispose();
            NativeMethods.DestroyIconHandle(handle);
        }

        _cache.Clear();
    }

    /// <summary>A colour matrix that keeps alpha and replaces RGB with <paramref name="c"/>.</summary>
    private static ColorMatrix Recolour(Color c) => new(
    [
        [0, 0, 0, 0, 0],
        [0, 0, 0, 0, 0],
        [0, 0, 0, 0, 0],
        [0, 0, 0, 1, 0],
        [c.R / 255f, c.G / 255f, c.B / 255f, 0, 1],
    ]);

    /// <summary>Two Ink bars in the bottom-right quadrant with a 1 px BgBottom outline.</summary>
    private static void DrawPauseMark(Graphics g, int size)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        var barWidth = Math.Max(2, (int)Math.Round(size * 0.14));
        var barHeight = Math.Max(5, (int)Math.Round(size * 0.4));
        var gap = Math.Max(1, (int)Math.Round(size * 0.08));
        var right = size - 1;
        var bottom = size - 1;
        var left = right - ((2 * barWidth) + gap) + 1;
        var top = bottom - barHeight + 1;
        using (var outline = new SolidBrush(Theme.BgBottom))
        {
            g.FillRectangle(outline, left - 1, top - 1, (2 * barWidth) + gap + 2, barHeight + 2);
        }

        using var ink = new SolidBrush(Theme.Ink);
        g.FillRectangle(ink, left, top, barWidth, barHeight);
        g.FillRectangle(ink, left + barWidth + gap, top, barWidth, barHeight);
    }
}
