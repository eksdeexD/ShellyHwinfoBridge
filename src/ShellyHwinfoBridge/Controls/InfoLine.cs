using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>Kind of an <see cref="InfoLine"/>.</summary>
internal enum InfoTone
{
    /// <summary>Plain secondary text, no icon ("Input voltage: 230 V curve …").</summary>
    Info,

    /// <summary>Warning triangle and Warning-coloured text.</summary>
    Warning,

    /// <summary>Error circle and Error-coloured text.</summary>
    Error,

    /// <summary>Success tick and Ink text.</summary>
    Success,

    /// <summary>Neutral ring and secondary text (a check that has not passed yet).</summary>
    Pending,
}

/// <summary>
/// A short status line with an optional icon and an optional trailing link ("Change", "Check again", "Continue
/// anyway"). Text wraps when the control is taller than one line; <see cref="GetPreferredHeight"/> gives the height.
/// Text without a link may use <c>**bold**</c> markup (<see cref="InlineMarkup"/>).
/// </summary>
internal sealed class InfoLine : ThemedControl
{
    private InfoTone _tone;
    private string? _linkText;
    private Rectangle _linkBounds;
    private bool _linkHover;

    public InfoLine()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>Raised when the link is clicked (or Enter/Space while focused).</summary>
    public event EventHandler? LinkClicked;

    /// <summary>Gets or sets the kind.</summary>
    public InfoTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets the trailing link text (null for none). A link makes the control focusable.</summary>
    public string? LinkText
    {
        get => _linkText;
        set
        {
            _linkText = string.IsNullOrEmpty(value) ? null : value;
            TabStop = _linkText is not null;
            AccessibleRole = _linkText is null ? AccessibleRole.StaticText : AccessibleRole.Link;
            Invalidate();
        }
    }

