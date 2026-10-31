using System.Globalization;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>A PSU in the step-3 list.</summary>
/// <param name="Id">The database id.</param>
/// <param name="Title">Model and revision ("Prime Fanless TX-700"), with the brand when no brand filter is set.</param>
/// <param name="Subtitle">"80 Plus Titanium · Cybenetics ETA Titanium".</param>
/// <param name="LinkText">"report 21PS1872A ↗", or <c>null</c> without a source URL.</param>
/// <param name="Url">The source report URL, or <c>null</c>.</param>
public sealed record PsuRow(string Id, string Title, string Subtitle, string? LinkText, string? Url);

/// <summary>A value tile: caption and value ("20 % load", "90 %").</summary>
/// <param name="Caption">The caption.</param>
/// <param name="Value">The value.</param>
public sealed record WizardTile(string Caption, string Value);

/// <summary>How the manual curve is entered.</summary>
public enum ManualCurveEntry
{
    /// <summary>Rated wattage and efficiency at 10/20/50/100 % load, which generates the grid.</summary>
    EfficiencyForm,

    /// <summary>The (AC W, DC W) grid.</summary>
    PointGrid,
}

/// <summary>
/// Wizard step 3 (and the Settings "Change…" dialog): database, generic or manual curve, plus the input voltage.
/// Database filters cascade Brand → Wattage → Rating (each with "Any"); a parent change resets children that are no
/// longer available; the search text narrows the filtered list (intersection).
/// </summary>
public sealed class PsuStepModel
{
    /// <summary>The approximate-curve warning of generic mode.</summary>
    public const string GenericWarning = "Approximate: least accurate at low load. Use the database or a manual curve if you can.";

    private static readonly double[] PreviewLoads = [0.20, 0.50, 1.00];

    private readonly PsuDatabase _database;
    private readonly PsuDatabaseQuery _query;
    private readonly CultureInfo _culture;
    private List<CurvePoint> _manualPoints = [];
    private List<string> _manualInputErrors = [];

