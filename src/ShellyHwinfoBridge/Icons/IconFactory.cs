using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Icons;

/// <summary>
/// Builds the tray icons from the embedded logo: the accent colour (online), grey #5F6973 (offline) and grey with a pause mark
/// (paused). Each icon is a recoloured bitmap turned into an HICON with <see cref="Bitmap.GetHicon"/>; the handles are
/// destroyed when the factory is disposed, so dispose it only after its icons are no longer shown.
/// </summary>
internal sealed class IconFactory : IDisposable
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    private readonly Dictionary<TrayIconState, (Icon Icon, IntPtr Handle)> _cache = [];

    /// <summary>The frame sizes of the window icon (title bar, taskbar, Alt+Tab).</summary>
    private static readonly int[] WindowIconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    private static Icon? _windowIcon;
    private static int _windowIconArgb;

    /// <summary>
    /// Gets the window icon in the current accent (<see cref="Theme.Accent"/>), built at runtime as a multi-size icon
    /// and rebuilt when the accent changed. Shared by every window; do not dispose it.
    /// </summary>
    public static Icon WindowIcon
    {
        get
        {
            var argb = Theme.Accent.ToArgb();
            if (_windowIcon is null || _windowIconArgb != argb)
            {
                // The previous icon may still be assigned to a window that has not switched yet: leave it to the GC.
                _windowIcon = BuildWindowIcon();
                _windowIconArgb = argb;
            }

            return _windowIcon;
        }
    }


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

    /// <summary>Builds an .ico (PNG frames, one per <see cref="WindowIconSizes"/>) of the online glyph in the accent.</summary>
    private static Icon BuildWindowIcon()
    {
        var frames = new List<byte[]>(WindowIconSizes.Length);
        foreach (var size in WindowIconSizes)
        {
            using var bitmap = Render(TrayIconState.Online, size);
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            frames.Add(png.ToArray());
        }

        using var ico = new MemoryStream();
        using (var w = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)frames.Count);
            var offset = 6 + (16 * frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                var size = WindowIconSizes[i];
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }

            foreach (var frame in frames)
            {
                w.Write(frame);
            }
        }

        ico.Position = 0;
        return new Icon(ico);
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
