using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Tests.TestSupport;
using static ShellyHwinfoBridge.Core.Tests.Energy.EnergyTestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Energy;

public sealed class EnergyLoggerTests : IDisposable
{
    private const string Base = SensorNaming.BaseKeyPath;
    private const string MonitorId = "11111111-1111-1111-1111-111111111111";
    private const string LampId = "22222222-2222-2222-2222-222222222222";
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
        Assert.Equal(24, hours.Count);
        Assert.Equal(0.05, hours[^1].AcKwh, 9);
        Assert.Equal(0.34, hours[^2].AcKwh, 9);

        var days = logger.GetBuckets(EnergyPeriod.Days);
        Assert.Equal(0.39, days[^1].AcKwh, 9);
        Assert.Equal(12, logger.GetBuckets(EnergyPeriod.Months).Count);
    }

    [Fact]
    public void DeviceKeyCombinesHostAndId()
    {
        Assert.Equal("10.0.0.2|shellyplugmg3-aa", EnergyLogger.BuildDeviceKey(" 10.0.0.2 ", "ShellyPlugMG3-AA"));
        Assert.Equal("plug.local|", EnergyLogger.BuildDeviceKey("Plug.local", null));
    }

    private static PowerReadingEventArgs DeviceReading(string id, double acW, double? counterWh) =>
        new(new ShellyReading(acW, 230, acW / 230, counterWh), PollingService.AcOnly(acW, 230), id);

    private EnergyLogger CreateMultiLogger(bool monitorIncluded = true)
    {
        var logger = CreateLogger();
        logger.SetDevices(
        [
            new EnergyDevice(AppSettings.PrimaryDeviceId, "PC", Plug, true),
            new EnergyDevice(MonitorId, "Monitor", "10.0.0.3|", monitorIncluded),
            new EnergyDevice(LampId, "Lamp", "10.0.0.4|", false),
        ]);
        return logger;
    }

    private void PollAll(EnergyLogger logger, double pcCounter, double monitorCounter, double lampCounter)
    {
        logger.OnReading(Reading(300, 280, pcCounter));
        logger.OnReading(DeviceReading(MonitorId, 30, monitorCounter));
        logger.OnReading(DeviceReading(LampId, 10, lampCounter));
        _time.Advance(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AdditionalDevicesLogToTheirOwnFolderWithTheSameColumnsAndNoDc()
    {
        _time.SetUtcNow(Utc(2026, 10, 30, 18, 59, 58));
        var logger = CreateMultiLogger();
        PollAll(logger, 1000, 500, 100);
        PollAll(logger, 1001, 500.5, 100.1);
        PollAll(logger, 1002, 501, 100.2);

        var pcFile = Path.Combine(_dir, "2026-10.csv");
        var monitorFile = Path.Combine(_dir, MonitorId, "2026-10.csv");
        var lampFile = Path.Combine(_dir, LampId, "2026-10.csv");
        Assert.True(File.Exists(pcFile));
        Assert.True(File.Exists(monitorFile));
        Assert.True(File.Exists(lampFile), "Excluded devices are still logged.");
        Assert.Equal(File.ReadAllLines(pcFile)[0], File.ReadAllLines(monitorFile)[0]);

        var monitorRow = Assert.Single(new EnergyCsvStore(Path.Combine(_dir, MonitorId)).Read(Utc(2026, 10, 1), Utc(2026, 11, 1)));
        Assert.Equal(1, monitorRow.AcWh, 9);
        Assert.Equal(0, monitorRow.DcEstWh);
        Assert.True(File.Exists(Path.Combine(_dir, MonitorId, EnergyCsvStore.StateFileName)));
    }

    [Fact]
    public void TotalsAndHwinfoSumThePcAndIncludedDevicesOnly()
    {
        using var _ = new CultureScope("da-DK");
        var logger = CreateMultiLogger();
        PollAll(logger, 1000, 500, 100);
        PollAll(logger, 1100, 550, 200);

        // PC 100 Wh + monitor 50 Wh; the lamp's 100 Wh is excluded.
        Assert.Equal(0.15, logger.GetTotals().TodayAcKwh, 9);
        Assert.Equal("0.15", Value("Other1"));
        Assert.Equal(0.1, logger.GetDeviceTotals(LampId).TodayAcKwh, 9);
        Assert.Equal(0.05, logger.GetDeviceTotals(MonitorId).TodayAcKwh, 9);
    }

    [Fact]
    public void ExcludingADeviceRemovesItsWholeHistoryFromTotalsAndIncludingBringsItBack()
    {
        new EnergyCsvStore(Path.Combine(_dir, MonitorId)).Append([new EnergyHourRecord(Utc(2026, 10, 30, 10), 400, 0, 3600, 1, 1, 1)]);
        _store.Append([new EnergyHourRecord(Utc(2026, 10, 30, 10), 1000, 900, 3600, 1, 1, 1)]);
        var logger = CreateMultiLogger(monitorIncluded: false);
        Assert.Equal(1.0, logger.GetTotals().TodayAcKwh, 9);
        Assert.Single(logger.GetChartData(EnergyPeriod.Days).Devices);

        logger.SetDevices(
        [
            new EnergyDevice(AppSettings.PrimaryDeviceId, "PC", Plug, true),
            new EnergyDevice(MonitorId, "Monitor", "10.0.0.3|", true),
        ]);
        Assert.Equal(1.4, logger.GetTotals().TodayAcKwh, 9);
        var chart = logger.GetChartData(EnergyPeriod.Days);
        Assert.Equal(["PC", "Monitor"], chart.Devices.Select(d => d.Label));
        Assert.Equal(1.4, chart.Combined[^1].AcKwh, 9);
        Assert.Equal(0.4, chart.Devices[1].Buckets[^1].AcKwh, 9);
        Assert.Equal(0.9, chart.Combined[^1].DcEstKwh, 9);
    }

    [Fact]
    public void ACounterResetOnOneDeviceDiscardsOnlyThatDelta()
    {
        var logger = CreateMultiLogger();
        PollAll(logger, 1000, 500, 100);
        PollAll(logger, 1010, 2, 100);

        // The monitor's counter went down: its delta is dropped, the PC's 10 Wh count.
        Assert.Equal(0.01, logger.GetTotals().TodayAcKwh, 9);
        PollAll(logger, 1020, 7, 100);
        Assert.Equal(0.025, logger.GetTotals().TodayAcKwh, 9);
    }

    [Fact]
    public void ReadingsOfUnknownDevicesAreIgnored()
    {
        var logger = CreateLogger();
        logger.OnReading(DeviceReading(MonitorId, 30, 500));
        Assert.Equal(0, logger.GetTotals().TodayAcKwh);
        Assert.False(Directory.Exists(Path.Combine(_dir, MonitorId)));
    }

    [Fact]
    public void DevicesFromSettingsListsThePcFirst()
    {
        var settings = new AppSettings { Device = new ShellyDeviceSettings { Host = "10.0.0.2" } };
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor", Host = "10.0.0.3", IncludeInTotals = false });
        var devices = EnergyLogger.DevicesFrom(settings);
        Assert.Equal([AppSettings.PrimaryDeviceId, MonitorId], devices.Select(d => d.Id));
        Assert.False(devices[1].IncludeInTotals);
        Assert.Equal("10.0.0.3|", devices[1].DeviceKey);
    }

    private static EnergyTotals Round(EnergyTotals t) =>
        new(Math.Round(t.TodayAcKwh, 9), Math.Round(t.MonthAcKwh, 9), Math.Round(t.TodayDcEstKwh, 9), Math.Round(t.MonthDcEstKwh, 9));
}
