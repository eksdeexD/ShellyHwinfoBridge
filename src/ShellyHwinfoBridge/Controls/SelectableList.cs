using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>A row of a <see cref="SelectableList"/>.</summary>
/// <param name="Title">Bold first line ("Prime Fanless TX-700"); column 1 in column mode.</param>
/// <param name="Subtitle">Secondary line ("S3PL-10112EU · 192.168.1.42"), or null; not shown in column mode.</param>
/// <param name="LinkText">
/// Clickable text after the subtitle, or (column mode) on the last line of the last column ("report 21PS1872A ·
/// 2021-07-16 ↗"); or null.
/// </param>
/// <param name="Tag">Caller data (an id, a device, a URL).</param>
internal sealed record SelectableRow(string Title, string? Subtitle = null, string? LinkText = null, object? Tag = null)
{
    /// <summary>Gets the middle column's text in column mode ("80 Plus Gold"), or null.</summary>
    public string? Column2 { get; init; }

    /// <summary>Gets the last column's lines in column mode ("Cybenetics ETA Gold (230 V)"); the link follows them.</summary>
    public IReadOnlyList<string> Column3 { get; init; } = [];
}

/// <summary>
/// A card-framed list of rows separated by 1 px lines; the selected row is SurfaceElevated with a 4 DIP Accent bar at
/// the left. Scrolls with the wheel, the keyboard and a thin drawn scrollbar (no virtualisation; lists are short).
/// With <see cref="ColumnWidthsDip"/> set, rows are three columns: the title takes the remaining width (up to two
/// lines), then <see cref="SelectableRow.Column2"/> and <see cref="SelectableRow.Column3"/> plus the link at fixed
/// widths; <see cref="ColumnHeaders"/> adds a thin non-scrolling header row.
/// </summary>
internal sealed class SelectableList : ThemedControl
{
    private const int HeaderHeightDip = 28;
    private const int ColumnGapDip = 16;
    private const int LineGapDip = 2;

    private readonly List<SelectableRow> _rows = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private int _hoverLink = -1;
    private int _scroll;
    private bool _draggingThumb;
    private int _dragStartY;
    private int _dragStartScroll;
    private int _maxColumnLines = 1;

    public SelectableList()
    {
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when a row's link is clicked; the argument is the row index.</summary>
    public event EventHandler<int>? LinkClicked;

    /// <summary>Gets or sets the minimum row height in DIP (column mode grows it to fit the tallest last column).</summary>
    public int RowHeightDip { get; set; } = 48;

    /// <summary>
    /// Gets or sets the fixed DIP widths of columns 2 and 3 (column mode), or null for the title + subtitle layout.
    /// </summary>
    public int[]? ColumnWidthsDip { get; set; }

    /// <summary>Gets or sets the column header texts (column mode), or null for no header row.</summary>
    public IReadOnlyList<string>? ColumnHeaders { get; set; }

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

    private bool ColumnMode => ColumnWidthsDip is { Length: >= 2 };

    private int HeaderHeight => ColumnMode && ColumnHeaders is { Count: > 0 } ? this.Px(HeaderHeightDip) : 0;

    private int ViewHeight => Math.Max(0, Height - HeaderHeight);

    private int LineHeight => Gfx.Measure("Ag", Fonts.Body).Height;

    private int RowHeight
    {
        get
        {
            var height = this.Px(RowHeightDip);
            if (ColumnMode)
            {
                height = Math.Max(height, (_maxColumnLines * LineHeight) + ((_maxColumnLines - 1) * this.Px(LineGapDip)) + this.Px(16));
            }

            return Math.Max(1, height);
        }
    }

    private int MaxScroll => Math.Max(0, (_rows.Count * RowHeight) - ViewHeight);

    protected override AccessibleObject CreateAccessibilityInstance() => new ThemedAccessibleObject(this)
    {
        ValueText = () => SelectedRow?.Title,
        Children = () => Enumerable.Range(0, _rows.Count)
            .Select(i => (AccessibleObject)new ThemedItemAccessibleObject(
                this,
                i,
                AccessibleRole.ListItem,
                index => index < _rows.Count ? AccessibleText(_rows[index]) : string.Empty,
                index => index == _selectedIndex,
                index => SelectedIndex = index,
                index => new Rectangle(0, HeaderHeight + (index * RowHeight) - _scroll, Width, RowHeight)))
            .ToList(),
    };

    /// <summary>Replaces the rows and selects <paramref name="selectedIndex"/> without raising the event.</summary>
    public void SetRows(IEnumerable<SelectableRow> rows, int selectedIndex = -1)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _maxColumnLines = Math.Max(1, _rows.Count == 0 ? 1 : _rows.Max(r => r.Column3.Count + (string.IsNullOrEmpty(r.LinkText) ? 0 : 1)));
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
        var page = Math.Max(1, ViewHeight / RowHeight);
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

        if (ThumbBounds() is { } thumb && thumb.Width > 0 && e.X >= Width - this.Px(14) && e.Y >= HeaderHeight)
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
            var track = ViewHeight - ThumbBounds()!.Value.Height;
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

            // The header goes over the rows scrolled under it (GDI text ignores the Graphics clip).
            if (HeaderHeight > 0)
            {
                PaintHeader(g);
            }

            g.Restore(state);
        }

