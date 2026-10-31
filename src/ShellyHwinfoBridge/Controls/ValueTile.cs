using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>A small dark tile with a bold value and a tiny caption under it (SurfaceElevated, radius 8).</summary>
internal sealed class ValueTile : ThemedControl
{
    private string _caption = string.Empty;
    private string _value = string.Empty;

    public ValueTile()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>Gets or sets the caption ("Power"), drawn tracked uppercase.</summary>
    public string Caption
    {
        get => _caption;
        set
        {
            if (_caption != (value ?? string.Empty))
            {
                _caption = value ?? string.Empty;
                AccessibleName = _caption;
                Invalidate();
            }
        }
    }

    /// <summary>Gets or sets the value ("312.4 W", or "—").</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (_value != (value ?? string.Empty))
            {
                _value = value ?? string.Empty;
                AccessibleDescription = _value;
                Invalidate();
            }
        }
    }

    /// <summary>Gets or sets the value colour.</summary>
    public UiTone ValueTone { get; set; } = UiTone.Ink;

    /// <summary>Preferred size: 110 × 50 DIP.</summary>
    public override Size GetPreferredSize(Size proposedSize) => new(this.Px(110), this.Px(50));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.FillRounded(g, Theme.SurfaceElevated, new RectangleF(0, 0, Width, Height), this.PxF(Theme.ChipRadius));
        var f = Fonts;
        var pad = this.Px(10);
        var valueHeight = Gfx.Measure("0", f.CardTitle).Height;
        var captionHeight = Gfx.MeasureTracked("A", f.Caption, 0).Height;
        var gap = this.Px(3);
        var y = (Height - valueHeight - gap - captionHeight) / 2;
        Gfx.Text(g, _value, f.CardTitle, new Rectangle(pad, y, Width - (2 * pad), valueHeight), Theme.ToneColor(ValueTone));
        Gfx.DrawTracked(g, _caption, f.Caption, new Point(pad, y + valueHeight + gap), Theme.InkSecondary, this.PxF(Theme.CaptionTracking));
    }
}
