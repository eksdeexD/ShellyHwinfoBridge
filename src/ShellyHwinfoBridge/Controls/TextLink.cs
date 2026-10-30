using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>Colour of a <see cref="TextLink"/>.</summary>
internal enum LinkTone
{
    /// <summary>Accent orange ("Change…", "Edit…", "Advanced: edit formula").</summary>
    Accent,

    /// <summary>Ink ("Cancel").</summary>
    Ink,

    /// <summary>Error red ("Remove HWiNFO sensors", "Exit ShellyHwinfoBridge").</summary>
    Danger,
}

/// <summary>A clickable text link. Sizes itself to its text (<see cref="Control.AutoSize"/> is on).</summary>
internal sealed class TextLink : ThemedControl
{
    private LinkTone _tone = LinkTone.Accent;
    private bool _underline;
    private bool _semibold;

    public TextLink()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AutoSize = true;
        AccessibleRole = AccessibleRole.Link;
    }

    /// <summary>Gets or sets the colour.</summary>
    public LinkTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets whether the text is always underlined (otherwise only on hover).</summary>
    public bool Underline
    {
        get => _underline;
        set
        {
            _underline = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets whether the text is semibold (as "Change…" in Settings).</summary>
    public bool Semibold
    {
        get => _semibold;
        set
        {
            _semibold = value;
            AdjustSize();
            Invalidate();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var s = Gfx.Measure(Text, CurrentFont());
        return new Size(s.Width + this.Px(2), s.Height + this.Px(4));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AccessibleName = Text;
        AdjustSize();
        base.OnTextChanged(e);
    }

    protected override void OnThemeDpiChanged() => AdjustSize();

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var color = !Enabled ? Theme.InkDisabled : Tone switch
        {
            LinkTone.Ink => Theme.Ink,
            LinkTone.Danger => Theme.Error,
            _ => Hovered ? Theme.AccentHover : Theme.Accent,
        };
        var font = CurrentFont();
        if (Underline || (Hovered && Enabled))
        {
            using var underlined = new Font(font, font.Style | FontStyle.Underline);
            Gfx.Text(e.Graphics, Text, underlined, ClientRectangle, color, Gfx.SingleLine | TextFormatFlags.VerticalCenter);
        }
        else
        {
            Gfx.Text(e.Graphics, Text, font, ClientRectangle, color, Gfx.SingleLine | TextFormatFlags.VerticalCenter);
        }

        if (ShowFocus)
        {
            using var pen = new Pen(Theme.FocusBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private Font CurrentFont() => Semibold ? Fonts.BodySemibold : Fonts.Body;

    private void AdjustSize()
    {
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }
    }
}
