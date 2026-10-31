using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 4 (design/final/wizard-step4-sensors.png), also hosted by the Settings "Edit" dialog: a scrollable card of
/// HWiNFO device groups with a checkbox, value and category per power sensor; the warnings; and the formula preview
/// with "Advanced: edit formula". Without shared memory: a notice with "Check again" and typed rows with "Add sensor".
/// </summary>
internal sealed class SensorStepView : WizardStepView
{
    private const float RowHeight = 28;
    private const float HeaderHeight = 24;
    private const float CategoryWidth = 130;

    private static readonly IReadOnlyList<WizardOption<SensorCategory?>> ManualCategories =
        SensorStepModel.CategoryOptions.Where(o => o.Value is not null).ToList();

    private readonly SensorStepModel _model;
    private readonly Action _reload;
    private readonly InfoLine _notice = new() { Tone = InfoTone.Warning, Text = SensorStepModel.SharedMemoryOffText, LinkText = "Check again" };
    private readonly CardPanel _card = new() { PaddingDip = 0, AccessibleName = "Sensors" };
    private readonly RowsPanel _rows = new();
    private readonly List<InfoLine> _warnings = [];
    private readonly CardPanel _formulaCard = new() { AccessibleName = "Formula preview" };
    private readonly TextLabel _formulaCaption = new() { Text = "Formula preview", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _formula = new() { Role = FontRole.Mono, WordWrap = true, AccessibleName = "Formula" };
    private readonly TextLink _advanced = new() { Text = "Advanced: edit formula", Semibold = true };
    private readonly ThemedTextBox _editor = new() { Multiline = true, Monospace = true, AccessibleName = "Formula" };
    private readonly TextLink _reset = new() { Text = "Reset to generated", Semibold = true };
    private readonly List<(Control Control, float X, float Y, float Width, float Height)> _placed = [];
    private readonly Dictionary<int, (ThemedCheckBox Box, TextLabel Value, ThemedDropDown Category)> _sensorControls = [];
    private readonly Dictionary<int, (ThemedTextBox Name, ThemedDropDown Category)> _manualControls = [];
    private TextLink? _addSensor;
    private string? _structure;
    private float _contentHeight;
    private bool _editorOpen;

    /// <summary>Creates the view.</summary>
    /// <param name="model">The step model.</param>
    /// <param name="reload">Reads HWiNFO's shared memory again and reloads the model ("Check again").</param>
    public SensorStepView(SensorStepModel model, Action reload)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _reload = reload ?? throw new ArgumentNullException(nameof(reload));
        _card.Controls.Add(_rows);
        _formulaCard.Controls.AddRange([_formulaCaption, _formula, _advanced, _editor, _reset]);
        Controls.AddRange([_notice, _card, _formulaCard]);
        _editorOpen = false;
        _advanced.Visible = false;

        _notice.LinkClicked += (_, _) => _reload();
        _advanced.Click += (_, _) =>
        {
            _editorOpen = !_editorOpen;
            RefreshView();
            if (_editorOpen)
            {
                _editor.Focus();
            }
        };
        _rows.Layout += (_, _) => LayoutRows();
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    protected override void OnRefreshView()
    {
        var structure = Structure();
        if (structure != _structure)
        {
            _structure = structure;
            BuildRows();
        }

        foreach (var row in _model.Groups.SelectMany(g => g.Rows))
        {
            if (_sensorControls.TryGetValue(row.Id, out var c))
            {
                c.Box.Checked = row.Checked;
                c.Value.Text = row.ValueText;
                c.Category.SelectedIndex = IndexOf(SensorStepModel.CategoryOptions, row.Category);
            }
        }

        foreach (var row in _model.ManualRows)
        {
            if (_manualControls.TryGetValue(row.Id, out var c))
            {
                SetTextIfChanged(c.Name, row.Name);
                c.Category.SelectedIndex = IndexOf(ManualCategories, row.Category);
                c.Name.HasError = !string.IsNullOrEmpty(row.Name) && !SensorNaming.IsValidName(row.Name);
            }
        }

        _notice.Visible = _model.IsManual;

        var warnings = _model.Warnings;
        while (_warnings.Count < warnings.Count)
        {
            var line = new InfoLine { Tone = InfoTone.Warning };
            _warnings.Add(line);
            Controls.Add(line);
        }

        for (var i = 0; i < _warnings.Count; i++)
        {
            _warnings[i].Visible = i < warnings.Count;
            if (i < warnings.Count)
            {
                _warnings[i].Text = warnings[i];
            }
        }

        _formula.Text = _model.CalculationPreview;
        _formula.Visible = !_editorOpen;
        _editor.Visible = _editorOpen;
        _reset.Visible = _editorOpen;
        _reset.Enabled = false;
        _advanced.Visible = false;
        if (!_editor.InnerTextBox.Focused || !_editorOpen)
        {
            SetTextIfChanged(_editor, _model.CalculationPreview);
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = Theme.Gap;
        const float p = Theme.CardPadding;

        var formulaTextHeight = Math.Max(20f, _formula.GetPreferredHeight(Px(w - (2 * p))) * 96f / DeviceDpi);
        var formulaHeight = _editorOpen ? 120f : 32 + formulaTextHeight + 10;
        var formulaTop = h - formulaHeight;
        _formulaCard.Bounds = Dip(0, formulaTop, w, formulaHeight);
        _formulaCaption.Bounds = Dip(p, 10, 300, 20);
        var linkSize = _advanced.GetPreferredSize(Size.Empty);
        _advanced.Bounds = new Rectangle(_formulaCard.Width - Px(p) - linkSize.Width, Px(9), linkSize.Width, linkSize.Height);
        _formula.Bounds = Dip(p, 32, w - (2 * p), formulaTextHeight);
        _editor.Bounds = Dip(p, 32, w - (2 * p), formulaHeight - 32 - 12);
        var resetSize = _reset.GetPreferredSize(Size.Empty);
        _reset.Bounds = new Rectangle(_advanced.Left - Px(20) - resetSize.Width, Px(9), resetSize.Width, resetSize.Height);

        var widthPx = Px(w);
        var bottom = _formulaCard.Top - Px(gap);
        var visibleWarnings = _warnings.Where(l => l.Visible).ToList();
        for (var i = visibleWarnings.Count - 1; i >= 0; i--)
        {
            var line = visibleWarnings[i];
            var height = Math.Max(Px(Theme.LineHeight), line.GetPreferredHeight(widthPx - Px(4)));
            line.Bounds = new Rectangle(Px(4), bottom - height, widthPx - Px(4), height);
            bottom -= height + Px(4);
        }

        if (visibleWarnings.Count > 0)
        {
            bottom -= Px(gap - 4);
        }

        var top = 0;
        if (_notice.Visible)
        {
            var height = _notice.GetPreferredHeight(widthPx);
            _notice.Bounds = new Rectangle(0, 0, widthPx, height);
            top = height + Px(8);
        }

        _card.Bounds = new Rectangle(0, top, widthPx, Math.Max(Px(80), bottom - top));
        var inset = Px(2);
        _rows.Bounds = new Rectangle(inset, Px(6), _card.Width - (2 * inset), _card.Height - Px(12));
        LayoutRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    private static int IndexOf(IReadOnlyList<WizardOption<SensorCategory?>> options, SensorCategory? value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Value == value)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnModelChanged(object? sender, EventArgs e) => RefreshView();

    private string Structure() => _model.IsManual
        ? "manual:" + string.Join(",", _model.ManualRows.Select(r => r.Id))
        : "groups:" + string.Join("|", _model.Groups.Select(g => g.Device + ":" + string.Join(",", g.Rows.Select(r => r.Id))));

    /// <summary>Creates the row controls for the current groups or manual rows (positions in DIP inside the scroll panel).</summary>
    private void BuildRows()
    {
        _rows.SuspendLayout();
        foreach (var (control, _, _, _, _) in _placed)
        {
            _rows.Controls.Remove(control);
            control.Dispose();
        }

        _placed.Clear();
        _sensorControls.Clear();
        _manualControls.Clear();
        _addSensor = null;
        var y = 6f;
        const float left = 14;

        if (_model.IsManual)
        {
            foreach (var row in _model.ManualRows)
            {
                var id = row.Id;
                var name = new ThemedTextBox { Placeholder = "Sensor name as shown in HWiNFO, e.g. GPU Power", AccessibleName = "Sensor name" };
                name.SetText(row.Name);
                name.TextChanged += (_, _) =>
                {
                    if (!IsRefreshing)
                    {
                        _model.SetManualName(id, name.Text);
                    }
                };
                var category = new ThemedDropDown { AccessibleName = "Category" };
                category.SetItems(ManualCategories.Select(o => new DropDownItem(o.Value, o.Text)), IndexOf(ManualCategories, row.Category));
                category.SelectionChanged += (_, _) =>
                {
                    if (!IsRefreshing && category.SelectedValue is SensorCategory c)
                    {
                        _model.SetManualCategory(id, c);
                    }
                };
                var remove = new TextLink { Text = "×", Tone = LinkTone.Ink, AccessibleName = "Remove sensor" };
                remove.Click += (_, _) => BeginInvoke(() => _model.RemoveManual(id));

                Place(name, left, y, -1, Theme.FieldHeight);
                Place(category, -CategoryWidth - 44, y + 1, CategoryWidth, 28);
                Place(remove, -32, y + 4, 18, 22);
                _manualControls[id] = (name, category);
                y += 38;
            }

            _addSensor = new TextLink { Text = "Add sensor", Semibold = true };
            _addSensor.Click += (_, _) => BeginInvoke(() => _model.AddManual());
            var addWidth = _addSensor.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi;
            Place(_addSensor, -(addWidth + 14), y + 2, addWidth, 24);
            y += 30;
        }
        else
        {
            for (var g = 0; g < _model.Groups.Count; g++)
            {
                var group = _model.Groups[g];
                if (g > 0)
                {
                    Place(new Panel { BackColor = Theme.Border }, left + 4, y + 3, -2, 1);
                    y += 12;
                }

                Place(new TextLabel { Text = group.Device, Role = FontRole.SmallSemibold, Tone = UiTone.Secondary }, left + 4, y, -2, 20);
                y += HeaderHeight;
                foreach (var row in group.Rows)
                {
                    var id = row.Id;
                    var box = new ThemedCheckBox { Text = row.Name, Checked = row.Checked, AccessibleName = row.Name };
                    box.CheckedChanged += (_, _) =>
                    {
                        if (!IsRefreshing)
                        {
                            _model.SetChecked(id, box.Checked);
                        }
                    };
                    var value = new TextLabel { Text = row.ValueText, Tone = UiTone.Secondary };
                    var category = new ThemedDropDown { AccessibleName = row.Name + " category" };
                    category.SetItems(SensorStepModel.CategoryOptions.Select(o => new DropDownItem(o.Value, o.Text)), IndexOf(SensorStepModel.CategoryOptions, row.Category));
                    category.SelectionChanged += (_, _) =>
                    {
                        if (!IsRefreshing)
                        {
                            _model.SetCategory(id, category.SelectedValue as SensorCategory?);
                        }
                    };

                    var boxWidth = Math.Min(420, (box.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi) + 4);
                    Place(box, left, y, boxWidth, RowHeight);

                    // The device next to the name, so sensors with the same name ("Total Power" of each DIMM) differ.
                    if (group.Device != SensorStepModel.MissingGroup && SensorDisplay.ShortDevice(group.Device) is { Length: > 0 } shortDevice)
                    {
                        Place(new TextLabel { Text = shortDevice, Role = FontRole.Small, Tone = UiTone.Secondary, AccessibleName = row.Name + " device: " + shortDevice }, left + boxWidth + 8, y, -3, RowHeight);
                        box.AccessibleName = row.Name + " (" + shortDevice + ")";
                    }

                    Place(value, float.NaN, y, 110, RowHeight);
                    Place(category, -CategoryWidth - 18, y + 2, CategoryWidth, RowHeight - 4);
                    _sensorControls[id] = (box, value, category);
                    y += RowHeight + 2;
                }
            }
        }

        _contentHeight = y;
        _rows.ResumeLayout(false);
        LayoutRows();
    }

    /// <summary>
    /// Records a row control. X &lt; 0 is measured from the right edge, NaN means "value column" (58 % of the width);
    /// a width of -1 stretches to the category column, -2 to the right edge, -3 to the value column.
    /// </summary>
    private void Place(Control control, float x, float y, float width, float height)
    {
        _rows.Controls.Add(control);
        _placed.Add((control, x, y, width, height));
    }

    private void LayoutRows()
    {
        if (_rows.Width <= 0)
        {
            return;
        }

        var width = _rows.ClientSize.Width;
        var scroll = _rows.AutoScrollPosition.Y;
        _rows.AutoScrollMinSize = new Size(0, Px(_contentHeight));
        foreach (var (control, x, y, w, h) in _placed)
        {
            int left;
            if (float.IsNaN(x))
            {
                left = (int)(width * 0.58f) - Px(14);
            }
            else
            {
                left = x < 0 ? width + Px(x) : Px(x);
            }

            var right = w switch
            {
                -2 => width - Px(18),
                -3 => (int)(width * 0.58f) - Px(14) - Px(10),
                _ => width - Px(CategoryWidth + 44 + 10),
            };
            var controlWidth = w < 0 ? Math.Max(Px(40), right - left) : Px(w);
            if (!float.IsNaN(x) && x >= 0 && w > 0 && control is ThemedCheckBox)
            {
                controlWidth = Math.Min(controlWidth, (int)(width * 0.58f) - Px(14) - left - Px(10));
            }

            control.Bounds = new Rectangle(left, Px(y) + scroll, controlWidth, Math.Max(1, Px(h)));
        }
    }

    /// <summary>The scrollable surface of the sensor card (opaque Surface, dark scrollbar).</summary>
    private sealed class RowsPanel : Panel
    {
        public RowsPanel()
        {
            AutoScroll = true;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.UseDarkExplorerTheme(this);
        }
    }
}
