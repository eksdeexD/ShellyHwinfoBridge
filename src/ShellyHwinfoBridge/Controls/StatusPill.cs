using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// The status pill: 1 px Border, coloured dot (Success / Warning / Error / InkSecondary) and Body text. When
/// <see cref="Clickable"/> it is a command (Enter or Space as well as a click).
/// </summary>
internal sealed class StatusPill : ThemedControl
{
    private UiTone _dot = UiTone.Secondary;
    private bool _clickable;

    public StatusPill()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    public UiTone Dot => _dot;

    /// <summary>Gets or sets whether the pill is a command (hand cursor, hover border, focusable; Click opens Manage Shellys).</summary>
    public bool Clickable
    {
        get => _clickable;
        set
        {
            _clickable = value;
            TabStop = value;
            Cursor = value ? Cursors.Hand : Cursors.Default;
            AccessibleRole = value ? AccessibleRole.PushButton : AccessibleRole.StaticText;
            Invalidate();
        }
    }

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

    protected override bool IsInputKey(Keys keyData) => (_clickable && keyData is Keys.Enter or Keys.Space) || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_clickable && e.KeyCode is Keys.Space or Keys.Enter && Enabled)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
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
        var border = _clickable && ShowFocus ? Theme.FocusBorder : _clickable && Hovered && Enabled ? Theme.HoverBorder : Theme.Border;
        Gfx.DrawRounded(g, border, Math.Max(1f, this.PxF(1)), RectangleF.Inflate(r, -0.5f, -0.5f), Height / 2f);
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
