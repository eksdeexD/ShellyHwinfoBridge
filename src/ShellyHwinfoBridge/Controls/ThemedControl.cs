using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// Base of the owner-drawn controls: double-buffered user paint, backdrop painted from the themed parent
/// ("fake transparency"), hover/press tracking and per-DPI fonts.
/// </summary>
internal abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor,
            true);
        ForeColor = Theme.Ink;

        // Static by default; interactive controls switch TabStop on.
        TabStop = false;
    }

    /// <summary>Gets the fonts for the current DPI.</summary>
    protected ThemeFontSet Fonts => ThemeFonts.For(DeviceDpi);

    /// <summary>Gets whether the mouse is over the control.</summary>
    protected bool Hovered { get; private set; }

    /// <summary>Gets whether the left mouse button is held on the control.</summary>
    protected bool Pressed { get; private set; }

    /// <summary>Gets whether to draw the keyboard focus border.</summary>
    protected bool ShowFocus => Focused && ShowFocusCues;

    protected override void OnPaintBackground(PaintEventArgs e) => Gfx.PaintBackdrop(this, e.Graphics);

    protected override void OnMouseEnter(EventArgs e)
    {
        Hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        Hovered = false;
        Pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Pressed = true;
            if (CanFocus && TabStop)
            {
                Focus();
            }

            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (Pressed)
        {
            Pressed = false;
            Invalidate();
        }

        base.OnMouseUp(e);
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        OnThemeDpiChanged();
        Invalidate();
    }

    /// <summary>Called after the control's DPI changed; re-measure here.</summary>
    protected virtual void OnThemeDpiChanged()
    {
    }

    /// <summary>Draws the 1 px accent focus border inside <paramref name="r"/>.</summary>
    protected void DrawFocusBorder(Graphics g, RectangleF r, float radius)
    {
        if (ShowFocus)
        {
            Gfx.DrawRounded(g, Theme.FocusBorder, Math.Max(1f, this.PxF(1)), r, radius);
        }
    }
}

/// <summary>
/// A layout container that is "transparent": its BackColor is Transparent, so <see cref="Gfx.PaintBackdrop"/>
/// looks through it to the themed form or card behind.
/// </summary>
internal class ThemedPanel : Panel
{
    public ThemedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Ink;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Gfx.PaintBackdrop(this, e.Graphics);
}