using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>One row of the "Customize HWiNFO sensor names" table.</summary>
/// <param name="KeyId">The sensor key id (<see cref="SensorKey.Id"/>), used to address the row.</param>
/// <param name="Subkey">The registry subkey (<c>Power0</c>), for reference.</param>
/// <param name="DefaultName">The default name (left column).</param>
/// <param name="CustomName">The user's name (right column); empty means the default.</param>
/// <param name="Error">The validation error of the row, or <c>null</c>.</param>
public sealed record SensorNameRow(string KeyId, string Subkey, string DefaultName, string CustomName, string? Error)
{
    /// <summary>Gets the name HWiNFO will show: the custom name, or the default when empty.</summary>
    public string EffectiveName => CustomName.Trim().Length > 0 ? CustomName.Trim() : DefaultName;
}

/// <summary>
/// The "Customize HWiNFO sensor names" dialog: a row per published sensor (default name, your name), "Reset to
/// defaults", validation (non-empty, unique within the group, no double quote), and the rename warning before saving
/// changed names (<see cref="DeviceFormModel.RenameWarningText"/>). Nothing is applied before <see cref="Save"/>.
/// </summary>
/// <remarks>Not thread-safe: call from the UI thread.</remarks>
public sealed class SensorNamesModel
{
    private readonly IBridgeRuntime _runtime;
    private readonly AppSettings _settings;
    private readonly List<(SensorKey Key, string Subkey, string Default)> _sensors;
    private readonly Dictionary<string, string> _custom = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _original;

    /// <summary>Creates the model from the runtime's current settings.</summary>
    /// <param name="runtime">The runtime.</param>
    public SensorNamesModel(IBridgeRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _settings = runtime.Settings?.Clone() ?? new AppSettings();
        _sensors = SensorLayout.Build(_settings).Slots.Select(s => (s.Key, s.Subkey, SensorNaming.DefaultName(s.Key, _settings))).ToList();
        foreach (var (key, _, _) in _sensors)
        {
            if (_settings.SensorNames.TryGetValue(key.Id, out var name))
            {
                _custom[key.Id] = name;
            }
        }

        _original = EffectiveNames();
    }

    /// <summary>Raised when anything visible changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the rows in subkey order (Power sensors, then Other sensors).</summary>
    public IReadOnlyList<SensorNameRow> Rows
    {
        get
        {
            var effective = EffectiveNames();
            var duplicates = effective.Values.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _sensors.Select(s =>
            {
                var custom = _custom.GetValueOrDefault(s.Key.Id) ?? string.Empty;
                var name = effective[s.Key.Id];
                string? error = null;
                if (custom.Trim().Length > 0 && !SensorNaming.IsValidName(custom))
                {
                    error = custom.Contains('"', StringComparison.Ordinal)
                        ? "The name can't contain a double quote."
                        : $"Use at most {SensorNaming.MaxNameLength} characters.";
                }
                else if (duplicates.Contains(name))
                {
                    error = "Another sensor already has this name.";
                }

                return new SensorNameRow(s.Key.Id, s.Subkey, s.Default, custom, error);
            }).ToList();
        }
    }

    /// <summary>Gets whether every row is valid (Save is allowed).</summary>
    public bool IsValid => Rows.All(r => r.Error is null);

    /// <summary>Gets whether any effective name differs from the saved ones.</summary>
    public bool HasChanges
    {
        get
        {
            var now = EffectiveNames();
            return now.Any(p => !string.Equals(_original.GetValueOrDefault(p.Key), p.Value, StringComparison.Ordinal));
        }
    }

    /// <summary>Gets whether to show the rename warning before saving (names changed).</summary>
    public bool ShowRenameWarning => HasChanges;

    /// <summary>
    /// Gets the "Update the names in HWiNFO" help for the pending change: every sensor whose effective name differs from
    /// the saved one (old → new), in row order. Opened by "Show me how" next to the rename warning.
    /// </summary>
    public HwinfoRenameHelp RenameHelp
    {
        get
        {
            var now = EffectiveNames();
            return HwinfoRenameHelp.ForSensorNames(_original, _sensors.Select(s => new KeyValuePair<string, string>(s.Key.Id, now[s.Key.Id])));
        }
    }

    /// <summary>Gets the last apply error, or <c>null</c>.</summary>
    public string? ApplyError { get; private set; }

    /// <summary>Sets a row's custom name; empty text means the default.</summary>
    /// <param name="keyId">The row's <see cref="SensorNameRow.KeyId"/>.</param>
    /// <param name="name">The name.</param>
    public void SetCustomName(string keyId, string? name)
    {
        if (!_sensors.Any(s => s.Key.Id == keyId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            _custom.Remove(keyId);
        }
        else
        {
            _custom[keyId] = name;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Reset to defaults": clears every custom name (applied on <see cref="Save"/>).</summary>
    public void ResetToDefaults()
    {
        _custom.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves the names through the runtime (only the non-default names are stored). Show the rename warning first when
    /// <see cref="ShowRenameWarning"/>.
    /// </summary>
    /// <returns>The outcome, or <c>null</c> when the names are invalid.</returns>
    public ApplyOutcome? Save()
    {
        if (!IsValid)
        {
            return null;
        }

        var clone = _runtime.Settings?.Clone() ?? new AppSettings();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, _, defaultName) in _sensors)
        {
            if (_custom.TryGetValue(key.Id, out var custom) && custom.Trim().Length > 0 && !string.Equals(custom.Trim(), defaultName, StringComparison.Ordinal))
            {
                names[key.Id] = custom.Trim();
            }
        }

        clone.SensorNames = names;
        var outcome = _runtime.Apply(clone);
        ApplyError = outcome.Success ? null : outcome.Error;
        Changed?.Invoke(this, EventArgs.Empty);
        return outcome;
    }

    private Dictionary<string, string> EffectiveNames() =>
        _sensors.ToDictionary(
            s => s.Key.Id,
            s => _custom.TryGetValue(s.Key.Id, out var c) && c.Trim().Length > 0 ? c.Trim() : s.Default,
            StringComparer.Ordinal);
}
