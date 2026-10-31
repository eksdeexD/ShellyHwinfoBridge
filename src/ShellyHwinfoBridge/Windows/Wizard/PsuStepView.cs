using System.Diagnostics;
using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 3 (design/final/wizard-step3-psu.png), also hosted by the Settings "Change" dialog: three mode cards; database
/// filters (Brand, Wattage, Rating with "Any", search) and the model list with report links; the generic wattage and
/// rating with the 20/50/100 % preview; the manual efficiency form or point grid with CSV import/export and validation;
/// and the input voltage line with "Change" (Auto | 115 V | 230 V).
/// </summary>
internal sealed class PsuStepView : WizardStepView
{
    private static readonly InputVoltageMode[] VoltageModes = [InputVoltageMode.Auto, InputVoltageMode.V115, InputVoltageMode.V230];

    private readonly PsuStepModel _model;
    private readonly RadioCard _database = new() { Title = "From database", Description = "Measured efficiency curves from Cybenetics reports", Group = "psu-mode" };
    private readonly RadioCard _generic = new() { Title = "Generic by rating", Description = "Approximate curve from the 80 Plus minimum requirements", Group = "psu-mode" };
    private readonly RadioCard _manual = new() { Title = "Manual curve", Description = "Enter your own AC/DC points or import a CSV", Group = "psu-mode" };

    // Database mode.
    private readonly ThemedDropDown _brand = new() { Prefix = "Brand" };
    private readonly ThemedDropDown _wattage = new() { Prefix = "Wattage" };
    private readonly ThemedDropDown _rating = new() { Prefix = "Rating" };
    private readonly ThemedTextBox _search = new() { Placeholder = "Search model…", AccessibleName = "Search model" };
    private readonly SelectableList _list = new() { RowHeightDip = 50, AccessibleName = "Power supplies" };

