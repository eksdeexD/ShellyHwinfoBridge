namespace ShellyHwinfoBridge.Core.Polling;

/// <summary>Poll interval and HTTP timeout.</summary>
public sealed record PollingOptions
{
    /// <summary>Smallest allowed interval in milliseconds.</summary>
    public const int MinIntervalMs = 500;

    /// <summary>Largest allowed interval in milliseconds.</summary>
    public const int MaxIntervalMs = 5000;

    /// <summary>Default interval in milliseconds.</summary>
    public const int DefaultIntervalMs = 1000;

    /// <summary>Default HTTP timeout in milliseconds.</summary>
    public const int DefaultTimeoutMs = 800;

    /// <summary>Smallest allowed timeout in milliseconds.</summary>
    public const int MinTimeoutMs = 100;

    /// <summary>The timeout is always at least this much shorter than the interval.</summary>
    public const int TimeoutMarginMs = 100;

    /// <summary>Consecutive failures after which the device is reported offline.</summary>
    public const int OfflineAfterFailures = 3;

    /// <summary>Gets the poll interval in milliseconds.</summary>
    public int IntervalMs { get; init; } = DefaultIntervalMs;

    /// <summary>Gets the HTTP timeout in milliseconds.</summary>
    public int TimeoutMs { get; init; } = DefaultTimeoutMs;

    /// <summary>Gets the interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(IntervalMs);

    /// <summary>Gets the timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);

    /// <summary>
    /// Returns options with the interval clamped to 500–5000 ms and the timeout clamped to
    /// [<see cref="MinTimeoutMs"/>, interval − <see cref="TimeoutMarginMs"/>].
    /// </summary>
    public PollingOptions Normalize()
    {
        var interval = Math.Clamp(IntervalMs, MinIntervalMs, MaxIntervalMs);
        var timeout = Math.Clamp(TimeoutMs, MinTimeoutMs, interval - TimeoutMarginMs);
        return new PollingOptions { IntervalMs = interval, TimeoutMs = timeout };
    }
}
