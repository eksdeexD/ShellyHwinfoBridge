using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A device colour swatch: a rounded square (radius 5) filled with <see cref="Colour"/> and a 1 px Border ring. When
/// <see cref="Clickable"/> it is focusable and opens the colour picker of its owner on click, Enter or Space
/// (the owner handles <see cref="Control.Click"/>).
/// </summary>
internal sealed class ColourSwatch : ThemedControl
{
    private Color _colour = Theme.Accent;
    private bool _clickable;

    public ColourSwatch()
    {
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
    }

    /// <summary>Gets or sets the colour.</summary>
    public Color Colour
    {
        get => _colour;
        set
        {
            if (_colour != value)
            {
                _colour = value;
                AccessibleDescription = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                Invalidate();
            }
        }
    }

    /// <summary>Gets or sets whether the swatch is a button that picks a colour.</summary>
    public bool Clickable
    {
        get => _clickable;
        set
        {
            _clickable = value;
            TabStop = value;
            Cursor = value ? Cursors.Hand : Cursors.Default;
            AccessibleRole = value ? AccessibleRole.PushButton : AccessibleRole.Graphic;
            Invalidate();
        }
    }

    /// <summary>Parses <c>#RRGGBB</c>; returns <paramref name="fallback"/> for anything else.</summary>
    public static Color Parse(string? hex, Color fallback)
    {
        var text = (hex ?? string.Empty).Trim().TrimStart('#');
        return text.Length == 6 && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
            : fallback;
    }

    /// <summary>Formats a colour as <c>#RRGGBB</c>.</summary>
    public static string ToHex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    protected override AccessibleObject CreateAccessibilityInstance() => new ThemedAccessibleObject(this)
    {
        ActionName = () => _clickable ? "Pick colour" : null,
        Action = () =>
        {
            if (_clickable)
            {
                OnClick(EventArgs.Empty);
            }
        },
    };

    protected override bool IsInputKey(Keys keyData) => (_clickable && keyData is Keys.Enter or Keys.Space) || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_clickable && Enabled && e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (_clickable && Enabled)
        {
            base.OnClick(e);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var size = Math.Min(Width, Height) - this.PxF(4);
        var box = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
        var radius = this.PxF(5);
        var ring = ShowFocus ? Theme.FocusBorder : Hovered && _clickable && Enabled ? Theme.InkSecondary : Theme.Border;
        Gfx.FillAndBorder(g, Enabled ? _colour : Theme.Blend(_colour, Theme.Surface, 0.6f), ring, Math.Max(1f, this.PxF(ShowFocus ? 1.5f : 1f)), box, radius);
    }
}
