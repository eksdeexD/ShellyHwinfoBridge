using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>A bar of the <see cref="BarChart"/>.</summary>
/// <param name="Value">The value in chart units (kWh); bars without data use 0.</param>
/// <param name="Label">The X label under the bar, or null for none.</param>
/// <param name="TooltipLines">The hover tooltip, one entry per line ("Thu 18:00", "0.34 kWh", "PC est. 0.31 kWh").</param>
/// <param name="Colour">The bar colour, or null for Accent.</param>
internal sealed record ChartBar(double Value, string? Label, IReadOnlyList<string> TooltipLines, Color? Colour = null);

/// <summary>One period of a grouped chart: the bars drawn side by side (one per device).</summary>
/// <param name="Label">The X label under the group, or null for none.</param>
/// <param name="Bars">The bars, left to right.</param>
internal sealed record ChartGroup(string? Label, IReadOnlyList<ChartBar> Bars);

/// <summary>
/// The energy bar chart (GDI+, no library): Y labels at the left with horizontal grid lines only (Grid colour), square
/// bars, X labels under labelled bars. Combined: one Accent bar per period, 60 % of its slot. Grouped
/// (<see cref="SetData"/> with groups): each period shows its bars side by side in their own colours, the group 80 % of
/// its slot. The hovered bar turns lighter (AccentHover for Accent) and the chart draws its own themed tooltip over it
/// (Surface fill, 1 px Border, 8 px radius, Body text): above the bar, or inside the plot when there is no room above.
/// No WinForms ToolTip is involved; leaving the chart hides it.
/// </summary>
internal sealed class BarChart : ThemedControl
{
    private IReadOnlyList<ChartBar> _bars = [];
    private IReadOnlyList<ChartGroup> _groups = [];
    private List<(int Group, int Bar)> _flat = [];
    private IReadOnlyList<string> _yLabels = [];
    private double _yMax = 1;
    private int _hovered = -1;

    public BarChart()
    {
        AccessibleRole = AccessibleRole.Chart;
    }

    /// <summary>Raised when the hovered bar changes (-1 when none).</summary>
    public event EventHandler? HoveredIndexChanged;

    /// <summary>Gets the combined bars.</summary>
    public IReadOnlyList<ChartBar> Bars => _bars;

    /// <summary>Gets the groups (empty for the combined chart).</summary>
    public IReadOnlyList<ChartGroup> Groups => _groups;

    /// <summary>Gets whether the chart draws grouped bars.</summary>
    public bool IsGrouped => _groups.Count > 0;

    /// <summary>Gets the hovered bar index (into <see cref="Bars"/>, or into the groups' bars in reading order), or -1.</summary>
    public int HoveredIndex => _hovered;

    /// <summary>Gets the hovered bar's tooltip on one line ("Thu 18:00 · 0.34 kWh"), or null.</summary>
    public string? HoveredTooltip => ItemAt(_hovered) is { } bar ? string.Join(" · ", bar.TooltipLines) : null;

    /// <summary>Gets or sets the height reserved for X labels in DIP (default 28).</summary>
    public int XLabelHeightDip { get; set; } = 28;

    private int ItemCount => IsGrouped ? _flat.Count : _bars.Count;

    private int SlotCount => IsGrouped ? _groups.Count : _bars.Count;

    /// <summary>
    /// Sets the data. <paramref name="yLabels"/> are drawn evenly from 0 (first) to <paramref name="yMax"/> (last), one
    /// grid line each, e.g. ["0", "0.2", "0.4 kWh"]. With non-empty <paramref name="groups"/> the grouped bars are drawn
    /// instead of <paramref name="bars"/>.
    /// </summary>
    public void SetData(IReadOnlyList<ChartBar> bars, double yMax, IReadOnlyList<string> yLabels, IReadOnlyList<ChartGroup>? groups = null)
    {
        _bars = bars ?? [];
        _groups = groups ?? [];
        _flat = [];
        for (var g = 0; g < _groups.Count; g++)
        {
            for (var b = 0; b < _groups[g].Bars.Count; b++)
            {
                _flat.Add((g, b));
            }
        }

        _yMax = yMax > 0 && double.IsFinite(yMax) ? yMax : 1;
        _yLabels = yLabels ?? [];
        if (_hovered >= ItemCount)
        {
            _hovered = -1;
            AccessibleDescription = null;
        }
        else if (_hovered >= 0)
        {
            AccessibleDescription = HoveredTooltip;
        }

        Invalidate();
    }