        Gfx.DrawRounded(g, ShowFocus ? Theme.FocusBorder : Theme.Border, borderWidth, r, radius);

        if (_rows.Count == 0 && !string.IsNullOrEmpty(EmptyText))
        {
            Gfx.Text(g, EmptyText, Fonts.Body, new Rectangle(0, HeaderHeight, Width, ViewHeight), Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (ThumbBounds() is { } thumb && thumb.Width > 0)
        {
            Gfx.FillRounded(g, Theme.InkDisabled, thumb, thumb.Width / 2f);
        }
    }

    /// <summary>The left edges and widths of the three columns in column mode (pixels).</summary>
    private (int Left1, int Width1, int Left2, int Width2, int Left3, int Width3) Columns()
    {
        var widths = ColumnWidthsDip!;
        var pad = this.Px(16);
        var gap = this.Px(ColumnGapDip);
        var right = Width - this.Px(20);
        var width3 = this.Px(widths[1]);
        var left3 = right - width3;
        var width2 = this.Px(widths[0]);
        var left2 = left3 - gap - width2;
        return (pad, Math.Max(0, left2 - gap - pad), left2, width2, left3, width3);
    }

    private void PaintHeader(Graphics g)
    {
        var headers = ColumnHeaders!;
        var height = HeaderHeight;
        var (left1, width1, left2, width2, left3, width3) = Columns();
        var lefts = new[] { (left1, width1), (left2, width2), (left3, width3) };
        using (var background = new SolidBrush(Theme.Surface))
        {
            g.FillRectangle(background, 0, 0, Width, height);
        }

        for (var i = 0; i < headers.Count && i < lefts.Length; i++)
        {
            Gfx.Text(g, headers[i], Fonts.Small, new Rectangle(lefts[i].Item1, 0, lefts[i].Item2, height), Theme.InkSecondary);
        }

        using var separator = new SolidBrush(Theme.Border);
        g.FillRectangle(separator, 0, height - Math.Max(1, this.Px(1)), Width, Math.Max(1, this.Px(1)));
    }

    private void PaintRows(Graphics g)
    {
        var rowHeight = RowHeight;
        var pad = this.Px(16);
        var titleFont = Fonts.ItemTitle;
        var line = Math.Max(1, this.Px(1));
        using var separator = new SolidBrush(Theme.Border);
        for (var i = 0; i < _rows.Count; i++)
        {
            var top = HeaderHeight + (i * rowHeight) - _scroll;
            if (top + rowHeight < HeaderHeight || top > Height)
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

            if (ColumnMode)
            {
                PaintColumns(g, i, top, rowHeight);
                continue;
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
                    if (subWidth > 0)
                    {
                        Gfx.Text(g, "·", Fonts.Body, new Rectangle(link.Left - this.Px(14), subTop, this.Px(10), subHeight), Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.HorizontalCenter);
                    }

                    PaintLink(g, i, link);
                }
            }
        }
    }

