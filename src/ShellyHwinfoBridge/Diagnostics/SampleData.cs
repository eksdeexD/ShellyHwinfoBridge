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
/// AC 312.4 W, DC 294.8 W, loss 17.6 W, other 48.1 W; 2.41 kWh today, 48.7 kWh this month) for rendered screens.
/// </summary>
internal static class SampleData
{
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

    public static PowerReadingEventArgs Reading() =>
        new(new ShellyReading(312.4, 226.9, 1.38, 123456), new PowerReading(312.4, 312.4, 294.8, 17.6, InputVoltage.V230));

    public static HwinfoMonitorSnapshot Hwinfo() => new(true, true, true, true, 48.1, @"C:\Program Files\HWiNFO64\HWiNFO64.exe", OtherSource.SharedMemory);

    public static EnergyTotals Totals() => new(2.41, 48.7, 2.2, 45.1);

    public static MainViewState MainView(RuntimeState state = RuntimeState.Running, OnlineState online = OnlineState.Online, HwinfoMonitorSnapshot? hwinfo = null, AppSettings? settings = null, PsuDatabase? database = null) =>
        MainWindowPresenter.Build(
            Reading(),
            state,
            online,
            hwinfo ?? Hwinfo(),
            state == RuntimeState.Unconfigured ? EnergyTotals.Zero : Totals(),
            state == RuntimeState.Unconfigured ? null : settings ?? Settings(),
            database ?? PsuDatabaseLoader.LoadEmbedded(),
            Culture);

    /// <summary>The sample settings with the preview's 230 V-only PSU and the 115 V override (shows the curve note).</summary>
    public static AppSettings SingleVoltageSettings()
    {
        var settings = Settings();
        settings.Psu.DatabaseId = WizardPreview.SingleVoltageId;
        settings.VoltageMode = InputVoltageMode.V115;
        return settings;
    }

    /// <summary>A plausible 48-hour profile ending with the current hour, as the chart model.</summary>
    public static EnergyChartModel Chart(EnergyPeriod period)
    {
        var tz = TimeZoneInfo.Utc;
        var (from, _) = EnergyAggregator.RangeFor(period, NowUtc, tz);
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

        return EnergyChartModel.Build(EnergyAggregator.Aggregate(rows, period, NowUtc, tz), period, Culture);
    }
}
