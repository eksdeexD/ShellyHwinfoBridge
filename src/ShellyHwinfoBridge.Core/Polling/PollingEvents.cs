using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Polling;

/// <summary>Connection state of a Shelly device as seen by its poller.</summary>
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
    /// <param name="power">The converted values (an additional device has DC and loss 0).</param>
    /// <param name="deviceId">The device id (<see cref="AppSettings.PrimaryDeviceId"/> for the PC's plug).</param>
    public PowerReadingEventArgs(ShellyReading raw, PowerReading power, string deviceId = AppSettings.PrimaryDeviceId)
    {
        Raw = raw;
        Power = power;
        DeviceId = deviceId ?? AppSettings.PrimaryDeviceId;
    }

    /// <summary>Gets the raw Shelly reading (power, voltage, current).</summary>
    public ShellyReading Raw { get; }

    /// <summary>Gets the converted AC/DC/loss values.</summary>
    public PowerReading Power { get; }

    /// <summary>Gets the id of the device that was read.</summary>
    public string DeviceId { get; }

    /// <summary>Gets whether the reading is from the primary (PC) plug.</summary>
    public bool IsPrimary => AppSettings.IsPrimary(DeviceId);
}

/// <summary>Event data for an online-state transition.</summary>
public sealed class OnlineStateChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="previous">The previous state.</param>
    /// <param name="current">The new state.</param>
    /// <param name="lastError">The error that caused an offline transition, if any.</param>
    /// <param name="deviceId">The device id.</param>
    public OnlineStateChangedEventArgs(OnlineState previous, OnlineState current, Exception? lastError, string deviceId = AppSettings.PrimaryDeviceId)
    {
        Previous = previous;
        Current = current;
        LastError = lastError;
        DeviceId = deviceId ?? AppSettings.PrimaryDeviceId;
    }

    /// <summary>Gets the previous state.</summary>
    public OnlineState Previous { get; }

    /// <summary>Gets the new state.</summary>
    public OnlineState Current { get; }

    /// <summary>Gets the last error for offline transitions.</summary>
    public Exception? LastError { get; }

    /// <summary>Gets the id of the device.</summary>
    public string DeviceId { get; }
}

/// <summary>Event data for a failed poll.</summary>
/// <param name="DeviceId">The device id.</param>
/// <param name="ConsecutiveFailures">The failures in a row, including this one.</param>
/// <param name="State">The state after the failure.</param>
/// <param name="Error">The error.</param>
public sealed record PollFailedEventArgs(string DeviceId, int ConsecutiveFailures, OnlineState State, Exception Error);
