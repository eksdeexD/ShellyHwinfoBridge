using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>
/// A runtime stand-in for rendered screens: answers the connection test with the sample reading, accepts Apply and
/// reports the database as up to date. Nothing is saved or polled.
/// </summary>
internal sealed class PreviewRuntime : IBridgeRuntime
{
    /// <summary>Creates a stand-in.</summary>
    /// <param name="settings">The current settings, or <c>null</c> for first run.</param>
    /// <param name="hasPassword">Whether a password is "stored".</param>
    /// <param name="database">The PSU database; <c>null</c> for the embedded one.</param>
    public PreviewRuntime(AppSettings? settings = null, bool hasPassword = false, PsuDatabase? database = null)
    {
        Settings = settings;
        HasPassword = hasPassword;
        Database = database ?? PsuDatabaseLoader.LoadEmbedded();
    }

    public event EventHandler? SettingsChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<PowerReadingEventArgs>? ReadingReceived
    {
        add { }
        remove { }
    }

    public event EventHandler? SensorKeysCreated
    {
        add { }
        remove { }
    }

    public AppSettings? Settings { get; private set; }

    public bool HasPassword { get; private set; }

    public string? Password => null;

    public PsuDatabase Database { get; }

    public RuntimeState State => Settings is null ? RuntimeState.Unconfigured : RuntimeState.Running;

    public OnlineState OnlineState => OnlineState.Online;

    public PowerReadingEventArgs? LastReading => SampleData.Reading();

    public IReadOnlyList<DeviceStatus> Devices =>
    [
        new(AppSettings.PrimaryDeviceId, AppSettings.PrimaryLabel, Settings?.Device.Host ?? string.Empty, Settings?.Device.Name, Settings?.Device.Model, OnlineState.Online, LastReading?.Power.AcWatts, true),
        .. (Settings?.AdditionalDevices ?? []).Select((d, i) => new DeviceStatus(d.Id, d.Label, d.Host, d.Name, d.Model, i == 0 ? OnlineState.Online : OnlineState.Offline, i == 0 ? SampleData.MonitorWatts : null, d.IncludeInTotals)),
    ];

    public double? TotalWatts => Settings is { HasAdditionalDevices: true } s
        ? LastReading!.Power.AcWatts + s.AdditionalDevices.Take(1).Where(d => d.IncludeInTotals).Sum(_ => SampleData.MonitorWatts)
        : null;

    public OtherComponentsState Other => new(48.1, OtherSource.SharedMemory, true, [], true);

    public string? GetDevicePassword(string deviceId) => null;

    public void Start()
    {
    }

    public Task StopAsync() => Task.CompletedTask;

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public ApplyOutcome Apply(AppSettings settings, string? newPassword = null, IReadOnlyDictionary<string, string>? devicePasswords = null)
    {
        Settings = settings;
        if (newPassword is not null)
        {
            HasPassword = newPassword.Length > 0;
        }

        return new ApplyOutcome(true, null, []);
    }

    public ApplyOutcome ApplyFromWizard(AppSettings draft, string? password, bool startWithWindows, IReadOnlyDictionary<string, string>? devicePasswords = null) => Apply(draft, password);

    public Task<DatabaseUpdateResult> CheckDatabaseUpdateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DatabaseUpdateResult(DatabaseUpdateStatus.AlreadyCurrent, "The database is already up to date.", null, []));

    public void RemoveSensors()
    {
    }

    public Task<ConnectionTestResult> TestConnectionAsync(string host, string? password, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectionTestResult(true, SampleData.Reading().Raw, new ShellyDeviceInfo("PlugMGen3-2", "S3PL-10112EU", "PlugMG3", 3), null));
}
