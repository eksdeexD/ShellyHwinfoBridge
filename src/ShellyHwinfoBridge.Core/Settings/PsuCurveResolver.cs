using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;

namespace ShellyHwinfoBridge.Core.Settings;

/// <summary>Turns a <see cref="PsuSettings"/> selection into curves.</summary>
public static class PsuCurveResolver
{
    /// <summary>Resolves the curves for the selection, or <c>null</c> when the selection is incomplete or invalid.</summary>
    /// <param name="psu">The PSU selection.</param>
    /// <param name="database">The PSU database (for database mode).</param>
    public static PsuCurveSet? Resolve(PsuSettings psu, PsuDatabase database)
    {
        ArgumentNullException.ThrowIfNull(psu);
        ArgumentNullException.ThrowIfNull(database);
        switch (psu.Mode)
        {
            case PsuSelectionMode.Database:
                return psu.DatabaseId is null ? null : new PsuDatabaseQuery(database).FindById(psu.DatabaseId)?.GetCurveSet();

            case PsuSelectionMode.Generic:
                return psu.GenericWattage is int watts && watts > 0 && psu.GenericRating is EightyPlusRating rating
                    ? GenericCurveGenerator.GenerateSet(watts, rating)
                    : null;

            case PsuSelectionMode.Manual:
                return psu.ManualCurve is not null && PsuCurve.TryCreate(psu.ManualCurve, out var curve, out _)
                    ? PsuCurveSet.Single(curve!)
                    : null;

            default:
                return null;
        }
    }
}