    /// <summary>Highlights a bar as if hovered (-1 for none); used by the screen renderer.</summary>
    public void SetHoveredIndex(int index) => SetHovered(index >= 0 && index < ItemCount ? index : -1);

    /// <summary>Highlights bar <paramref name="bar"/> of group <paramref name="group"/> as if hovered (screen renderer).</summary>
    public void SetHoveredBar(int group, int bar) => SetHovered(_flat.IndexOf((group, bar)));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        SetHovered(ItemAtPoint(e.Location));
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHovered(-1);
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var plot = PlotBounds();
        var labelFont = Fonts.Body;
        var line = Math.Max(1, this.Px(1));
        using (var grid = new SolidBrush(Theme.Grid))
        {
            var count = Math.Max(2, _yLabels.Count);
            for (var i = 0; i < count; i++)
            {
                var y = plot.Bottom - (int)Math.Round(plot.Height * i / (double)(count - 1));
                g.FillRectangle(grid, plot.Left, y, plot.Width, line);
                if (i < _yLabels.Count)
                {
                    var size = Gfx.Measure(_yLabels[i], labelFont);
                    Gfx.Text(g, _yLabels[i], labelFont, new Rectangle(0, y - (size.Height / 2) - this.Px(1), plot.Left - this.Px(8), size.Height), Theme.InkSecondary, Gfx.SingleLine);
                }
            }
        }

        if (ItemCount == 0)
        {
            return;
        }

        for (var i = 0; i < ItemCount; i++)
        {
            var bar = ItemAt(i)!;
            var rect = BarRect(i, plot);
            if (rect.Height > 0)
            {
                var colour = bar.Colour ?? Theme.Accent;
                if (i == _hovered)
                {
                    colour = bar.Colour is null ? Theme.AccentHover : Theme.Blend(colour, Color.White, 0.12f);
                }

                using var brush = new SolidBrush(colour);
                g.FillRectangle(brush, rect);
            }
        }

        var slot = plot.Width / (float)SlotCount;
        for (var s = 0; s < SlotCount; s++)
        {
            if ((IsGrouped ? _groups[s].Label : _bars[s].Label) is { } label)
            {
                var size = Gfx.Measure(label, labelFont);
                var cx = plot.Left + (s * slot) + (slot / 2f);
                var left = Math.Min((int)Math.Round(cx - (size.Width / 2f)), Width - size.Width - this.Px(2));
                var rect = new Rectangle(left, plot.Bottom + this.Px(10), size.Width + this.Px(2), size.Height);
                Gfx.Text(g, label, labelFont, rect, Theme.InkSecondary, Gfx.SingleLine);
            }
        }

