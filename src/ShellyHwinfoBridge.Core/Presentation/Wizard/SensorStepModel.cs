using System.Globalization;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation.Wizard;

/// <summary>A sensor row of step 4.</summary>
/// <param name="Id">The row id (stable for one <see cref="SensorStepModel.Load"/>).</param>
/// <param name="Device">The HWiNFO device.</param>
/// <param name="Name">The sensor name (HWiNFO label).</param>
/// <param name="ValueText">The current value ("142.3 W"), or empty.</param>
/// <param name="Checked">Whether the sensor is selected.</param>
/// <param name="Category">The category when selected, else <c>null</c>.</param>
public sealed record SensorRow(int Id, string Device, string Name, string ValueText, bool Checked, SensorCategory? Category);

/// <summary>A device group of step 4.</summary>
/// <param name="Device">The HWiNFO device name ("CPU [#0]: Intel Core i9-14900K").</param>
/// <param name="Rows">The power sensors of the device.</param>
public sealed record SensorGroup(string Device, IReadOnlyList<SensorRow> Rows);

/// <summary>A manually typed sensor (fallback without shared memory).</summary>
/// <param name="Id">The row id.</param>
/// <param name="Name">The sensor name.</param>
/// <param name="Category">The category.</param>
public sealed record ManualSensorRow(int Id, string Name, SensorCategory Category);

/// <summary>
/// Wizard step 4 (and the Settings "Edit…" dialog): pick the component power sensors from HWiNFO's shared memory, or type
/// them when shared memory is off; assign categories; preview or override the formula.
/// </summary>
public sealed class SensorStepModel
{
    /// <summary>The group that holds existing selections HWiNFO does not report right now.</summary>
    public const string MissingGroup = "Selected before, not reported by HWiNFO now";

    /// <summary>The fallback notice when shared memory is off.</summary>
    public const string SharedMemoryOffText =
        "Shared memory is off, so HWiNFO's sensor list can't be read. Turn on Shared Memory Support (step 1) and check again, or type the sensor names below.";

    private readonly CultureInfo _culture;
    private List<SensorGroup> _groups = [];
    private List<ManualSensorRow> _manual = [];
    private List<string> _allNames = [];
    private int _nextManualId;

    /// <summary>Creates the model.</summary>
    /// <param name="culture">The display culture; <c>null</c> for the current culture.</param>
    public SensorStepModel(CultureInfo? culture = null) => _culture = culture ?? CultureInfo.CurrentCulture;

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the category options of a row ("—" first).</summary>
    public static IReadOnlyList<WizardOption<SensorCategory?>> CategoryOptions { get; } =
    [
        new(null, UiFormat.Dash),
        new(SensorCategory.Cpu, "CPU"),
        new(SensorCategory.Gpu, "GPU"),
        new(SensorCategory.Ram, "RAM"),
        new(SensorCategory.OtherMeasured, "Other measured"),
    ];

    /// <summary>Gets whether <see cref="Load"/> ran.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Gets whether the sensors are typed by hand (no shared memory).</summary>
    public bool IsManual { get; private set; }

    /// <summary>Gets the device groups (shared memory mode).</summary>
    public IReadOnlyList<SensorGroup> Groups => _groups;

    /// <summary>Gets the typed rows (manual mode).</summary>
    public IReadOnlyList<ManualSensorRow> ManualRows => _manual;

    /// <summary>Gets the selected sensors in display order.</summary>
    public IReadOnlyList<SensorSelection> Selected => IsManual
        ? _manual.Where(m => m.Name.Trim().Length > 0)
            .Select(m => new SensorSelection { Name = m.Name.Trim(), Category = m.Category })
            .ToList()
        : _groups.SelectMany(g => g.Rows)
            .Where(r => r.Checked)
            .Select(r => new SensorSelection
            {
                Name = r.Name,
                Category = r.Category ?? SensorCategory.OtherMeasured,
                Device = r.Device == MissingGroup ? null : r.Device,
            })
            .ToList();

