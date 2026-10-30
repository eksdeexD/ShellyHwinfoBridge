using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>A rounded-square check box (22 DIP, radius 5): Accent with a white tick when checked, 1.5 px ring otherwise.</summary>
internal sealed class ThemedCheckBox : ThemedControl
{
    private bool _checked;

    public ThemedCheckBox()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    /// <summary>Raised when <see cref="Checked"/> changes (by the user or in code).</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Gets or sets whether the box is ticked.</summary>
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
            AccessibleDescription = value ? "Checked" : "Not checked";
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = string.IsNullOrEmpty(Text) ? 0 : Gfx.Measure(Text, Fonts.Body).Width + this.Px(14);
        return new Size(this.Px(22) + text + this.Px(2), Math.Max(this.Px(28), Gfx.Measure(Text, Fonts.Body).Height));
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
        var size = this.PxF(22);
        var box = new RectangleF(this.PxF(1), (Height - size) / 2f, size, size);
        var radius = this.PxF(5);
        if (Checked)
        {
            Gfx.FillRounded(g, !Enabled ? Theme.InkDisabled : Hovered ? Theme.AccentHover : Theme.Accent, box, radius);
            Gfx.DrawCheck(g, Theme.OnAccent, RectangleF.Inflate(box, -size * 0.08f, -size * 0.08f), Math.Max(1.5f, this.PxF(2.2f)));
            if (ShowFocus)
            {
                Gfx.DrawRounded(g, Theme.Ink, Math.Max(1f, this.PxF(1)), RectangleF.Inflate(box, this.PxF(2), this.PxF(2)), radius + this.PxF(2));
            }
        }
        else
        {
            var ring = ShowFocus ? Theme.FocusBorder : Hovered && Enabled ? Theme.InkSecondary : Theme.InkDisabled;
            Gfx.DrawRounded(g, ring, Math.Max(1f, this.PxF(1.5f)), box, radius);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var left = (int)Math.Ceiling(box.Right) + this.Px(14);
            Gfx.Text(g, Text, Fonts.Body, new Rectangle(left, 0, Width - left, Height), Enabled ? Theme.Ink : Theme.InkDisabled);
        }
    }
}
