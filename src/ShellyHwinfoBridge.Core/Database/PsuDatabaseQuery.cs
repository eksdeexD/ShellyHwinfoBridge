using ShellyHwinfoBridge.Core.Curves;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>
/// Queries for the wizard's cascading filters (Brand → Wattage → 80 Plus rating → models) and name search.
/// A <c>null</c> filter argument means "any".
/// </summary>
public sealed class PsuDatabaseQuery
{
    private readonly IReadOnlyList<PsuEntry> _entries;

    /// <summary>Creates a query over a database.</summary>
    /// <param name="database">The database.</param>
    public PsuDatabaseQuery(PsuDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _entries = database.Psus;
    }

    /// <summary>Returns the distinct brands, sorted alphabetically.</summary>
    public IReadOnlyList<string> GetBrands() =>
        _entries.Select(e => e.Brand).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Returns the distinct wattages for a brand, ascending.</summary>
    /// <param name="brand">Brand filter, or <c>null</c> for any.</param>
    public IReadOnlyList<int> GetWattages(string? brand) =>
        Filter(brand, null, null).Select(e => e.Wattage).Distinct().Order().ToList();

    /// <summary>Returns the distinct 80 Plus ratings for a brand and wattage, from lowest to highest.</summary>
    /// <param name="brand">Brand filter, or <c>null</c> for any.</param>
    /// <param name="wattage">Wattage filter, or <c>null</c> for any.</param>
    public IReadOnlyList<string> GetRatings(string? brand, int? wattage) =>
        Filter(brand, wattage, null)
            .Select(e => e.Rating80Plus)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(RatingRank)
            .ThenBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Returns the matching models, sorted by brand, model and revision.</summary>
    /// <param name="brand">Brand filter, or <c>null</c> for any.</param>
    /// <param name="wattage">Wattage filter, or <c>null</c> for any.</param>
    /// <param name="rating80Plus">Rating filter, or <c>null</c> for any.</param>
    public IReadOnlyList<PsuEntry> GetModels(string? brand, int? wattage, string? rating80Plus) =>
        Sort(Filter(brand, wattage, rating80Plus));

    /// <summary>
    /// Searches by name: every whitespace-separated term must occur (case-insensitive) in the entry's
    /// brand, series, model or revision. An empty query returns everything.
    /// </summary>
    /// <param name="text">The search text.</param>
    public IReadOnlyList<PsuEntry> Search(string? text)
    {
        var terms = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Sort(_entries.Where(e =>
        {
            var haystack = $"{e.Brand} {e.Series} {e.Model} {e.Revision}";
            return terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        }));
    }

    /// <summary>Finds an entry by id (case-insensitive).</summary>
    /// <param name="id">The entry id.</param>
    public PsuEntry? FindById(string id) =>
        _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<PsuEntry> Filter(string? brand, int? wattage, string? rating) =>
        _entries.Where(e =>
            (brand is null || string.Equals(e.Brand, brand, StringComparison.OrdinalIgnoreCase)) &&
            (wattage is null || e.Wattage == wattage) &&
            (rating is null || string.Equals(e.Rating80Plus, rating, StringComparison.OrdinalIgnoreCase)));

    private static List<PsuEntry> Sort(IEnumerable<PsuEntry> entries) =>
        entries
            .OrderBy(e => e.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Revision ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int RatingRank(string rating) =>
        Enum.TryParse<EightyPlusRating>(rating, ignoreCase: true, out var r) ? (int)r : -1;
}
