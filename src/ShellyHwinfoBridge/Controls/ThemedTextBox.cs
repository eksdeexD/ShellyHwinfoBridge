using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A text field: borderless inner <see cref="TextBox"/> on a Field-coloured rounded box; border Border, Accent on
/// focus, Error when <see cref="HasError"/>. <see cref="TextCommitted"/> fires on Enter (single-line) or when focus
/// leaves after an edit. <see cref="IsModified"/> tells whether the user typed since the last <see cref="SetText"/>.
/// </summary>
internal sealed class ThemedTextBox : UserControl, IThemedSurface
{
    // Initialised before the base constructor runs, because UserControl lays out (OnLayout) while being constructed.
    private readonly PlaceholderTextBox _inner = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Field,
        ForeColor = Theme.Ink,
    };

    private bool _hasError;
    private bool _monospace;
    private bool _hovered;
    private bool _dirtySinceCommit;

    public ThemedTextBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Transparent;
        _inner.TextChanged += (_, _) =>
        {
            IsModified = true;
            _dirtySinceCommit = true;
            OnTextChanged(EventArgs.Empty);
        };
        _inner.GotFocus += (_, _) => Invalidate();
        _inner.LostFocus += (_, _) =>
        {
            Invalidate();
            Commit();
        };
        _inner.KeyDown += OnInnerKeyDown;
        _inner.MouseEnter += (_, _) => SetHover(true);
        _inner.MouseLeave += (_, _) => SetHover(false);
        Controls.Add(_inner);
        ApplyFont();
    }

    /// <summary>Raised on Enter (single-line) or on leaving the field after an edit.</summary>
    public event EventHandler? TextCommitted;

    /// <summary>Gets or sets the text (setting it in code does not mark the field modified).</summary>
    [AllowNull]
    public override string Text
    {
        get => _inner.Text;
        set => SetText(value);
    }

    /// <summary>Gets or sets the placeholder shown while empty.</summary>
    public string Placeholder
    {
        get => _inner.Placeholder;
        set
        {
            _inner.Placeholder = value ?? string.Empty;
            _inner.Invalidate();
        }
    }

    /// <summary>Gets or sets whether the text is masked.</summary>
    public bool UseSystemPasswordChar
    {
        get => _inner.UseSystemPasswordChar;
        set => _inner.UseSystemPasswordChar = value;
    }

    /// <summary>Gets or sets whether the field has several lines (with a dark vertical scrollbar).</summary>
    public bool Multiline
    {
        get => _inner.Multiline;
        set
        {
            _inner.Multiline = value;
            _inner.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
            _inner.AcceptsReturn = value;
            _inner.WordWrap = value;
            PerformLayout();
        }
    }

    /// <summary>Gets or sets whether the field uses the monospace font.</summary>
    public bool Monospace
    {
        get => _monospace;
        set
        {
            _monospace = value;
            ApplyFont();
            PerformLayout();
        }
    }

    /// <summary>Gets or sets whether the field is read-only.</summary>
    public bool ReadOnly
    {
        get => _inner.ReadOnly;
        set => _inner.ReadOnly = value;
    }

    /// <summary>Gets or sets the maximum length.</summary>
    public int MaxLength
    {
        get => _inner.MaxLength;
        set => _inner.MaxLength = value;
    }

    /// <summary>Gets or sets whether the border shows an error.</summary>
    public bool HasError
    {
        get => _hasError;
        set
        {
            _hasError = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets whether the user typed since the text was last set in code.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsModified { get; set; }

    /// <summary>Gets the inner text box (for selection, focus and accessibility tweaks).</summary>
    public TextBox InnerTextBox => _inner;

    /// <summary>Sets the text from code without marking it modified or raising <see cref="TextCommitted"/>.</summary>
    public void SetText(string? text)
    {
        if (_inner.Text == (text ?? string.Empty))
        {
            return;
        }

        _inner.Text = text ?? string.Empty;
        IsModified = false;
        _dirtySinceCommit = false;
    }

    /// <summary>Paints the field fill behind children (the inner text box).</summary>
    public void PaintSurface(Graphics g, Rectangle area)
    {
        using var brush = new SolidBrush(Theme.Field);
        g.FillRectangle(brush, area);
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(this.Px(120), proposedSize.Width), Multiline ? this.Px(120) : this.Px(44));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkExplorerTheme(_inner);

        // Screen readers land on the inner edit box: give it the field's name (the outer box is only the frame).
        _inner.AccessibleName ??= AccessibleName;
        _inner.AccessibleDescription ??= string.IsNullOrEmpty(Placeholder) ? null : Placeholder;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _inner.Focus();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        SetHover(true);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _inner.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _inner.ForeColor = Enabled ? Theme.Ink : Theme.InkDisabled;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyFont();
        PerformLayout();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var padX = this.Px(14);
        if (Multiline)
        {
            var padY = this.Px(10);
            _inner.Bounds = new Rectangle(padX, padY, Math.Max(1, Width - padX - this.Px(4)), Math.Max(1, Height - (2 * padY)));
        }
        else
        {
            var h = _inner.PreferredHeight;
            _inner.Bounds = new Rectangle(padX, (Height - h) / 2, Math.Max(1, Width - (2 * padX)), h);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Gfx.PaintBackdrop(this, e.Graphics);
        var border = !Enabled ? Theme.Border : _hasError ? Theme.Error : _inner.Focused ? Theme.FocusBorder : _hovered ? Theme.HoverBorder : Theme.Border;
        Gfx.FillAndBorder(e.Graphics, Theme.Field, border, Math.Max(1f, this.PxF(1)), new RectangleF(0, 0, Width, Height), this.PxF(Theme.ChipRadius));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    private void ApplyFont()
    {
        var fonts = ThemeFonts.For(DeviceDpi);
        _inner.Font = _monospace ? fonts.Mono : fonts.Body;
    }

    private void SetHover(bool hovered)
    {
        if (_hovered != hovered)
        {
            _hovered = hovered;
            Invalidate();
        }
    }

    private void OnInnerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !Multiline)
        {
            e.SuppressKeyPress = true;
            _dirtySinceCommit = true;
            Commit();
        }
    }

    private void Commit()
    {
        if (!_dirtySinceCommit)
        {
            return;
        }

        _dirtySinceCommit = false;
        TextCommitted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A borderless text box that draws its placeholder in InkSecondary (the built-in one uses GrayText).</summary>
    private sealed class PlaceholderTextBox : TextBox
    {
        private const int WmPaint = 0x000F;
        private const int WmPrintClient = 0x0318;

        public string Placeholder { get; set; } = string.Empty;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if ((m.Msg != WmPaint && m.Msg != WmPrintClient) || TextLength > 0 || Focused || Placeholder.Length == 0)
            {
                return;
            }

            using var g = m.Msg == WmPrintClient ? Graphics.FromHdc(m.WParam) : CreateGraphics();
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (Multiline ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Placeholder, Font, new Rectangle(1, 0, ClientSize.Width - 1, ClientSize.Height), Theme.InkSecondary, BackColor, flags);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (TextLength <= 1)
            {
                Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }
}