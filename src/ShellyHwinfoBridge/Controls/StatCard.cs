using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A live-value card: tracked uppercase caption (InkSecondary), large value (Ink, or Accent for the DC card) and a
/// secondary line. In placeholder state the hint text replaces the value and sub-line.
/// </summary>
internal sealed class StatCard : CardPanel
{
    private string _caption = string.Empty;
    private string _value = string.Empty;
    private string _sub = string.Empty;
    private UiTone _valueTone = UiTone.Ink;
    private bool _placeholder;

    public StatCard()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    public string Caption => _caption;

    public string Value => _value;

    public string Sub => _sub;

    public UiTone ValueTone => _valueTone;

    public bool Placeholder => _placeholder;

    /// <summary>Sets all fields; repaints only when something changed.</summary>
    /// <param name="caption">Caption, drawn uppercase ("AC Power (Wall)").</param>
    /// <param name="value">Value with unit ("312.4 W"); in placeholder state the hint text.</param>
    /// <param name="sub">Secondary line ("from Shelly plug").</param>
    /// <param name="valueTone">Value colour (Ink, or Accent for DC).</param>
    /// <param name="placeholder">Whether <paramref name="value"/> is a hint instead of a number.</param>
    public void Apply(string caption, string value, string sub, UiTone valueTone, bool placeholder)
    {
        caption ??= string.Empty;
        value ??= string.Empty;
        sub ??= string.Empty;
        if (caption == _caption && value == _value && sub == _sub && valueTone == _valueTone && placeholder == _placeholder)
        {
            return;
        }

        _caption = caption;
        _value = value;
        _sub = sub;
        _valueTone = valueTone;
        _placeholder = placeholder;
        AccessibleName = caption;
        AccessibleDescription = placeholder ? value : value + ", " + sub;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var f = Fonts;
        var left = this.Px(24);
        var width = ClientSize.Width - left - this.Px(16);
        var tracking = this.PxF(Theme.CaptionTracking);

        var captionSize = Gfx.MeasureTracked(_caption, f.Caption, tracking);
        var valueHeight = Gfx.Measure("0", f.StatValue).Height;
        var subHeight = Gfx.Measure("Ag", f.Body).Height;
        var gap1 = this.Px(4);
        var gap2 = this.Px(2);
        var total = captionSize.Height + gap1 + valueHeight + gap2 + subHeight;
        var y = Math.Max(this.Px(16), (ClientSize.Height - total) / 2);

        Gfx.DrawTracked(g, _caption, f.Caption, new Point(left, y), Theme.InkSecondary, tracking);
        y += captionSize.Height + gap1;

        if (_placeholder)
        {
            var area = new Rectangle(left, y, width, ClientSize.Height - y - this.Px(12));
            Gfx.Text(g, _value, f.Body, area, Theme.InkSecondary, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            return;
        }

        Gfx.Text(g, _value, f.StatValue, new Rectangle(left - this.Px(1), y, width, valueHeight), Theme.ToneColor(_valueTone));
        y += valueHeight + gap2;
        Gfx.Text(g, _sub, f.Body, new Rectangle(left, y, width, subHeight), Theme.InkSecondary);
    }
}
