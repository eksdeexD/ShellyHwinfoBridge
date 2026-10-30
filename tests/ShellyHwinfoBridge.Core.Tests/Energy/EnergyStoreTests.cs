using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Tests.TestSupport;
using static ShellyHwinfoBridge.Core.Tests.Energy.EnergyTestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Energy;

public sealed class EnergyStoreTests : IDisposable
{
    private readonly string _dir = NewTempDirectory();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void CsvRowRoundTripsUnderDanishCulture()
    {
        using var _ = new CultureScope("da-DK");
        var record = new EnergyHourRecord(Utc(2026, 10, 30, 18), 312.4567, 294.1, 3600, 150.25, 410.5, 312.4);

        var line = EnergyCsv.FormatRow(record);

        Assert.Equal("2026-10-30T18:00:00Z,312.457,294.100,3600,150.250,410.500,312.400", line);
        Assert.True(EnergyCsv.TryParseRow(line, out var parsed));
        Assert.Equal(record with { AcWh = 312.457 }, parsed);
    }

    [Fact]
    public void CsvRowWithoutSamplesHasEmptyColumns()
    {
        var record = new EnergyHourRecord(Utc(2026, 1, 2, 3), 12, 0, 0, null, null, null);
        var line = EnergyCsv.FormatRow(record);

        Assert.Equal("2026-01-02T03:00:00Z,12.000,0.000,0,,,", line);
        Assert.True(EnergyCsv.TryParseRow(line, out var parsed));
        Assert.Equal(record, parsed);
    }

    [Theory]
    [InlineData(EnergyCsv.Header)]
    [InlineData("")]
    [InlineData("2026-01-02T03:00:00Z,12.000,0.000,0,,")]
    [InlineData("2026-13-02T03:00:00Z,12.000,0.000,0,,,")]
    [InlineData("2026-01-02T03:00:00Z,abc,0.000,0,,,")]
    [InlineData("2026-01-02T03:00:00Z,1,0.000,-1,,,")]
    [InlineData("2026-01-02T03:00:00Z,1,0.000,1,x,,")]
    [InlineData("2026-01-02T03:00:00Z,,0.000,1,,,")]
    public void BadRowsAreSkipped(string line)
    {
        Assert.False(EnergyCsv.TryParseRow(line, out _));
    }

    [Fact]
    public void AppendWritesOneFilePerUtcMonthWithHeader()
    {
        var store = new EnergyCsvStore(_dir);
        store.Append([
            new EnergyHourRecord(Utc(2026, 10, 31, 23), 100, 90, 3600, 90, 110, 100),
            new EnergyHourRecord(Utc(2026, 11, 1, 0), 50, 45, 3600, 40, 60, 50),
        ]);
        store.Append([new EnergyHourRecord(Utc(2026, 11, 1, 1), 60, 55, 3600, 50, 70, 60)]);

        var october = File.ReadAllLines(Path.Combine(_dir, "2026-10.csv"));
        var november = File.ReadAllLines(Path.Combine(_dir, "2026-11.csv"));
        Assert.Equal([EnergyCsv.Header, "2026-10-31T23:00:00Z,100.000,90.000,3600,90.000,110.000,100.000"], october);
        Assert.Equal(3, november.Length);
        Assert.Equal(EnergyCsv.Header, november[0]);

        var all = store.Read(Utc(2026, 10, 1), Utc(2026, 12, 1));
        Assert.Equal([Utc(2026, 10, 31, 23), Utc(2026, 11, 1, 0), Utc(2026, 11, 1, 1)], all.Select(r => r.HourStartUtc));
        Assert.Single(store.Read(Utc(2026, 11, 1, 1), Utc(2026, 11, 1, 2)));
        Assert.Empty(store.Read(Utc(2026, 11, 1, 2), Utc(2026, 11, 1, 2)));
    }

    [Fact]
    public void ReadMergesRowsForTheSameHour()
    {
        var store = new EnergyCsvStore(_dir);
        var hour = Utc(2026, 10, 30, 18);
        store.Append([new EnergyHourRecord(hour, 30, 28, 100, 100, 200, 150)]);
        store.Append([new EnergyHourRecord(hour, 70, 60, 300, 80, 180, 110)]);

        var merged = Assert.Single(store.Read(hour, hour.AddHours(1)));

        Assert.Equal(100, merged.AcWh, 9);
        Assert.Equal(88, merged.DcEstWh, 9);
        Assert.Equal(400, merged.Samples);
        Assert.Equal(80, merged.AcWMin);
        Assert.Equal(200, merged.AcWMax);
        Assert.Equal(120, merged.AcWAvg!.Value, 9);
    }

    [Fact]
    public void MergeWithBackfilledRowKeepsSampleStatistics()
    {
        var hour = Utc(2026, 10, 30, 18);
        var live = new EnergyHourRecord(hour, 30, 28, 100, 100, 200, 150);
        var backfill = new EnergyHourRecord(hour, 70, 0, 0, null, null, null);

        Assert.Equal(new EnergyHourRecord(hour, 100, 28, 100, 100, 200, 150), backfill.Merge(live));
    }

    [Fact]
    public void ReadSkipsCorruptLines()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(Path.Combine(_dir, "2026-10.csv"), [EnergyCsv.Header, "garbage", "2026-10-30T18:00:00Z,1.000,1.000,1,1,1,1", "2026-10-30T19:00"]);

        Assert.Single(new EnergyCsvStore(_dir).Read(Utc(2026, 10, 1), Utc(2026, 11, 1)));
    }

    [Fact]
    public void StateRoundTripsAndMissingOrCorruptLoadsNull()
    {
        var store = new EnergyCsvStore(_dir);
        Assert.Null(store.LoadState());

        var state = new EnergyCounterState("10.0.0.2|shellyplugmg3-aa", 12345.678, Utc(2026, 10, 30, 18, 15, 3));
        store.SaveState(state);
        Assert.Equal(state, store.LoadState());
        Assert.False(File.Exists(Path.Combine(_dir, EnergyCsvStore.StateFileName + ".tmp")));

        File.WriteAllText(Path.Combine(_dir, EnergyCsvStore.StateFileName), "{ nope");
        Assert.Null(store.LoadState());
    }
}