    /// <summary>Returns the height needed for the text at a width (device pixels).</summary>
    public int GetPreferredHeight(int width)
    {
        var textWidth = Math.Max(1, width - TextLeft() - LinkWidth());
        var lines = IsRich ? RichText.Measure(Text, Fonts.Body, Fonts.BodySemibold, textWidth).Height : Gfx.MeasureWrapped(Text, Fonts.Body, textWidth).Height;
        return Math.Max(lines, this.Px(24)) + this.Px(4);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = TextLeft() + (IsRich ? RichText.Measure(Text, Fonts.Body, Fonts.BodySemibold, int.MaxValue / 2).Width : Gfx.Measure(Text, Fonts.Body).Width) + LinkWidth() + this.Px(2);
        return new Size(width, GetPreferredHeight(width));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AccessibleName = InlineMarkup.Strip(Text);
        base.OnTextChanged(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ThemedAccessibleObject(this)
    {
        NameText = () => _linkText is null ? InlineMarkup.Strip(Text) : InlineMarkup.Strip(Text) + " · " + _linkText,
        ActionName = () => _linkText,
        Action = () =>
        {
            if (_linkText is not null)
            {
                LinkClicked?.Invoke(this, EventArgs.Empty);
            }
        },
    };

    protected override bool IsInputKey(Keys keyData) => (_linkText is not null && keyData is Keys.Enter or Keys.Space) || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_linkText is not null && e.KeyCode is Keys.Enter or Keys.Space)
        {
            LinkClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var over = _linkText is not null && _linkBounds.Contains(e.Location);
        if (over != _linkHover)
        {
            _linkHover = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (_linkText is not null && _linkBounds.Contains(e.Location))
        {
            LinkClicked?.Invoke(this, EventArgs.Empty);
        }

        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var f = Fonts.Body;
        var iconSize = this.Px(20);
        var lineHeight = Gfx.Measure("Ag", f).Height;
        var firstLineCenter = Math.Min(Height / 2, (lineHeight / 2) + this.Px(2));
        var wraps = Height > lineHeight + this.Px(12);
        var iconTop = wraps ? firstLineCenter - (iconSize / 2) : (Height - iconSize) / 2;
        if (HasIcon)
        {
            DrawIcon(g, new RectangleF(0, iconTop, iconSize, iconSize));
        }

        var color = _tone switch
        {
            InfoTone.Warning => Theme.Warning,
            InfoTone.Error => Theme.Error,
            InfoTone.Success => Theme.Ink,
            _ => Theme.InkSecondary,
        };
        var left = TextLeft();
        var linkWidth = LinkWidth();
        var textBounds = new Rectangle(left, 0, Math.Max(0, Width - left - linkWidth), Height);
        if (IsRich)
        {
            // Rich text is top-aligned on the first line like wrapped text, or centred when it fits one line.
            var size = RichText.Measure(Text, f, Fonts.BodySemibold, textBounds.Width);
            var top = Math.Max(0, (Height - size.Height) / 2);
            RichText.Draw(g, Text, f, Fonts.BodySemibold, new Rectangle(left, top, textBounds.Width, Height - top), color);
            return;
        }

        var flags = wraps
            ? TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
            : Gfx.SingleLine | TextFormatFlags.VerticalCenter;
        Gfx.Text(g, Text, f, textBounds, color, flags);

        if (_linkText is not null)
        {
            var textWidth = Math.Min(textBounds.Width, Gfx.Measure(Text, f).Width);
            var linkSize = Gfx.Measure(_linkText, f);
            var x = left + textWidth + this.Px(16);
            _linkBounds = new Rectangle(x, (Height - linkSize.Height) / 2 - this.Px(2), linkSize.Width + this.Px(2), linkSize.Height + this.Px(4));
            using var underline = new Font(f, FontStyle.Underline);
            Gfx.Text(g, _linkText, underline, _linkBounds, _linkHover ? Theme.AccentHover : Theme.Accent, Gfx.SingleLine | TextFormatFlags.VerticalCenter);
            if (ShowFocus)
            {
                using var pen = new Pen(Theme.FocusBorder);
                g.DrawRectangle(pen, _linkBounds.X, _linkBounds.Y, _linkBounds.Width - 1, _linkBounds.Height - 1);
            }
        }
    }

    private bool HasIcon => _tone != InfoTone.Info;

    private bool IsRich => _linkText is null && RichText.HasMarkup(Text);

    private int TextLeft() => HasIcon ? this.Px(20 + 12) : 0;

    private int LinkWidth() => _linkText is null ? 0 : Gfx.Measure(_linkText, Fonts.Body).Width + this.Px(20);

    private void DrawIcon(Graphics g, RectangleF r)
    {
        Gfx.Smooth(g);
        switch (_tone)
        {
            case InfoTone.Warning:
            {
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                var inset = r.Width * 0.04f;
                path.AddPolygon(new[]
                {
                    new PointF(r.Left + (r.Width / 2), r.Top + inset),
                    new PointF(r.Right - inset, r.Bottom - inset),
                    new PointF(r.Left + inset, r.Bottom - inset),
                });
                using var pen = new Pen(Theme.Warning, r.Width * 0.12f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                using var brush = new SolidBrush(Theme.Warning);
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
                DrawBang(g, r, Theme.BgBottom, r.Top + (r.Height * 0.36f), r.Top + (r.Height * 0.64f), r.Top + (r.Height * 0.80f));
                break;
            }

            case InfoTone.Error:
            {
                using var brush = new SolidBrush(Theme.Error);
                g.FillEllipse(brush, r);
                DrawBang(g, r, Theme.BgBottom, r.Top + (r.Height * 0.24f), r.Top + (r.Height * 0.56f), r.Top + (r.Height * 0.74f));
                break;
            }

            case InfoTone.Pending:
            {
                var ring = Math.Max(1.5f, this.PxF(1.5f));
                Gfx.DrawRounded(g, Theme.InkDisabled, ring, RectangleF.Inflate(r, -r.Width * 0.08f, -r.Height * 0.08f), r.Width / 2f);
                break;
            }

            case InfoTone.Success:
            {
                using var brush = new SolidBrush(Theme.Success);
                g.FillEllipse(brush, r);
                Gfx.DrawCheck(g, Theme.OnAccent, RectangleF.Inflate(r, -r.Width * 0.12f, -r.Height * 0.12f), r.Width * 0.11f);
                break;
            }
        }
    }

    private static void DrawBang(Graphics g, RectangleF r, Color color, float top, float bottom, float dotY)
    {
        var cx = r.Left + (r.Width / 2);
        var w = r.Width * 0.11f;
        using var pen = new Pen(color, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        g.DrawLine(pen, cx, top, cx, bottom);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - (w * 0.65f), dotY - (w * 0.65f), w * 1.3f, w * 1.3f);
    }
}
