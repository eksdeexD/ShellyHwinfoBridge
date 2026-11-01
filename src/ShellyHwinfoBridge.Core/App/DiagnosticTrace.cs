using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.App;

/// <summary>What one primary-device poll contributes to the <see cref="DiagnosticTrace"/>.</summary>
/// <param name="Reading">The primary device's reading (AC, DC, loss, voltage, current, request duration).</param>
/// <param name="Components">The component readings used for Other Components this tick.</param>
/// <param name="Tick">The calculator's details of this tick (<see cref="OtherComponentsCalculator.LastTick"/>).</param>
/// <param name="PublishedOther">The Other Components value written to HWiNFO this tick.</param>
public sealed record DiagnosticTraceRow(PowerReadingEventArgs Reading, ComponentReadings Components, OtherComponentsTick Tick, double PublishedOther);

/// <summary>
/// A manual diagnostic trace of the Other Components calculation: while running, one CSV row per primary-device poll
/// is appended to <c>trace-YYYYMMDD-HHmmss.csv</c> in <see cref="Directory"/> (invariant culture, ISO-8601 local
/// timestamps with milliseconds). Every row is flushed at once, so a crash loses nothing.
/// </summary>
/// <remarks>
/// <para>The sensor columns are fixed when the trace starts (one per selected sensor, in selection order); a later
/// change of the selection fills the columns by position.</para>
/// <para>Never throws into the caller of <see cref="Append"/>: a write error is logged once at Warning and stops the
/// trace. Starting while running is a no-op. Thread-safe; <see cref="StateChanged"/> is raised outside the lock.</para>
/// </remarks>
public sealed class DiagnosticTrace : IDisposable
{
    /// <summary>The fixed columns before the sensor columns.</summary>
    public static readonly IReadOnlyList<string> LeadingColumns =
        ["timestamp", "ac_w", "dc_w", "psu_loss_w", "efficiency", "component_source", "hwinfo_poll_time", "hwinfo_age_s"];

    /// <summary>The fixed columns after the sensor columns.</summary>
    public static readonly IReadOnlyList<string> TrailingColumns =
        ["component_sum_w", "other_raw_w", "other_published_w", "accepted", "shelly_voltage", "shelly_current", "poll_duration_ms"];

    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _file;
    private int _sensorColumns;
    private int _rows;

    /// <summary>Creates an idle trace.</summary>
    /// <param name="directory">The folder for the trace files (created on start).</param>
    /// <param name="timeProvider">The clock (row timestamps and the file name, in its local time zone); defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    public DiagnosticTrace(string directory, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Raised after the trace started or stopped (also when a write error stopped it); on the caller's thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Gets the folder of the trace files.</summary>
    public string Directory { get; }

