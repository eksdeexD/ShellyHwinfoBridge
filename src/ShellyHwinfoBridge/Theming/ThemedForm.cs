using System.Drawing.Drawing2D;

namespace ShellyHwinfoBridge.Theming;

/// <summary>
/// Base of every window: dark title bar, vertical BgTop→BgBottom gradient, manual DPI handling
/// (<see cref="AutoScaleMode.None"/>; subclasses lay out in device pixels computed from DIP in <see cref="Control.OnLayout"/>).
/// </summary>
internal class ThemedForm : Form, IThemedSurface
{
    private Size _clientSizeDip;
    private Size _minimumClientSizeDip;

    public ThemedForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.BgBottom;
        ForeColor = Theme.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        Font = ThemeFonts.For(DeviceDpi).Body;
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Icon = Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // Keep the default icon.
        }
    }

    /// <summary>Gets the fonts for the current DPI.</summary>
    protected ThemeFontSet Fonts => ThemeFonts.For(DeviceDpi);

    /// <summary>Sets the client size and minimum client size in DIP; applied now and again after DPI changes.</summary>
    public void SetClientSizeDip(Size clientDip, Size minimumClientDip)
    {
        _clientSizeDip = clientDip;
        _minimumClientSizeDip = minimumClientDip;
        ApplySizeForDpi(resizeClient: true);
    }

    /// <summary>Paints the gradient slice behind a child (<paramref name="area"/> in client coordinates).</summary>
    public void PaintSurface(Graphics g, Rectangle area) => PaintGradient(g, area);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyDarkTitleBar(Handle);
        ApplySizeForDpi(resizeClient: true);
        OnThemeDpiChanged();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Windows 10 sometimes needs the dark title bar attribute again after the first show.
        NativeMethods.ApplyDarkTitleBar(Handle);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplySizeForDpi(resizeClient: false);
        OnThemeDpiChanged();
    }

    /// <summary>Called after the DPI changed (and once when the handle is created): refresh fonts and re-layout.</summary>
    protected virtual void OnThemeDpiChanged()
    {
        Font = Fonts.Body;
        PerformLayout();
        Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintGradient(e.Graphics, e.ClipRectangle);

    private void PaintGradient(Graphics g, Rectangle area)
    {
        var client = ClientRectangle;
        if (client.Height <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        // A scrolling form anchors the gradient to its content, so scrolled (blitted) pixels and repainted children agree.
        var top = AutoScroll ? AutoScrollPosition.Y : 0;
        var height = AutoScroll ? Math.Max(client.Height, DisplayRectangle.Height) : client.Height;
        using var brush = new LinearGradientBrush(
            new Rectangle(0, top - 1, Math.Max(1, client.Width), height + 2),
            Theme.BgTop,
            Theme.BgBottom,
            LinearGradientMode.Vertical);
        g.FillRectangle(brush, area);
    }

    private void ApplySizeForDpi(bool resizeClient)
    {
        if (_minimumClientSizeDip != Size.Empty)
        {
            var chrome = Size - ClientSize;
            MinimumSize = new Size(this.Px(_minimumClientSizeDip.Width), this.Px(_minimumClientSizeDip.Height)) + chrome;
        }

        if (resizeClient && _clientSizeDip != Size.Empty)
        {
            ClientSize = new Size(this.Px(_clientSizeDip.Width), this.Px(_clientSizeDip.Height));
        }
    }
}
