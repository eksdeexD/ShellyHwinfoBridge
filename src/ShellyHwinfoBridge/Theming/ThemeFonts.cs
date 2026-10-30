using System.Collections.Concurrent;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ShellyHwinfoBridge.Theming;

/// <summary>Text roles of the type scale.</summary>
internal enum FontRole
{
    /// <summary>Window title ("ShellyHwinfoBridge", "Setup wizard", "Settings"): 20 pt semibold.</summary>
    AppTitle,

    /// <summary>Wizard page title: 24 pt semibold.</summary>
    PageTitle,

    /// <summary>Wizard page subtitle: 12 pt.</summary>
    Subtitle,

    /// <summary>Card title ("Energy", "Shelly device"): 15 pt semibold.</summary>
    CardTitle,

    /// <summary>Large stat value ("312.4 W"): 26 pt semibold.</summary>
    StatValue,

    /// <summary>Energy totals ("2.41 kWh"): 20 pt semibold.</summary>
    TotalValue,

    /// <summary>Body text: 11 pt.</summary>
    Body,

    /// <summary>Body text, semibold (selected segment, primary button, list titles): 11 pt.</summary>
    BodySemibold,

    /// <summary>Small text (footnotes, chart labels): 10 pt.</summary>
    Small,

    /// <summary>Uppercase tracked caption ("AC POWER (WALL)"): 8.5 pt.</summary>
    Caption,

    /// <summary>Monospace (formula): 11 pt.</summary>
    Mono,
}

/// <summary>
/// Fonts for one DPI, created in pixel units so GDI text (TextRenderer) renders at the right size on every monitor.
/// Instances are cached forever per DPI and must not be disposed by callers.
/// </summary>
internal sealed class ThemeFontSet
{
    private readonly Font[] _fonts;

    internal ThemeFontSet(int dpi)
    {
        Dpi = dpi;
        var roles = Enum.GetValues<FontRole>();
        _fonts = new Font[roles.Length];
        foreach (var role in roles)
        {
            var (family, points, style) = ThemeFonts.Spec(role);
            _fonts[(int)role] = new Font(family, points * dpi / 72f, style, GraphicsUnit.Pixel);
        }
    }

    public int Dpi { get; }

    public Font AppTitle => Get(FontRole.AppTitle);

    public Font PageTitle => Get(FontRole.PageTitle);

    public Font Subtitle => Get(FontRole.Subtitle);

    public Font CardTitle => Get(FontRole.CardTitle);

    public Font StatValue => Get(FontRole.StatValue);

    public Font TotalValue => Get(FontRole.TotalValue);

    public Font Body => Get(FontRole.Body);

    public Font BodySemibold => Get(FontRole.BodySemibold);

    public Font Small => Get(FontRole.Small);

    public Font Caption => Get(FontRole.Caption);

    public Font Mono => Get(FontRole.Mono);

    public Font Get(FontRole role) => _fonts[(int)role];
}

/// <summary>Chooses the font families once and hands out per-DPI <see cref="ThemeFontSet"/>s.</summary>
internal static class ThemeFonts
{
    private static readonly ConcurrentDictionary<int, ThemeFontSet> Cache = new();
    private static readonly Lazy<(string Regular, string Semibold, string Display, string Mono)> Families = new(PickFamilies);

    /// <summary>Gets the regular text family.</summary>
    public static string RegularFamily => Families.Value.Regular;

    /// <summary>Gets the semibold text family.</summary>
    public static string SemiboldFamily => Families.Value.Semibold;

    /// <summary>Gets the semibold display family (titles and large numbers).</summary>
    public static string DisplayFamily => Families.Value.Display;

    /// <summary>Gets the monospace family.</summary>
    public static string MonoFamily => Families.Value.Mono;

    /// <summary>Returns the fonts for a DPI (cached).</summary>
    public static ThemeFontSet For(int dpi) => Cache.GetOrAdd(dpi <= 0 ? 96 : dpi, d => new ThemeFontSet(d));

    /// <summary>Returns the fonts for a control's current DPI.</summary>
    public static ThemeFontSet For(Control control) => For(control.DeviceDpi);

    /// <summary>The application default font (Body, in points so WinForms scales it per monitor).</summary>
    public static Font CreateApplicationDefault() => new(RegularFamily, 11f, FontStyle.Regular, GraphicsUnit.Point);

    internal static (string Family, float Points, FontStyle Style) Spec(FontRole role) => role switch
    {
        FontRole.AppTitle => (DisplayFamily, 20f, FontStyle.Regular),
        FontRole.PageTitle => (DisplayFamily, 24f, FontStyle.Regular),
        FontRole.Subtitle => (RegularFamily, 12f, FontStyle.Regular),
        FontRole.CardTitle => (DisplayFamily, 15f, FontStyle.Regular),
        FontRole.StatValue => (DisplayFamily, 26f, FontStyle.Regular),
        FontRole.TotalValue => (DisplayFamily, 20f, FontStyle.Regular),
        FontRole.Body => (RegularFamily, 11f, FontStyle.Regular),
        FontRole.BodySemibold => (SemiboldFamily, 11f, FontStyle.Regular),
        FontRole.Small => (RegularFamily, 10f, FontStyle.Regular),
        FontRole.Caption => (RegularFamily, 8.5f, FontStyle.Regular),
        FontRole.Mono => (MonoFamily, 11f, FontStyle.Regular),
        _ => (RegularFamily, 11f, FontStyle.Regular),
    };

    private static (string, string, string, string) PickFamilies()
    {
        HashSet<string> installed;
        try
        {
            using var collection = new InstalledFontCollection();
            installed = collection.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            installed = [];
        }

        string First(params string[] candidates) => candidates.FirstOrDefault(installed.Contains) ?? candidates[^1];

        var regular = First("Segoe UI Variable Text", "Segoe UI");
        var semibold = First("Segoe UI Variable Text Semibold", "Segoe UI Variable Text Semib", "Segoe UI Semibold");
        var display = First("Segoe UI Variable Display Semibold", "Segoe UI Variable Display Semib", "Segoe UI Semibold");
        var mono = First("Cascadia Mono", "Consolas");
        return (regular, semibold, display, mono);
    }
}
