using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A rounded card: Surface fill, 1 px Border (1.5 px Accent when <see cref="Selected"/>), radius <see cref="Theme.CardRadius"/>, padding <see cref="Theme.CardPadding"/>,
/// optional title in CardTitle. Children are placed by the owner; <see cref="ContentBounds"/> gives the area under the title.
/// </summary>
internal class CardPanel : Panel, IThemedSurface
{
    private string? _title;
    private bool _selected;

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
    }

    /// <summary>Gets or sets the card title (null for none).</summary>
    public string? Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                AccessibleName ??= value;
                Invalidate();
            }
        }
    }

    /// <summary>Gets or sets whether the card is selected (accent border).</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                Invalidate();
            }
        }
    }

    /// <summary>Gets or sets the card padding in DIP (default <see cref="Theme.CardPadding"/>).</summary>
    public int PaddingDip { get; set; } = Theme.CardPadding;

    /// <summary>Gets the fonts for the current DPI.</summary>
    protected ThemeFontSet Fonts => ThemeFonts.For(DeviceDpi);

    /// <summary>Gets the padding in device pixels.</summary>
    public int PaddingPx => this.Px(PaddingDip);

    /// <summary>Gets the height of the title line in device pixels (0 without a title).</summary>
    public int TitleHeight => string.IsNullOrEmpty(Title) ? 0 : Gfx.Measure(Title, Fonts.CardTitle).Height;

    /// <summary>Gets the area for children: inside the padding and below the title (plus <see cref="Theme.TitleGap"/>).</summary>
    public Rectangle ContentBounds
    {
        get
        {
            var p = PaddingPx;
            var top = p + (TitleHeight > 0 ? TitleHeight + this.Px(Theme.TitleGap) : 0);
            return new Rectangle(p, top, Math.Max(0, ClientSize.Width - (2 * p)), Math.Max(0, ClientSize.Height - top - p));
        }
    }

    /// <summary>Paints the card surface behind a child.</summary>
    public virtual void PaintSurface(Graphics g, Rectangle area)
    {
        using var brush = new SolidBrush(Theme.Surface);
        g.FillRectangle(brush, area);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Gfx.PaintBackdrop(this, e.Graphics);
        PaintCard(e.Graphics);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!string.IsNullOrEmpty(Title))
        {
            var p = PaddingPx;
            Gfx.Text(e.Graphics, Title, Fonts.CardTitle, new Rectangle(p, p, ClientSize.Width - (2 * p), TitleHeight), Theme.Ink);
        }

        base.OnPaint(e);
    }

    /// <summary>Draws the rounded card (fill and border).</summary>
    protected virtual void PaintCard(Graphics g)
    {
        var r = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
        var radius = this.PxF(Theme.CardRadius);
        if (Selected)
        {
            Gfx.FillAndBorder(g, Theme.Surface, Theme.Accent, Math.Max(1.5f, this.PxF(1.5f)), r, radius);
        }
        else
        {
            Gfx.FillAndBorder(g, Theme.Surface, Theme.Border, Math.Max(1f, this.PxF(1)), r, radius);
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
        Invalidate(true);
    }
}
