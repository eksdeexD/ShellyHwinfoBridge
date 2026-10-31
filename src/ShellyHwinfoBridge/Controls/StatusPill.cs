using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>The connection status pill: 1 px Border, coloured dot (Success/Error/InkSecondary) and Body text.</summary>
internal sealed class StatusPill : ThemedControl
{
    private UiTone _dot = UiTone.Secondary;

    public StatusPill()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    public UiTone Dot => _dot;

    /// <summary>Sets the dot colour and text; repaints only when something changed.</summary>
    public void Apply(UiTone dot, string text)
    {
        text ??= string.Empty;
        if (dot == _dot && text == Text)
        {
            return;
        }

        _dot = dot;
        Text = text;
        AccessibleName = text;
        Invalidate();
    }

    /// <summary>Preferred size: dot, text and padding; 28 DIP high.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = Gfx.Measure(Text, Fonts.Body);
        return new Size(this.Px(12 + 8 + 8 + 14) + text.Width, this.Px(28));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        Gfx.DrawRounded(g, Theme.Border, Math.Max(1f, this.PxF(1)), r, Height / 2f);
        var dot = this.PxF(8);
        var x = this.PxF(12);
        Gfx.Smooth(g);
        using (var brush = new SolidBrush(Theme.ToneColor(_dot)))
        {
            g.FillEllipse(brush, x, (Height - dot) / 2f, dot, dot);
        }

        var textLeft = (int)(x + dot + this.PxF(8));
        Gfx.Text(g, Text, Fonts.Body, new Rectangle(textLeft, 0, Width - textLeft - this.Px(10), Height), Theme.Ink);
    }
}
