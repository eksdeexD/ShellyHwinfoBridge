using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>A bar of the <see cref="BarChart"/>.</summary>
/// <param name="Value">The value in chart units (kWh); bars without data use 0.</param>
/// <param name="Label">The X label under the bar, or null for none.</param>
/// <param name="Tooltip">The hover readout ("Thu 18:00 · 0.34 kWh · PC est. 0.31 kWh").</param>
internal sealed record ChartBar(double Value, string? Label, string Tooltip);

/// <summary>
/// The energy bar chart (GDI+, no library): Y labels at the left with horizontal grid lines only (Grid colour), square
/// Accent bars 60 % of their slot, X labels under labelled bars. Hovering raises <see cref="HoveredIndexChanged"/>;
/// the owner shows <see cref="HoveredTooltip"/> (in the card corner).
/// </summary>
internal sealed class BarChart : ThemedControl
{
    private IReadOnlyList<ChartBar> _bars = [];
    private IReadOnlyList<string> _yLabels = [];
    private double _yMax = 1;
    private int _hovered = -1;

    public BarChart()
    {
        AccessibleRole = AccessibleRole.Chart;
    }

    /// <summary>Raised when the hovered bar changes (-1 when none).</summary>
    public event EventHandler? HoveredIndexChanged;

    /// <summary>Gets the bars.</summary>
    public IReadOnlyList<ChartBar> Bars => _bars;

    /// <summary>Gets the hovered bar index, or -1.</summary>
    public int HoveredIndex => _hovered;

    /// <summary>Gets the hovered bar's tooltip, or null.</summary>
    public string? HoveredTooltip => _hovered >= 0 && _hovered < _bars.Count ? _bars[_hovered].Tooltip : null;

    /// <summary>Gets or sets the height reserved for X labels in DIP (default 28).</summary>
    public int XLabelHeightDip { get; set; } = 28;

    /// <summary>
    /// Sets the data. <paramref name="yLabels"/> are drawn evenly from 0 (first) to <paramref name="yMax"/> (last), one
    /// grid line each, e.g. ["0", "0.2", "0.4 kWh"].
    /// </summary>
    public void SetData(IReadOnlyList<ChartBar> bars, double yMax, IReadOnlyList<string> yLabels)
    {
        _bars = bars ?? [];
        _yMax = yMax > 0 && double.IsFinite(yMax) ? yMax : 1;
        _yLabels = yLabels ?? [];
        if (_hovered >= _bars.Count)
        {
            _hovered = -1;
        }

        Invalidate();
    }

    /// <summary>Highlights a bar as if hovered (-1 for none); used by the screen renderer.</summary>
    public void SetHoveredIndex(int index) => SetHovered(index >= 0 && index < _bars.Count ? index : -1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        SetHovered(BarAt(e.Location));
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

        if (_bars.Count == 0)
        {
            return;
        }

        var slot = plot.Width / (float)_bars.Count;
        var barWidth = Math.Max(1f, slot * 0.6f);
        using var bar = new SolidBrush(Theme.Accent);
        using var hover = new SolidBrush(Theme.AccentHover);
        for (var i = 0; i < _bars.Count; i++)
        {
            var value = Math.Clamp(_bars[i].Value, 0, _yMax);
            var height = (float)(plot.Height * value / _yMax);
            var x = plot.Left + (i * slot) + ((slot - barWidth) / 2f);
            if (height > 0)
            {
                g.FillRectangle(i == _hovered ? hover : bar, x, plot.Bottom - height, barWidth, height);
            }

            if (_bars[i].Label is { } label)
            {
                var size = Gfx.Measure(label, labelFont);
                var cx = plot.Left + (i * slot) + (slot / 2f);
                var left = Math.Min((int)Math.Round(cx - (size.Width / 2f)), Width - size.Width - this.Px(2));
                var rect = new Rectangle(left, plot.Bottom + this.Px(10), size.Width + this.Px(2), size.Height);
                Gfx.Text(g, label, labelFont, rect, Theme.InkSecondary, Gfx.SingleLine);
            }
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

    private int BarAt(Point p)
    {
        var plot = PlotBounds();
        if (_bars.Count == 0 || p.X < plot.Left || p.X >= plot.Right || p.Y < 0 || p.Y > Height)
        {
            return -1;
        }

        return Math.Clamp((int)((p.X - plot.Left) / (plot.Width / (float)_bars.Count)), 0, _bars.Count - 1);
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
