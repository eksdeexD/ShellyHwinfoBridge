namespace ShellyHwinfoBridge.Theming;

/// <summary>
/// Colour tokens and metrics from the design brief. Metrics are in device-independent pixels (DIP, 96 dpi);
/// convert with <see cref="Px(Control, float)"/> at layout/paint time.
/// </summary>
internal static class Theme
{
    // Brief tokens (exact hex).
    public static readonly Color BgTop = Hex(0x2B323A);
    public static readonly Color BgBottom = Hex(0x171B20);
    public static readonly Color Surface = Hex(0x232A31);
    public static readonly Color SurfaceElevated = Hex(0x2C343D);
    public static readonly Color Border = Hex(0x353E48);
    public static readonly Color Ink = Hex(0xECEFF3);
    public static readonly Color InkSecondary = Hex(0x9AA5B1);
    public static readonly Color InkDisabled = Hex(0x5F6973);
    public static readonly Color Accent = Hex(0xFF6600);
    public static readonly Color OnAccent = Hex(0xFFFFFF);
    public static readonly Color Success = Hex(0x5FB36B);
    public static readonly Color Warning = Hex(0xE0B341);
    public static readonly Color Error = Hex(0xE05A4F);
    public static readonly Color Grid = Hex(0x353E48);

    // Derived values (computed, not invented).
    public static readonly Color Field = BgBottom;
    public static readonly Color AccentHover = Blend(Accent, Color.White, 0.12f);
    public static readonly Color AccentPressed = Blend(Accent, Color.Black, 0.12f);
    public static readonly Color SecondaryHover = SurfaceElevated;
    public static readonly Color FocusBorder = Accent;
    public static readonly Color HoverBorder = InkDisabled;

    // Metrics in DIP.
    public const int CardRadius = 12;
    public const int ChipRadius = 8;
    public const int WindowPadding = 24;
    public const int Gap = 16;
    public const int CardPadding = 20;

    /// <summary>Converts DIP to device pixels for a DPI.</summary>
    public static int Scale(float dip, int dpi) => (int)Math.Round(dip * dpi / 96f, MidpointRounding.AwayFromZero);

    /// <summary>Converts DIP to fractional device pixels for a DPI.</summary>
    public static float ScaleF(float dip, int dpi) => dip * dpi / 96f;

    /// <summary>Converts DIP to device pixels using the control's current DPI.</summary>
    public static int Px(this Control control, float dip) => Scale(dip, control.DeviceDpi);

    /// <summary>Converts DIP to fractional device pixels using the control's current DPI.</summary>
    public static float PxF(this Control control, float dip) => ScaleF(dip, control.DeviceDpi);

    /// <summary>Linear blend of two colours; <paramref name="amount"/> 0 = <paramref name="from"/>, 1 = <paramref name="to"/>.</summary>
    public static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + ((to.A - from.A) * amount)),
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    /// <summary>Returns the colour for a tone.</summary>
    public static Color ToneColor(UiTone tone) => tone switch
    {
        UiTone.Accent => Accent,
        UiTone.Success => Success,
        UiTone.Warning => Warning,
        UiTone.Error => Error,
        UiTone.Secondary => InkSecondary,
        UiTone.Disabled => InkDisabled,
        _ => Ink,
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}

/// <summary>
/// UI-side colour role. Agent B's Core <c>Tone</c> (Presentation) maps onto this one-to-one by name.
/// </summary>
internal enum UiTone
{
    Ink,
    Secondary,
    Disabled,
    Accent,
    Success,
    Warning,
    Error,
}
