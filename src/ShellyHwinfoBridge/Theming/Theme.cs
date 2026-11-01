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

    /// <summary>The default accent (the app's orange, also the colour of the exe's embedded icon).</summary>
    public static readonly Color DefaultAccent = Hex(0xFF6600);
    public static readonly Color Success = Hex(0x5FB36B);
    public static readonly Color Warning = Hex(0xE0B341);
    public static readonly Color Error = Hex(0xE05A4F);
    public static readonly Color Grid = Hex(0x353E48);

    // Derived values (computed, not invented).
    public static readonly Color Field = BgBottom;
    public static readonly Color SecondaryHover = SurfaceElevated;
    public static readonly Color HoverBorder = InkDisabled;

    /// <summary>
    /// Raised on the UI thread after <see cref="SetAccent"/> changed the accent: open windows repaint, and the window and
    /// tray icons are drawn again in the new colour.
    /// </summary>
    public static event EventHandler? AccentChanged;

    /// <summary>
    /// Gets the accent colour: the "Accent colour" from Manage Shellys (settings <c>BaseColour</c>, default
    /// <see cref="DefaultAccent"/>). Text actions, primary buttons, toggles, selections, focus borders, the PC's chart
    /// colour and the runtime-drawn icons use it.
    /// </summary>
    public static Color Accent { get; private set; } = DefaultAccent;

    /// <summary>Gets the text colour on an accent fill: white, or near-black when the accent is light.</summary>
    public static Color OnAccent { get; private set; } = Color.White;

    /// <summary>Gets the accent of a hovered control.</summary>
    public static Color AccentHover { get; private set; } = Blend(DefaultAccent, Color.White, 0.12f);

    /// <summary>Gets the accent of a pressed control.</summary>
    public static Color AccentPressed { get; private set; } = Blend(DefaultAccent, Color.Black, 0.12f);

    /// <summary>Gets the border of a focused control (the accent).</summary>
    public static Color FocusBorder => Accent;

    /// <summary>Sets the accent colour; raises <see cref="AccentChanged"/> when it changed. Call on the UI thread.</summary>
    /// <param name="accent">The new accent (alpha is ignored).</param>
    public static void SetAccent(Color accent)
    {
        accent = Color.FromArgb(255, accent.R, accent.G, accent.B);
        if (accent.ToArgb() == Accent.ToArgb())
        {
            return;
        }

        Accent = accent;
        AccentHover = Blend(accent, Color.White, 0.12f);
        AccentPressed = Blend(accent, Color.Black, 0.12f);
        OnAccent = RelativeLuminance(accent) > 0.5 ? BgBottom : Color.White;
        AccentChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Returns the WCAG relative luminance of a colour (0 black … 1 white).</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    // Metrics in DIP (the compact scale: every window, page and card uses these instead of its own numbers).
    public const int CardRadius = 10;
    public const int ChipRadius = 6;

    /// <summary>Space between a window's edge and its content.</summary>
    public const int WindowPadding = 16;

    /// <summary>Space between cards (and between columns).</summary>
    public const int Gap = 10;

    /// <summary>Space between stacked rows inside a card.</summary>
    public const int RowGap = 8;

    /// <summary>Inset of a card's content from its border.</summary>
    public const int CardPadding = 14;

    /// <summary>Space between a card's title line and its content.</summary>
    public const int TitleGap = 8;

    /// <summary>Height of a button and of a segmented control.</summary>
    public const int ButtonHeight = 28;

    /// <summary>Height of a single-line text field or drop-down.</summary>
    public const int FieldHeight = 30;

    /// <summary>Height of one line of controls (labels, links, switches, check boxes, sliders).</summary>
    public const int LineHeight = 22;

    /// <summary>Width of the main window's client area (fixed).</summary>
    public const int MainWindowWidth = 1040;

    /// <summary>Height of the main window's client area (fixed).</summary>
    public const int MainWindowHeight = 678;

    /// <summary>Letter spacing of uppercase captions, in DIP per character.</summary>
    public const float CaptionTracking = 1.5f;

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
