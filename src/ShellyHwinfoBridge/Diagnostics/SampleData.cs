using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>
/// The design brief's example records (Friday 30 October 2026, 19:40; plug "PlugMGen3-2"; TX-700; four sensors;
/// AC 312.4 W, DC 294.8 W, loss 17.6 W, other 48.1 W; 2.41 kWh today, 48.7 kWh this month) for rendered screens, plus
/// a second plug ("Monitor", 36.5 W) for the multi-device screens.
/// </summary>
internal static class SampleData
{
    /// <summary>The fixed id of the sample "Monitor" device.</summary>
    public const string MonitorId = "6f1c2a7e-5b8d-4c3a-9e2f-1a2b3c4d5e6f";

    /// <summary>The sample monitor's power in watts.</summary>
    public const double MonitorWatts = 36.5;

    /// <summary>The culture used for rendered screens, so they compare 1:1 with the design images.</summary>
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>kWh per local hour (0–23), read off the design image's chart.</summary>
    private static readonly double[] DailyProfile =
    [
        0.07, 0.06, 0.05, 0.045, 0.04, 0.04, 0.04, 0.04, 0.06, 0.08, 0.085, 0.10,
        0.12, 0.14, 0.15, 0.17, 0.19, 0.23, 0.28, 0.31, 0.23, 0.16, 0.12, 0.09,
    ];

    public static DateTimeOffset NowUtc { get; } = new(2026, 10, 30, 18, 40, 0, TimeSpan.Zero);

    public static AppSettings Settings() => new()
    {
        Device = new ShellyDeviceSettings { Host = "192.168.1.42", Name = "PlugMGen3-2", Model = "S3PL-10112EU" },
        Psu = new PsuSettings { Mode = PsuSelectionMode.Database, DatabaseId = "seasonic-prime-fanless-tx-700" },
        Sensors =
        [
            new SensorSelection { Name = "CPU Package Power", Category = SensorCategory.Cpu },
            new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu },
            new SensorSelection { Name = "DIMM1 Power", Category = SensorCategory.Ram },
            new SensorSelection { Name = "DIMM3 Power", Category = SensorCategory.Ram },
        ],
    };

    /// <summary>The sample monitor plug (an additional device, included in the totals).</summary>
    public static AdditionalDeviceSettings Monitor() => new()
    {
        Id = MonitorId,
        Label = "Monitor",
        Host = "192.168.1.57",
        Name = "PlugSG3-1",
        Model = "S3PL-00112EU",
        IncludeInTotals = true,
    };

    /// <summary>The sample settings with the monitor plug added.</summary>
    public static AppSettings TwoDeviceSettings(EnergyChartMode mode = EnergyChartMode.Combined)
    {
        var settings = Settings();
        settings.AdditionalDevices = [Monitor()];
        settings.ChartMode = mode;
        return settings;
    }

    public static PowerReadingEventArgs Reading() =>
        new(new ShellyReading(312.4, 226.9, 1.38, 123456), new PowerReading(312.4, 312.4, 294.8, 17.6, InputVoltage.V230));

    public static HwinfoMonitorSnapshot Hwinfo() => new(true, true, true, true, @"C:\Program Files\HWiNFO64\HWiNFO64.exe", OtherSource.SharedMemory);

    /// <summary>HWiNFO running with shared memory off and no usable gadget export (the Other card's "How to enable?").</summary>
    public static HwinfoMonitorSnapshot HwinfoWithoutComponents() => new(true, true, false, false, @"C:\Program Files\HWiNFO64\HWiNFO64.exe", OtherSource.None);

    /// <summary>The app-computed Other Components from shared memory.</summary>
    public static OtherComponentsState Other() => new(48.1, OtherSource.SharedMemory, true, [], true);

    public static EnergyTotals Totals() => new(2.41, 48.7, 2.2, 45.1);

    public static MainViewState MainView(RuntimeState state = RuntimeState.Running, OnlineState online = OnlineState.Online, HwinfoMonitorSnapshot? hwinfo = null, AppSettings? settings = null, PsuDatabase? database = null, OtherComponentsState? other = null, OnlineState additionalState = OnlineState.Online)
    {
        var effective = state == RuntimeState.Unconfigured ? null : settings ?? Settings();
        var total = effective is { HasAdditionalDevices: true } ? Reading().Power.AcWatts + MonitorWatts : (double?)null;
        var devices = effective is null
            ? null
            : MainWindowPresenter.DevicesFromSettings(effective, online).Select((d, i) => i == 0 ? d : d with { State = additionalState }).ToList();
        return MainWindowPresenter.Build(
            Reading(),
            state,
            online,
            hwinfo ?? Hwinfo(),
            state == RuntimeState.Unconfigured ? EnergyTotals.Zero : Totals(),
            effective,
            database ?? PsuDatabaseLoader.LoadEmbedded(),
            Culture,
            other ?? Other(),
            total,
            devices);
    }

    /// <summary>The sample settings with the preview's 230 V-only PSU and the 115 V override (shows the curve note).</summary>
    public static AppSettings SingleVoltageSettings()
    {
        var settings = Settings();
        settings.Psu.DatabaseId = WizardPreview.SingleVoltageId;
        settings.VoltageMode = InputVoltageMode.V115;
        return settings;
    }

    /// <summary>A plausible profile ending with the current hour, as the chart model.</summary>
    public static EnergyChartModel Chart(EnergyPeriod period) =>
        EnergyChartModel.Build(EnergyAggregator.Aggregate(PcRows(period), period, NowUtc, TimeZoneInfo.Utc), period, Culture);

    /// <summary>The chart with the sample monitor (combined or per device, as <paramref name="settings"/> says).</summary>
    public static EnergyChartModel TwoDeviceChart(EnergyPeriod period, AppSettings settings)
    {
        var tz = TimeZoneInfo.Utc;
        var pc = PcRows(period);

        // The monitor draws about 36 W while the PC is busy and stays in standby (0.5 W) otherwise.
        var monitor = pc.Select(r => r with { AcWh = r.AcWh > 90 ? 36.5 : 0.5, DcEstWh = 0 }).ToList();
        var combined = pc.Zip(monitor, (a, b) => a with { AcWh = a.AcWh + b.AcWh }).ToList();
        var data = new EnergyChartData(
            EnergyAggregator.Aggregate(combined, period, NowUtc, tz),
            [
                new DeviceEnergySeries(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, EnergyAggregator.Aggregate(pc, period, NowUtc, tz)),
                new DeviceEnergySeries(MonitorId, "Monitor", EnergyAggregator.Aggregate(monitor, period, NowUtc, tz)),
            ]);
        return EnergyChartModel.Build(data, period, settings, Culture);
    }

    private static List<EnergyHourRecord> PcRows(EnergyPeriod period)
    {
        var (from, _) = EnergyAggregator.RangeFor(period, NowUtc, TimeZoneInfo.Utc);
        var rows = new List<EnergyHourRecord>();
        var hours = (int)(NowUtc - from).TotalHours + 1;
        for (var i = 0; i < hours; i++)
        {
            var hour = EnergyHourRecord.HourStart(from).AddHours(i);
            var day = (hour - NowUtc).TotalDays;
            var kwh = DailyProfile[hour.Hour] * (1 + (0.12 * Math.Sin(day * 0.9))) * (1 + (0.04 * Math.Sin(i * 1.7)));
            if (hour.Hour == 18 && hour.Date == NowUtc.Date)
            {
                kwh = 0.34;
            }

            rows.Add(new EnergyHourRecord(hour, kwh * 1000, kwh * 920, 3600, null, null, null));
        }

        return rows;
    }
}
