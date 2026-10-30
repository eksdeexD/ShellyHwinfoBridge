using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Tests.TestSupport;
using static ShellyHwinfoBridge.Core.Tests.Energy.EnergyTestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Energy;

public sealed class EnergyLoggerTests : IDisposable
{
    private const string Base = HwinfoSensorNames.BaseKeyPath;
    private const string Plug = "10.0.0.2|shellyplugmg3-aa";

    private readonly string _dir = NewTempDirectory();
    private readonly InMemoryRegistryBackend _backend = new();
    private readonly FakeTimeProvider _time = new(Utc(2026, 10, 30, 18, 0, 10));
    private readonly EnergyCsvStore _store;
    private readonly HwinfoSensorRegistry _registry;

    public EnergyLoggerTests()
    {
        _store = new EnergyCsvStore(_dir);
        _registry = new HwinfoSensorRegistry(_backend);
        _registry.EnsureKeys();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string? Value(string subkey) => _backend.GetString(Base + "\\" + subkey, "Value");

    private EnergyLogger CreateLogger() => new(_store, _registry, _time, CentralEurope) { DeviceKey = Plug };

    private static PowerReadingEventArgs Reading(double acW, double dcW, double? counterWh) =>
        new(new ShellyReading(acW, 230, acW / 230, counterWh), new PowerReading(acW, acW, dcW, acW - dcW, InputVoltage.V230));

    private void Poll(EnergyLogger logger, double acW, double dcW, double? counterWh)
    {
        logger.OnReading(Reading(acW, dcW, counterWh));
        _time.Advance(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WritesTodayAndMonthToHwinfoOnEveryReading()
    {
        using var _ = new CultureScope("da-DK");
        _store.Append([new EnergyHourRecord(Utc(2026, 10, 30, 10), 2400, 2200, 3600, 1, 1, 1), new EnergyHourRecord(Utc(2026, 10, 5, 10), 46000, 0, 0, null, null, null)]);
        var logger = CreateLogger();
        var updates = 0;
        logger.Updated += (_, _) => updates++;

        Poll(logger, 300, 280, 1000);
        Assert.Equal("2.40", Value("Other1"));
        Assert.Equal("48.40", Value("Other2"));

        Poll(logger, 300, 280, 1010);
        Poll(logger, 300, 280, 1020);
        Assert.Equal("2.42", Value("Other1"));
        Assert.Equal("48.42", Value("Other2"));
        Assert.Equal(3, updates);
        Assert.Equal(Round(new EnergyTotals(2.42, 48.42, 2.2 + (2 * 280 / 3600.0 / 1000), 2.2 + (2 * 280 / 3600.0 / 1000))), Round(logger.GetTotals()));
    }

    [Fact]
    public void OfflineKeepsTheEnergyValues()
    {
        var logger = CreateLogger();
        Poll(logger, 300, 280, 1000);
        Poll(logger, 300, 280, 1200);

        _registry.SetOffline();

        Assert.Equal("0.20", Value("Other1"));
        Assert.Equal("0.20", Value("Other2"));
        Assert.Equal("0.0", Value("Power0"));
    }

    [Fact]
    public void CompletedHourIsAppendedAndStateSaved()
    {
        _time.SetUtcNow(Utc(2026, 10, 30, 18, 59, 58));
        var logger = CreateLogger();
        Poll(logger, 3600, 3600, 1000);
        Poll(logger, 3600, 3600, 1001);
        Assert.False(File.Exists(Path.Combine(_dir, "2026-10.csv")));

        Poll(logger, 3600, 3600, 1002);

        var row = Assert.Single(_store.Read(Utc(2026, 10, 1), Utc(2026, 11, 1)));
        Assert.Equal(Utc(2026, 10, 30, 18), row.HourStartUtc);
        Assert.Equal(2, row.AcWh, 9);
        Assert.Equal(2, row.DcEstWh, 9);
        Assert.Equal(2, row.Samples);
        Assert.Equal(new EnergyCounterState(Plug, 1002, Utc(2026, 10, 30, 19, 0, 0)), _store.LoadState());
    }

    [Fact]
    public void StateIsSavedAtMostEveryMinute()
    {
        var logger = CreateLogger();
        Poll(logger, 100, 90, 1000);
        Assert.Equal(1000, _store.LoadState()!.LastCounterWh);

        Poll(logger, 100, 90, 1001);
        Assert.Equal(1000, _store.LoadState()!.LastCounterWh);

        _time.Advance(TimeSpan.FromSeconds(60));
        Poll(logger, 100, 90, 1003);
        Assert.Equal(1003, _store.LoadState()!.LastCounterWh);
    }

    [Fact]
    public void FlushWritesThePartialHourAndRestartBackfillsTheGap()
    {
        var logger = CreateLogger();
        Poll(logger, 100, 90, 1000);
        Poll(logger, 100, 90, 1000.1);
        logger.Flush();

        var partial = Assert.Single(_store.Read(Utc(2026, 10, 30), Utc(2026, 10, 31)));
        Assert.Equal(2, partial.Samples);
        Assert.Equal(0.1, partial.AcWh, 9);

        // Restart two hours later; the plug counted 200 Wh meanwhile.
        _time.SetUtcNow(Utc(2026, 10, 30, 20, 0, 11));
        var restarted = CreateLogger();
        restarted.OnReading(Reading(100, 90, 1200.1));

        var rows = _store.Read(Utc(2026, 10, 30), Utc(2026, 10, 31));
        Assert.Equal([Utc(2026, 10, 30, 18), Utc(2026, 10, 30, 19)], rows.Select(r => r.HourStartUtc));
        Assert.Equal(2, rows[0].Samples);
        Assert.Equal(0.1 + (200.0 * 3589 / 7200), rows[0].AcWh, 2);
        Assert.Equal(100, rows[1].AcWh, 2);
        Assert.Equal(0.2001, restarted.GetTotals().TodayAcKwh, 4);
    }

    [Fact]
    public void ChangedDeviceKeyDoesNotBackfill()
    {
        var logger = CreateLogger();
        Poll(logger, 100, 90, 1000);
        logger.Flush();

        _time.Advance(TimeSpan.FromHours(3));
        var other = new EnergyLogger(_store, _registry, _time, CentralEurope) { DeviceKey = "10.0.0.9|shellyplugmg3-bb" };
        other.OnReading(Reading(100, 90, 50_000));

        Assert.Equal(0, other.GetTotals().TodayAcKwh, 9);
    }

    [Fact]
    public void DisabledLoggerIgnoresReadings()
    {
        var logger = CreateLogger();
        logger.Enabled = false;

        Poll(logger, 100, 90, 1000);
        Poll(logger, 100, 90, 1100);

        Assert.Equal("0.00", Value("Other1"));
        Assert.Null(_store.LoadState());

        // Re-enabling starts from a new baseline: the time logging was off is not counted.
        logger.Enabled = true;
        Poll(logger, 100, 90, 1200);
        Poll(logger, 100, 90, 1201);
        Assert.Equal(0.001, logger.GetTotals().TodayAcKwh, 9);
    }

    [Fact]
    public void BucketsIncludeTheHourInProgress()
    {
        _store.Append([new EnergyHourRecord(Utc(2026, 10, 30, 17), 340, 310, 3600, 1, 1, 1)]);
        var logger = CreateLogger();
        Poll(logger, 100, 90, 1000);
        Poll(logger, 100, 90, 1050);

        var hours = logger.GetBuckets(EnergyPeriod.Hours);
        Assert.Equal(48, hours.Count);
        Assert.Equal(0.05, hours[^1].AcKwh, 9);
        Assert.Equal(0.34, hours[^2].AcKwh, 9);

        var days = logger.GetBuckets(EnergyPeriod.Days);
        Assert.Equal(0.39, days[^1].AcKwh, 9);
        Assert.Equal(24, logger.GetBuckets(EnergyPeriod.Months).Count);
    }

    [Fact]
    public void DeviceKeyCombinesHostAndId()
    {
        Assert.Equal("10.0.0.2|shellyplugmg3-aa", EnergyLogger.BuildDeviceKey(" 10.0.0.2 ", "ShellyPlugMG3-AA"));
        Assert.Equal("plug.local|", EnergyLogger.BuildDeviceKey("Plug.local", null));
    }

    private static EnergyTotals Round(EnergyTotals t) =>
        new(Math.Round(t.TodayAcKwh, 9), Math.Round(t.MonthAcKwh, 9), Math.Round(t.TodayDcEstKwh, 9), Math.Round(t.MonthDcEstKwh, 9));
}
