using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A themed hover tooltip, drawn like the energy chart's tooltip (Surface box, 1 px Border, chip radius, Body text): it
/// appears under its target after a short hover and disappears when the pointer leaves. It is a child of the target's
/// window (placed on top of everything), so it needs no separate popup window and shows in rendered previews.
/// </summary>
internal sealed class HoverTip : ThemedControl
{
    private const int MaxWidthDip = 300;
    private const int PadDip = 8;
    private const int OffsetDip = 6;

    private readonly Control _target;
    private readonly System.Windows.Forms.Timer _delay = new() { Interval = 450 };

    private HoverTip(Control target, string text)
    {
        _target = target;
        Text = text;
        Visible = false;
        TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.ToolTip;
        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            ShowTip();
        };
        target.MouseEnter += (_, _) => _delay.Start();
        target.MouseLeave += (_, _) => HideTip();
        target.MouseDown += (_, _) => HideTip();
        target.VisibleChanged += (_, _) => HideTip();
        target.Disposed += (_, _) => Dispose();
        target.AccessibleDescription = text;
    }

    /// <summary>Attaches a tooltip with <paramref name="text"/> to <paramref name="target"/>; it lives as long as the target.</summary>
    public static HoverTip Attach(Control target, string text)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new HoverTip(target, text ?? string.Empty);
    }

    /// <summary>Shows the tooltip at once (screen renderer).</summary>
    public void ShowTip()
    {
        if (_target.IsDisposed || !_target.Visible || _target.FindForm() is not { } form)
        {
            return;
        }

        if (Parent != form)
        {
            form.Controls.Add(this);
        }

        var pad = this.Px(PadDip);
        var maxText = this.Px(MaxWidthDip) - (2 * pad);
        var single = Gfx.Measure(Text, Fonts.Body);
        var textSize = single.Width <= maxText ? single : Gfx.MeasureWrapped(Text, Fonts.Body, maxText);
        var size = new Size(textSize.Width + (2 * pad) + this.Px(2), textSize.Height + (2 * pad));

        // Under the target, left-aligned with it; above it when there is no room below; always inside the window.
        var origin = form.PointToClient(_target.Parent!.PointToScreen(_target.Location));
        var x = Math.Clamp(origin.X, 0, Math.Max(0, form.ClientSize.Width - size.Width));
        var y = origin.Y + _target.Height + this.Px(OffsetDip);
        if (y + size.Height > form.ClientSize.Height)
        {
            y = Math.Max(0, origin.Y - this.Px(OffsetDip) - size.Height);
        }

        Bounds = new Rectangle(new Point(x, y), size);
        Visible = true;
        BringToFront();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _delay.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var box = new RectangleF(0, 0, Width - 1, Height - 1);
        Gfx.FillAndBorder(g, Theme.Surface, Theme.Border, Math.Max(1f, this.PxF(1)), box, this.PxF(Theme.ChipRadius));
        var pad = this.Px(PadDip);
        TextRenderer.DrawText(
            g,
            Text,
            Fonts.Body,
            new Rectangle(pad, pad, Width - (2 * pad), Height - (2 * pad)),
            Theme.Ink,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private void HideTip()
    {
        _delay.Stop();
        if (Visible)
        {
            Visible = false;
        }
    }
}
