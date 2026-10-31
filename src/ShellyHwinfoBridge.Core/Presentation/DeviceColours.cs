using System.Globalization;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>An RGB colour (0–255 per channel).</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct RgbColour(byte R, byte G, byte B)
{
    /// <summary>Formats the colour as <c>#RRGGBB</c> (upper-case hex).</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");
}

/// <summary>
/// Device colours for the energy chart, legend and tooltips: <c>#RRGGBB</c> parsing and formatting, the automatic
/// schemes (computed in HSL keeping the base colour's saturation and lightness: 2 devices complementary, 3 triad,
/// 4 tetrad/square) and the colour of each device.
/// </summary>
public static class DeviceColours
{
    /// <summary>The app's orange (the theme's accent colour); the default base colour and the PC's colour.</summary>
    public const string DefaultBase = "#FF6600";

    /// <summary>Parses <c>#RRGGBB</c> (the leading <c>#</c> is optional, case-insensitive, surrounding whitespace ignored).</summary>
    /// <param name="text">The text.</param>
    /// <param name="colour">The colour.</param>
    public static bool TryParse(string? text, out RgbColour colour)
    {
        colour = default;
        var t = (text ?? string.Empty).Trim();
        if (t.StartsWith('#'))
        {
            t = t[1..];
        }

        if (t.Length != 6 || !int.TryParse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        colour = new RgbColour((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        return true;
    }

    /// <summary>Returns the canonical <c>#RRGGBB</c> form of a colour text, or <c>null</c> when it is not a colour.</summary>
    /// <param name="text">The text.</param>
    public static string? Normalize(string? text) => TryParse(text, out var colour) ? colour.ToString() : null;

    /// <summary>Converts a colour to HSL: hue in degrees [0, 360), saturation and lightness in [0, 1].</summary>
    /// <param name="colour">The colour.</param>
    public static (double H, double S, double L) ToHsl(RgbColour colour)
    {
        var r = colour.R / 255.0;
        var g = colour.G / 255.0;
        var b = colour.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        if (d < 1e-12)
        {
            return (0, 0, l);
        }

        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h * 60 % 360, s, l);
    }

    /// <summary>Converts HSL (hue in degrees, saturation and lightness in [0, 1]) to RGB.</summary>
    /// <param name="h">Hue in degrees (any value; wrapped).</param>
    /// <param name="s">Saturation.</param>
    /// <param name="l">Lightness.</param>
    public static RgbColour FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-12)
        {
            var grey = ToByte(l);
            return new RgbColour(grey, grey, grey);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        return new RgbColour(ToByte(Hue(p, q, h + (1.0 / 3))), ToByte(Hue(p, q, h)), ToByte(Hue(p, q, h - (1.0 / 3))));
    }

    /// <summary>
    /// Returns the automatic scheme for <paramref name="count"/> devices: the base colour first, then the base rotated by
    /// 360° / count (2 = complementary, 3 = triad, 4 = tetrad/square), keeping saturation and lightness.
    /// </summary>
    /// <param name="baseColour">The base colour (<c>#RRGGBB</c>; an invalid text uses <see cref="DefaultBase"/>).</param>
    /// <param name="count">The number of devices (1–<see cref="AppSettings.MaxDevices"/>).</param>
    public static IReadOnlyList<string> Scheme(string? baseColour, int count)
    {
        count = Math.Clamp(count, 1, AppSettings.MaxDevices);
        if (!TryParse(baseColour, out var rgb))
        {
            TryParse(DefaultBase, out rgb);
        }

        var (h, s, l) = ToHsl(rgb);
        var colours = new List<string>(count) { rgb.ToString() };
        for (var i = 1; i < count; i++)
        {
            colours.Add(FromHsl(h + (360.0 * i / count), s, l).ToString());
        }

        return colours;
    }

    /// <summary>
    /// Returns every device's colour in display order (PC first, then the additional devices): in
    /// <see cref="ColourMode.AutomaticScheme"/> the scheme over all devices; in <see cref="ColourMode.Custom"/> each
    /// device's own colour, falling back to its scheme colour when it has none.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>Device id → <c>#RRGGBB</c>.</returns>
    public static IReadOnlyDictionary<string, string> Resolve(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ids = settings.GetDeviceIds();
        var scheme = Scheme(settings.BaseColour, ids.Count);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ids.Count; i++)
        {
            string? custom = null;
            if (settings.ColourMode == ColourMode.Custom)
            {
                custom = i == 0 ? settings.Device.Colour : settings.AdditionalDevices[i - 1].Colour;
            }

            result[ids[i]] = Normalize(custom) ?? scheme[i];
        }

        return result;
    }

    /// <summary>Returns one device's colour (see <see cref="Resolve"/>); unknown ids get the base colour.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="deviceId">The device id.</param>
    public static string ColourOf(AppSettings settings, string deviceId) =>
        Resolve(settings).TryGetValue(deviceId, out var colour) ? colour : Normalize(settings.BaseColour) ?? DefaultBase;

    private static double Hue(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + ((q - p) * 6 * t);
        }

        if (t < 1.0 / 2)
        {
            return q;
        }

        return t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
}
