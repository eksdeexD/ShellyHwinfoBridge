using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>A row of a <see cref="SelectableList"/>.</summary>
/// <param name="Title">Bold first line ("Prime Fanless TX-700").</param>
/// <param name="Subtitle">Secondary line ("80 Plus Titanium · Cybenetics ETA Titanium"), or null.</param>
/// <param name="LinkText">Clickable text after the subtitle ("report 21PS1872A ↗"), or null.</param>
/// <param name="Tag">Caller data (an id, a device, a URL).</param>
internal sealed record SelectableRow(string Title, string? Subtitle = null, string? LinkText = null, object? Tag = null);

/// <summary>
/// A card-framed list of rows separated by 1 px lines; the selected row is SurfaceElevated with a 4 DIP Accent bar at
/// the left. Scrolls with the wheel, the keyboard and a thin drawn scrollbar (no virtualisation; lists are short).
/// </summary>
internal sealed class SelectableList : ThemedControl
{
    private readonly List<SelectableRow> _rows = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private int _hoverLink = -1;
    private int _scroll;
    private bool _draggingThumb;
    private int _dragStartY;
    private int _dragStartScroll;

    public SelectableList()
    {
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when a row's link is clicked; the argument is the row index.</summary>
    public event EventHandler<int>? LinkClicked;

    /// <summary>Gets or sets the row height in DIP (default 70).</summary>
    public int RowHeightDip { get; set; } = 48;

    /// <summary>Gets or sets the text shown when there are no rows ("Searching…", "No match").</summary>
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>Gets the rows.</summary>
    public IReadOnlyList<SelectableRow> Rows => _rows;

    /// <summary>Gets the selected row, or null.</summary>
    public SelectableRow? SelectedRow => _selectedIndex >= 0 && _selectedIndex < _rows.Count ? _rows[_selectedIndex] : null;

    /// <summary>Gets or sets the selected index (-1 for none); raises <see cref="SelectedIndexChanged"/> on change.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _rows.Count == 0 ? -1 : Math.Clamp(value, -1, _rows.Count - 1);
            if (value == _selectedIndex)
            {
                return;
            }

            _selectedIndex = value;
            AccessibleDescription = SelectedRow?.Title;
            EnsureVisible(value);
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
        ValueText = () => SelectedRow?.Title,
        Children = () => Enumerable.Range(0, _rows.Count)
            .Select(i => (AccessibleObject)new ThemedItemAccessibleObject(
                this,
                i,
                AccessibleRole.ListItem,
                index => index < _rows.Count ? string.Join(", ", new[] { _rows[index].Title, _rows[index].Subtitle }.Where(s => !string.IsNullOrEmpty(s))) : string.Empty,
                index => index == _selectedIndex,
                index => SelectedIndex = index,
                index => new Rectangle(0, (index * RowHeight) - _scroll, Width, RowHeight)))
            .ToList(),
    };