    /// <summary>Gets the selected names that cannot be used in a formula.</summary>
    public IReadOnlyList<string> InvalidNames =>
        Selected.Select(s => s.Name).Where(n => !HwinfoFormula.IsValidSensorName(n)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Gets the warnings: sub-component pairs, duplicate names, invalid names, none selected.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            var selected = Selected;
            var warnings = new List<string>();
            if (!IsManual)
            {
                foreach (var pair in SensorSelectionAnalyzer.FindSubComponentPairs(selected.Where(s => s.Device is not null).Select(s => new SensorInfo(s.Device!, s.Name))))
                {
                    warnings.Add($"{pair.SubComponent.Name} is part of {pair.Aggregate.Name}. Selecting both would count it twice.");
                }
            }

            foreach (var duplicate in SensorSelectionAnalyzer.FindDuplicateNames(selected.Select(s => s.Name), IsManual ? null : _allNames))
            {
                var count = duplicate.Count == 2 ? "Two" : duplicate.Count.ToString(_culture);
                warnings.Add($"{count} sensors are called \"{duplicate.Name}\". HWiNFO formulas reference sensors by name, so this is ambiguous. Rename one of them in HWiNFO (right-click the sensor › Rename).");
            }

            foreach (var name in InvalidNames)
            {
                warnings.Add($"\"{name.Replace("\"", string.Empty, StringComparison.Ordinal)}\" contains a double quote and can't be used in a formula. Rename it in HWiNFO (right-click the sensor › Rename).");
            }

            if (selected.Count == 0 && !IsCustomFormula)
            {
                warnings.Add("No sensors selected: Other Components will equal DC Power (Est.).");
            }

            return warnings;
        }
    }

    /// <summary>Gets the formula built from the valid selected names.</summary>
    public string GeneratedFormula =>
        HwinfoFormula.Build(Selected.Select(s => s.Name).Where(HwinfoFormula.IsValidSensorName));

    /// <summary>Gets the custom formula, or <c>null</c> when the generated one is used.</summary>
    public string? CustomFormula { get; private set; }

    /// <summary>Gets whether a custom formula is set.</summary>
    public bool IsCustomFormula => !string.IsNullOrWhiteSpace(CustomFormula);

    /// <summary>Gets the formula that will be written.</summary>
    public string EffectiveFormula => IsCustomFormula ? CustomFormula!.Trim() : GeneratedFormula;

    /// <summary>Gets whether Next is allowed: no invalid names and a non-blank formula.</summary>
    public bool IsValid => InvalidNames.Count == 0 && !string.IsNullOrWhiteSpace(EffectiveFormula);

    /// <summary>
    /// Loads the sensors. With <paramref name="groups"/> (shared memory on), lists the W sensors per device (without the
    /// bridge's own "Shelly Power" device) and pre-checks <paramref name="existing"/> or, when that is empty, the
    /// heuristic suggestion. Without groups, switches to manual rows filled from <paramref name="existing"/>.
    /// </summary>
    /// <param name="groups">The power sensor groups from shared memory, or <c>null</c> when it is off.</param>
    /// <param name="existing">The current selection (empty on first run).</param>
    /// <param name="customFormula">The current custom formula, or <c>null</c>.</param>
    public void Load(IReadOnlyList<HwinfoDeviceGroup>? groups, IReadOnlyList<SensorSelection> existing, string? customFormula)
    {
        ArgumentNullException.ThrowIfNull(existing);
        CustomFormula = string.IsNullOrWhiteSpace(customFormula) ? null : customFormula;
        IsLoaded = true;
        if (groups is null)
        {
            IsManual = true;
            _groups = [];
            _allNames = [];
            _nextManualId = 0;
            _manual = existing.Select(s => new ManualSensorRow(_nextManualId++, s.Name, s.Category)).ToList();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsManual = false;
        _manual = [];
        var sources = groups
            .Where(g => !WizardText.IsBridgeDevice(g.Device))
            .Select(g => (g.Device, Readings: g.Readings.Where(r => r.IsWatts).ToList()))
            .Where(g => g.Readings.Count > 0)
            .ToList();
        _allNames = sources.SelectMany(g => g.Readings.Select(r => r.Label.Trim())).ToList();

        Dictionary<(string Device, string Name), SensorCategory> picked;
        if (existing.Count > 0)
        {
            picked = [];
            foreach (var s in existing)
            {
                var match = sources
                    .SelectMany(g => g.Readings.Select(r => (g.Device, Name: r.Label.Trim())))
                    .Where(x => x.Name.Equals(s.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                (s.Device is null || x.Device.Equals(s.Device, StringComparison.OrdinalIgnoreCase)))
                    .Select(x => ((string Device, string Name)?)x)
                    .FirstOrDefault();
                if (match is { } m)
                {
                    picked.TryAdd(m, s.Category);
                }
            }
        }
        else
        {
            var all = sources.SelectMany(g => g.Readings.Select(r => new SensorInfo(g.Device, r.Label.Trim()))).ToList();
            picked = SensorHeuristics.SuggestSelection(all)
                .ToDictionary(s => (s.Device, s.Name), s => SensorHeuristics.GetCandidateCategory(s.Name) ?? SensorHeuristics.GuessCategory(s.Name));
        }

        var id = 0;
        var result = new List<SensorGroup>();
        foreach (var (device, readings) in sources)
        {
            var rows = readings.Select(r =>
            {
                var name = r.Label.Trim();
                var isPicked = picked.TryGetValue((device, name), out var category);
                return new SensorRow(id++, device, name, UiFormat.Watts(r.Value, _culture), isPicked, isPicked ? category : null);
            }).ToList();
            result.Add(new SensorGroup(device, rows));
        }

        // Existing selections HWiNFO does not report now stay selected in their own group.
        var missing = existing
            .Where(s => !sources.Any(g => g.Readings.Any(r => r.Label.Trim().Equals(s.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                                             (s.Device is null || g.Device.Equals(s.Device, StringComparison.OrdinalIgnoreCase)))))
            .Select(s => new SensorRow(id++, MissingGroup, s.Name.Trim(), string.Empty, true, s.Category))
            .ToList();
        if (missing.Count > 0)
        {
            result.Add(new SensorGroup(MissingGroup, missing));
        }

        _groups = result;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Checks or unchecks a row; checking assigns a guessed category, unchecking clears it.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="isChecked">Whether the row is selected.</param>
    public void SetChecked(int id, bool isChecked) =>
        UpdateRow(id, r => r.Checked == isChecked
            ? r
            : r with { Checked = isChecked, Category = isChecked ? r.Category ?? Guess(r) : null });

    /// <summary>Guesses a row's category from its name, else from its device ("CPU [#0]: …" → CPU).</summary>
    private static SensorCategory Guess(SensorRow row)
    {
        var category = SensorHeuristics.GuessCategory(row.Name);
        return category == SensorCategory.OtherMeasured && row.Device != MissingGroup ? SensorHeuristics.GuessCategory(row.Device) : category;
    }

    /// <summary>Sets a row's category; a category checks the row, "—" (<c>null</c>) unchecks it.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="category">The category, or <c>null</c>.</param>
    public void SetCategory(int id, SensorCategory? category) =>
        UpdateRow(id, r => r with { Checked = category is not null, Category = category });

    /// <summary>Adds an empty manual row.</summary>
    /// <returns>The new row's id.</returns>
    public int AddManual()
    {
        var id = _nextManualId++;
        _manual.Add(new ManualSensorRow(id, string.Empty, SensorCategory.OtherMeasured));
        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }

    /// <summary>Removes a manual row.</summary>
    /// <param name="id">The row id.</param>
    public void RemoveManual(int id)
    {
        if (_manual.RemoveAll(m => m.Id == id) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets a manual row's name; the category is guessed while it is still the default.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="name">The name.</param>
    public void SetManualName(int id, string? name)
    {
        var index = _manual.FindIndex(m => m.Id == id);
        if (index < 0 || _manual[index].Name == (name ?? string.Empty))
        {
            return;
        }

        var row = _manual[index];
        var category = row.Name.Length == 0 && row.Category == SensorCategory.OtherMeasured ? SensorHeuristics.GuessCategory(name ?? string.Empty) : row.Category;
        _manual[index] = row with { Name = name ?? string.Empty, Category = category };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets a manual row's category.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="category">The category.</param>
    public void SetManualCategory(int id, SensorCategory category)
    {
        var index = _manual.FindIndex(m => m.Id == id);
        if (index >= 0 && _manual[index].Category != category)
        {
            _manual[index] = _manual[index] with { Category = category };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets the formula text; a text equal to the generated formula (or blank) means "generated".</summary>
    /// <param name="formula">The formula.</param>
    public void SetCustomFormula(string? formula)
    {
        var value = string.IsNullOrWhiteSpace(formula) || formula.Trim() == GeneratedFormula ? null : formula;
        if (value != CustomFormula)
        {
            CustomFormula = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drops the custom formula ("Reset to generated").</summary>
    public void ResetFormula() => SetCustomFormula(null);

    /// <summary>Writes the selection and the custom formula into settings.</summary>
    /// <param name="settings">The draft settings.</param>
    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sensors = Selected.ToList();
        settings.CustomFormula = IsCustomFormula ? CustomFormula!.Trim() : null;
    }

    private void UpdateRow(int id, Func<SensorRow, SensorRow> change)
    {
        for (var g = 0; g < _groups.Count; g++)
        {
            var rows = _groups[g].Rows;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Id != id)
                {
                    continue;
                }

                var updated = change(rows[i]);
                if (updated == rows[i])
                {
                    return;
                }

                var list = rows.ToList();
                list[i] = updated;
                _groups[g] = _groups[g] with { Rows = list };
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
    }
}
