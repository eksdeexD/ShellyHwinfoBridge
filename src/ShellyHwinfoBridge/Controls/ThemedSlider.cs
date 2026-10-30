using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// A horizontal slider: 4 DIP Border track, Accent fill up to an 18 DIP Accent knob. Values snap to
/// <see cref="SmallChange"/> steps from <see cref="Minimum"/>. <see cref="ValueChanged"/> fires while dragging,
/// <see cref="ValueCommitted"/> on mouse up or after a key press.
/// </summary>
internal sealed class ThemedSlider : ThemedControl
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _smallChange = 1;
    private bool _dragging;
    private int _valueAtPress;

    public ThemedSlider()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
    }

    /// <summary>Raised whenever <see cref="Value"/> changes (also during dragging).</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised when the user finishes a change (mouse up, key press).</summary>
    public event EventHandler? ValueCommitted;

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            _maximum = Math.Max(_maximum, value);
            SetValueCore(_value, raise: false);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            SetValueCore(_value, raise: false);
            Invalidate();
        }
    }

    /// <summary>Gets or sets the step (arrow keys and snapping); at least 1.</summary>
    public int SmallChange
    {
        get => _smallChange;
        set => _smallChange = Math.Max(1, value);
    }

    /// <summary>Gets or sets the step for Page Up/Down; defaults to 10 steps.</summary>
    public int LargeChange { get; set; }

    /// <summary>Gets or sets the value (clamped and snapped). Setting in code raises <see cref="ValueChanged"/> only.</summary>
    public int Value
    {
        get => _value;
        set => SetValueCore(value, raise: true);
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(this.Px(160), proposedSize.Width), this.Px(28));

    protected override AccessibleObject CreateAccessibilityInstance() => new ThemedAccessibleObject(this)
    {
        ValueText = () => _value.ToString(System.Globalization.CultureInfo.CurrentCulture),
        ValueSetter = text =>
        {
            if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var parsed))
            {
                var before = _value;
                Value = parsed;
                if (_value != before)
                {
                    ValueCommitted?.Invoke(this, EventArgs.Empty);
                }
            }
        },
    };

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var large = LargeChange > 0 ? LargeChange : _smallChange * 10;
        int? target = e.KeyCode switch
        {
            Keys.Left or Keys.Down => _value - _smallChange,
            Keys.Right or Keys.Up => _value + _smallChange,
            Keys.PageDown => _value - large,
            Keys.PageUp => _value + large,
            Keys.Home => _minimum,
            Keys.End => _maximum,
            _ => null,
        };
        if (target is int t)
        {
            var before = _value;
            Value = t;
            if (_value != before)
            {
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }

            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _valueAtPress = _value;
            Capture = true;
            Value = ValueAt(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Value = ValueAt(e.X);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            if (_value != _valueAtPress)
            {
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }
        }

        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var knob = this.PxF(18);
        var trackHeight = this.PxF(5);
        var left = knob / 2f;
        var right = Width - (knob / 2f);
        var cy = Height / 2f;
        var track = new RectangleF(left, cy - (trackHeight / 2f), Math.Max(0, right - left), trackHeight);
        Gfx.FillRounded(g, Theme.Border, track, trackHeight / 2f);
        var x = KnobX();
        var accent = Enabled ? Theme.Accent : Theme.InkDisabled;
        if (x > left)
        {
            Gfx.FillRounded(g, accent, new RectangleF(left, track.Top, x - left, trackHeight), trackHeight / 2f);
        }

        Gfx.Smooth(g);
        using (var brush = new SolidBrush(Enabled && (Hovered || _dragging) ? Theme.AccentHover : accent))
        {
            g.FillEllipse(brush, x - (knob / 2f), cy - (knob / 2f), knob, knob);
        }

        if (ShowFocus)
        {
            using var pen = new Pen(Theme.Ink, Math.Max(1f, this.PxF(1)));
            var ring = knob + this.PxF(5);
            g.DrawEllipse(pen, x - (ring / 2f), cy - (ring / 2f), ring, ring);
        }
    }

    private float KnobX()
    {
        var knob = this.PxF(18);
        var span = Math.Max(1f, Width - knob);
        var fraction = _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);
        return (knob / 2f) + (fraction * span);
    }

    private int ValueAt(int x)
    {
        var knob = this.PxF(18);
        var span = Math.Max(1f, Width - knob);
        var fraction = Math.Clamp((x - (knob / 2f)) / span, 0f, 1f);
        return _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
    }

    private void SetValueCore(int value, bool raise)
    {
        value = Math.Clamp(value, _minimum, _maximum);
        var steps = (int)Math.Round((value - _minimum) / (double)_smallChange);
        value = Math.Clamp(_minimum + (steps * _smallChange), _minimum, _maximum);
        if (value == _value)
        {
            return;
        }

        _value = value;
        AccessibleDescription = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Invalidate();
        if (IsHandleCreated)
        {
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
        if (raise)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