    private void PaintColumns(Graphics g, int index, int top, int rowHeight)
    {
        var row = _rows[index];
        var (left1, width1, left2, width2, left3, width3) = Columns();
        var inset = this.Px(6);

        // Column 1: the title, wrapped onto a second line when it is too long, then cut with an ellipsis.
        var titleFont = Fonts.ItemTitle;
        const TextFormatFlags wrap = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var titleLine = Gfx.Measure("Ag", titleFont).Height;
        var titleHeight = Math.Min(Math.Min(2 * titleLine, rowHeight - (2 * inset)), Gfx.MeasureWrapped(row.Title, titleFont, width1).Height);
        Gfx.Text(g, row.Title, titleFont, new Rectangle(left1, top + ((rowHeight - titleHeight) / 2), width1, titleHeight), Theme.Ink, wrap);

        // Column 2: the 80 Plus rating, centred on the row.
        Gfx.Text(g, row.Column2, Fonts.Body, new Rectangle(left2, top, width2, rowHeight), Theme.InkSecondary);

        // Column 3: the lines, then the link, as one block centred on the row.
        var lineHeight = LineHeight;
        var y = Column3Top(row, top, rowHeight);
        foreach (var text in row.Column3)
        {
            Gfx.Text(g, text, Fonts.Body, new Rectangle(left3, y, width3, lineHeight), Theme.InkSecondary);
            y += lineHeight + this.Px(LineGapDip);
        }

        if (LinkBounds(index) is { } link)
        {
            PaintLink(g, index, link);
        }
    }

    private void PaintLink(Graphics g, int index, Rectangle link)
    {
        var hovered = _hoverLink == index;
        using var font = hovered ? new Font(Fonts.Body, FontStyle.Underline) : null;
        Gfx.Text(g, _rows[index].LinkText, font ?? Fonts.Body, link, hovered ? Theme.Accent : Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.VerticalCenter);
    }

    private int Column3Top(SelectableRow row, int top, int rowHeight)
    {
        var lines = row.Column3.Count + (string.IsNullOrEmpty(row.LinkText) ? 0 : 1);
        var block = (lines * LineHeight) + (Math.Max(0, lines - 1) * this.Px(LineGapDip));
        return top + ((rowHeight - block) / 2);
    }

    private Rectangle? LinkBounds(int index)
    {
        if (index < 0 || index >= _rows.Count || string.IsNullOrEmpty(_rows[index].LinkText))
        {
            return null;
        }

        var row = _rows[index];
        var rowHeight = RowHeight;
        var top = HeaderHeight + (index * rowHeight) - _scroll;
        var width = Gfx.Measure(row.LinkText, Fonts.Body).Width + this.Px(2);
        if (ColumnMode)
        {
            var columns = Columns();
            var lineTop = Column3Top(row, top, rowHeight) + (row.Column3.Count * (LineHeight + this.Px(LineGapDip)));
            return new Rectangle(columns.Left3, lineTop, Math.Min(width, columns.Width3 + this.Px(2)), LineHeight);
        }

        var titleHeight = Gfx.Measure(row.Title, Fonts.ItemTitle).Height;
        var subHeight = Gfx.Measure("Ag", Fonts.Body).Height;
        var gap = this.Px(2);
        var y = top + ((rowHeight - titleHeight - gap - subHeight) / 2) + titleHeight + gap;
        var pad = this.Px(16);
        var subWidth = string.IsNullOrEmpty(row.Subtitle) ? 0 : Gfx.Measure(row.Subtitle, Fonts.Body).Width + this.Px(18);
        return new Rectangle(pad + subWidth, y, width, subHeight);
    }

    private static string AccessibleText(SelectableRow row) =>
        string.Join(", ", new[] { row.Title, row.Subtitle, row.Column2 }.Concat(row.Column3).Append(row.LinkText).Where(s => !string.IsNullOrEmpty(s)));

    private int RowAt(int y)
    {
        if (_rows.Count == 0 || y < HeaderHeight)
        {
            return -1;
        }

        var index = (y - HeaderHeight + _scroll) / RowHeight;
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    private RectangleF? ThumbBounds()
    {
        var content = _rows.Count * RowHeight;
        var view = ViewHeight;
        if (content <= view || view <= 0)
        {
            return null;
        }

        var width = this.PxF(5);
        var inset = this.PxF(6);
        var track = view - (2 * inset);
        var thumb = Math.Max(this.PxF(24), track * view / content);
        var top = HeaderHeight + inset + ((track - thumb) * _scroll / (float)MaxScroll);
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
        else if (top + RowHeight > _scroll + ViewHeight)
        {
            ScrollTo(top + RowHeight - ViewHeight);
        }
    }
}
