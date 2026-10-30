using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class ChartAndTrayTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    // Friday 30 October 2026, 18:40 UTC.
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 18, 40, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.34, 0.4)]
    [InlineData(0.4, 0.4)]
    [InlineData(0.45, 0.5)]
    [InlineData(0.09, 0.1)]
    [InlineData(2.1, 3)]
    [InlineData(7, 8)]
    [InlineData(12, 20)]
    [InlineData(1, 1)]
    [InlineData(0, 0.1)]
    public void NiceMax(double max, double expected) => Assert.Equal(expected, ChartScale.Nice(max), 9);

    [Fact]
    public void YLabelsCarryTheUnitOnTop()
    {
        Assert.Equal(["0", "0.2", "0.4 kWh"], ChartScale.Labels(0.4, EnUs));
        Assert.Equal(["0", "0,05", "0,1 kWh"], ChartScale.Labels(0.1, CultureInfo.GetCultureInfo("da-DK")));
        Assert.Equal(["0", "1.5", "3 kWh"], ChartScale.Labels(3, EnUs));
    }

    [Fact]
    public void HoursChartHas48BarsLabelledEveryFourHours()
    {
        var rows = new[] { new EnergyHourRecord(new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero), 340, 310, 2400, 300, 400, 340) };
        var buckets = EnergyAggregator.Aggregate(rows, EnergyPeriod.Hours, Now, TimeZoneInfo.Utc);

        var chart = EnergyChartModel.Build(buckets, EnergyPeriod.Hours, EnUs);

        Assert.Equal(48, chart.Bars.Count);
        Assert.Equal(12, chart.Bars.Count(b => b.Label is not null));
        Assert.All(chart.Bars.Where(b => b.Label is not null), b => Assert.Equal(0, int.Parse(b.Label!, CultureInfo.InvariantCulture) % 4));
        Assert.Equal("16", chart.Bars[^3].Label);
        Assert.Equal("Fri 18:00 · 0.34 kWh · PC est. 0.31 kWh", chart.Bars[^1].Tooltip);
        Assert.Equal("Fri 17:00 · no data", chart.Bars[^2].Tooltip);
        Assert.False(chart.Bars[^2].HasData);
        Assert.Equal(0.4, chart.YMax, 9);
        Assert.Equal(["0", "0.2", "0.4 kWh"], chart.YLabels);
    }

    [Fact]
    public void DaysAndMonthsLabelSpacing()
    {
        var days = EnergyChartModel.Build(EnergyAggregator.Aggregate([], EnergyPeriod.Days, Now, TimeZoneInfo.Utc), EnergyPeriod.Days, EnUs);
        Assert.Equal(60, days.Bars.Count);
        Assert.Equal(9, days.Bars.Count(b => b.Label is not null));
        Assert.Equal("10/30", days.Bars[^1].Label);
        Assert.Equal("10/23", days.Bars[^8].Label);
        Assert.Equal("Fri 10/30 · no data", days.Bars[^1].Tooltip);
        Assert.Equal(ChartScale.EmptyMax, days.YMax);

        var months = EnergyChartModel.Build(EnergyAggregator.Aggregate([], EnergyPeriod.Months, Now, TimeZoneInfo.Utc), EnergyPeriod.Months, EnUs);
        Assert.Equal(24, months.Bars.Count);
        Assert.Equal(8, months.Bars.Count(b => b.Label is not null));
        Assert.Equal("Oct", months.Bars[^1].Label);
        Assert.Equal("Jul", months.Bars[^4].Label);
        Assert.Equal("October 2026 · no data", months.Bars[^1].Tooltip);
    }

    [Theory]
    [InlineData("en-US", "M/d")]
    [InlineData("da-DK", "dd.MM")]
    [InlineData("de-DE", "dd.MM")]
    [InlineData("ja-JP", "MM/dd")]
    public void DayMonthPatternDropsTheYear(string culture, string expected) =>
        Assert.Equal(expected, EnergyChartModel.DayMonthPattern(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void TrayTooltipWithAndWithoutSharedMemory()
    {
        var reading = BriefExample.Reading();
        var hw = BriefExample.Hwinfo();
        Assert.Equal("AC 312.4 W · DC 294.8 W · Other 48.1 W", TrayPresenter.Tooltip(RuntimeState.Running, OnlineState.Online, reading, hw, EnUs));
        Assert.Equal("AC 312.4 W · DC 294.8 W", TrayPresenter.Tooltip(RuntimeState.Running, OnlineState.Online, reading, hw with { SharedMemoryAvailable = false }, EnUs));
        Assert.Equal("AC 312.4 W · DC 294.8 W", TrayPresenter.Tooltip(RuntimeState.Running, OnlineState.Online, reading, hw with { SensorsVisible = false }, EnUs));
        Assert.Equal("Shelly offline", TrayPresenter.Tooltip(RuntimeState.Running, OnlineState.Offline, null, hw, EnUs));
        Assert.Equal("Polling paused", TrayPresenter.Tooltip(RuntimeState.Paused, OnlineState.Unknown, null, hw, EnUs));
        Assert.Equal("Setup not completed", TrayPresenter.Tooltip(RuntimeState.Unconfigured, OnlineState.Unknown, null, hw, EnUs));
        Assert.Equal("No PSU curve: open Settings", TrayPresenter.Tooltip(RuntimeState.CurveUnavailable, OnlineState.Unknown, null, hw, EnUs));
        Assert.True(TrayPresenter.Tooltip(RuntimeState.Running, OnlineState.Online, reading, hw, EnUs).Length <= TrayPresenter.MaxTooltipLength);
    }

    [Fact]
    public void TrayIconStates()
    {
        Assert.Equal(TrayIconState.Online, TrayPresenter.IconState(RuntimeState.Running, OnlineState.Online));
        Assert.Equal(TrayIconState.Offline, TrayPresenter.IconState(RuntimeState.Running, OnlineState.Offline));
        Assert.Equal(TrayIconState.Offline, TrayPresenter.IconState(RuntimeState.Running, OnlineState.Unknown));
        Assert.Equal(TrayIconState.Paused, TrayPresenter.IconState(RuntimeState.Paused, OnlineState.Unknown));
        Assert.Equal(TrayIconState.Offline, TrayPresenter.IconState(RuntimeState.Unconfigured, OnlineState.Unknown));
    }
}
