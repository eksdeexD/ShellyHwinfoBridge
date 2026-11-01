using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Tests.App;

public sealed class DiagnosticTraceTests : IDisposable
{
    private const string ExpectedHeader =
        "timestamp,ac_w,dc_w,psu_loss_w,efficiency,component_source,hwinfo_poll_time,hwinfo_age_s," +
        "CPU Package Power,GPU Power,DIMM1 Power," +
        "component_sum_w,other_raw_w,other_published_w,accepted,shelly_voltage,shelly_current,poll_duration_ms";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shb-trace-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 13, 30, 5, 123, TimeSpan.Zero));

    public DiagnosticTraceTests() =>
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static IReadOnlyList<SensorSelection> Sensors() =>
    [
        new SensorSelection { Name = "CPU Package Power", Category = SensorCategory.Cpu },
        new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu },
        new SensorSelection { Name = "DIMM1 Power", Category = SensorCategory.Ram },
    ];

    private static PowerReadingEventArgs Reading(TimeSpan? duration = null) => new(
        new ShellyReading(312.4, 226.9, 1.38),
        new PowerReading(312.4, 312.4, 294.8, 17.6, InputVoltage.V230),
        AppSettings.PrimaryDeviceId,
        duration);

    private DiagnosticTrace Create() => new(_dir, _time);

    private static string[] ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void WritesTheHeaderAndOneFormattedRowPerPoll()
    {
        using var trace = Create();
        var calculator = new OtherComponentsCalculator();
        var components = new ComponentReadings(OtherSource.SharedMemory, [100, 150, 5], true, [])
        {
            HwinfoPollTime = new DateTimeOffset(2026, 11, 1, 13, 30, 4, TimeSpan.Zero),
        };
        var published = calculator.Update(294.8, components);

        Assert.True(trace.Start(Sensors()));
        trace.Append(new DiagnosticTraceRow(Reading(TimeSpan.FromMilliseconds(42.5)), components, calculator.LastTick, published));
        var path = trace.CurrentFile!;
        trace.Stop();

        Assert.Equal(Path.Combine(_dir, "trace-20261101-143005.csv"), path);
        var lines = ReadLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal(ExpectedHeader, lines[0]);
        Assert.Equal(
            "2026-11-01T14:30:05.123+01:00,312.40,294.80,17.60,0.9437,SharedMemory,2026-11-01T14:30:04.000+01:00,1.123," +
            "100.00,150.00,5.00,255.00,39.80,39.80,true,226.9,1.380,42.5",
            lines[1]);
        Assert.Equal(1, trace.RowCount);
    }

    [Fact]
    public void MissingComponentLeavesItsCellsEmptyAndIsNotAccepted()
    {
        using var trace = Create();
        var calculator = new OtherComponentsCalculator();
        var good = new ComponentReadings(OtherSource.Gadget, [100, 150, 5], true, []);
        calculator.Update(294.8, good);
        var missing = new ComponentReadings(OtherSource.Gadget, [100, null, 5], true, []);
        var published = calculator.Update(294.8, missing);

        trace.Start(Sensors());
        trace.Append(new DiagnosticTraceRow(Reading(), missing, calculator.LastTick, published));
        var path = trace.CurrentFile!;
        trace.Stop();

        var cells = ReadLines(path)[1].Split(',');
        Assert.Equal("Gadget", cells[5]);
        Assert.Equal(string.Empty, cells[6]);
        Assert.Equal(string.Empty, cells[7]);
        Assert.Equal(["100.00", string.Empty, "5.00"], cells[8..11]);
        Assert.Equal(string.Empty, cells[11]);
        Assert.Equal(string.Empty, cells[12]);
        Assert.Equal("39.80", cells[13]);
        Assert.Equal("false", cells[14]);
        Assert.Equal(string.Empty, cells[17]);
        Assert.Equal(18, cells.Length);
    }

    [Fact]
    public void NoSourceWritesNosourceAndEmptyCells()
    {
        using var trace = Create();
        var calculator = new OtherComponentsCalculator();
        var none = new ComponentReadings(OtherSource.None, [], true, ["CPU Package Power"]);
        var published = calculator.Update(294.8, none);

        trace.Start(Sensors());
        trace.Append(new DiagnosticTraceRow(Reading(), none, calculator.LastTick, published));
        var path = trace.CurrentFile!;
        trace.Stop();

        var cells = ReadLines(path)[1].Split(',');
        Assert.Equal(18, cells.Length);
        Assert.Equal("None", cells[5]);
        Assert.All(cells[6..13], c => Assert.Equal(string.Empty, c));
        Assert.Equal("0.00", cells[13]);
        Assert.Equal("nosource", cells[14]);
    }

    [Fact]
    public void StartAndStopAreIdempotent()
    {
        using var trace = Create();
        var changes = 0;
        trace.StateChanged += (_, _) => changes++;

        Assert.False(trace.IsRunning);
        Assert.Null(trace.CurrentFile);
        trace.Stop();
        trace.Append(new DiagnosticTraceRow(Reading(), ComponentReadings.NotRunning, OtherComponentsTick.NoSource, 0));
        Assert.False(Directory.Exists(_dir));

        Assert.True(trace.Start(Sensors()));
        var first = trace.CurrentFile;
        Assert.True(trace.Start(Sensors()));
        Assert.Equal(first, trace.CurrentFile);
        Assert.True(trace.IsRunning);
        trace.Stop();
        trace.Stop();
        Assert.False(trace.IsRunning);
        Assert.Null(trace.CurrentFile);
        Assert.Equal(2, changes);
        Assert.Single(ReadLines(first!));

        // A new start in the same second gets its own file.
        Assert.True(trace.Start(Sensors()));
        Assert.Equal(Path.Combine(_dir, "trace-20261101-143005-2.csv"), trace.CurrentFile);
        trace.Stop();
        Assert.Equal(4, changes);
    }

    [Fact]
    public void FailedStartReturnsFalseAndStaysIdle()
    {
        File.WriteAllText(_dir, "a file where the folder should be");
        try
        {
            using var trace = Create();
            Assert.False(trace.Start(Sensors()));
            Assert.False(trace.IsRunning);
        }
        finally
        {
            File.Delete(_dir);
        }
    }

    [Fact]
    public void SensorColumnsAreSanitizedAndUnique()
    {
        var columns = DiagnosticTrace.SensorColumns(
        [
            new SensorSelection { Name = "Total Power", Device = "DIMM #1", Category = SensorCategory.Ram },
            new SensorSelection { Name = "Total Power", Device = "DIMM #2", Category = SensorCategory.Ram },
            new SensorSelection { Name = "GPU \"Power\", board", Category = SensorCategory.Gpu },
            new SensorSelection { Name = "GPU \"Power\", board", Category = SensorCategory.Gpu },
            new SensorSelection { Name = "timestamp", Category = SensorCategory.OtherMeasured },
        ]);

        Assert.Equal(["Total Power", "Total Power [DIMM _2]", "GPU _Power__ board", "GPU _Power__ board 2", "timestamp 2"], columns);
    }

    [Fact]
    public void CalculatorExposesTheUnclampedResultWithoutChangingThePublishedValue()
    {
        var calculator = new OtherComponentsCalculator();
        Assert.Equal(OtherTickOutcome.NoSource, calculator.LastTick.Outcome);

        Assert.Equal(40, calculator.Update(300, new ComponentReadings(OtherSource.SharedMemory, [100, 160], true, [])));
        Assert.Equal(new OtherComponentsTick(OtherTickOutcome.Accepted, 260, 40), calculator.LastTick);

        // A spike: the raw result is negative and discarded; the published value is held.
        Assert.Equal(40, calculator.Update(300, new ComponentReadings(OtherSource.SharedMemory, [100, 250], true, [])));
        Assert.Equal(new OtherComponentsTick(OtherTickOutcome.Discarded, 350, -50), calculator.LastTick);

        // A missing component: no sum, no raw result.
        Assert.Equal(40, calculator.Update(300, new ComponentReadings(OtherSource.SharedMemory, [100, null], true, [])));
        Assert.Equal(new OtherComponentsTick(OtherTickOutcome.Discarded, null, null), calculator.LastTick);

        // No source resets.
        Assert.Equal(0, calculator.Update(300, ComponentReadings.NotRunning));
        Assert.Equal(OtherComponentsTick.NoSource, calculator.LastTick);
    }
}
