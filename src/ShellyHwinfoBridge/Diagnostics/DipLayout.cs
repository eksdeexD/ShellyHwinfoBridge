using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>Fixed placements in DIP, converted to device pixels on every layout (for the gallery).</summary>
internal sealed class DipLayout
{
    private readonly List<(Control Control, RectangleF Dip)> _items = [];

    public T Place<T>(Control parent, T control, float x, float y, float width, float height)
        where T : Control
    {
        parent.Controls.Add(control);
        _items.Add((control, new RectangleF(x, y, width, height)));
        return control;
    }

    public void Apply(int dpi)
    {
        foreach (var (control, r) in _items)
        {
            control.Bounds = new Rectangle(Theme.Scale(r.X, dpi), Theme.Scale(r.Y, dpi), Theme.Scale(r.Width, dpi), Theme.Scale(r.Height, dpi));
        }
    }
}
