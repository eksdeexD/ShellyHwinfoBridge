using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// Static text in a theme font and tone: single line with ellipsis, wrapped (<see cref="WordWrap"/>), or an uppercase
/// tracked caption (<see cref="Caption"/>). Paints the backdrop of its parent ("transparent").
/// </summary>
internal sealed class TextLabel : ThemedControl
{
    private FontRole _role = FontRole.Body;
    private UiTone _tone = UiTone.Ink;
    private bool _wordWrap;
    private bool _caption;

    public TextLabel()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>Gets or sets the font role.</summary>
    public FontRole Role
    {
        get => _role;
        set
        {
            _role = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets the colour.</summary>
    public UiTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets whether the text wraps (top-aligned) instead of one line with ellipsis.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            _wordWrap = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets whether the text is drawn as an uppercase tracked caption ("FORMULA PREVIEW").</summary>
    public bool Caption
    {
        get => _caption;
        set
        {
            _caption = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets whether the single line is centred vertically (default) or top-aligned.</summary>
    public bool VerticalCenter { get; set; } = true;

    /// <summary>Returns the height needed for <paramref name="width"/> pixels.</summary>
    public int GetPreferredHeight(int width)
    {
        var font = Fonts.Get(_role);
        if (_caption)
        {
            return Gfx.MeasureTracked("A", font, 0).Height + this.Px(2);
        }

        return _wordWrap ? Gfx.MeasureWrapped(Text, font, Math.Max(1, width)).Height + this.Px(2) : Gfx.Measure("Ag", font).Height + this.Px(2);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var font = Fonts.Get(_role);
        var width = _caption ? Gfx.MeasureTracked(Text.ToUpperInvariant(), font, this.PxF(Theme.CaptionTracking)).Width : Gfx.Measure(Text, font).Width;
        return new Size(width + this.Px(2), GetPreferredHeight(proposedSize.Width > 0 ? proposedSize.Width : width + this.Px(2)));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AccessibleName = Text;
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var font = Fonts.Get(_role);
        var color = Enabled ? Theme.ToneColor(_tone) : Theme.InkDisabled;
        if (_caption)
        {
            var height = Gfx.MeasureTracked("A", font, 0).Height;
            Gfx.DrawTracked(e.Graphics, Text.ToUpperInvariant(), font, new Point(0, VerticalCenter ? Math.Max(0, (Height - height) / 2) : 0), color, this.PxF(Theme.CaptionTracking));
            return;
        }

        var flags = _wordWrap
            ? TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis
            : Gfx.SingleLine | (VerticalCenter ? TextFormatFlags.VerticalCenter : TextFormatFlags.Top);
        Gfx.Text(e.Graphics, Text, font, ClientRectangle, color, flags);
    }
}
