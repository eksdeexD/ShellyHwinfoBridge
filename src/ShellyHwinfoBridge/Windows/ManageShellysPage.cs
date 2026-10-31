using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The Manage Shellys page, opened from the settings page inside the main window (same title row: "‹ Settings" back link,
/// title, "Changes apply immediately."). The "Devices" card lists every device (colour swatch, in Custom mode with its
/// editable <c>#RRGGBB</c> field; label, host, model, status pill, "Include in totals", Edit, Remove) with Add device
/// under it (disabled with the hint at the cap); the "Chart colours" card switches between the automatic scheme (base
/// colour swatch and its editable <c>#RRGGBB</c> field) and custom colours. A swatch click opens the colour dialog; a
/// typed colour applies on Enter or when the field loses focus, and an invalid one shows
/// <see cref="ManageShellysModel.ColourError"/>. Add and Edit open the shared <see cref="DeviceFormView"/> in a
/// <see cref="StepDialog"/>; saving a changed label asks with the rename warning first. Every change goes through
/// <see cref="ManageShellysModel"/>, which applies it through the runtime at once.
/// </summary>
internal sealed class ManageShellysPage : ScrollPanel
{
    /// <summary>The page's back command (in the same place as the settings page's "‹ Home").</summary>
    public const string BackText = "‹ Settings";

    // Layout in DIP, page coordinates (as the settings page).
    private const float Pad = Theme.WindowPadding;
    private const float Gap = Theme.Gap;
    private const float P = Theme.CardPadding;
    private const float MinContentWidth = 640;
    private const float CaptionHeight = 18;
    private const float RowHeight = 36;
    private const float ColumnGap = 10;
    private const float SwatchSize = 18;
    private const float HexWidth = 74;
    private const float HexHeight = 26;
    private const float BottomPad = 12;

    private static readonly ColourMode[] ColourModes = [ColourMode.AutomaticScheme, ColourMode.Custom];

    private readonly ManageShellysModel _model;
    private readonly TextLink _back = new() { Text = BackText, Semibold = true, AccessibleName = "Back to settings" };
    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error, Visible = false };

    private readonly CardPanel _devicesCard = new() { Title = "Devices" };
    private readonly TextLabel _labelCaption = new() { Text = "Device", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _hostCaption = new() { Text = "Host", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _modelCaption = new() { Text = "Model", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _statusCaption = new() { Text = "Status", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _includeCaption = new() { Text = "In totals", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLink _add = new() { Text = "Add device", Semibold = true };
    private readonly InfoLine _addHint = new() { Visible = false };
    private readonly List<RowControls> _rows = [];

    private readonly CardPanel _coloursCard = new() { Title = "Chart colours" };
    private readonly SegmentedControl _colourMode = new() { AccessibleName = "Colour mode" };
    private readonly TextLabel _baseLabel = new() { Text = "Base colour", Tone = UiTone.Secondary };
    private readonly ColourSwatch _baseSwatch = new() { Clickable = true, AccessibleName = "Base colour" };
    private readonly ThemedTextBox _baseHex = new() { MaxLength = 7, Placeholder = "#RRGGBB", AccessibleName = "Base colour as #RRGGBB" };
    private readonly TextLabel _colourText = new() { Tone = UiTone.Secondary, WordWrap = true };
    private readonly InfoLine _colourError = new() { Tone = InfoTone.Error, Visible = false };

    private string _structure = string.Empty;
    private bool _applying;
    private int _modalDepth;
    private float _footnoteLeft;

    // The colour field whose typed text was rejected: it keeps that text (marked red) while the error is shown.
    private ThemedTextBox? _rejectedHex;

    /// <summary>Creates the page.</summary>
    /// <param name="model">The page model (<see cref="SettingsPresenter.CreateManageShellys"/>).</param>
    public ManageShellysPage(ManageShellysModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        AccessibleName = "Manage Shellys";
        AccessibleRole = AccessibleRole.Pane;

        SuspendLayout();
        _colourMode.SetItems(["Automatic scheme", "Custom"], 0);
        _back.TabIndex = 0;
        _devicesCard.TabIndex = 1;
        _coloursCard.TabIndex = 2;
        _applyError.TabIndex = 3;
        _devicesCard.Controls.AddRange([_labelCaption, _hostCaption, _modelCaption, _statusCaption, _includeCaption, _add, _addHint]);
        _coloursCard.Controls.AddRange([_colourMode, _baseLabel, _baseSwatch, _baseHex, _colourText, _colourError]);
        for (var i = 0; i < _coloursCard.Controls.Count; i++)
        {
            _coloursCard.Controls[i].TabIndex = i;
        }

        Controls.AddRange([_back, _devicesCard, _coloursCard, _applyError]);
        ResumeLayout(false);

        _back.Click += (_, _) => Defer(() => BackRequested?.Invoke(this, EventArgs.Empty));
        _add.Click += (_, _) =>
        {
            if (_model.BeginAdd() is { } form)
            {
                ShowForm(form);
            }
        };
        _colourMode.SelectedIndexChanged += (_, _) =>
        {
            if (!_applying)
            {
                _model.SetColourMode(ColourModes[Math.Max(0, _colourMode.SelectedIndex)]);
            }
        };
        _baseSwatch.Click += (_, _) =>
        {
            if (PickColour(_model.BaseColour) is { } colour)
            {
                _rejectedHex = null;
                _model.SetBaseColour(colour);
            }
        };
        _baseHex.TextCommitted += (_, _) => CommitHex(_baseHex, text => _model.SetBaseColour(text));
        _devicesCard.Paint += PaintRowSeparators;
        _model.Changed += OnModelChanged;
        ApplyState();
    }

    /// <summary>Raised when "‹ Settings" is clicked (the main window shows the settings page again).</summary>
    public event EventHandler? BackRequested;

    /// <summary>Gets whether a dialog (device form, confirmation, colour picker) is open over the page.</summary>
    public bool IsShowingDialog => _modalDepth > 0;

    /// <summary>Gets the model (screen renderer).</summary>
    internal ManageShellysModel Model => _model;

    /// <summary>Re-reads the runtime (online statuses); call when the runtime's state changed.</summary>
    public void RefreshState() => _model.Refresh();

    /// <summary>Puts the keyboard focus on the first device's Edit command (when the page is opened).</summary>
    public void FocusFirstField()
    {
        if (_rows.Count > 0 && _rows[0].Edit.CanFocus)
        {
            _rows[0].Edit.Focus();
        }
        else if (_add.CanFocus)
        {
            _add.Focus();
        }
    }

    /// <summary>Types <paramref name="text"/> into a colour field and commits it as the user would (screen renderer).</summary>
    internal void PreviewTypeColour(int row, string text)
    {
        var box = row < 0 ? _baseHex : _rows[row].Hex;
        box.InnerTextBox.Text = text;
        CommitHex(box, value => row < 0 ? _model.SetBaseColour(value) : _model.SetDeviceColour(_model.Rows[row].Id, value));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // The base constructor already lays out (AutoScroll), before the model is set.
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0 || _model is null)
        {
            base.OnLayout(levent);
            return;
        }

        var dpi = DeviceDpi;
        float Dip(int px) => px * 96f / dpi;
        float PrefWidth(Control c) => c.GetPreferredSize(Size.Empty).Width * 96f / dpi;
        float PrefHeight(Control c) => c.GetPreferredSize(Size.Empty).Height * 96f / dpi;
        var scroll = AutoScrollPosition;
        var contentWidth = Math.Max(Dip(ClientSize.Width), MinContentWidth);
        var width = contentWidth - (2 * Pad);

        Rectangle At(float x, float y, float w, float h) =>
            new(this.Px(x) + scroll.X, this.Px(y) + scroll.Y, this.Px(w), this.Px(h));
        Rectangle R(float x, float y, float w, float h) => new(this.Px(x), this.Px(y), this.Px(w), this.Px(h));

        // Title row: the back link, then the title (painted) and the footnote or the apply error.
        _footnoteLeft = PageHeader.Layout(this, _back, "Manage Shellys", scroll);
        _applyError.Bounds = At(_footnoteLeft, PageHeader.Top + ((PageHeader.Height - Theme.LineHeight) / 2), Math.Max(0, contentWidth - Pad - _footnoteLeft), Theme.LineHeight);

        // Devices card: column captions, one row per device, then Add device and its hint. In Custom mode the colour
        // column also holds each device's #RRGGBB field.
        var t = P + (Gfx.Measure("Ag", ThemeFonts.For(dpi).CardTitle).Height * 96f / dpi) + Theme.TitleGap;
        var inner = width - (2 * P);
        var custom = _model.CanPickDeviceColours;
        var colourWidth = SwatchSize + (custom ? 8 + HexWidth : 0);
        const float includeWidth = 72;
        var actionsWidth = _rows.Count == 0 ? 0 : _rows.Max(r => PrefWidth(r.Edit) + 12 + PrefWidth(r.Remove));
        var flexible = Math.Max(240, inner - colourWidth - includeWidth - actionsWidth - (6 * ColumnGap));
        var statusWidth = Math.Min(flexible * 0.4f, Math.Max(100, _rows.Count == 0 ? 0 : _rows.Max(r => PrefWidth(r.Status))));
        var textWidth = flexible - statusWidth;
        var labelWidth = textWidth * 0.3f;
        var hostWidth = textWidth * 0.35f;
        var modelWidth = textWidth - labelWidth - hostWidth;
        var xLabel = P + colourWidth + ColumnGap;
        var xHost = xLabel + labelWidth + ColumnGap;
        var xModel = xHost + hostWidth + ColumnGap;
        var xStatus = xModel + modelWidth + ColumnGap;
        var xInclude = xStatus + statusWidth + ColumnGap;
        var xActions = xInclude + includeWidth + ColumnGap;

        _labelCaption.Bounds = R(xLabel, t, labelWidth, CaptionHeight);
        _hostCaption.Bounds = R(xHost, t, hostWidth, CaptionHeight);
        _modelCaption.Bounds = R(xModel, t, modelWidth, CaptionHeight);
        _statusCaption.Bounds = R(xStatus, t, statusWidth, CaptionHeight);
        _includeCaption.Bounds = R(xInclude, t, includeWidth, CaptionHeight);

        var rowsTop = RowsTop(t);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var top = rowsTop + (i * RowHeight);
            float Centre(float h) => top + ((RowHeight - h) / 2);
            row.Swatch.Bounds = R(P, Centre(SwatchSize), SwatchSize, SwatchSize);
            row.Hex.Bounds = R(P + SwatchSize + 8, Centre(HexHeight), HexWidth, HexHeight);
            row.Label.Bounds = R(xLabel, top + 2, labelWidth, RowHeight - 4);
            row.Host.Bounds = R(xHost, top + 2, hostWidth, RowHeight - 4);
            row.Model.Bounds = R(xModel, top + 2, modelWidth, RowHeight - 4);
            row.Status.Bounds = R(xStatus, Centre(24), Math.Min(statusWidth, PrefWidth(row.Status)), 24);
            row.Include.Bounds = R(xInclude, Centre(Theme.LineHeight), Theme.LineHeight, Theme.LineHeight);
            var editWidth = PrefWidth(row.Edit);
            var linkHeight = PrefHeight(row.Edit);
            row.Edit.Bounds = R(xActions, Centre(linkHeight), editWidth, linkHeight);
            row.Remove.Bounds = R(xActions + editWidth + 12, Centre(linkHeight), PrefWidth(row.Remove), linkHeight);
        }

        var addTop = rowsTop + (_rows.Count * RowHeight) + 8;
        var addWidth = PrefWidth(_add);
        var addHeight = PrefHeight(_add);
        _add.Bounds = R(P, addTop + ((Theme.LineHeight - addHeight) / 2), addWidth, addHeight);
        _addHint.Bounds = R(P + addWidth + 16, addTop, Math.Max(0, inner - addWidth - 16), Theme.LineHeight);
        var devicesHeight = addTop + Theme.LineHeight + BottomPad;
        _devicesCard.Bounds = At(Pad, PageHeader.ContentTop, width, devicesHeight);

        // Chart colours: the mode segments and (automatic scheme) the base colour swatch and field; the explanation and
        // an input error under them.
        var modeWidth = PrefWidth(_colourMode);
        _colourMode.Bounds = R(P, t, modeWidth, Theme.ButtonHeight);
        var baseLeft = P + modeWidth + 24;
        var baseLabelWidth = PrefWidth(_baseLabel);
        _baseLabel.Bounds = R(baseLeft, t, baseLabelWidth, Theme.ButtonHeight);
        var swatchLeft = baseLeft + baseLabelWidth + 10;
        _baseSwatch.Bounds = R(swatchLeft, t + ((Theme.ButtonHeight - 22) / 2), 22, 22);
        _baseHex.Bounds = R(swatchLeft + 22 + 8, t + ((Theme.ButtonHeight - HexHeight) / 2), HexWidth, HexHeight);
        var textTop = t + Theme.ButtonHeight + 8;
        var textHeight = _colourText.GetPreferredHeight(this.Px(inner)) * 96f / dpi;
        _colourText.Bounds = R(P, textTop, inner, textHeight);
        var errorTop = textTop + textHeight + 4;
        _colourError.Bounds = R(P, errorTop, inner, Theme.LineHeight);
        var coloursHeight = errorTop + (_colourError.Visible ? Theme.LineHeight + 4 : 0) + BottomPad - 4;
        var coloursTop = PageHeader.ContentTop + devicesHeight + Gap;
        _coloursCard.Bounds = At(Pad, coloursTop, width, coloursHeight);

        var contentHeight = coloursTop + coloursHeight + 16;
        var minSize = new Size(contentWidth > Dip(ClientSize.Width) + 0.5f ? this.Px(contentWidth) : 0, this.Px(contentHeight));
        if (AutoScrollMinSize != minSize)
        {
            AutoScrollMinSize = minSize;
        }

        base.OnLayout(levent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PageHeader.Paint(e.Graphics, this, _back, "Manage Shellys", _footnoteLeft, !_applyError.Visible, AutoScrollPosition);
    }

    private static float RowsTop(float contentTop) => contentTop + CaptionHeight + 4;

    private void PaintRowSeparators(object? sender, PaintEventArgs e)
    {
        // 1 px lines between the header and the rows, and between rows (as in SelectableList).
        var line = Math.Max(1, this.Px(1));
        using var brush = new SolidBrush(Theme.Border);
        var left = this.Px(P);
        var width = _devicesCard.Width - (2 * left);
        var rowsTop = RowsTop(_devicesCard.ContentBounds.Top * 96f / DeviceDpi);
        for (var i = 0; i < _rows.Count; i++)
        {
            e.Graphics.FillRectangle(brush, left, this.Px(rowsTop + (i * RowHeight)), width, line);
        }
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        var rows = _model.Rows;
        _applying = true;
        try
        {
            var structure = string.Join("|", rows.Select(r => r.Id));
            if (structure != _structure)
            {
                _structure = structure;
                BuildRows(rows);
            }

            if (_model.ColourError is null)
            {
                _rejectedHex = null;
            }

            var custom = _model.CanPickDeviceColours;
            for (var i = 0; i < rows.Count && i < _rows.Count; i++)
            {
                var row = rows[i];
                var c = _rows[i];
                c.Swatch.Colour = ColourSwatch.Parse(row.Colour, Theme.Accent);
                c.Swatch.Clickable = custom;
                c.Swatch.AccessibleName = custom ? "Pick the colour of " + row.Label : "Colour of " + row.Label;
                c.Hex.Visible = custom;
                c.Hex.AccessibleName = "Colour of " + row.Label + " as #RRGGBB";
                SetHex(c.Hex, row.Colour);
                c.Label.Text = row.Label;
                c.Host.Text = row.Host.Length > 0 ? row.Host : "—";
                c.Model.Text = row.Model.Length > 0 ? row.Model : "—";
                c.Status.Apply(row.StatusTone.ToUi(), row.StatusText);
                c.Include.Checked = row.IncludeInTotals;
                c.Include.Enabled = row.CanToggleInclude;
                c.Include.AccessibleName = "Include " + row.Label + " in totals";
                c.Edit.AccessibleName = "Edit " + row.Label;
                c.Edit.Enabled = _model.Form is null;
                c.Remove.Visible = row.CanRemove;
                c.Remove.AccessibleName = "Remove " + row.Label;
            }

            _add.Enabled = _model.CanAdd;
            _addHint.Visible = _model.AddHint is not null;
            _addHint.Text = _model.AddHint ?? string.Empty;

            _colourMode.SetItems(["Automatic scheme", "Custom"], Math.Max(0, Array.IndexOf(ColourModes, _model.ColourMode)));
            var automatic = _model.ColourMode == ColourMode.AutomaticScheme;
            _baseLabel.Visible = automatic;
            _baseSwatch.Visible = automatic;
            _baseHex.Visible = automatic;
            _baseSwatch.Colour = ColourSwatch.Parse(_model.BaseColour, Theme.Accent);
            SetHex(_baseHex, _model.BaseColour);
            _colourText.Text = automatic
                ? "The PC uses the base colour; the other devices get colours derived from it (2 devices complementary, 3 a triad, 4 a square). Click the swatch or type a colour as #RRGGBB to change the base colour."
                : "Each device has its own colour: click a device's swatch in the list or type its colour as #RRGGBB.";
            _colourError.Visible = _model.ColourError is not null;
            _colourError.Text = _model.ColourError ?? string.Empty;

            _applyError.Visible = _model.ApplyError is not null;
            _applyError.Text = _model.ApplyError is null ? string.Empty : "Could not apply the change: " + _model.ApplyError;
        }
        finally
        {
            _applying = false;
        }

        PerformLayout();
        Invalidate(true);
    }

    /// <summary>Shows a stored colour in its field, unless the user is typing in it or it holds a rejected colour.</summary>
    private void SetHex(ThemedTextBox box, string colour)
    {
        box.HasError = ReferenceEquals(box, _rejectedHex);
        if (box.HasError || (box.ContainsFocus && box.IsModified))
        {
            return;
        }

        if (box.Text != colour)
        {
            box.SetText(colour);
        }
    }

    /// <summary>Applies a typed colour (Enter or leaving the field); a rejected one stays in the field, marked red.</summary>
    private void CommitHex(ThemedTextBox box, Func<string, bool> apply)
    {
        if (_applying || !box.IsModified)
        {
            return;
        }

        box.IsModified = false;
        _rejectedHex = null;
        if (!apply(box.Text.Trim()))
        {
            _rejectedHex = box;
            box.HasError = true;
        }
    }

    private void BuildRows(IReadOnlyList<DeviceRow> rows)
    {
        SuspendLayout();
        foreach (var old in _rows)
        {
            foreach (var control in old.All)
            {
                _devicesCard.Controls.Remove(control);
                control.Dispose();
            }
        }

        _rows.Clear();
        _rejectedHex = null;
        var tab = _devicesCard.Controls.Count;
        foreach (var row in rows)
        {
            var id = row.Id;
            var c = new RowControls(
                new ColourSwatch(),
                new ThemedTextBox { MaxLength = 7, Placeholder = "#RRGGBB" },
                new TextLabel { Role = FontRole.BodySemibold },
                new TextLabel(),
                new TextLabel { Tone = UiTone.Secondary },
                new StatusPill(),
                new ThemedCheckBox(),
                new TextLink { Text = "Edit", Semibold = true },
                new TextLink { Text = "Remove", Tone = LinkTone.Danger, Semibold = true });
            c.Swatch.Click += (_, _) => PickDeviceColour(id);
            c.Hex.TextCommitted += (_, _) => CommitHex(c.Hex, text => _model.SetDeviceColour(id, text));
            c.Include.CheckedChanged += (_, _) =>
            {
                if (!_applying)
                {
                    _model.SetIncludeInTotals(id, c.Include.Checked);
                }
            };
            c.Edit.Click += (_, _) =>
            {
                if (_model.BeginEdit(id) is { } form)
                {
                    ShowForm(form);
                }
            };
            c.Remove.Click += (_, _) => Defer(() => RemoveDevice(id));
            foreach (var control in c.All)
            {
                control.TabIndex = tab++;
                _devicesCard.Controls.Add(control);
            }

            _rows.Add(c);
        }

        // Add device after the rows in the tab order.
        _add.TabIndex = tab++;
        _addHint.TabIndex = tab;
        ResumeLayout(false);
    }

    /// <summary>Shows the device form in a step dialog; Save asks with the rename warning when the label changed.</summary>
    private void ShowForm(DeviceFormModel form)
    {
        var view = new DeviceFormView(form, inline: false);
        StepDialog? dialog = null;
        using var created = new StepDialog(
            form.Title,
            form.IsPrimary
                ? "The PC's plug: its AC reading goes through the PSU curve. Pick it from the list or type its address."
                : "Pick the plug from the list or type its address. It is measured on the AC side only.",
            view,
            () => SaveForm(view, dialog));
        dialog = created;
        var result = ShowModal(() => created.ShowDialog(FindForm()));
        if (result != DialogResult.OK && _model.Form is not null)
        {
            _model.CancelForm();
        }
    }

    private string? SaveForm(DeviceFormView view, IWin32Window? owner)
    {
        var form = view.Form;
        if (view.ValidationMessage is { } problem)
        {
            return problem;
        }

        if (form.ShowRenameWarning &&
            ThemedDialog.Show(owner, "Rename the device?", DeviceFormModel.RenameWarningText, "Rename", "Cancel") != DialogResult.OK)
        {
            return StepDialog.StayOpen;
        }

        var outcome = _model.SaveForm();
        if (outcome is null)
        {
            return "The form is not complete yet.";
        }

        return outcome.Success ? null : "Could not save: " + (outcome.Error ?? "unknown error");
    }

    private void RemoveDevice(string id)
    {
        var row = _model.Rows.FirstOrDefault(r => r.Id == id);
        if (row is null || !row.CanRemove)
        {
            return;
        }

        var answer = ShowModal(() => ThemedDialog.Show(
            FindForm(),
            "Remove " + row.Label + "?",
            "Its HWiNFO sensors are removed (restart HWiNFO afterwards) and it leaves the totals. Its energy history stays on disk.",
            "Remove",
            "Cancel"));
        if (answer == DialogResult.OK)
        {
            _model.Remove(id);
        }
    }

    private void PickDeviceColour(string id)
    {
        if (!_model.CanPickDeviceColours || _model.Rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            return;
        }

        if (PickColour(row.Colour) is { } colour)
        {
            _rejectedHex = null;
            _model.SetDeviceColour(id, colour);
        }
    }

    /// <summary>Opens the standard colour dialog; returns <c>#RRGGBB</c>, or <c>null</c> when cancelled.</summary>
    private string? PickColour(string current)
    {
        using var dialog = new ColorDialog { Color = ColourSwatch.Parse(current, Theme.Accent), FullOpen = true, AnyColor = true };
        return ShowModal(() => dialog.ShowDialog(FindForm())) == DialogResult.OK ? ColourSwatch.ToHex(dialog.Color) : null;
    }

    private DialogResult ShowModal(Func<DialogResult> show)
    {
        _modalDepth++;
        try
        {
            return show();
        }
        finally
        {
            _modalDepth--;
        }
    }

    /// <summary>Runs <paramref name="action"/> once the current message is handled (on the window, which outlives the page).</summary>
    private void Defer(Action action)
    {
        if (FindForm() is { IsHandleCreated: true, IsDisposed: false } form)
        {
            form.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>The controls of one device row.</summary>
    private sealed record RowControls(ColourSwatch Swatch, ThemedTextBox Hex, TextLabel Label, TextLabel Host, TextLabel Model, StatusPill Status, ThemedCheckBox Include, TextLink Edit, TextLink Remove)
    {
        public IEnumerable<Control> All => [Swatch, Hex, Label, Host, Model, Status, Include, Edit, Remove];
    }
}
