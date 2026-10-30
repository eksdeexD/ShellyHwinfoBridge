namespace ShellyHwinfoBridge.Core.Sensors;

/// <summary>A selected sensor whose name is not unique, which makes HWiNFO formulas ambiguous.</summary>
/// <param name="Name">The sensor name.</param>
/// <param name="Count">How many sensors share the name.</param>
public sealed record DuplicateNameWarning(string Name, int Count);

/// <summary>A selected sensor that is probably already included in another selected sensor.</summary>
/// <param name="Aggregate">The total, e.g. <c>CPU Package Power</c>.</param>
/// <param name="SubComponent">The sensor likely included in the total, e.g. <c>IA Cores Power</c>.</param>
public sealed record SubComponentWarning(SensorInfo Aggregate, SensorInfo SubComponent);

/// <summary>Checks a sensor selection for problems before the formula is built.</summary>
public static class SensorSelectionAnalyzer
{
    /// <summary>
    /// Finds selected names that occur more than once (case-insensitive, trimmed), counted across
    /// <paramref name="allSensorNames"/> when given (a name shared with an unselected sensor is ambiguous too),
    /// otherwise across the selection itself.
    /// </summary>
    /// <param name="selectedNames">The selected sensor names.</param>
    /// <param name="allSensorNames">All sensor names known from HWiNFO, if available.</param>
    public static IReadOnlyList<DuplicateNameWarning> FindDuplicateNames(
        IEnumerable<string> selectedNames,
        IEnumerable<string>? allSensorNames = null)
    {
        ArgumentNullException.ThrowIfNull(selectedNames);
        var selected = selectedNames.Select(n => n.Trim()).ToList();
        var counts = (allSensorNames?.Select(n => n.Trim()) ?? selected)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return selected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => counts.TryGetValue(n, out var c) && c > 1)
            .Select(n => new DuplicateNameWarning(n, counts[n]))
            .ToList();
    }

    /// <summary>
    /// Finds selected pairs where one sensor is probably part of another.
    /// </summary>
    /// <remarks>
    /// Heuristic: when an aggregate sensor (see <see cref="SensorHeuristics.IsAggregate"/>) is selected, every
    /// other selected non-DIMM sensor of the same HWiNFO device is reported as a probable sub-component
    /// (e.g. core, SoC or rail powers next to Package Power, or 12V-rail powers next to GPU Power).
    /// Sensors without a device name (manual entry) are never paired.
    /// </remarks>
    /// <param name="selected">The selected sensors.</param>
    public static IReadOnlyList<SubComponentWarning> FindSubComponentPairs(IEnumerable<SensorInfo> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var list = selected.Where(s => !string.IsNullOrWhiteSpace(s.Device)).ToList();
        var warnings = new List<SubComponentWarning>();

        for (var i = 0; i < list.Count; i++)
        {
            var aggregate = list[i];
            if (!SensorHeuristics.IsAggregate(aggregate.Name))
            {
                continue;
            }

            for (var j = 0; j < list.Count; j++)
            {
                var other = list[j];
                if (i == j ||
                    !string.Equals(aggregate.Device, other.Device, StringComparison.OrdinalIgnoreCase) ||
                    SensorHeuristics.GetCandidateCategory(other.Name) == SensorCategory.Ram)
                {
                    continue;
                }

                // Report a pair of two aggregates only once.
                if (SensorHeuristics.IsAggregate(other.Name) && j < i)
                {
                    continue;
                }

                warnings.Add(new SubComponentWarning(aggregate, other));
            }
        }

        return warnings;
    }
}
