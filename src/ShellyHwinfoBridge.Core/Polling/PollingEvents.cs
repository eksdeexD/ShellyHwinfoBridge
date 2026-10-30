using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Polling;

/// <summary>Connection state of the Shelly device as seen by the poller.</summary>
public enum OnlineState
{
    /// <summary>Not yet known (not started, paused or stopped).</summary>
    Unknown,

    /// <summary>The last poll succeeded.</summary>
    Online,

    /// <summary>At least <see cref="PollingOptions.OfflineAfterFailures"/> consecutive polls failed.</summary>
    Offline,
}

/// <summary>Event data for a new reading.</summary>
public sealed class PowerReadingEventArgs : EventArgs
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="raw">The raw Shelly reading.</param>
    /// <param name="power">The converted values.</param>
    public PowerReadingEventArgs(ShellyReading raw, PowerReading power)
    {
        Raw = raw;
        Power = power;
    }

    /// <summary>Gets the raw Shelly reading (power, voltage, current).</summary>
    public ShellyReading Raw { get; }

    /// <summary>Gets the converted AC/DC/loss values.</summary>
    public PowerReading Power { get; }
}

/// <summary>Event data for an online-state transition.</summary>
public sealed class OnlineStateChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="previous">The previous state.</param>
    /// <param name="current">The new state.</param>
    /// <param name="lastError">The error that caused an offline transition, if any.</param>
    public OnlineStateChangedEventArgs(OnlineState previous, OnlineState current, Exception? lastError)
    {
        Previous = previous;
        Current = current;
        LastError = lastError;
    }

    /// <summary>Gets the previous state.</summary>
    public OnlineState Previous { get; }

    /// <summary>Gets the new state.</summary>
    public OnlineState Current { get; }

    /// <summary>Gets the last error for offline transitions.</summary>
    public Exception? LastError { get; }
}
