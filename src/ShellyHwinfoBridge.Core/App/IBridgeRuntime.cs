using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.App;

/// <summary>What the bridge is doing.</summary>
public enum RuntimeState
{
    /// <summary>No settings yet (first run, or the wizard was cancelled): nothing is polled.</summary>
    Unconfigured,

    /// <summary>Configured, but the PSU selection does not resolve to a curve (e.g. the database entry is gone).</summary>
    CurveUnavailable,

    /// <summary>Polling the Shelly.</summary>
    Running,

    /// <summary>Polling was paused by the user (or by "Remove HWiNFO sensors").</summary>
    Paused,
}

/// <summary>The result of applying settings.</summary>
/// <param name="Success">Whether everything was saved and applied.</param>
/// <param name="Error">A short user-facing error when <paramref name="Success"/> is <c>false</c>.</param>
/// <param name="ChangedKeys">The names of the settings that changed (for logging; never values).</param>
public sealed record ApplyOutcome(bool Success, string? Error, IReadOnlyList<string> ChangedKeys)
{
    /// <summary>Creates a failed outcome.</summary>
    /// <param name="error">The user-facing error.</param>
    public static ApplyOutcome Failed(string error) => new(false, error, []);
}

/// <summary>The result of a connection test.</summary>
/// <param name="Success">Whether a reading was received.</param>
/// <param name="Reading">The reading, when successful.</param>
/// <param name="DeviceInfo">The device identity, when it could be read.</param>
/// <param name="Error">A short user-facing error when not successful.</param>
public sealed record ConnectionTestResult(bool Success, ShellyReading? Reading, ShellyDeviceInfo? DeviceInfo, string? Error);

/// <summary>
/// The running bridge: owns the settings, the poller, the Shelly client and the energy logger, and applies setting
/// changes without a restart. UI code reads its state through presenters.
/// </summary>
/// <remarks>Events are raised on arbitrary threads (thread-pool for readings); UI subscribers must marshal.</remarks>
public interface IBridgeRuntime
{
    /// <summary>Raised after settings (or the PSU database) changed. Do not mutate <see cref="Settings"/>; clone it.</summary>
    event EventHandler? SettingsChanged;

    /// <summary>Raised when <see cref="State"/> or <see cref="OnlineState"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised after each successful poll.</summary>
    event EventHandler<PowerReadingEventArgs>? ReadingReceived;

    /// <summary>Raised when resuming re-created the HWiNFO sensor keys, so HWiNFO must be restarted.</summary>
    event EventHandler? SensorKeysCreated;

    /// <summary>Gets the current settings, or <c>null</c> on first run. Treat as read-only.</summary>
    AppSettings? Settings { get; }

    /// <summary>Gets whether a stored device password exists.</summary>
    bool HasPassword { get; }

    /// <summary>Gets the decrypted device password, or <c>null</c>. Never log or display it.</summary>
    string? Password { get; }

    /// <summary>Gets the PSU database in use.</summary>
    PsuDatabase Database { get; }

    /// <summary>Gets the runtime state.</summary>
    RuntimeState State { get; }

    /// <summary>Gets the poller's online state (<see cref="Polling.OnlineState.Unknown"/> while not polling).</summary>
    OnlineState OnlineState { get; }

    /// <summary>Gets the last reading, or <c>null</c> when none was received since the last start, pause or offline transition.</summary>
    PowerReadingEventArgs? LastReading { get; }

    /// <summary>Starts polling when configured. Safe to call more than once.</summary>
    void Start();

    /// <summary>Stops polling (sensors go offline) and flushes the energy log.</summary>
    Task StopAsync();

    /// <summary>Pauses polling; the sensors go offline.</summary>
    void Pause();

    /// <summary>Resumes polling (re-creating the HWiNFO keys if they were removed).</summary>
    void Resume();

    /// <summary>
    /// Saves <paramref name="settings"/> and applies only what changed (client, calculator, interval, formula,
    /// autostart, energy logging). The caller passes a modified clone of <see cref="Settings"/>.
    /// </summary>
    /// <param name="settings">The new settings (taken over by the runtime).</param>
    /// <param name="newPassword"><c>null</c> keeps the stored password, an empty string clears it, any other value replaces it.</param>
    ApplyOutcome Apply(AppSettings settings, string? newPassword = null);

    /// <summary>
    /// Applies the wizard's result: saves settings and password, creates the sensor keys, writes the formula, sets
    /// autostart, then starts or resumes polling. Nothing is changed before this call.
    /// </summary>
    /// <param name="draft">The wizard's settings (taken over by the runtime).</param>
    /// <param name="password">The device password, or <c>null</c>/empty for none.</param>
    /// <param name="startWithWindows">Whether to start with Windows.</param>
    ApplyOutcome ApplyFromWizard(AppSettings draft, string? password, bool startWithWindows);

    /// <summary>Checks for a PSU database update; on success reloads the database and the curve.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DatabaseUpdateResult> CheckDatabaseUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses polling and deletes the HWiNFO sensor keys (polling stays paused, a write would re-create them).</summary>
    void RemoveSensors();

    /// <summary>Reads the device info and one reading from a host (3 s timeout), without changing anything.</summary>
    /// <param name="host">The host.</param>
    /// <param name="password">The password, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConnectionTestResult> TestConnectionAsync(string host, string? password, CancellationToken cancellationToken = default);
}
