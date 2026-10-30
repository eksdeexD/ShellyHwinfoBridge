using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>Styles a <see cref="DataGridView"/> (manual curve grid) for the dark theme.</summary>
internal static class GridStyler
{
    /// <summary>Applies colours, fonts and row heights for the grid's current DPI; call again after a DPI change.</summary>
    public static void Apply(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var fonts = ThemeFonts.For(grid.DeviceDpi);
        grid.BackgroundColor = Theme.Surface;
        grid.GridColor = Theme.Border;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.Font = fonts.Body;

        grid.DefaultCellStyle.BackColor = Theme.Surface;
        grid.DefaultCellStyle.ForeColor = Theme.Ink;
        grid.DefaultCellStyle.SelectionBackColor = Theme.SurfaceElevated;
        grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        grid.DefaultCellStyle.Font = fonts.Body;
        grid.DefaultCellStyle.Padding = new Padding(Theme.Scale(8, grid.DeviceDpi), 0, Theme.Scale(8, grid.DeviceDpi), 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Surface;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.InkSecondary;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Surface;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.InkSecondary;
        grid.ColumnHeadersDefaultCellStyle.Font = fonts.Body;
        grid.ColumnHeadersDefaultCellStyle.Padding = grid.DefaultCellStyle.Padding;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = Theme.Scale(36, grid.DeviceDpi);
        grid.RowTemplate.Height = Theme.Scale(34, grid.DeviceDpi);
        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Height = grid.RowTemplate.Height;
        }

        if (grid.IsHandleCreated)
        {
            DarkenScrollBars(grid);
        }
        else
        {
            grid.HandleCreated -= OnHandleCreated;
            grid.HandleCreated += OnHandleCreated;
        }
    }

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is DataGridView grid)
        {
            DarkenScrollBars(grid);
        }
    }

    private static void DarkenScrollBars(DataGridView grid)
    {
        NativeMethods.UseDarkExplorerTheme(grid);
        foreach (var bar in grid.Controls.OfType<ScrollBar>())
        {
            NativeMethods.UseDarkExplorerTheme(bar);
        }
    }
}