    /// <summary>Creates the model.</summary>
    /// <param name="database">The PSU database.</param>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    public PsuStepModel(PsuDatabase database, CultureInfo? culture = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _query = new PsuDatabaseQuery(database);
        _culture = culture ?? CultureInfo.CurrentCulture;
        Rebuild();
    }

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the PSU database.</summary>
    public PsuDatabase Database => _database;

    /// <summary>Gets or sets the selection mode.</summary>
    public PsuSelectionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                RaiseChanged();
            }
        }
    }

    private PsuSelectionMode _mode = PsuSelectionMode.Database;

    // ---- Database mode ----

    /// <summary>Gets the brand filter (<c>null</c> = Any).</summary>
    public string? Brand { get; private set; }

    /// <summary>Gets the wattage filter (<c>null</c> = Any).</summary>
    public int? Wattage { get; private set; }

    /// <summary>Gets the 80 Plus rating filter (<c>null</c> = Any).</summary>
    public string? Rating { get; private set; }

    /// <summary>Gets the search text.</summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Gets the brand options ("Any" first).</summary>
    public IReadOnlyList<WizardOption<string?>> BrandOptions { get; private set; } = [];

    /// <summary>Gets the wattage options for the brand ("Any" first, then "700 W", …).</summary>
    public IReadOnlyList<WizardOption<int?>> WattageOptions { get; private set; } = [];

    /// <summary>Gets the rating options for brand and wattage ("Any" first).</summary>
    public IReadOnlyList<WizardOption<string?>> RatingOptions { get; private set; } = [];

    /// <summary>Gets the matching models: the filter result intersected with the search.</summary>
    public IReadOnlyList<PsuRow> Rows { get; private set; } = [];

    /// <summary>Gets the selected database id (kept while filters hide it; cleared only by another selection).</summary>
    public string? SelectedId { get; private set; }

    /// <summary>Gets the index of <see cref="SelectedId"/> in <see cref="Rows"/>, or -1.</summary>
    public int SelectedRowIndex => SelectedId is null ? -1 : Rows.ToList().FindIndex(r => string.Equals(r.Id, SelectedId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the selected database entry.</summary>
    public PsuEntry? SelectedEntry => SelectedId is null ? null : _query.FindById(SelectedId);

    /// <summary>Gets the text for an empty list.</summary>
    public string EmptyListText => _database.Psus.Count == 0
        ? "The PSU database is empty. Use a generic or manual curve."
        : "No PSU matches. Change the filters, or use a generic or manual curve.";

    // ---- Generic mode ----

    /// <summary>Gets the rated wattage text of generic mode.</summary>
    public string GenericWattageText { get; private set; } = string.Empty;

    /// <summary>Gets the parsed generic wattage, or <c>null</c>.</summary>
    public int? GenericWattage => TryParseWattage(GenericWattageText, out var w) ? w : null;

    /// <summary>Gets the error for the generic wattage field, or <c>null</c>.</summary>
    public string? GenericWattageError =>
        GenericWattageText.Trim().Length == 0 || GenericWattage is not null ? null : "Enter the rated wattage in W, e.g. 850.";

    /// <summary>Gets the generic 80 Plus rating.</summary>
    public EightyPlusRating? GenericRating { get; private set; }

    /// <summary>Gets the rating options of generic mode (White … Titanium).</summary>
    public IReadOnlyList<WizardOption<EightyPlusRating?>> GenericRatingOptions { get; } =
        Enum.GetValues<EightyPlusRating>().Select(r => new WizardOption<EightyPlusRating?>(r, RatingText(r))).ToList();

    /// <summary>Gets the efficiency at 20/50/100 % load of the generic curve for the effective voltage.</summary>
    public IReadOnlyList<WizardTile> GenericPreview
    {
        get
        {
            var rating = GenericRating;
            var requirements = rating is EightyPlusRating r ? GenericCurveGenerator.GetRequirements(r, EffectiveVoltage) : null;
            double?[] values = requirements is null ? [null, null, null] : [requirements.At20Percent, requirements.At50Percent, requirements.At100Percent];
            return PreviewLoads.Select((load, i) => new WizardTile(
                $"{(load * 100).ToString("0", _culture)} % load",
                values[i] is double v ? v.ToString("0", _culture) + " %" : UiFormat.Dash)).ToList();
        }
    }

    // ---- Manual mode ----

    /// <summary>Gets or sets how the manual curve is entered.</summary>
    public ManualCurveEntry ManualEntry
    {
        get => _manualEntry;
        set
        {
            if (_manualEntry != value)
            {
                _manualEntry = value;
                RaiseChanged();
            }
        }
    }

    private ManualCurveEntry _manualEntry = ManualCurveEntry.PointGrid;

    /// <summary>Gets the manual curve points (parsed rows).</summary>
    public IReadOnlyList<CurvePoint> ManualPoints => _manualPoints;

    /// <summary>Gets the manual-curve problems: parse errors of the grid or CSV, then curve validation errors.</summary>
    public IReadOnlyList<string> ManualErrors =>
        _manualInputErrors.Concat(CurveValidator.Validate(_manualPoints).Select(e => e.Message)).ToList();

    /// <summary>Gets the error of the efficiency form, or <c>null</c>.</summary>
    public string? FormError { get; private set; }

    // ---- Voltage ----

    /// <summary>Gets or sets the input voltage mode.</summary>
    public InputVoltageMode VoltageMode
    {
        get => _voltageMode;
        set
        {
            if (_voltageMode != value)
            {
                _voltageMode = value;
                RaiseChanged();
            }
        }
    }

    private InputVoltageMode _voltageMode = InputVoltageMode.Auto;

    /// <summary>Gets or sets the voltage the plug measured (from the step-2 test), if any.</summary>
    public double? MeasuredVolts
    {
        get => _measuredVolts;
        set
        {
            if (_measuredVolts != value)
            {
                _measuredVolts = value;
                RaiseChanged();
            }
        }
    }

    private double? _measuredVolts;

    /// <summary>Gets the voltage of the curve that will be used.</summary>
    public InputVoltage EffectiveVoltage => VoltageSelector.Resolve(VoltageMode, MeasuredVolts);

    /// <summary>
    /// Gets the voltage line ("Input voltage: 230 V curve (plug measured 226.9 V)"). When the selected PSU has no curve
    /// for that voltage it reads "Input voltage: 230 V curve (115 V data not available for this unit) · plug measured 118.2 V".
    /// </summary>
    public string VoltageText
    {
        get
        {
            var measured = MeasuredVolts is double v && double.IsFinite(v) && v > 0 ? UiFormat.Volts(v, _culture) : null;
            var note = PsuCurveResolver.Resolve(BuildPsuSettings(), _database)?.CurveNote(EffectiveVoltage);
            if (note is not null)
            {
                var how = VoltageMode == InputVoltageMode.Auto
                    ? (measured is null ? null : "plug measured " + measured)
                    : (measured is null ? "set manually" : "set manually; plug measured " + measured);
                return how is null ? "Input voltage: " + note : $"Input voltage: {note} · {how}";
            }

            var curve = $"Input voltage: {(int)EffectiveVoltage} V curve";
            if (VoltageMode == InputVoltageMode.Auto)
            {
                return measured is null ? curve + " (picked automatically from the plug's voltage)" : $"{curve} (plug measured {measured})";
            }

            return measured is null ? curve + " (set manually)" : $"{curve} (set manually; plug measured {measured})";
        }
    }

    // ---- Validity ----

    /// <summary>Gets whether the current selection resolves to a curve (Next is allowed).</summary>
    public bool IsValid => PsuCurveResolver.Resolve(BuildPsuSettings(), _database) is not null;

    /// <summary>Gets why Next is not allowed, or <c>null</c>.</summary>
    public string? ValidationMessage
    {
        get
        {
            if (IsValid)
            {
                return null;
            }

            return Mode switch
            {
                PsuSelectionMode.Database => SelectedEntry is null ? "Select your PSU in the list." : "This database entry has no usable curve.",
                PsuSelectionMode.Generic => "Enter the rated wattage and pick the 80 Plus rating.",
                _ => "Enter at least two valid curve points.",
            };
        }
    }

    // ---- Loading and applying ----

    /// <summary>Initialises the model from settings (re-run or the Settings dialog).</summary>
    /// <param name="settings">The settings.</param>
    public void LoadFrom(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var psu = settings.Psu ?? new PsuSettings();
        _mode = psu.Mode;
        SelectedId = psu.DatabaseId;
        Brand = SelectedEntry?.Brand;
        Wattage = null;
        Rating = null;
        SearchText = string.Empty;
        GenericWattageText = psu.GenericWattage?.ToString(_culture) ?? string.Empty;
        GenericRating = psu.GenericRating;
        _manualPoints = psu.ManualCurve?.ToList() ?? [];
        _manualInputErrors = [];
        _manualEntry = _manualPoints.Count > 0 ? ManualCurveEntry.PointGrid : ManualCurveEntry.EfficiencyForm;
        _voltageMode = settings.VoltageMode;
        Rebuild();
        RaiseChanged();
    }

    /// <summary>Returns the PSU settings for the current state (all three modes' data is kept).</summary>
    public PsuSettings BuildPsuSettings() => new()
    {
        Mode = Mode,
        DatabaseId = SelectedId,
        GenericWattage = GenericWattage,
        GenericRating = GenericRating,
        ManualCurve = _manualPoints.Count > 0 ? _manualPoints.ToList() : null,
    };

    /// <summary>Writes the PSU selection and the voltage mode into settings.</summary>
    /// <param name="settings">The draft settings.</param>
    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Psu = BuildPsuSettings();
        settings.VoltageMode = VoltageMode;
    }

    // ---- Database mode actions ----

    /// <summary>Sets the brand filter; wattage and rating are reset when no longer available.</summary>
    /// <param name="brand">The brand, or <c>null</c> for Any.</param>
    public void SetBrand(string? brand)
    {
        Brand = string.IsNullOrWhiteSpace(brand) ? null : brand;
        Rebuild();
        RaiseChanged();
    }

    /// <summary>Sets the wattage filter; the rating is reset when no longer available.</summary>
    /// <param name="wattage">The wattage, or <c>null</c> for Any.</param>
    public void SetWattage(int? wattage)
    {
        Wattage = wattage;
        Rebuild();
        RaiseChanged();
    }

    /// <summary>Sets the rating filter.</summary>
    /// <param name="rating">The rating, or <c>null</c> for Any.</param>
    public void SetRating(string? rating)
    {
        Rating = string.IsNullOrWhiteSpace(rating) ? null : rating;
        Rebuild();
        RaiseChanged();
    }

    /// <summary>Sets the search text (narrows the filtered list).</summary>
    /// <param name="text">The text.</param>
    public void SetSearchText(string? text)
    {
        text ??= string.Empty;
        if (text == SearchText)
        {
            return;
        }

        SearchText = text;
        Rebuild();
        RaiseChanged();
    }

    /// <summary>Selects a database entry (and switches to database mode).</summary>
    /// <param name="id">The entry id, or <c>null</c> to clear.</param>
    public void Select(string? id)
    {
        SelectedId = id is null ? null : _query.FindById(id)?.Id;
        _mode = PsuSelectionMode.Database;
        RaiseChanged();
    }

    // ---- Generic mode actions ----

    /// <summary>Sets the generic wattage text.</summary>
    /// <param name="text">The text.</param>
    public void SetGenericWattage(string? text)
    {
        GenericWattageText = text ?? string.Empty;
        RaiseChanged();
    }

    /// <summary>Sets the generic rating.</summary>
    /// <param name="rating">The rating.</param>
    public void SetGenericRating(EightyPlusRating? rating)
    {
        GenericRating = rating;
        RaiseChanged();
    }

    // ---- Manual mode actions ----

    /// <summary>Replaces the manual points from grid rows of text; blank rows are ignored.</summary>
    /// <param name="rows">The (AC, DC) cell texts in grid order.</param>
    public void SetManualRows(IEnumerable<(string? Ac, string? Dc)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var points = new List<CurvePoint>();
        var errors = new List<string>();
        var number = 0;
        foreach (var (ac, dc) in rows)
        {
            number++;
            if (string.IsNullOrWhiteSpace(ac) && string.IsNullOrWhiteSpace(dc))
            {
                continue;
            }

            if (!WizardText.TryParseNumber(ac, _culture, out var acW) || !WizardText.TryParseNumber(dc, _culture, out var dcW))
            {
                errors.Add($"Grid row {number}: enter numbers for AC W and DC W.");
                continue;
            }

            points.Add(new CurvePoint(acW, dcW));
        }

        _manualPoints = points;
        _manualInputErrors = errors;
        RaiseChanged();
    }

    /// <summary>Replaces the manual points (e.g. after generating them).</summary>
    /// <param name="points">The points.</param>
    public void SetManualPoints(IEnumerable<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _manualPoints = points.ToList();
        _manualInputErrors = [];
        RaiseChanged();
    }

    /// <summary>
    /// Generates the grid from the rated wattage and the efficiency in % at 10/20/50/100 % load, and switches to the
    /// point grid. On invalid input sets <see cref="FormError"/> and returns <c>false</c>.
    /// </summary>
    /// <param name="rated">The rated DC wattage text.</param>
    /// <param name="eff10">Efficiency at 10 % load.</param>
    /// <param name="eff20">Efficiency at 20 % load.</param>
    /// <param name="eff50">Efficiency at 50 % load.</param>
    /// <param name="eff100">Efficiency at 100 % load.</param>
    public bool GenerateFromEfficiencies(string? rated, string? eff10, string? eff20, string? eff50, string? eff100)
    {
        if (!TryParseWattage(rated, out var watts))
        {
            FormError = "Enter the rated wattage in W, e.g. 750.";
            RaiseChanged();
            return false;
        }

        var texts = new[] { eff10, eff20, eff50, eff100 };
        var values = new double[texts.Length];
        for (var i = 0; i < texts.Length; i++)
        {
            if (!WizardText.TryParseNumber(texts[i], _culture, out values[i]) || values[i] <= 0 || values[i] >= 100)
            {
                var load = (SimplifiedCurveBuilder.LoadFractions[i] * 100).ToString("0", _culture);
                FormError = $"Enter the efficiency at {load} % load as a percentage between 0 and 100.";
                RaiseChanged();
                return false;
            }
        }

        FormError = null;
        _manualPoints = SimplifiedCurveBuilder.BuildPoints(watts, values[0], values[1], values[2], values[3]).ToList();
        _manualInputErrors = [];
        _manualEntry = ManualCurveEntry.PointGrid;
        RaiseChanged();
        return true;
    }

    /// <summary>Imports a CSV (<c>ac_w,dc_w</c>); on parse errors the points are kept and the errors shown.</summary>
    /// <param name="text">The CSV text.</param>
    /// <returns><c>true</c> when the file parsed without errors.</returns>
    public bool ImportCsv(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = CurveCsv.Parse(text);
        if (!result.Success)
        {
            _manualInputErrors = result.Errors.Select(e => e.Message).ToList();
            RaiseChanged();
            return false;
        }

        _manualPoints = result.Points.ToList();
        _manualInputErrors = [];
        _manualEntry = ManualCurveEntry.PointGrid;
        RaiseChanged();
        return true;
    }

    /// <summary>Exports the manual points as CSV.</summary>
    public string ExportCsv() => CurveCsv.Export(_manualPoints);

    /// <summary>Formats a grid cell value.</summary>
    /// <param name="watts">The value.</param>
    public string FormatGridValue(double watts) => watts.ToString("0.###", _culture);

    private static string RatingText(EightyPlusRating rating) => rating == EightyPlusRating.White ? "80 Plus (White)" : rating.ToString();

    private bool TryParseWattage(string? text, out int watts)
    {
        if (WizardText.TryParseNumber(text, _culture, out var value) && value >= 50 && value <= 10000 && Math.Abs(value - Math.Round(value)) < 1e-9)
        {
            watts = (int)Math.Round(value);
            return true;
        }

        watts = 0;
        return false;
    }

    private void Rebuild()
    {
        var brands = _query.GetBrands();
        if (Brand is not null && !brands.Contains(Brand, StringComparer.OrdinalIgnoreCase))
        {
            Brand = null;
        }

        var wattages = _query.GetWattages(Brand);
        if (Wattage is int w && !wattages.Contains(w))
        {
            Wattage = null;
        }

        var ratings = _query.GetRatings(Brand, Wattage);
        if (Rating is not null && !ratings.Contains(Rating, StringComparer.OrdinalIgnoreCase))
        {
            Rating = null;
        }

        BrandOptions = brands.Select(b => new WizardOption<string?>(b, b)).Prepend(new WizardOption<string?>(null, WizardText.Any)).ToList();
        WattageOptions = wattages.Select(x => new WizardOption<int?>(x, x.ToString(_culture) + " W")).Prepend(new WizardOption<int?>(null, WizardText.Any)).ToList();
        RatingOptions = ratings.Select(r => new WizardOption<string?>(r, r)).Prepend(new WizardOption<string?>(null, WizardText.Any)).ToList();

        var found = _query.Search(SearchText).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Rows = _query.GetModels(Brand, Wattage, Rating)
            .Where(e => found.Contains(e.Id))
            .Select(ToRow)
            .ToList();
    }

    private PsuRow ToRow(PsuEntry e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Rating80Plus) && !e.Rating80Plus.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("80 Plus " + e.Rating80Plus);
        }

        if (!string.IsNullOrWhiteSpace(e.RatingCybenetics))
        {
            parts.Add("Cybenetics ETA " + e.RatingCybenetics);
        }

        if (parts.Count == 0)
        {
            parts.Add(e.Wattage.ToString(_culture) + " W");
        }

        string? link = null;
        string? url = null;
        if (e.Source is { } source && !string.IsNullOrWhiteSpace(source.Url))
        {
            url = source.Url;
            link = !string.IsNullOrWhiteSpace(source.ReportId) ? $"report {source.ReportId} ↗" : $"{source.Name} ↗".Trim();
        }

        var title = Brand is null ? $"{e.Brand} {e.DisplayName}".Trim() : e.DisplayName;
        return new PsuRow(e.Id, title, string.Join(" · ", parts), link, url);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
