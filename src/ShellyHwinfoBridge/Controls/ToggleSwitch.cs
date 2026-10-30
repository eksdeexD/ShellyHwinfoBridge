using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>An on/off switch (44 × 24 DIP pill) with an optional label to its right.</summary>
internal sealed class ToggleSwitch : ThemedControl
{
    private bool _checked;

    public ToggleSwitch()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    /// <summary>Raised when <see cref="Checked"/> changes (by the user or in code).</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Gets or sets whether the switch is on.</summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            AccessibleDescription = value ? "On" : "Off";
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = string.IsNullOrEmpty(Text) ? 0 : Gfx.Measure(Text, Fonts.Body).Width + this.Px(16);
        return new Size(this.Px(44) + text + this.Px(2), Math.Max(this.Px(28), Gfx.Measure(Text, Fonts.Body).Height));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AccessibleName = Text;
        base.OnTextChanged(e);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && Enabled)
        {
            Checked = !Checked;
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var w = this.PxF(44);
        var h = this.PxF(24);
        var track = new RectangleF(0, (Height - h) / 2f, w, h);
        var fill = !Enabled ? Theme.Border : Checked ? (Hovered ? Theme.AccentHover : Theme.Accent) : (Hovered ? Theme.HoverBorder : Theme.Border);
        Gfx.FillRounded(g, fill, track, h / 2f);
        if (ShowFocus)
        {
            Gfx.DrawRounded(g, Checked ? Theme.Ink : Theme.FocusBorder, Math.Max(1f, this.PxF(1)), track, h / 2f);
        }

        var inset = this.PxF(3);
        var knob = h - (2 * inset);
        var x = Checked ? track.Right - inset - knob : track.Left + inset;
        var knobColor = !Enabled ? Theme.InkDisabled : Checked ? Theme.OnAccent : Theme.InkSecondary;
        using (var brush = new SolidBrush(knobColor))
        {
            Gfx.Smooth(g);
            g.FillEllipse(brush, x, track.Top + inset, knob, knob);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var left = (int)Math.Ceiling(w) + this.Px(16);
            Gfx.Text(g, Text, Fonts.Body, new Rectangle(left, 0, Width - left, Height), Enabled ? Theme.Ink : Theme.InkDisabled);
        }
    }
}
