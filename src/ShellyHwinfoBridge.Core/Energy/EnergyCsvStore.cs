using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ShellyHwinfoBridge.Core.Energy;

/// <summary>Persists hourly energy rows and the counter state.</summary>
public interface IEnergyStore
{
    /// <summary>Appends rows (any order, any months).</summary>
    /// <param name="records">The rows.</param>
    void Append(IEnumerable<EnergyHourRecord> records);

    /// <summary>
    /// Reads the rows whose hour starts in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), sorted by hour,
    /// with rows for the same hour merged.
    /// </summary>
    /// <param name="fromUtc">Inclusive start.</param>
    /// <param name="toUtc">Exclusive end.</param>
    IReadOnlyList<EnergyHourRecord> Read(DateTimeOffset fromUtc, DateTimeOffset toUtc);

    /// <summary>Loads the saved counter state, or <c>null</c> when there is none or it is unreadable.</summary>
    EnergyCounterState? LoadState();

    /// <summary>Saves the counter state atomically.</summary>
    /// <param name="state">The state.</param>
    void SaveState(EnergyCounterState state);
}

/// <summary>
/// Stores rows in one CSV per UTC month (<c>YYYY-MM.csv</c>) and the counter state in <c>state.json</c>,
/// all in one directory (by default <see cref="AppPaths.EnergyDirectory"/>).
/// </summary>
public sealed class EnergyCsvStore : IEnergyStore
{
    /// <summary>The state file name.</summary>
    public const string StateFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly object _gate = new();

    /// <summary>Creates a store.</summary>
    /// <param name="directory">The directory; created on the first write.</param>
    public EnergyCsvStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    /// <summary>Gets the directory.</summary>
    public string Directory { get; }

    /// <summary>Returns the file name for the month containing <paramref name="hourUtc"/>.</summary>
    /// <param name="hourUtc">Any instant.</param>
    public static string FileNameFor(DateTimeOffset hourUtc) =>
        hourUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv";

    /// <inheritdoc />
    public void Append(IEnumerable<EnergyHourRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            foreach (var month in records.GroupBy(r => FileNameFor(r.HourStartUtc)))
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = Path.Combine(Directory, month.Key);
                var builder = new StringBuilder();
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                if (stream.Length == 0)
                {
                    builder.Append(EnergyCsv.Header).Append("\r\n");
                }

                foreach (var record in month.OrderBy(r => r.HourStartUtc))
                {
                    builder.Append(EnergyCsv.FormatRow(record)).Append("\r\n");
                }

                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<EnergyHourRecord> Read(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc)
        {
            return [];
        }

        var rows = new Dictionary<DateTimeOffset, EnergyHourRecord>();
        lock (_gate)
        {
            var from = fromUtc.UtcDateTime;
            var last = toUtc.UtcDateTime.AddTicks(-1);
            for (var month = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                 month <= last;
                 month = month.AddMonths(1))
            {
                var path = Path.Combine(Directory, FileNameFor(new DateTimeOffset(month, TimeSpan.Zero)));
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var line in ReadLines(path))
                {
                    if (!EnergyCsv.TryParseRow(line, out var record) || record.HourStartUtc < fromUtc || record.HourStartUtc >= toUtc)
                    {
                        continue;
                    }

                    rows[record.HourStartUtc] = rows.TryGetValue(record.HourStartUtc, out var existing) ? existing.Merge(record) : record;
                }
            }
        }

        return rows.Values.OrderBy(r => r.HourStartUtc).ToList();
    }

    /// <inheritdoc />
    public EnergyCounterState? LoadState()
    {
        var path = Path.Combine(Directory, StateFileName);
        try
        {
            lock (_gate)
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var state = JsonSerializer.Deserialize<EnergyCounterState>(File.ReadAllText(path), JsonOptions);
                return state is { DeviceKey: not null } ? state : null;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void SaveState(EnergyCounterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, StateFileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
    }

    private static List<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
