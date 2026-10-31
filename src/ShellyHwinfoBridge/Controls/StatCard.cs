using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A live-value card: tracked uppercase caption (InkSecondary), large value (Ink, or Accent for the DC card) and a
/// secondary line. In placeholder state the hint text replaces the value and sub-line. In a narrow card (five cards in a
/// row) the caption drops its letter spacing and the value uses the smaller totals font, so nothing is clipped.
/// The owner may place a child (the Other card's "How to enable?" button) in the bottom
/// <see cref="ReservedBottomDip"/> DIP, which the hint text leaves free.
/// </summary>
internal sealed class StatCard : CardPanel
{
    private string _caption = string.Empty;
    private string _value = string.Empty;
    private string _sub = string.Empty;
    private UiTone _valueTone = UiTone.Ink;
    private bool _placeholder;
    private int _reservedBottomDip;
    private bool _compact;

    public StatCard()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    public string Caption => _caption;

    public string Value => _value;

    public string Sub => _sub;

    public UiTone ValueTone => _valueTone;

    public bool Placeholder => _placeholder;

    /// <summary>Gets or sets the height in DIP kept free at the bottom of a placeholder card (for a button); 0 for none.</summary>
    public int ReservedBottomDip
    {
        get => _reservedBottomDip;
        set
        {
            if (_reservedBottomDip != value)
            {
                _reservedBottomDip = Math.Max(0, value);
                Invalidate();
            }
        }
    }

    /// <summary>Gets the left text inset in device pixels (<see cref="Theme.CardPadding"/>, 10 DIP in a narrow card).</summary>
    public int TextLeft => this.Px(IsNarrow ? 10 : Theme.CardPadding);

    private bool IsNarrow => ClientSize.Width < this.Px(140);

    private int TextWidth => ClientSize.Width - TextLeft - this.Px(IsNarrow ? 6 : 10);

    /// <summary>
    /// Gets or sets whether the card draws compactly (caption without letter spacing, value in the smaller totals font).
    /// The owner sets it on every card of a row at once, so the row stays consistent (see <see cref="FitsFullSize"/>).
    /// </summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (_compact != value)
            {
                _compact = value;
                Invalidate();
            }
        }
    }

    /// <summary>Gets whether the value fits the card's width in the large font (a caption only loses its letter spacing).</summary>
    public bool FitsFullSize => _placeholder || Gfx.Measure(_value, Fonts.StatValue).Width <= TextWidth;

    /// <summary>Sets all fields; repaints only when something changed.</summary>
    /// <param name="caption">Caption, drawn uppercase ("PC Power (AC)").</param>
    /// <param name="value">Value with unit ("312.4 W"); in placeholder state the hint text.</param>
    /// <param name="sub">Secondary line ("from Shelly plug").</param>
    /// <param name="valueTone">Value colour (Ink, or Accent for DC).</param>
    /// <param name="placeholder">Whether <paramref name="value"/> is a hint instead of a number.</param>
    public void Apply(string caption, string value, string sub, UiTone valueTone, bool placeholder)
    {
        caption ??= string.Empty;
        value ??= string.Empty;
        sub ??= string.Empty;
        if (caption == _caption && value == _value && sub == _sub && valueTone == _valueTone && placeholder == _placeholder)
        {
            return;
        }

        _caption = caption;
        _value = value;
        _sub = sub;
        _valueTone = valueTone;
        _placeholder = placeholder;
        AccessibleName = caption;
        AccessibleDescription = placeholder || sub.Length == 0 ? value : value + ", " + sub;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var f = Fonts;
        var left = TextLeft;
        var width = TextWidth;

        // Compact (or a single card that does not fit): no letter spacing and the smaller value font.
        var tracking = _compact ? 0 : this.PxF(Theme.CaptionTracking);
        var captionSize = Gfx.MeasureTracked(_caption, f.Caption, tracking);
        if (captionSize.Width > width)
        {
            tracking = 0;
            captionSize = Gfx.MeasureTracked(_caption, f.Caption, tracking);
        }

        var valueFont = _compact || Gfx.Measure(_value, f.StatValue).Width > width ? f.TotalValue : f.StatValue;
        var valueHeight = Gfx.Measure("0", valueFont).Height;
        var subHeight = Gfx.Measure("Ag", f.Body).Height;
        var gap1 = this.Px(3);
        var gap2 = this.Px(2);
        var total = captionSize.Height + gap1 + valueHeight + gap2 + subHeight;
        var y = Math.Max(this.Px(10), (ClientSize.Height - total) / 2);
        if (_placeholder && _reservedBottomDip > 0)
        {
            y = this.Px(10);
        }

        if (captionSize.Width > width)
        {
            // Still too wide: one line with an ellipsis instead of overflowing the card.
            Gfx.Text(g, _caption.ToUpperInvariant(), f.Caption, new Rectangle(left, y, width, captionSize.Height), Theme.InkSecondary, Gfx.SingleLine);
        }
        else
        {
            Gfx.DrawTracked(g, _caption, f.Caption, new Point(left, y), Theme.InkSecondary, tracking);
        }

        y += captionSize.Height + gap1;

        if (_placeholder)
        {
            var bottom = ClientSize.Height - this.Px(6) - this.Px(_reservedBottomDip);
            var area = new Rectangle(left, y, width, Math.Max(0, bottom - y));
            Gfx.Text(g, _value, f.Body, area, Theme.InkSecondary, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            return;
        }

        Gfx.Text(g, _value, valueFont, new Rectangle(left - this.Px(1), y, width, valueHeight), Theme.ToneColor(_valueTone));
        y += valueHeight + gap2;
        Gfx.Text(g, _sub, f.Body, new Rectangle(left, y, width, subHeight), Theme.InkSecondary);
    }
}
