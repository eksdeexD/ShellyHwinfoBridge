using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The "Customize HWiNFO sensor names" table (<see cref="SensorNamesModel"/>), hosted by a <see cref="StepDialog"/>:
/// a header row (DEFAULT NAME, YOUR NAME, "Reset to defaults") and one row per published sensor with its default name,
/// an editable name (empty = default, shown as the placeholder) and the row's validation error. The rows scroll when
/// the dialog is short. Nothing is applied before Save.
/// </summary>
internal sealed class SensorNamesView : WizardStepView
{
    private const float HeaderHeight = 34;
    private const float RowHeight = 42;
    private const float DefaultWidth = 300;
    private const float NameWidth = 340;

    private readonly SensorNamesModel _model;
    private readonly TextLabel _defaultCaption = new() { Text = "Default name", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _nameCaption = new() { Text = "Your name", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLink _reset = new() { Text = "Reset to defaults" };
    private readonly ScrollArea _rows = new();
    private readonly List<(string KeyId, TextLabel Default, ThemedTextBox Name, InfoLine Error)> _controls = [];
    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error, Visible = false };

    public SensorNamesView(SensorNamesModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        AccessibleName = "HWiNFO sensor names";
        var index = 0;
        foreach (var row in model.Rows)
        {
            var keyId = row.KeyId;
            var label = new TextLabel { Text = row.DefaultName };
            var name = new ThemedTextBox { Placeholder = row.DefaultName, AccessibleName = "Your name for " + row.DefaultName, MaxLength = Core.Hwinfo.SensorNaming.MaxNameLength, TabIndex = index++ };
            var error = new InfoLine { Tone = InfoTone.Error, Visible = false };
            name.TextChanged += (_, _) =>
            {
                if (!IsRefreshing)
                {
                    _model.SetCustomName(keyId, name.Text);
                }
            };
            _rows.Controls.AddRange([label, name, error]);
            _controls.Add((keyId, label, name, error));
        }

        _reset.TabIndex = 0;
        _rows.TabIndex = 1;
        Controls.AddRange([_defaultCaption, _nameCaption, _reset, _rows, _applyError]);
        _reset.Click += (_, _) => _model.ResetToDefaults();
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    public override void OnStepShown()
    {
        if (_controls.Count > 0)
        {
            _controls[0].Name.Focus();
        }
    }

    protected override void OnRefreshView()
    {
        var rows = _model.Rows;
        for (var i = 0; i < _controls.Count && i < rows.Count; i++)
        {
            var row = rows[i];
            var (_, _, name, error) = _controls[i];
            SetTextIfChanged(name, row.CustomName);
            name.HasError = row.Error is not null;
            error.Visible = row.Error is not null;
            error.Text = row.Error ?? string.Empty;
            name.AccessibleDescription = row.Error;
        }

        _reset.Enabled = rows.Any(r => r.CustomName.Length > 0);
        _applyError.Visible = _model.ApplyError is not null;
        _applyError.Text = _model.ApplyError ?? string.Empty;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        var resetSize = _reset.GetPreferredSize(Size.Empty);
        _defaultCaption.Bounds = Dip(4, 6, DefaultWidth, 22);
        _nameCaption.Bounds = Dip(4 + DefaultWidth + 16, 6, NameWidth, 22);
        _reset.Bounds = new Rectangle(Width - resetSize.Width, Px(4), resetSize.Width, resetSize.Height);

        var errorHeight = _applyError.Visible ? 32f : 0f;
        _rows.Bounds = Dip(0, HeaderHeight, w, Math.Max(RowHeight, h - HeaderHeight - errorHeight));
        _applyError.Bounds = Dip(0, h - 28, w, 28);

        var scroll = _rows.AutoScrollPosition;
        var errorLeft = 4 + DefaultWidth + 16 + NameWidth + 16;
        var errorWidth = Math.Max(0, w - errorLeft - 24);
        for (var i = 0; i < _controls.Count; i++)
        {
            var (_, label, name, error) = _controls[i];
            var top = i * RowHeight;
            label.Bounds = Offset(Dip(4, top, DefaultWidth, 34), scroll);
            name.Bounds = Offset(Dip(4 + DefaultWidth + 16, top, NameWidth, 34), scroll);
            error.Bounds = Offset(Dip(errorLeft, top + 3, errorWidth, 28), scroll);
        }

        var content = new Size(0, Px(_controls.Count * RowHeight));
        if (_rows.AutoScrollMinSize != content)
        {
            _rows.AutoScrollMinSize = content;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    private static Rectangle Offset(Rectangle r, Point scroll) => new(r.X + scroll.X, r.Y + scroll.Y, r.Width, r.Height);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            RefreshView();
        }
    }

    /// <summary>A transparent panel that scrolls its rows and repaints the backdrop the scroll blitted.</summary>
    private sealed class ScrollArea : ThemedPanel
    {
        public ScrollArea()
        {
            AutoScroll = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.UseDarkExplorerTheme(this);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate(true);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate(true);
        }
    }
}
