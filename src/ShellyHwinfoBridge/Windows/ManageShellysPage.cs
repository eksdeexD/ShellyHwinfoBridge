using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The Manage Shellys page, opened from the settings page inside the main window (same pattern: "‹ Settings" back link,
/// title, "Changes apply immediately."). The "Devices" card lists every device (colour swatch, label, host, model,
/// status pill, "Include in totals", Edit…, Remove) with Add device under it (disabled with the hint at the cap); the
/// "Chart colours" card switches between the automatic scheme (base colour picker) and custom colours (each row's
/// swatch picks the device's colour). Add and Edit open the shared <see cref="DeviceFormView"/> in a
/// <see cref="StepDialog"/>; saving a changed label asks with the rename warning first. Every change goes through
/// <see cref="ManageShellysModel"/>, which applies it through the runtime at once.
/// </summary>
internal sealed class ManageShellysPage : ThemedPanel
{
    // Layout in DIP, page coordinates (as the settings page).
    private const float Pad = 24;
    private const float TitleRowTop = 6;
    private const float TitleRowHeight = 34;
    private const float CardsTop = 50;
    private const float RowGap = 10;
    private const float MinContentWidth = 880;
    private const float HeaderTop = 58;
    private const float RowsTop = 88;
    private const float RowHeight = 52;

    private static readonly ColourMode[] ColourModes = [ColourMode.AutomaticScheme, ColourMode.Custom];

    private readonly ManageShellysModel _model;
    private readonly TextLink _back = new() { Text = "‹ Settings", Semibold = true, AccessibleName = "Back to settings" };
    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error, Visible = false };

    private readonly CardPanel _devicesCard = new() { Title = "Devices" };
    private readonly TextLabel _labelCaption = new() { Text = "Device", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _hostCaption = new() { Text = "Host", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _modelCaption = new() { Text = "Model", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _statusCaption = new() { Text = "Status", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _includeCaption = new() { Text = "In totals", Caption = true, Tone = UiTone.Secondary };
    private readonly PillButton _add = new() { Text = "Add device", Variant = PillVariant.Primary };
    private readonly InfoLine _addHint = new() { Visible = false };
    private readonly List<RowControls> _rows = [];

    private readonly CardPanel _coloursCard = new() { Title = "Chart colours" };
    private readonly SegmentedControl _colourMode = new() { AccessibleName = "Colour mode" };
    private readonly TextLabel _baseLabel = new() { Text = "Base colour", Tone = UiTone.Secondary };
    private readonly ColourSwatch _baseSwatch = new() { Clickable = true, AccessibleName = "Base colour" };
    private readonly TextLabel _baseHex = new() { Tone = UiTone.Secondary };
    private readonly TextLabel _colourText = new() { Tone = UiTone.Secondary, WordWrap = true };
    private readonly InfoLine _colourError = new() { Tone = InfoTone.Error, Visible = false };

    private string _structure = string.Empty;
    private bool _applying;
    private int _modalDepth;
    private float _footnoteLeft;

    /// <summary>Creates the page.</summary>
    /// <param name="model">The page model (<see cref="SettingsPresenter.CreateManageShellys"/>).</param>
    public ManageShellysPage(ManageShellysModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        AccessibleName = "Manage Shellys";
        AccessibleRole = AccessibleRole.Pane;
        AutoScroll = true;

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
                _model.SetBaseColour(colour);
            }
        };
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

    /// <summary>Puts the keyboard focus on the first device's Edit link (when the page is opened).</summary>
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkExplorerTheme(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
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

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            base.OnLayout(levent);
            return;
        }

        var dpi = DeviceDpi;
        float Dip(int px) => px * 96f / dpi;
        float PrefWidth(Control c) => c.GetPreferredSize(Size.Empty).Width * 96f / dpi;
        var scroll = AutoScrollPosition;
        var contentWidth = Math.Max(Dip(ClientSize.Width), MinContentWidth);
        var width = contentWidth - (2 * Pad);

        Rectangle At(float x, float y, float w, float h) =>
            new(this.Px(x) + scroll.X, this.Px(y) + scroll.Y, this.Px(w), this.Px(h));
        Rectangle R(float x, float y, float w, float h) => new(this.Px(x), this.Px(y), this.Px(w), this.Px(h));

        // Title row: the back link, then the title (painted) and the footnote or the apply error.
        var backWidth = PrefWidth(_back);
        _back.Bounds = At(Pad, TitleRowTop + ((TitleRowHeight - 28) / 2), backWidth, 28);
        var titleWidth = Gfx.Measure("Manage Shellys", ThemeFonts.For(dpi).CardTitle).Width * 96f / dpi;
        _footnoteLeft = Pad + backWidth + 14 + titleWidth + 16;
        _applyError.Bounds = At(_footnoteLeft, TitleRowTop + ((TitleRowHeight - 28) / 2), Math.Max(0, contentWidth - Pad - _footnoteLeft), 28);

        // Devices card: column captions, one row per device, then Add device and its hint.
        var inner = width - 40;
        const float swatchWidth = 28;
        const float includeWidth = 96;
        var actionsWidth = 150f;
        var flexible = Math.Max(200, inner - swatchWidth - includeWidth - actionsWidth - (5 * 12));
        var labelWidth = flexible * 0.22f;
        var hostWidth = flexible * 0.24f;
        var modelWidth = flexible * 0.24f;
        var statusWidth = flexible - labelWidth - hostWidth - modelWidth;
        var xLabel = 20 + swatchWidth + 12;
        var xHost = xLabel + labelWidth + 12;
        var xModel = xHost + hostWidth + 12;
        var xStatus = xModel + modelWidth + 12;
        var xInclude = xStatus + statusWidth + 12;
        var xActions = xInclude + includeWidth + 12;

        _labelCaption.Bounds = R(xLabel, HeaderTop, labelWidth, 22);
        _hostCaption.Bounds = R(xHost, HeaderTop, hostWidth, 22);
        _modelCaption.Bounds = R(xModel, HeaderTop, modelWidth, 22);
        _statusCaption.Bounds = R(xStatus, HeaderTop, statusWidth, 22);
        _includeCaption.Bounds = R(xInclude, HeaderTop, includeWidth, 22);

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var top = RowsTop + (i * RowHeight);
            row.Swatch.Bounds = R(20, top + ((RowHeight - swatchWidth) / 2), swatchWidth, swatchWidth);
            row.Label.Bounds = R(xLabel, top + 2, labelWidth, RowHeight - 4);
            row.Host.Bounds = R(xHost, top + 2, hostWidth, RowHeight - 4);
            row.Model.Bounds = R(xModel, top + 2, modelWidth, RowHeight - 4);
            var pillWidth = Math.Min(statusWidth, Math.Max(110, PrefWidth(row.Status)));
            row.Status.Bounds = R(xStatus, top + ((RowHeight - 34) / 2), pillWidth, 34);
            row.Include.Bounds = R(xInclude, top + ((RowHeight - 28) / 2), 28, 28);
            var editWidth = PrefWidth(row.Edit);
            row.Edit.Bounds = R(xActions, top + ((RowHeight - 26) / 2), editWidth, 26);
            row.Remove.Bounds = R(xActions + editWidth + 16, top + ((RowHeight - 26) / 2), PrefWidth(row.Remove), 26);
        }

        var addTop = RowsTop + (_rows.Count * RowHeight) + 12;
        var addWidth = Math.Max(150, PrefWidth(_add) - 16);
        _add.Bounds = R(20, addTop, addWidth, 38);
        _addHint.Bounds = R(20 + addWidth + 16, addTop + 5, Math.Max(0, inner - addWidth - 16), 28);
        var devicesHeight = addTop + 38 + 20;
        _devicesCard.Bounds = At(Pad, CardsTop, width, devicesHeight);

        // Chart colours: the mode segments, the base colour (automatic scheme), the explanation and an input error.
        var modeWidth = Math.Max(320, PrefWidth(_colourMode));
        _colourMode.Bounds = R(20, 58, modeWidth, 38);
        var baseLeft = 20 + modeWidth + 24;
        _baseLabel.Bounds = R(baseLeft, 58, 96, 38);
        _baseSwatch.Bounds = R(baseLeft + 96 + 8, 59, 36, 36);
        _baseHex.Bounds = R(baseLeft + 96 + 8 + 36 + 10, 58, 90, 38);
        var textHeight = _colourText.GetPreferredHeight(this.Px(inner)) * 96f / dpi;
        _colourText.Bounds = R(20, 108, inner, textHeight);
        var errorTop = 108 + textHeight + 6;
        _colourError.Bounds = R(20, errorTop, inner, 28);
        var coloursHeight = errorTop + (_colourError.Visible ? 28 + 6 : 0) + 14;
        var coloursTop = CardsTop + devicesHeight + RowGap;
        _coloursCard.Bounds = At(Pad, coloursTop, width, coloursHeight);

        var contentHeight = coloursTop + coloursHeight + 20;
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
        var g = e.Graphics;
        var scroll = AutoScrollPosition;
        var f = ThemeFonts.For(DeviceDpi);
        var titleTop = this.Px(TitleRowTop) + scroll.Y;
        var titleHeight = this.Px(TitleRowHeight);
        var titleLeft = _back.Right + this.Px(14);
        Gfx.Text(g, "Manage Shellys", f.CardTitle, new Rectangle(titleLeft, titleTop, Math.Max(0, ClientSize.Width - titleLeft), titleHeight), Theme.Ink);

        if (!_applyError.Visible)
        {
            var titleText = Gfx.Measure("Manage Shellys", f.CardTitle).Height;
            var smallText = Gfx.Measure("Ag", f.Small).Height;
            var top = titleTop + ((titleHeight - titleText) / 2) + titleText - smallText - this.Px(3);
            var footnote = new Rectangle(this.Px(_footnoteLeft) + scroll.X, top, Math.Max(0, ClientSize.Width - this.Px(_footnoteLeft + Pad) - scroll.X), smallText);
            Gfx.Text(g, "Changes apply immediately.", f.Small, footnote, Theme.InkSecondary, Gfx.SingleLine);
        }
    }

    private void PaintRowSeparators(object? sender, PaintEventArgs e)
    {
        // 1 px lines between the header and the rows, and between rows (as in SelectableList).
        var line = Math.Max(1, this.Px(1));
        using var brush = new SolidBrush(Theme.Border);
        var left = this.Px(20);
        var width = _devicesCard.Width - (2 * left);
        for (var i = 0; i < _rows.Count; i++)
        {
            e.Graphics.FillRectangle(brush, left, this.Px(RowsTop + (i * RowHeight)), width, line);
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

            var custom = _model.CanPickDeviceColours;
            for (var i = 0; i < rows.Count && i < _rows.Count; i++)
            {
                var row = rows[i];
                var c = _rows[i];
                c.Swatch.Colour = ColourSwatch.Parse(row.Colour, Theme.Accent);
                c.Swatch.Clickable = custom;
                c.Swatch.AccessibleName = custom ? "Pick the colour of " + row.Label : "Colour of " + row.Label;
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
            _baseHex.Text = _model.BaseColour;
            _colourText.Text = automatic
                ? "The PC uses the base colour; the other devices get colours derived from it (2 devices complementary, 3 a triad, 4 a square). Click the swatch to change the base colour."
                : "Each device has its own colour: click a device's swatch in the list to pick it.";
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
        var tab = _devicesCard.Controls.Count;
        foreach (var row in rows)
        {
            var id = row.Id;
            var c = new RowControls(
                new ColourSwatch(),
                new TextLabel { Role = FontRole.BodySemibold },
                new TextLabel(),
                new TextLabel { Tone = UiTone.Secondary },
                new StatusPill(),
                new ThemedCheckBox(),
                new TextLink { Text = "Edit…", Semibold = true },
                new TextLink { Text = "Remove", Tone = LinkTone.Danger });
            c.Swatch.Click += (_, _) => PickDeviceColour(id);
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
    private sealed record RowControls(ColourSwatch Swatch, TextLabel Label, TextLabel Host, TextLabel Model, StatusPill Status, ThemedCheckBox Include, TextLink Edit, TextLink Remove)
    {
        public IEnumerable<Control> All => [Swatch, Label, Host, Model, Status, Include, Edit, Remove];
    }
}