        if (_hovered >= 0 && _hovered < ItemCount)
        {
            DrawTooltip(g, plot);
        }
    }

    private ChartBar? ItemAt(int index)
    {
        if (index < 0 || index >= ItemCount)
        {
            return null;
        }

        if (!IsGrouped)
        {
            return _bars[index];
        }

        var (group, bar) = _flat[index];
        return _groups[group].Bars[bar];
    }

    /// <summary>Returns the drawn rectangle of an item (height 0 for an empty bar).</summary>
    private RectangleF BarRect(int index, Rectangle plot)
    {
        var bar = ItemAt(index)!;
        var height = (float)(plot.Height * Math.Clamp(bar.Value, 0, _yMax) / _yMax);
        var slot = plot.Width / (float)SlotCount;
        if (!IsGrouped)
        {
            var width = Math.Max(1f, slot * 0.6f);
            return new RectangleF(plot.Left + (index * slot) + ((slot - width) / 2f), plot.Bottom - height, width, height);
        }

        var (group, position) = _flat[index];
        var count = Math.Max(1, _groups[group].Bars.Count);
        var groupWidth = Math.Max(count, slot * 0.8f);
        var barWidth = groupWidth / count;
        var x = plot.Left + (group * slot) + ((slot - groupWidth) / 2f) + (position * barWidth);

        // A 1 px gap between neighbouring bars when they are wide enough to keep it.
        var drawn = barWidth >= this.PxF(4) && position < count - 1 ? barWidth - Math.Max(1f, this.PxF(1)) : barWidth;
        return new RectangleF(x, plot.Bottom - height, Math.Max(1f, drawn), height);
    }

    /// <summary>Draws the hovered bar's tooltip: centred over the bar, kept inside the control.</summary>
    private void DrawTooltip(Graphics g, Rectangle plot)
    {
        var lines = ItemAt(_hovered)!.TooltipLines;
        if (lines.Count == 0)
        {
            return;
        }

        var font = Fonts.Body;
        var pad = this.Px(8);
        var lineGap = this.Px(2);
        var sizes = lines.Select(l => Gfx.Measure(l, font)).ToList();
        var width = sizes.Max(s => s.Width) + (2 * pad) + this.Px(2);
        var height = sizes.Sum(s => s.Height) + (lineGap * (lines.Count - 1)) + (2 * pad);
        var offset = this.Px(8);

        var rect = BarRect(_hovered, plot);
        var centre = rect.Left + (rect.Width / 2f);
        var barTop = (int)Math.Floor(rect.Top);
        var x = (int)Math.Round(centre - (width / 2f));
        x = Math.Clamp(x, 0, Math.Max(0, Width - width));

        // Above the bar. When that would leave the control, inside the plot next to the bar (left of it in the right
        // half, right of it in the left half), so the hovered bar stays visible.
        var y = barTop - offset - height;
        if (y < 0)
        {
            var half = (rect.Width / 2f) + offset;
            x = centre > plot.Left + (plot.Width / 2f)
                ? (int)Math.Floor(centre - half - width)
                : (int)Math.Ceiling(centre + half);
            x = Math.Clamp(x, 0, Math.Max(0, Width - width));
            y = plot.Top;
        }

        y = Math.Clamp(y, 0, Math.Max(0, Height - height));

        var box = new Rectangle(x, y, width, height);
        Gfx.FillAndBorder(g, Theme.Surface, Theme.Border, Math.Max(1f, this.PxF(1)), box, this.PxF(Theme.ChipRadius));
        var top = y + pad;
        for (var i = 0; i < lines.Count; i++)
        {
            var color = i == 0 ? Theme.InkSecondary : Theme.Ink;
            Gfx.Text(g, lines[i], font, new Rectangle(x + pad, top, width - (2 * pad), sizes[i].Height), color, Gfx.SingleLine);
            top += sizes[i].Height + lineGap;
        }
    }

    private Rectangle PlotBounds()
    {
        var labelWidth = _yLabels.Count == 0 ? 0 : _yLabels.Max(l => Gfx.Measure(l, Fonts.Body).Width);
        var left = labelWidth + this.Px(16);
        var top = Gfx.Measure("Ag", Fonts.Body).Height / 2;
        var bottom = Height - this.Px(XLabelHeightDip);
        return new Rectangle(left, top, Math.Max(1, Width - left), Math.Max(1, bottom - top));
    }

    private int ItemAtPoint(Point p)
    {
        var plot = PlotBounds();
        if (ItemCount == 0 || p.X < plot.Left || p.X >= plot.Right || p.Y < 0 || p.Y > Height)
        {
            return -1;
        }

        var slot = plot.Width / (float)SlotCount;
        var s = Math.Clamp((int)((p.X - plot.Left) / slot), 0, SlotCount - 1);
        if (!IsGrouped)
        {
            return s;
        }

        // Inside a group: the bar under the pointer, or the nearest one in the gap between groups.
        var count = _groups[s].Bars.Count;
        if (count == 0)
        {
            return -1;
        }

        var groupWidth = Math.Max(count, slot * 0.8f);
        var groupLeft = plot.Left + (s * slot) + ((slot - groupWidth) / 2f);
        var position = Math.Clamp((int)((p.X - groupLeft) / (groupWidth / count)), 0, count - 1);
        return _flat.IndexOf((s, position));
    }

    private void SetHovered(int index)
    {
        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        AccessibleDescription = HoveredTooltip;
        Invalidate();
        HoveredIndexChanged?.Invoke(this, EventArgs.Empty);
    }
}