    /// <summary>Replaces the rows and selects <paramref name="selectedIndex"/> without raising the event.</summary>
    public void SetRows(IEnumerable<SelectableRow> rows, int selectedIndex = -1)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _selectedIndex = _rows.Count == 0 ? -1 : Math.Clamp(selectedIndex, -1, _rows.Count - 1);
        _hoverIndex = -1;
        _hoverLink = -1;
        _scroll = 0;
        EnsureVisible(_selectedIndex);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var page = Math.Max(1, Height / RowHeight);
        int? target = e.KeyCode switch
        {
            Keys.Up => Math.Max(0, _selectedIndex - 1),
            Keys.Down => Math.Min(_rows.Count - 1, _selectedIndex + 1),
            Keys.Home => 0,
            Keys.End => _rows.Count - 1,
            Keys.PageUp => Math.Max(0, _selectedIndex - page),
            Keys.PageDown => Math.Min(_rows.Count - 1, _selectedIndex + page),
            _ => null,
        };
        if (target is int t && _rows.Count > 0)
        {
            SelectedIndex = t;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(_scroll - (e.Delta / 120 * RowHeight / 2));
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (ThumbBounds() is { } thumb && thumb.Width > 0 && e.X >= Width - this.Px(14))
        {
            _draggingThumb = true;
            _dragStartY = e.Y;
            _dragStartScroll = _scroll;
            Capture = true;
            return;
        }

        var index = RowAt(e.Y);
        if (index >= 0)
        {
            if (LinkBounds(index) is { } link && link.Contains(e.Location))
            {
                LinkClicked?.Invoke(this, index);
            }

            SelectedIndex = index;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingThumb)
        {
            var track = Height - ThumbBounds()!.Value.Height;
            if (track > 0)
            {
                ScrollTo(_dragStartScroll + (int)((e.Y - _dragStartY) * (MaxScroll / (double)track)));
            }

            return;
        }

        var index = RowAt(e.Y);
        var link = index >= 0 && LinkBounds(index) is { } r && r.Contains(e.Location) ? index : -1;
        if (index != _hoverIndex || link != _hoverLink)
        {
            _hoverIndex = index;
            _hoverLink = link;
            Cursor = link >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_draggingThumb)
        {
            _draggingThumb = false;
            Capture = false;
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        _hoverLink = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnResize(EventArgs e)
    {
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        EnsureVisible(_selectedIndex);
        base.OnResize(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        var radius = this.PxF(Theme.CardRadius);
        var borderWidth = Math.Max(1f, this.PxF(1));
        Gfx.FillRounded(g, Theme.Surface, r, radius);

        using (var clip = Gfx.RoundedRect(RectangleF.Inflate(r, -borderWidth / 2f, -borderWidth / 2f), radius))
        {
            var state = g.Save();
            g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
            PaintRows(g);
            g.Restore(state);
        }

        Gfx.DrawRounded(g, ShowFocus ? Theme.FocusBorder : Theme.Border, borderWidth, r, radius);

        if (_rows.Count == 0 && !string.IsNullOrEmpty(EmptyText))
        {
            Gfx.Text(g, EmptyText, Fonts.Body, ClientRectangle, Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (ThumbBounds() is { } thumb && thumb.Width > 0)
        {
            Gfx.FillRounded(g, Theme.InkDisabled, thumb, thumb.Width / 2f);
        }
    }

    private int RowHeight => Math.Max(1, this.Px(RowHeightDip));

    private int MaxScroll => Math.Max(0, (_rows.Count * RowHeight) - Height);

    private void PaintRows(Graphics g)
    {
        var rowHeight = RowHeight;
        var pad = this.Px(16);
        var titleFont = Fonts.ItemTitle;
        var line = Math.Max(1, this.Px(1));
        using var separator = new SolidBrush(Theme.Border);
        for (var i = 0; i < _rows.Count; i++)
        {
            var top = (i * rowHeight) - _scroll;
            if (top + rowHeight < 0 || top > Height)
            {
                continue;
            }

            var row = _rows[i];
            if (i == _selectedIndex || i == _hoverIndex)
            {
                using var fill = new SolidBrush(i == _selectedIndex ? Theme.SurfaceElevated : Theme.Blend(Theme.Surface, Theme.SurfaceElevated, 0.5f));
                g.FillRectangle(fill, 0, top, Width, rowHeight);
            }

            if (i == _selectedIndex)
            {
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, 0, top, this.Px(3), rowHeight);
            }

            if (i > 0)
            {
                g.FillRectangle(separator, 0, top, Width, line);
            }

            var titleHeight = Gfx.Measure(row.Title, titleFont).Height;
            var subHeight = string.IsNullOrEmpty(row.Subtitle) && string.IsNullOrEmpty(row.LinkText) ? 0 : Gfx.Measure("Ag", Fonts.Body).Height;
            var gap = subHeight > 0 ? this.Px(2) : 0;
            var y = top + ((rowHeight - titleHeight - gap - subHeight) / 2);
            Gfx.Text(g, row.Title, titleFont, new Rectangle(pad, y, Width - pad - this.Px(20), titleHeight), Theme.Ink);
            if (subHeight > 0)
            {
                var subTop = y + titleHeight + gap;
                var subWidth = string.IsNullOrEmpty(row.Subtitle) ? 0 : Gfx.Measure(row.Subtitle, Fonts.Body).Width;
                Gfx.Text(g, row.Subtitle, Fonts.Body, new Rectangle(pad, subTop, Width - pad - this.Px(20), subHeight), Theme.InkSecondary);
                if (!string.IsNullOrEmpty(row.LinkText))
                {
                    var link = LinkBounds(i)!.Value;
                    var hovered = _hoverLink == i;
                    if (subWidth > 0)
                    {
                        Gfx.Text(g, "·", Fonts.Body, new Rectangle(link.Left - this.Px(14), subTop, this.Px(10), subHeight), Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.HorizontalCenter);
                    }

                    using var font = hovered ? new Font(Fonts.Body, FontStyle.Underline) : null;
                    Gfx.Text(g, row.LinkText, font ?? Fonts.Body, link, hovered ? Theme.Accent : Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }

    private Rectangle? LinkBounds(int index)
    {
        if (index < 0 || index >= _rows.Count || string.IsNullOrEmpty(_rows[index].LinkText))
        {
            return null;
        }

        var row = _rows[index];
        var rowHeight = RowHeight;
        var titleHeight = Gfx.Measure(row.Title, Fonts.ItemTitle).Height;
        var subHeight = Gfx.Measure("Ag", Fonts.Body).Height;
        var gap = this.Px(2);
        var top = (index * rowHeight) - _scroll;
        var y = top + ((rowHeight - titleHeight - gap - subHeight) / 2) + titleHeight + gap;
        var pad = this.Px(16);
        var subWidth = string.IsNullOrEmpty(row.Subtitle) ? 0 : Gfx.Measure(row.Subtitle, Fonts.Body).Width + this.Px(18);
        var width = Gfx.Measure(row.LinkText, Fonts.Body).Width + this.Px(2);
        return new Rectangle(pad + subWidth, y, width, subHeight);
    }

    private int RowAt(int y)
    {
        if (_rows.Count == 0)
        {
            return -1;
        }

        var index = (y + _scroll) / RowHeight;
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    private RectangleF? ThumbBounds()
    {
        var content = _rows.Count * RowHeight;
        if (content <= Height || Height <= 0)
        {
            return null;
        }

        var width = this.PxF(5);
        var inset = this.PxF(6);
        var track = Height - (2 * inset);
        var thumb = Math.Max(this.PxF(24), track * Height / content);
        var top = inset + ((track - thumb) * _scroll / (float)MaxScroll);
        return new RectangleF(Width - inset - width, top, width, thumb);
    }

    private void ScrollTo(int value)
    {
        value = Math.Clamp(value, 0, MaxScroll);
        if (value != _scroll)
        {
            _scroll = value;
            Invalidate();
        }
    }

    private void EnsureVisible(int index)
    {
        if (index < 0)
        {
            return;
        }

        var top = index * RowHeight;
        if (top < _scroll)
        {
            ScrollTo(top);
        }
        else if (top + RowHeight > _scroll + Height)
        {
            ScrollTo(top + RowHeight - Height);
        }
    }
}
