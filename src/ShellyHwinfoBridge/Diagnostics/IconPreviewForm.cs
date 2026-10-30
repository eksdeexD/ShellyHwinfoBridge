using System.Drawing.Drawing2D;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Icons;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>Shows the tray icon states at 16/20/24/32 px, actual size and magnified, for visual checks (rendered by --render-screens).</summary>
internal sealed class IconPreviewForm : ThemedForm
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    public IconPreviewForm()
    {
        Text = "Tray icons";
        SetClientSizeDip(new Size(760, 420), new Size(760, 420));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var y = this.Px(24);
        foreach (var state in Enum.GetValues<TrayIconState>())
        {
            Gfx.Text(g, state.ToString(), Fonts.Body, new Rectangle(this.Px(24), y, this.Px(100), this.Px(32)), Theme.Ink);
            var x = this.Px(130);
            foreach (var size in Sizes)
            {
                using var bitmap = IconFactory.Render(state, size);
                g.DrawImage(bitmap, x, y);
                var state0 = g.Save();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bitmap, new Rectangle(x + size + this.Px(8), y, size * 4, size * 4));
                g.Restore(state0);
                x += (size * 5) + this.Px(24);
            }

            y += this.Px(132);
        }
    }
}
