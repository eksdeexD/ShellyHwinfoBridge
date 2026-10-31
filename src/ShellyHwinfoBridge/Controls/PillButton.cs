using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>Button style.</summary>
internal enum PillVariant
{
    /// <summary>Accent fill, white semibold text.</summary>
    Primary,

    /// <summary>Transparent with a 1 px Border, Ink text.</summary>
    Secondary,
}

/// <summary>
/// The app's one button style: a rectangle without corner radius (Primary: accent fill; Secondary: 1 px border).
/// Space/Enter click; usable as AcceptButton/CancelButton. Commands that are not buttons use a semibold <see cref="TextLink"/>.
/// </summary>
internal sealed class PillButton : ThemedControl, IButtonControl
{
    private PillVariant _variant = PillVariant.Secondary;
    private bool _isDefault;

    public PillButton()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }

    /// <summary>Gets or sets the style.</summary>
    public PillVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            Invalidate();
        }
    }

    /// <inheritdoc />
    public DialogResult DialogResult { get; set; }

    /// <inheritdoc />
    public void NotifyDefault(bool value)
    {
        _isDefault = value;
        Invalidate();
    }

    /// <inheritdoc />
    public void PerformClick()
    {
        if (CanSelect || (Enabled && Visible))
        {
            OnClick(EventArgs.Empty);
        }
    }

    /// <summary>Preferred size: text plus 16 DIP side padding (at least 72 DIP), <see cref="Theme.ButtonHeight"/> high.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = Gfx.Measure(Text, ButtonFont());
        return new Size(Math.Max(this.Px(72), text.Width + this.Px(32)), this.Px(Theme.ButtonHeight));
    }

    protected override void OnClick(EventArgs e)
    {
        if (DialogResult != DialogResult.None && FindForm() is { } form)
        {
            form.DialogResult = DialogResult;
        }

        base.OnClick(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new ThemedAccessibleObject(this) { ActionName = () => "Press", Action = PerformClick };

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
        {
            PerformClick();
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AccessibleName = Text;
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        const float radius = 0f;
        var border = Math.Max(1f, this.PxF(1));
        Color text;
        if (Variant == PillVariant.Primary)
        {
            var fill = !Enabled ? Theme.InkDisabled : Pressed ? Theme.AccentPressed : Hovered ? Theme.AccentHover : Theme.Accent;
            Gfx.FillRounded(g, fill, r, radius);
            text = Enabled ? Theme.OnAccent : Theme.BgBottom;
            if (ShowFocus)
            {
                Gfx.DrawRounded(g, Theme.Ink, border, RectangleF.Inflate(r, -this.PxF(2), -this.PxF(2)), radius);
            }
        }
        else
        {
            if (Enabled && (Hovered || Pressed))
            {
                Gfx.FillRounded(g, Pressed ? Theme.Surface : Theme.SecondaryHover, r, radius);
            }

            var outline = !Enabled ? Theme.Border : ShowFocus ? Theme.FocusBorder : Hovered ? Theme.HoverBorder : Theme.Border;
            Gfx.DrawRounded(g, outline, border, r, radius);
            text = Enabled ? Theme.Ink : Theme.InkDisabled;
        }

        Gfx.Text(g, Text, ButtonFont(), ClientRectangle, text, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Font ButtonFont() => Variant == PillVariant.Primary ? Fonts.BodySemibold : Fonts.Body;
}
