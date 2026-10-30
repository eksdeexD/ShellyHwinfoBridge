using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

/// <summary>The design brief's example records.</summary>
internal static class BriefExample
{
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

    public static HwinfoMonitorSnapshot Hwinfo() => new(true, true, true, true, 48.1, @"C:\Program Files\HWiNFO64\HWiNFO64.exe");

    public static PsuDatabase Database() => PsuDatabaseLoader.LoadEmbedded();
}

/// <summary>A runtime stand-in that records Apply calls.</summary>
internal sealed class FakeRuntime : IBridgeRuntime
{
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

    public AppSettings? Settings { get; set; } = BriefExample.Settings();

    public bool HasPassword => !string.IsNullOrEmpty(Settings?.Device.ProtectedPassword);

    public string? Password { get; set; }

    public PsuDatabase Database { get; set; } = BriefExample.Database();

    public RuntimeState State { get; set; } = RuntimeState.Running;

    public OnlineState OnlineState { get; set; } = OnlineState.Online;

    public PowerReadingEventArgs? LastReading { get; set; } = BriefExample.Reading();

    public List<(AppSettings Settings, string? Password)> Applied { get; } = [];

    public int RemoveCalls { get; private set; }

    public ConnectionTestResult TestResult { get; set; } = new(true, new ShellyReading(312.4, 226.9, 1.38), new ShellyDeviceInfo("PlugMGen3-2", null, null, 3), null);

    public (string Host, string? Password)? LastTest { get; private set; }

    public void Start()
    {
    }

    public Task StopAsync() => Task.CompletedTask;

    public void Pause() => State = RuntimeState.Paused;

    public void Resume() => State = RuntimeState.Running;

    public ApplyOutcome Apply(AppSettings settings, string? newPassword = null)
    {
        settings.Normalize();
        if (newPassword is not null)
        {
            settings.Device.ProtectedPassword = newPassword.Length == 0 ? null : "enc:" + newPassword;
            Password = newPassword.Length == 0 ? null : newPassword;
        }

        Applied.Add((settings, newPassword));
        Settings = settings;
        return new ApplyOutcome(true, null, []);
    }

    public ApplyOutcome ApplyFromWizard(AppSettings draft, string? password, bool startWithWindows) => Apply(draft, password ?? string.Empty);

    public Task<DatabaseUpdateResult> CheckDatabaseUpdateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DatabaseUpdateResult(DatabaseUpdateStatus.AlreadyCurrent, "The database is already up to date (2026-10-29).", null, []));

    public void RemoveSensors() => RemoveCalls++;

    public Task<ConnectionTestResult> TestConnectionAsync(string host, string? password, CancellationToken cancellationToken = default)
    {
        LastTest = (host, password);
        return Task.FromResult(TestResult);
    }
}