    /// <summary>Gets whether the trace is recording.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _writer is not null;
            }
        }
    }

    /// <summary>Gets the full path of the file being recorded, or <c>null</c> while idle.</summary>
    public string? CurrentFile
    {
        get
        {
            lock (_gate)
            {
                return _writer is null ? null : _file;
            }
        }
    }

    /// <summary>Gets the number of rows written to the current (or the last) file.</summary>
    public int RowCount
    {
        get
        {
            lock (_gate)
            {
                return _rows;
            }
        }
    }

    /// <summary>Returns the trace file name for a start time: <c>trace-YYYYMMDD-HHmmss.csv</c>.</summary>
    /// <param name="localTime">The start time (local).</param>
    public static string FileNameFor(DateTimeOffset localTime) =>
        "trace-" + localTime.ToString("yyyyMMdd-HHmmss", Inv) + ".csv";

    /// <summary>
    /// Returns the sensor column names: the sensor name with every character other than letters, digits, space and
    /// <c>-_.()[]/+</c> replaced by <c>_</c>; a duplicate gets its device in brackets, then a number.
    /// </summary>
    /// <param name="sensors">The selected sensors.</param>
    public static IReadOnlyList<string> SensorColumns(IReadOnlyList<SensorSelection> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        var used = new HashSet<string>(LeadingColumns.Concat(TrailingColumns), StringComparer.OrdinalIgnoreCase);
        var columns = new List<string>(sensors.Count);
        foreach (var sensor in sensors)
        {
            var name = Sanitize(sensor.Name);
            if (used.Contains(name) && !string.IsNullOrWhiteSpace(sensor.Device))
            {
                name = Sanitize(sensor.Name + " [" + sensor.Device + "]");
            }

            var unique = name;
            for (var n = 2; used.Contains(unique); n++)
            {
                unique = name + " " + n.ToString(Inv);
            }

            used.Add(unique);
            columns.Add(unique);
        }

        return columns;
    }

    /// <summary>Returns the CSV header line for the selected sensors.</summary>
    /// <param name="sensors">The selected sensors.</param>
    public static string Header(IReadOnlyList<SensorSelection> sensors) =>
        string.Join(",", LeadingColumns.Concat(SensorColumns(sensors)).Concat(TrailingColumns));

    /// <summary>Formats one row (no line break).</summary>
    /// <param name="timestamp">The row time (local).</param>
    /// <param name="row">The poll's data.</param>
    /// <param name="sensorColumns">The number of sensor columns of the header.</param>
    public static string FormatRow(DateTimeOffset timestamp, DiagnosticTraceRow row, int sensorColumns)
    {
        ArgumentNullException.ThrowIfNull(row);
        var power = row.Reading.Power;
        var raw = row.Reading.Raw;
        var components = row.Components;
        var poll = components.HwinfoPollTime is { } p ? p.ToOffset(timestamp.Offset) : (DateTimeOffset?)null;
        var cells = new List<string>
        {
            timestamp.ToString(TimeFormat, Inv),
            Watts(power.AcWatts),
            Watts(power.DcWatts),
            Watts(power.LossWatts),
            power.AcWatts > 0 && double.IsFinite(power.DcWatts) ? (power.DcWatts / power.AcWatts).ToString("0.0000", Inv) : string.Empty,
            components.Source.ToString(),
            poll?.ToString(TimeFormat, Inv) ?? string.Empty,
            poll is { } at ? (timestamp - at).TotalSeconds.ToString("0.000", Inv) : string.Empty,
        };
        for (var i = 0; i < sensorColumns; i++)
        {
            cells.Add(i < components.Values.Count ? Watts(components.Values[i]) : string.Empty);
        }

        cells.Add(Watts(row.Tick.ComponentSum));
        cells.Add(Watts(row.Tick.Raw));
        cells.Add(Watts(row.PublishedOther));
        cells.Add(row.Tick.Outcome switch
        {
            OtherTickOutcome.Accepted => "true",
            OtherTickOutcome.Discarded => "false",
            _ => "nosource",
        });
        cells.Add(raw.Volts is double v && double.IsFinite(v) ? v.ToString("0.0", Inv) : string.Empty);
        cells.Add(raw.Amps is double a && double.IsFinite(a) ? a.ToString("0.000", Inv) : string.Empty);
        cells.Add(row.Reading.RequestDuration is { } d ? d.TotalMilliseconds.ToString("0.0", Inv) : string.Empty);
        return string.Join(",", cells);
    }

    /// <summary>Starts recording to a new file with the header for <paramref name="sensors"/>; a no-op while running.</summary>
    /// <param name="sensors">The selected component sensors (one column each).</param>
    /// <returns>Whether the trace is running afterwards (<c>false</c> when the file could not be created; logged).</returns>
    public bool Start(IReadOnlyList<SensorSelection> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        string path;
        lock (_gate)
        {
            if (_writer is not null)
            {
                return true;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var name = FileNameFor(_time.GetLocalNow());
                path = Path.Combine(Directory, name);
                FileStream? stream = null;
                for (var n = 2; stream is null; n++)
                {
                    try
                    {
                        stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    }
                    catch (IOException) when (File.Exists(path) && n < 100)
                    {
                        path = Path.Combine(Directory, Path.GetFileNameWithoutExtension(name) + "-" + n.ToString(Inv) + ".csv");
                    }
                }

                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                writer.WriteLine(Header(sensors));
                _writer = writer;
                _file = path;
                _sensorColumns = sensors.Count;
                _rows = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
            {
                _logger.LogWarning(ex, "Starting the diagnostic trace in {Directory} failed.", Directory);
                CloseLocked();
                return false;
            }
        }

        _logger.LogInformation("Diagnostic trace started: {Path}", path);
        OnStateChanged();
        return true;
    }

    /// <summary>Stops recording and closes the file; a no-op while idle.</summary>
    public void Stop()
    {
        string? path;
        int rows;
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            path = _file;
            rows = _rows;
            CloseLocked();
        }

        _logger.LogInformation("Diagnostic trace stopped: {Path}, {Rows} rows", path, rows);
        OnStateChanged();
    }

    /// <summary>Appends one row while running (ignored while idle). Never throws: a failure stops the trace.</summary>
    /// <param name="row">The poll's data.</param>
    public void Append(DiagnosticTraceRow row)
    {
        string? failedPath = null;
        lock (_gate)
        {
            if (_writer is null || row is null)
            {
                return;
            }

            try
            {
                _writer.WriteLine(FormatRow(_time.GetLocalNow(), row, _sensorColumns));
                _rows++;
                return;
            }
            catch (Exception ex)
            {
                failedPath = _file;
                _logger.LogWarning(ex, "Writing the diagnostic trace {Path} failed; the trace is stopped.", failedPath);
                CloseLocked();
            }
        }

        OnStateChanged();
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private static string Watts(double? value) =>
        value is double v && double.IsFinite(v) ? v.ToString("0.00", Inv) : string.Empty;

    private static string Sanitize(string? name)
    {
        var builder = new StringBuilder();
        foreach (var ch in (name ?? string.Empty).Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) || " -_.()[]/+".Contains(ch, StringComparison.Ordinal) ? ch : '_');
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "sensor" : result;
    }

    private void CloseLocked()
    {
        var writer = _writer;
        _writer = null;
        try
        {
            writer?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Closing the diagnostic trace failed.");
        }
    }

    private void OnStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A diagnostic trace subscriber failed.");
        }
    }
}
