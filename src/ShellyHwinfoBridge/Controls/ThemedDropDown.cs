using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>An entry of a <see cref="ThemedDropDown"/>.</summary>
/// <param name="Value">The value (any object; may be <c>null</c> for "Any" or "—").</param>
/// <param name="Text">The text shown.</param>
internal sealed record DropDownItem(object? Value, string Text);

/// <summary>
/// A drop-down field: Field fill, 1 px border, radius 8, optional InkSecondary prefix ("Brand"), the selected text and a
/// chevron. Opens a <see cref="ContextMenuStrip"/> with <see cref="DarkMenuRenderer"/> (keyboard, wheel and auto-close
/// come from the menu). Up/Down change the selection without opening; Space, Enter, F4 or Alt+Down open it.
/// </summary>
internal sealed class ThemedDropDown : ThemedControl
{
    private readonly List<DropDownItem> _items = [];
    private int _selectedIndex = -1;
    private string? _prefix;
    private ContextMenuStrip? _menu;

    public ThemedDropDown()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    /// <summary>Raised when the user (or code) changes the selection.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Gets or sets the prefix drawn before the value ("Brand"), or null.</summary>
    public string? Prefix
    {
        get => _prefix;
        set
        {
            _prefix = value;
            AccessibleName = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets the text shown when nothing is selected.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Gets the items.</summary>
    public IReadOnlyList<DropDownItem> Items => _items;

    /// <summary>Gets or sets the selected index (-1 for none). Setting raises <see cref="SelectionChanged"/> when it changes.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (value == _selectedIndex)
            {
                return;
            }

            _selectedIndex = value;
            AccessibleDescription = SelectedItem?.Text;
            Invalidate();
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the selected item, or null.</summary>
    public DropDownItem? SelectedItem => _selectedIndex >= 0 ? _items[_selectedIndex] : null;

    /// <summary>Gets the selected item's value, or null.</summary>
    public object? SelectedValue => SelectedItem?.Value;

    /// <summary>Replaces the items and selects <paramref name="selectedIndex"/> without raising <see cref="SelectionChanged"/>.</summary>
    public void SetItems(IEnumerable<DropDownItem> items, int selectedIndex = -1)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, -1, _items.Count - 1);
        AccessibleDescription = SelectedItem?.Text;
        Invalidate();
    }

    /// <summary>Selects the first item whose value equals <paramref name="value"/> (raises <see cref="SelectionChanged"/> on change).</summary>
    public void SelectValue(object? value) => SelectedIndex = _items.FindIndex(i => Equals(i.Value, value));

    /// <summary>Opens the list under the field.</summary>
    public void OpenList()
    {
        if (_items.Count == 0 || !Enabled)
        {
            return;
        }

        _menu?.Dispose();
        _menu = new ContextMenuStrip();
        DarkMenuRenderer.Apply(_menu);
        _menu.MinimumSize = new Size(Width, 0);
        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(_items[i].Text)
            {
                Checked = i == _selectedIndex,
                Padding = new Padding(this.Px(4), this.Px(3), this.Px(4), this.Px(3)),
            };
            item.Click += (_, _) => SelectedIndex = index;
            _menu.Items.Add(item);
        }

        _menu.Closed += (_, _) => Invalidate();
        _menu.Show(this, new Point(0, Height + this.Px(2)));
        if (_selectedIndex >= 0 && _selectedIndex < _menu.Items.Count)
        {
            _menu.Items[_selectedIndex].Select();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var prefix = string.IsNullOrEmpty(_prefix) ? 0 : Gfx.Measure(_prefix, Fonts.Body).Width + this.Px(8);
        var widest = _items.Count == 0 ? Gfx.Measure(Placeholder, Fonts.Body).Width : _items.Max(i => Gfx.Measure(i.Text, Fonts.Body).Width);
        return new Size(this.Px(10) + prefix + widest + this.Px(30), this.Px(Theme.FieldHeight));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ThemedAccessibleObject(this)
    {
        ActionName = () => "Open",
        Action = OpenList,
        ValueText = () => SelectedItem?.Text ?? Placeholder,
        ValueSetter = text => SelectedIndex = _items.FindIndex(i => string.Equals(i.Text, text, StringComparison.CurrentCultureIgnoreCase)) is var index and >= 0 ? index : _selectedIndex,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            OpenList();
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Space or Keys.Enter or Keys.F4 or (Keys.Alt | Keys.Down) || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.F4 || (e.KeyCode == Keys.Down && e.Alt))
        {
            OpenList();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down)
        {
            SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up)
        {
            SelectedIndex = Math.Max(0, _selectedIndex - 1);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        var radius = this.PxF(Theme.ChipRadius);
        var open = _menu is { Visible: true };
        var border = !Enabled ? Theme.Border : Focused || open ? Theme.FocusBorder : Hovered ? Theme.HoverBorder : Theme.Border;
        Gfx.FillAndBorder(g, Theme.Field, border, Math.Max(1f, this.PxF(1)), r, radius);

        var x = this.Px(10);
        if (!string.IsNullOrEmpty(_prefix))
        {
            var prefixWidth = Gfx.Measure(_prefix, Fonts.Body).Width;
            Gfx.Text(g, _prefix, Fonts.Body, new Rectangle(x, 0, prefixWidth + this.Px(2), Height), Theme.InkSecondary);
            x += prefixWidth + this.Px(8);
        }

        var chevron = this.PxF(10);
        var chevronLeft = Width - this.PxF(10) - chevron;
        var text = SelectedItem?.Text;
        var color = !Enabled ? Theme.InkDisabled : text is null ? Theme.InkDisabled : Theme.Ink;
        Gfx.Text(g, text ?? Placeholder, Fonts.Body, new Rectangle(x, 0, Math.Max(0, (int)chevronLeft - x - this.Px(8)), Height), color);
        Gfx.DrawChevronDown(g, Enabled ? Theme.Ink : Theme.InkDisabled, new RectangleF(chevronLeft, (Height - chevron) / 2f, chevron, chevron), Math.Max(1.5f, this.PxF(1.6f)));
    }
}
