namespace ShellyHwinfoBridge.Core.Tests.Energy;

internal static class EnergyTestSupport
{
    /// <summary>A Central European zone (UTC+1, CEST UTC+2 from the last Sunday of March 02:00 to the last Sunday of October 03:00).</summary>
    public static TimeZoneInfo CentralEurope { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test/CET",
        TimeSpan.FromHours(1),
        "Test CET",
        "CET",
        "CEST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    public static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    public static string NewTempDirectory() => Path.Combine(Path.GetTempPath(), "shb-energy-" + Guid.NewGuid().ToString("N"));
}