    // Generic mode.
    private readonly CardPanel _genericCard = new() { Title = "Generic curve" };
    private readonly TextLabel _genericWattageLabel = new() { Text = "Rated wattage (W)", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _genericWattage = new() { Placeholder = "e.g. 850", AccessibleName = "Rated wattage" };
    private readonly TextLabel _genericRatingLabel = new() { Text = "80 Plus rating", Tone = UiTone.Secondary };
    private readonly ThemedDropDown _genericRating = new() { Placeholder = "Pick a rating", AccessibleName = "80 Plus rating" };
    private readonly InfoLine _genericWarning = new() { Tone = InfoTone.Warning, Text = PsuStepModel.GenericWarning };
    private readonly InfoLine _genericError = new() { Tone = InfoTone.Error };
    private readonly ValueTile[] _genericTiles = [new(), new(), new()];

    // Manual mode.
    private readonly SegmentedControl _manualEntry = new() { AccessibleName = "Manual curve entry" };
    private readonly CardPanel _formCard = new() { Title = "Efficiency at load" };
    private readonly TextLabel[] _formLabels =
    [
        new() { Text = "Rated W", Tone = UiTone.Secondary },
        new() { Text = "10 % load", Tone = UiTone.Secondary },
        new() { Text = "20 % load", Tone = UiTone.Secondary },
        new() { Text = "50 % load", Tone = UiTone.Secondary },
        new() { Text = "100 % load", Tone = UiTone.Secondary },
    ];

    private readonly ThemedTextBox[] _formFields =
    [
        new() { Placeholder = "W", AccessibleName = "Rated wattage" },
        new() { Placeholder = "%", AccessibleName = "Efficiency at 10 % load" },
        new() { Placeholder = "%", AccessibleName = "Efficiency at 20 % load" },
        new() { Placeholder = "%", AccessibleName = "Efficiency at 50 % load" },
        new() { Placeholder = "%", AccessibleName = "Efficiency at 100 % load" },
    ];

    private readonly TextLink _generate = new() { Text = "Generate grid", Semibold = true };
    private readonly InfoLine _formError = new() { Tone = InfoTone.Error };
    private readonly InfoLine _formHint = new() { Text = "Efficiency in % from the PSU's review or label. The grid can be edited afterwards." };
    private readonly DataGridView _grid = new() { AccessibleName = "Manual curve points" };
    private readonly TextLink _import = new() { Text = "Import CSV", Semibold = true };
    private readonly TextLink _export = new() { Text = "Export CSV", Semibold = true };
    private readonly TextLabel _gridHint = new() { Text = "One row per point: AC watts from the wall and the DC watts the PC gets at that load. At least 2 rows, AC increasing.", WordWrap = true, Tone = UiTone.Secondary };
    private readonly List<InfoLine> _manualErrors = [];

    // Voltage.
    private readonly InfoLine _voltage = new() { LinkText = "Change" };
    private readonly SegmentedControl _voltageMode = new() { AccessibleName = "Input voltage" };

    private IReadOnlyList<PsuRow>? _shownRows;
    private IReadOnlyList<CurvePoint>? _gridSource;
    private IReadOnlyList<CurvePoint>? _gridEdited;
    private bool _voltageOpen;

    public PsuStepView(PsuStepModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _list.EmptyText = _model.EmptyListText;
        _manualEntry.SetItems(["Efficiency form", "Point grid"], 1);
        _voltageMode.SetItems(["Auto", "115 V", "230 V"], 0);
        _voltageMode.Visible = false;
        _genericTiles[0].Caption = "20 % load";
        _genericTiles[1].Caption = "50 % load";
        _genericTiles[2].Caption = "100 % load";
        _genericRating.SetItems(_model.GenericRatingOptions.Select(o => new DropDownItem(o.Value, o.Text)));

        SetUpGrid();
        _genericCard.Controls.AddRange([_genericWattageLabel, _genericWattage, _genericRatingLabel, _genericRating, _genericWarning, _genericError, .. _genericTiles]);
        _formCard.Controls.AddRange([.. _formLabels, .. _formFields, _generate, _formError, _formHint]);
        Controls.AddRange(
        [
            _database, _generic, _manual,
            _brand, _wattage, _rating, _search, _list,
            _genericCard,
            _manualEntry, _formCard, _grid, _import, _export, _gridHint,
            _voltage, _voltageMode,
        ]);

        _database.CheckedChanged += (_, _) => OnModeCard(_database, PsuSelectionMode.Database);
        _generic.CheckedChanged += (_, _) => OnModeCard(_generic, PsuSelectionMode.Generic);
        _manual.CheckedChanged += (_, _) => OnModeCard(_manual, PsuSelectionMode.Manual);
        _brand.SelectionChanged += (_, _) => Guarded(() => _model.SetBrand(_brand.SelectedValue as string));
        _wattage.SelectionChanged += (_, _) => Guarded(() => _model.SetWattage(_wattage.SelectedValue as int?));
        _rating.SelectionChanged += (_, _) => Guarded(() => _model.SetRating(_rating.SelectedValue as string));
        _search.TextChanged += (_, _) => Guarded(() => _model.SetSearchText(_search.Text));
        _list.SelectedIndexChanged += (_, _) => Guarded(() =>
        {
            if (_list.SelectedRow?.Tag is string id)
            {
                _model.Select(id);
            }
        });
        _list.LinkClicked += (_, index) => OpenReport(index);
        _genericWattage.TextChanged += (_, _) => Guarded(() => _model.SetGenericWattage(_genericWattage.Text));
        _genericRating.SelectionChanged += (_, _) => Guarded(() => _model.SetGenericRating(_genericRating.SelectedValue as EightyPlusRating?));
        _manualEntry.SelectedIndexChanged += (_, _) => Guarded(() =>
            _model.ManualEntry = _manualEntry.SelectedIndex == 0 ? ManualCurveEntry.EfficiencyForm : ManualCurveEntry.PointGrid);
        _generate.Click += (_, _) => _model.GenerateFromEfficiencies(_formFields[0].Text, _formFields[1].Text, _formFields[2].Text, _formFields[3].Text, _formFields[4].Text);
        _import.Click += (_, _) => ImportCsv();
        _export.Click += (_, _) => ExportCsv();
        _voltage.LinkClicked += (_, _) =>
        {
            _voltageOpen = !_voltageOpen;
            RefreshView();
        };
        _voltageMode.SelectedIndexChanged += (_, _) => Guarded(() => _model.VoltageMode = VoltageModes[Math.Max(0, _voltageMode.SelectedIndex)]);
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    /// <summary>Shows the Auto | 115 V | 230 V override (as "Change" does).</summary>
    public void ShowVoltageOverride()
    {
        _voltageOpen = true;
        RefreshView();
    }

    protected override void OnRefreshView()
    {
        var mode = _model.Mode;
        _database.Checked = mode == PsuSelectionMode.Database;
        _generic.Checked = mode == PsuSelectionMode.Generic;
        _manual.Checked = mode == PsuSelectionMode.Manual;

        // Database.
        var db = mode == PsuSelectionMode.Database;
        foreach (var c in new Control[] { _brand, _wattage, _rating, _search, _list })
        {
            c.Visible = db;
        }

        _brand.SetItems(_model.BrandOptions.Select(o => new DropDownItem(o.Value, o.Text)), IndexOf(_model.BrandOptions, _model.Brand));
        _wattage.SetItems(_model.WattageOptions.Select(o => new DropDownItem(o.Value, o.Text)), IndexOf(_model.WattageOptions, _model.Wattage));
        _rating.SetItems(_model.RatingOptions.Select(o => new DropDownItem(o.Value, o.Text)), IndexOf(_model.RatingOptions, _model.Rating));
        SetTextIfChanged(_search, _model.SearchText);
        if (!ReferenceEquals(_shownRows, _model.Rows))
        {
            _shownRows = _model.Rows;
            _list.SetRows(_model.Rows.Select(r => new SelectableRow(r.Title, r.Subtitle, r.LinkText, r.Id)), _model.SelectedRowIndex);
        }
        else
        {
            _list.SelectedIndex = _model.SelectedRowIndex;
        }

        _list.EmptyText = _model.EmptyListText;

        // Generic.
        _genericCard.Visible = mode == PsuSelectionMode.Generic;
        SetTextIfChanged(_genericWattage, _model.GenericWattageText);
        _genericWattage.HasError = _model.GenericWattageError is not null;
        _genericError.Text = _model.GenericWattageError ?? string.Empty;
        _genericError.Visible = _model.GenericWattageError is not null;
        _genericRating.SetItems(_model.GenericRatingOptions.Select(o => new DropDownItem(o.Value, o.Text)), IndexOf(_model.GenericRatingOptions, _model.GenericRating));
        var preview = _model.GenericPreview;
        for (var i = 0; i < _genericTiles.Length && i < preview.Count; i++)
        {
            _genericTiles[i].Caption = preview[i].Caption;
            _genericTiles[i].Value = preview[i].Value;
        }

        // Manual.
        var manual = mode == PsuSelectionMode.Manual;
        var form = _model.ManualEntry == ManualCurveEntry.EfficiencyForm;
        _manualEntry.Visible = manual;
        _manualEntry.SetItems(["Efficiency form", "Point grid"], form ? 0 : 1);
        _formCard.Visible = manual && form;
        _formError.Text = _model.FormError ?? string.Empty;
        _formError.Visible = _model.FormError is not null;
        _formHint.Visible = _model.FormError is null;
        foreach (var c in new Control[] { _grid, _import, _export, _gridHint })
        {
            c.Visible = manual && !form;
        }

        if (!ReferenceEquals(_model.ManualPoints, _gridSource) && !ReferenceEquals(_model.ManualPoints, _gridEdited))
        {
            FillGrid();
        }

        var errors = manual && !form ? _model.ManualErrors.Take(5).ToList() : [];
        while (_manualErrors.Count < errors.Count)
        {
            var line = new InfoLine { Tone = InfoTone.Error };
            _manualErrors.Add(line);
            Controls.Add(line);
        }

        for (var i = 0; i < _manualErrors.Count; i++)
        {
            _manualErrors[i].Visible = i < errors.Count;
            if (i < errors.Count)
            {
                _manualErrors[i].Text = errors[i];
            }
        }

        // Voltage.
        _voltage.Text = _model.VoltageText;
        _voltage.LinkText = _voltageOpen ? "Done" : "Change";
        _voltageMode.Visible = _voltageOpen;
        _voltageMode.SetItems(["Auto", "115 V", "230 V"], Array.IndexOf(VoltageModes, _model.VoltageMode));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = Theme.Gap;
        const float p = Theme.CardPadding;
        const float cardHeight = 66;
        const float filtersTop = cardHeight + gap;
        const float listTop = filtersTop + Theme.FieldHeight + gap;
        float PrefWidth(Control c) => c.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi;
        float PrefHeight(Control c) => c.GetPreferredSize(Size.Empty).Height * 96f / DeviceDpi;

        var card = (w - (2 * gap)) / 3;
        _database.Bounds = Dip(0, 0, card, cardHeight);
        _generic.Bounds = Dip(card + gap, 0, card, cardHeight);
        _manual.Bounds = Dip(2 * (card + gap), 0, w - (2 * (card + gap)), cardHeight);

        // Bottom: the voltage line (and the override segments next to it).
        var voltageTop = h - Theme.ButtonHeight;
        var voltageWidth = Math.Min(_voltage.GetPreferredSize(Size.Empty).Width, Px(w - 240));
        _voltage.Bounds = new Rectangle(Px(2), Px(voltageTop + ((Theme.ButtonHeight - Theme.LineHeight) / 2)), voltageWidth, Px(Theme.LineHeight));
        _voltageMode.Bounds = new Rectangle(_voltage.Right + Px(12), Px(voltageTop), Math.Max(Px(200), _voltageMode.GetPreferredSize(Size.Empty).Width), Px(Theme.ButtonHeight));
        var contentBottom = voltageTop - gap;

        // Database: four equal columns, then the list.
        var column = (w - (3 * gap)) / 4;
        _brand.Bounds = Dip(0, filtersTop, column, Theme.FieldHeight);
        _wattage.Bounds = Dip(column + gap, filtersTop, column, Theme.FieldHeight);
        _rating.Bounds = Dip(2 * (column + gap), filtersTop, column, Theme.FieldHeight);
        _search.Bounds = Dip(3 * (column + gap), filtersTop, w - (3 * (column + gap)), Theme.FieldHeight);
        _list.Bounds = Dip(0, listTop, w, contentBottom - listTop);

        // Generic: one card with the wattage and rating fields and the 20/50/100 % tiles beside them.
        _genericCard.Bounds = Dip(0, filtersTop, w, contentBottom - filtersTop);
        var t = _genericCard.ContentBounds.Top * 96f / DeviceDpi;
        const float wattageWidth = 150;
        const float ratingWidth = 180;
        _genericWattageLabel.Bounds = Dip(p, t, wattageWidth, 18);
        _genericWattage.Bounds = Dip(p, t + 20, wattageWidth, Theme.FieldHeight);
        var ratingLeft = p + wattageWidth + 12;
        _genericRatingLabel.Bounds = Dip(ratingLeft, t, ratingWidth, 18);
        _genericRating.Bounds = Dip(ratingLeft, t + 20, ratingWidth, Theme.FieldHeight);
        var tileLeft = ratingLeft + ratingWidth + 16;
        var tileWidth = Math.Min(110, (w - p - tileLeft - 16) / 3);
        for (var i = 0; i < _genericTiles.Length; i++)
        {
            _genericTiles[i].Bounds = Dip(tileLeft + (i * (tileWidth + 8)), t, tileWidth, 50);
        }

        _genericWarning.Bounds = Dip(p, t + 62, w - (2 * p), Theme.LineHeight);
        _genericError.Bounds = Dip(p, t + 62 + Theme.LineHeight + 4, w - (2 * p), Theme.LineHeight);

        // Manual: entry switch, then the form card or the grid with its commands and errors.
        _manualEntry.Bounds = Dip(0, filtersTop, Math.Max(240, PrefWidth(_manualEntry)), Theme.ButtonHeight);
        var manualTop = filtersTop + Theme.ButtonHeight + gap;
        var manualHeight = contentBottom - manualTop;
        _formCard.Bounds = Dip(0, manualTop, w, manualHeight);
        var ft = _formCard.ContentBounds.Top * 96f / DeviceDpi;
        const float fieldWidth = 96;
        for (var i = 0; i < _formFields.Length; i++)
        {
            var x = p + (i * (fieldWidth + 10));
            _formLabels[i].Bounds = Dip(x, ft, fieldWidth, 18);
            _formFields[i].Bounds = Dip(x, ft + 20, fieldWidth, Theme.FieldHeight);
        }

        var generateHeight = PrefHeight(_generate);
        _generate.Bounds = Dip(p + (5 * (fieldWidth + 10)) + 6, ft + 20 + ((Theme.FieldHeight - generateHeight) / 2), PrefWidth(_generate), generateHeight);
        _formError.Bounds = Dip(p, ft + 20 + Theme.FieldHeight + 10, w - (2 * p), Theme.LineHeight);
        _formHint.Bounds = Dip(p, ft + 20 + Theme.FieldHeight + 10, w - (2 * p), Theme.LineHeight);

        const float gridWidth = 360;
        _grid.Bounds = Dip(0, manualTop, gridWidth, manualHeight);
        var right = gridWidth + gap;
        var importWidth = PrefWidth(_import);
        _import.Bounds = Dip(right, manualTop, importWidth, PrefHeight(_import));
        _export.Bounds = Dip(right + importWidth + 20, manualTop, PrefWidth(_export), PrefHeight(_export));
        var hintWidth = Px(w - right);
        _gridHint.Bounds = new Rectangle(Px(right), Px(manualTop + Theme.LineHeight + 6), hintWidth, _gridHint.GetPreferredHeight(hintWidth));
        var y = _gridHint.Bottom + Px(8);
        foreach (var line in _manualErrors.Where(l => l.Visible))
        {
            var height = line.GetPreferredHeight(hintWidth);
            line.Bounds = new Rectangle(Px(right), y, hintWidth, height);
            y += height + Px(4);
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        GridStyler.Apply(_grid);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    private static int IndexOf<T>(IReadOnlyList<WizardOption<T>> options, T value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(options[i].Value, value))
            {
                return i;
            }
        }

        return value is null ? -1 : 0;
    }

    private void OnModelChanged(object? sender, EventArgs e) => RefreshView();

    private void Guarded(Action action)
    {
        if (!IsRefreshing)
        {
            action();
        }
    }

    private void OnModeCard(RadioCard card, PsuSelectionMode mode)
    {
        if (!IsRefreshing && card.Checked)
        {
            _model.Mode = mode;
        }
    }

    private void SetUpGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ac", HeaderText = "AC W", SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dc", HeaderText = "DC W", SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        GridStyler.Apply(_grid);
        _grid.CellEndEdit += (_, _) => PushGrid();
        _grid.UserDeletedRow += (_, _) => PushGrid();
    }

    private void FillGrid()
    {
        _gridSource = _model.ManualPoints;
        _grid.Rows.Clear();
        foreach (var p in _model.ManualPoints)
        {
            _grid.Rows.Add(_model.FormatGridValue(p.AcWatts), _model.FormatGridValue(p.DcWatts));
        }
    }

    private void PushGrid()
    {
        if (IsRefreshing)
        {
            return;
        }

        var rows = _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => (Ac: r.Cells[0].Value?.ToString(), Dc: r.Cells[1].Value?.ToString()))
            .ToList();
        _model.SetManualRows(rows);
        _gridEdited = _model.ManualPoints;
    }

    private void OpenReport(int index)
    {
        if (index < 0 || index >= _model.Rows.Count || _model.Rows[index].Url is not { } url ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ThemedDialog.Show(FindForm(), "Could not open the report", ex.Message);
        }
    }

    private void ImportCsv()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Title = "Import curve CSV" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _model.ImportCsv(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedDialog.Show(FindForm(), "Import failed", ex.Message);
        }
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", Title = "Export curve CSV", FileName = "psu-curve.csv" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _model.ExportCsv());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedDialog.Show(FindForm(), "Export failed", ex.Message);
        }
    }
}
