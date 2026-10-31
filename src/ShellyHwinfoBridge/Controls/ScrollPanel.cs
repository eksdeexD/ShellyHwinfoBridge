using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A transparent, vertically scrolling panel (the settings and Manage Shellys pages, the sensor-name rows): dark
/// scrollbar, repaints the backdrop the scroll blitted, and keeps its scroll position while the focus stays put.
/// </summary>
/// <remarks>
/// WinForms scrolls the window's active control into view on every layout pass of the form
/// (<c>Form.OnLayout</c> → <c>ContainerControl.ScrollActiveControlIntoView</c>) and whenever the window is activated. A
/// page scrolled away from its focused control would jump back to it on the next unrelated layout (the main window
/// re-lays out its header when the status text changes). <see cref="ScrollToControl"/> therefore only follows the focus
/// when it moved to another control (Tab, a click); otherwise it keeps the position the user scrolled to.
/// </remarks>
internal class ScrollPanel : ThemedPanel
{
    private Control? _scrolledFor;

    public ScrollPanel()
    {
        AutoScroll = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkExplorerTheme(this);
    }

    protected override Point ScrollToControl(Control activeControl)
    {
        if (ReferenceEquals(activeControl, _scrolledFor))
        {
            return AutoScrollPosition;
        }

        _scrolledFor = activeControl;
        return base.ScrollToControl(activeControl);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);

        // The backdrop is the window's gradient, fixed to the window: repaint what the scroll blitted.
        Invalidate(true);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate(true);
    }
}
