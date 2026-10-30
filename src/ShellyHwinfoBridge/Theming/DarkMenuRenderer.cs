namespace ShellyHwinfoBridge.Theming;

/// <summary>Dark renderer for the tray menu and drop-down lists.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer()
        : base(new DarkColorTable())
    {
        RoundedEdges = false;
    }

    /// <summary>Applies the renderer, colours and the per-DPI body font to a menu.</summary>
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = new DarkMenuRenderer();
        menu.BackColor = Theme.Surface;
        menu.ForeColor = Theme.Ink;
        menu.Font = ThemeFonts.For(menu.DeviceDpi).Body;
        if (menu is ToolStripDropDownMenu dd)
        {
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = false;
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.InkDisabled
            : e.Item is ToolStripMenuItem { Checked: true } ? Theme.Accent
            : Theme.Ink;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.InkSecondary;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var r = e.Item.ContentRectangle;
        var y = r.Top + (r.Height / 2);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, r.Left + 4, y, r.Right - 4, y);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(Point.Empty, e.Item.Size);
        var selected = e.Item.Selected && e.Item.Enabled;
        using var brush = new SolidBrush(selected ? Theme.SurfaceElevated : Theme.Surface);
        e.Graphics.FillRectangle(brush, r);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var r = new Rectangle(Point.Empty, e.ToolStrip.Size);
        r.Width--;
        r.Height--;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, r);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public DarkColorTable()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => Theme.Surface;

        public override Color MenuBorder => Theme.Border;

        public override Color MenuItemBorder => Theme.SurfaceElevated;

        public override Color MenuItemSelected => Theme.SurfaceElevated;

        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceElevated;

        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceElevated;

        public override Color MenuItemPressedGradientBegin => Theme.SurfaceElevated;

        public override Color MenuItemPressedGradientEnd => Theme.SurfaceElevated;

        public override Color ImageMarginGradientBegin => Theme.Surface;

        public override Color ImageMarginGradientMiddle => Theme.Surface;

        public override Color ImageMarginGradientEnd => Theme.Surface;

        public override Color SeparatorDark => Theme.Border;

        public override Color SeparatorLight => Theme.Border;

        public override Color CheckBackground => Theme.SurfaceElevated;

        public override Color CheckSelectedBackground => Theme.SurfaceElevated;

        public override Color CheckPressedBackground => Theme.SurfaceElevated;
    }
}
