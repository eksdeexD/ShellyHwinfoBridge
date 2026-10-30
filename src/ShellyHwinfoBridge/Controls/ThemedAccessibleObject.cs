namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// The accessibility object of the owner-drawn controls. The default one of a plain <see cref="Control"/> cannot be
/// invoked and does not know the checked state, so screen readers and UI Automation (through the MSAA bridge) would see
/// a button that does nothing and a switch that is always off. Each control sets the parts it needs: the default
/// action (Invoke / Toggle / Select), extra states (Checked, Selected), the value text and, for composite controls,
/// child items.
/// </summary>
internal class ThemedAccessibleObject : Control.ControlAccessibleObject
{
    private readonly Control _owner;

    public ThemedAccessibleObject(Control owner)
        : base(owner)
    {
        _owner = owner;
    }

    /// <summary>Gets or sets the default action's name ("Press", "Toggle", "Select"); used when <see cref="Action"/> is set.</summary>
    public Func<string?>? ActionName { get; init; }

    /// <summary>Gets or sets what the default action does (runs on the UI thread; ignored while disabled or hidden).</summary>
    public Action? Action { get; init; }

    /// <summary>Gets or sets states added to the base state (e.g. Checked).</summary>
    public Func<AccessibleStates>? ExtraState { get; init; }

    /// <summary>Gets or sets the value text (slider value, selected item).</summary>
    public Func<string?>? ValueText { get; init; }

    /// <summary>Gets or sets how a value set through accessibility is applied.</summary>
    public Action<string>? ValueSetter { get; init; }

    /// <summary>Gets or sets the accessible name; defaults to the control's AccessibleName.</summary>
    public Func<string?>? NameText { get; init; }

    /// <summary>Gets or sets the child items (segments, list rows); <c>null</c> for none.</summary>
    public Func<IReadOnlyList<AccessibleObject>>? Children { get; init; }

    public override string? Name
    {
        get => NameText?.Invoke() ?? base.Name;
        set => base.Name = value;
    }

    public override string? DefaultAction => Action is null ? base.DefaultAction : _owner.Enabled ? ActionName?.Invoke() ?? "Press" : null;

    public override AccessibleStates State
    {
        get
        {
            var state = base.State;
            if (ExtraState is not null)
            {
                state |= ExtraState();
            }

            return state;
        }
    }

    public override string? Value
    {
        get => ValueText is null ? base.Value : ValueText();
        set
        {
            if (ValueSetter is null)
            {
                base.Value = value;
            }
            else if (value is not null && _owner.Enabled)
            {
                ValueSetter(value);
            }
        }
    }

    public override void DoDefaultAction()
    {
        if (Action is null)
        {
            base.DoDefaultAction();
        }
        else if (_owner.Enabled && _owner.Visible)
        {
            Action();
        }
    }

    public override int GetChildCount() => Children is null ? base.GetChildCount() : Children().Count;

    public override AccessibleObject? GetChild(int index)
    {
        if (Children is null)
        {
            return base.GetChild(index);
        }

        var children = Children();
        return index >= 0 && index < children.Count ? children[index] : null;
    }
}

/// <summary>An item inside a composite control (a segment, a list row): selectable, with its own bounds.</summary>
internal sealed class ThemedItemAccessibleObject : AccessibleObject
{
    private readonly Control _owner;
    private readonly int _index;
    private readonly AccessibleRole _role;
    private readonly Func<int, string> _name;
    private readonly Func<int, bool> _isSelected;
    private readonly Action<int> _select;
    private readonly Func<int, Rectangle> _bounds;

    public ThemedItemAccessibleObject(Control owner, int index, AccessibleRole role, Func<int, string> name, Func<int, bool> isSelected, Action<int> select, Func<int, Rectangle> bounds)
    {
        _owner = owner;
        _index = index;
        _role = role;
        _name = name;
        _isSelected = isSelected;
        _select = select;
        _bounds = bounds;
    }

    public override string? Name
    {
        get => _name(_index);
        set { }
    }

    public override AccessibleRole Role => _role;

    public override AccessibleStates State
    {
        get
        {
            var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
            if (_isSelected(_index))
            {
                state |= AccessibleStates.Selected | AccessibleStates.Focused;
            }

            if (!_owner.Enabled)
            {
                state |= AccessibleStates.Unavailable;
            }

            return state;
        }
    }

    public override AccessibleObject? Parent => _owner.AccessibilityObject;

    public override Rectangle Bounds => _owner.IsHandleCreated ? _owner.RectangleToScreen(_bounds(_index)) : Rectangle.Empty;

    public override string? DefaultAction => "Select";

    public override void DoDefaultAction() => SelectItem();

    public override void Select(AccessibleSelection flags)
    {
        if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.AddSelection)) != 0)
        {
            SelectItem();
        }

        if ((flags & AccessibleSelection.TakeFocus) != 0 && _owner.CanFocus)
        {
            _owner.Focus();
        }
    }

    private void SelectItem()
    {
        if (_owner.Enabled && _owner.Visible)
        {
            _select(_index);
        }
    }
}
