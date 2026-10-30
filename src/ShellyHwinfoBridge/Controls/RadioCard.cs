using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A selectable card with a radio ring (wizard PSU modes): title, one-line description, Accent border and dot when
/// checked. Cards with the same <see cref="Group"/> and parent are mutually exclusive.
/// </summary>
internal sealed class RadioCard : CardPanel
{
    private string _description = string.Empty;
    private bool _checked;
    private bool _hovered;

    public RadioCard()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.RadioButton;
    }

    /// <summary>Raised when <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Gets or sets the description under the title.</summary>
    public string Description
    {
        get => _description;
        set
        {
            _description = value ?? string.Empty;
            AccessibleDescription = _description;
            Invalidate();
        }
    }

    /// <summary>Gets or sets the exclusivity group (cards with the same parent and group).</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this card is the selected option; checking it unchecks the rest of the group.</summary>
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
            Selected = value;
            if (value && Parent is not null)
            {
                foreach (var other in Parent.Controls.OfType<RadioCard>())
                {
                    if (!ReferenceEquals(other, this) && other.Group == Group)
                    {
                        other.Checked = false;
                    }
                }
            }

            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = true;
        base.OnClick(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            Checked = true;
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down && Parent is not null)
        {
            var group = Parent.Controls.OfType<RadioCard>().Where(c => c.Group == Group && c.Enabled && c.Visible).OrderBy(c => c.Left).ThenBy(c => c.Top).ToList();
            var index = group.IndexOf(this) + (e.KeyCode is Keys.Left or Keys.Up ? -1 : 1);
            if (index >= 0 && index < group.Count)
            {
                group[index].Checked = true;
                group[index].Focus();
            }

            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void PaintCard(Graphics g)
    {
        var r = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
        var radius = this.PxF(Theme.CardRadius);
        if (Checked)
        {
            Gfx.FillAndBorder(g, Theme.Surface, Theme.Accent, Math.Max(1.5f, this.PxF(1.5f)), r, radius);
        }
        else
        {
            var border = Focused && ShowFocusCues ? Theme.FocusBorder : _hovered && Enabled ? Theme.HoverBorder : Theme.Border;
            Gfx.FillAndBorder(g, Theme.Surface, border, Math.Max(1f, this.PxF(1)), r, radius);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var pad = this.Px(18);
        var ring = this.PxF(20);
        var titleFont = Fonts.ItemTitle;
        var titleHeight = Gfx.Measure(Title, titleFont).Height;
        var ringTop = pad + ((titleHeight - ring) / 2f);
        var ringRect = new RectangleF(this.PxF(22), ringTop, ring, ring);
        Gfx.DrawRounded(g, Checked ? Theme.Accent : Theme.InkDisabled, Math.Max(1.5f, this.PxF(1.5f)), ringRect, ring / 2f);
        if (Checked)
        {
            var dot = ring * 0.5f;
            using var brush = new SolidBrush(Theme.Accent);
            Gfx.Smooth(g);
            g.FillEllipse(brush, ringRect.Left + ((ring - dot) / 2f), ringRect.Top + ((ring - dot) / 2f), dot, dot);
        }

        var left = (int)Math.Ceiling(ringRect.Right) + this.Px(20);
        var width = ClientSize.Width - left - pad;
        Gfx.Text(g, Title, titleFont, new Rectangle(left, pad, width, titleHeight), Enabled ? Theme.Ink : Theme.InkDisabled);
        var top = pad + titleHeight + this.Px(5);
        Gfx.Text(g, _description, Fonts.Body, new Rectangle(left, top, width, ClientSize.Height - top - this.Px(8)), Theme.InkSecondary, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
