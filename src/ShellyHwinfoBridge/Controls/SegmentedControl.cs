using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// Segmented button ("Hours | Days | Months", "Auto | 115 V | 230 V"), square like <see cref="PillButton"/>: 1 px Border| Days | Months", "Auto | 115 V | 230 V"): 1 px Border, 1 px separators between
/// unselected segments, selected segment filled SurfaceElevated with Ink semibold text. Arrow keys move the selection.
/// </summary>
internal sealed class SegmentedControl : ThemedControl
{
    private readonly List<string> _items = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;

    public SegmentedControl()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Gets the segment labels.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Gets or sets the selected segment (-1 for none).</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (value == _selectedIndex)
            {
                return;
            }

            _selectedIndex = value;
            AccessibleDescription = value >= 0 ? _items[value] : null;
            Invalidate();
            if (IsHandleCreated && value >= 0)
            {
                AccessibilityNotifyClients(AccessibleEvents.Selection, value);
            }

            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ThemedAccessibleObject(this)
    {
        ValueText = () => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null,
        Children = () => Enumerable.Range(0, _items.Count)
            .Select(i => (AccessibleObject)new ThemedItemAccessibleObject(
                this,
                i,
                AccessibleRole.PageTab,
                index => index < _items.Count ? _items[index] : string.Empty,
                index => index == _selectedIndex,
                index => SelectedIndex = index,
                index => Rectangle.Round(Segment(index))))
            .ToList(),
    };

    /// <summary>Replaces the items; keeps the selection when still in range (silently, no event).</summary>
    public void SetItems(IEnumerable<string> items, int selectedIndex = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);
        Invalidate();
    }

    /// <summary>Preferred size: equal segments wide enough for the longest label; <see cref="Theme.ButtonHeight"/> high.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var widest = _items.Count == 0 ? 0 : _items.Max(i => Gfx.Measure(i, Fonts.BodySemibold).Width);
        return new Size(_items.Count * (widest + this.Px(24)), this.Px(Theme.ButtonHeight));
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                e.Handled = true;
                break;
            case Keys.Right:
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
                e.Handled = true;
                break;
            case Keys.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;
            case Keys.End:
                SelectedIndex = _items.Count - 1;
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = HitTest(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitTest(e.Location) is var index and >= 0)
        {
            SelectedIndex = index;
        }

        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var count = _items.Count;
        var r = new RectangleF(0, 0, Width, Height);
        const float radius = 0f;
        var border = Math.Max(1f, this.PxF(1));
        if (count == 0)
        {
            Gfx.DrawRounded(g, Theme.Border, border, r, radius);
            return;
        }

        if (_selectedIndex >= 0)
        {
            var seg = Segment(_selectedIndex);
            using var outer = Gfx.RoundedRect(r, radius);
            var clip = g.Clip;
            g.SetClip(outer, System.Drawing.Drawing2D.CombineMode.Intersect);
            Gfx.FillRounded(g, Theme.SurfaceElevated, seg, radius);
            g.Clip = clip;
        }

        Gfx.DrawRounded(g, ShowFocus ? Theme.FocusBorder : Theme.Border, border, r, radius);

        using (var pen = new Pen(Theme.Border, border))
        {
            for (var i = 1; i < count; i++)
            {
                if (i == _selectedIndex || i - 1 == _selectedIndex)
                {
                    continue;
                }

                var x = (float)Math.Round(Segment(i).Left) + 0.5f;
                g.DrawLine(pen, x, border, x, Height - border);
            }
        }

        for (var i = 0; i < count; i++)
        {
            var seg = Rectangle.Round(Segment(i));
            var selected = i == _selectedIndex;
            var color = !Enabled ? Theme.InkDisabled : selected || i == _hoverIndex ? Theme.Ink : Theme.InkSecondary;
            Gfx.Text(g, _items[i], selected ? Fonts.BodySemibold : Fonts.Body, seg, color, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private RectangleF Segment(int index)
    {
        var w = Width / (float)Math.Max(1, _items.Count);
        return new RectangleF(index * w, 0, w, Height);
    }

    private int HitTest(Point p)
    {
        if (_items.Count == 0 || !ClientRectangle.Contains(p))
        {
            return -1;
        }

        return Math.Clamp((int)(p.X / (Width / (float)_items.Count)), 0, _items.Count - 1);
    }
}
