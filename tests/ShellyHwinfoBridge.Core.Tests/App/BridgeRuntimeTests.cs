using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Startup;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.App;

public sealed class BridgeRuntimeTests : IDisposable
{
    private const string Base = HwinfoSensorNames.BaseKeyPath;
    private const string Exe = @"C:\Apps\ShellyHwinfoBridge.exe";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shb-runtime-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryRegistryBackend _backend = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 18, 40, 0, TimeSpan.Zero));
    private readonly List<FakeAppShellyClient> _clients = [];
    private readonly SettingsStore _store;
    private readonly HwinfoSensorRegistry _registry;
    private readonly AutostartManager _autostart;
    private readonly DatabaseUpdater _updater;
    private readonly List<BridgeRuntime> _runtimes = [];

    public BridgeRuntimeTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new SettingsStore(Path.Combine(_dir, "settings.json"), new FakePasswordProtector());
        _registry = new HwinfoSensorRegistry(_backend);
        _autostart = new AutostartManager(_backend, Exe);
        var loader = new PsuDatabaseLoader(Path.Combine(_dir, "psu-database.json"));
        _updater = new DatabaseUpdater(loader, new StubHttpHandler());
    }

    public void Dispose()
    {
        foreach (var runtime in _runtimes)
        {
            runtime.StopAsync().GetAwaiter().GetResult();
        }

        _updater.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    internal static AppSettings ExampleSettings() => new()
    {
        Device = new ShellyDeviceSettings { Host = "192.168.1.42", Name = "PlugMGen3-2", Model = "S3PL-10112EU" },
        Psu = new PsuSettings { Mode = PsuSelectionMode.Database, DatabaseId = "seasonic-prime-fanless-tx-700" },
        Sensors =
        [
            new SensorSelection { Name = "CPU Package Power", Category = SensorCategory.Cpu },
            new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu },
        ],
    };

    private BridgeRuntime Create(AppSettings? settings, string? password = null)
    {
        if (settings is not null && password is not null)
        {
            _store.SetPassword(settings, password);
        }

        var energy = new EnergyLogger(new EnergyCsvStore(Path.Combine(_dir, "energy")), _registry, _time, TimeZoneInfo.Utc);
        var runtime = new BridgeRuntime(
            settings,
            _store,
            new PsuDatabaseLoader(Path.Combine(_dir, "psu-database.json")),
            _updater,
            _registry,
            _autostart,
            energy,
            (host, pw) =>
            {
                var client = new FakeAppShellyClient(host, pw);
                lock (_clients)
                {
                    _clients.Add(client);
                }

                return client;
            },
            _time);
        _runtimes.Add(runtime);
        return runtime;
    }

    private static async Task<PowerReadingEventArgs> NextReading(BridgeRuntime runtime, Action trigger)
    {
        var tcs = new TaskCompletionSource<PowerReadingEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, PowerReadingEventArgs e) => tcs.TrySetResult(e);
        runtime.ReadingReceived += Handler;
        try
        {
            trigger();
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            runtime.ReadingReceived -= Handler;
        }
    }

    private string? Value(string subkey) => _backend.GetString(Base + "\\" + subkey, "Value");

    [Fact]
    public void FirstRunIsUnconfiguredAndPollsNothing()
    {
        var runtime = Create(null);
        runtime.Start();

        Assert.Equal(RuntimeState.Unconfigured, runtime.State);
        Assert.Equal(OnlineState.Unknown, runtime.OnlineState);
        Assert.Empty(_clients);
        Assert.Null(runtime.Settings);
    }

    [Fact]
    public void MissingCurveGivesCurveUnavailable()
    {
        var settings = ExampleSettings();
        settings.Psu.DatabaseId = "no-such-psu";
        var runtime = Create(settings);
        runtime.Start();

        Assert.Equal(RuntimeState.CurveUnavailable, runtime.State);
        Assert.Empty(_clients);
    }

    [Fact]
    public async Task StartPollsWritesSensorsAndFeedsEnergyLogger()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings(), "secret");

        var reading = await NextReading(runtime, runtime.Start);

        Assert.Equal(RuntimeState.Running, runtime.State);
        var client = Assert.Single(_clients);
        Assert.Equal("192.168.1.42", client.Host);
        Assert.Equal("secret", client.Password);
        Assert.Equal(312.4, reading.Power.AcWatts, 6);
        Assert.Same(reading, runtime.LastReading);
        Assert.Equal("312.4", Value(HwinfoSensorRegistry.AcSubkey));
        Assert.Equal("1", Value(HwinfoSensorRegistry.OnlineSubkey));
        Assert.Equal("\"DC Power (Est.)\" - \"CPU Package Power\" - \"GPU Power\"", Value(HwinfoSensorRegistry.OtherSubkey));
        Assert.Equal("0.00", Value(HwinfoSensorRegistry.EnergyTodaySubkey));
    }

    [Fact]
    public async Task ApplyRebuildsOnlyWhatChanged()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);
        var first = Assert.Single(_clients);

        var interval = runtime.Settings!.Clone();
        interval.PollIntervalMs = 2000;
        var outcome = runtime.Apply(interval);
        Assert.True(outcome.Success);
        Assert.Equal(["PollInterval"], outcome.ChangedKeys);
        Assert.Single(_clients);
        Assert.False(first.Disposed);

        var host = runtime.Settings!.Clone();
        host.Device.Host = "192.168.1.43";
        outcome = runtime.Apply(host);
        Assert.Equal(["Device.Host"], outcome.ChangedKeys);
        Assert.Equal(2, _clients.Count);
        Assert.True(first.Disposed);
        Assert.Equal("192.168.1.43", _clients[1].Host);

        var formula = runtime.Settings!.Clone();
        formula.Sensors.Add(new SensorSelection { Name = "DIMM1 Power", Category = SensorCategory.Ram });
        formula.StartWithWindows = true;
        outcome = runtime.Apply(formula);
        Assert.Equal(["Sensors", "StartWithWindows"], outcome.ChangedKeys);
        Assert.EndsWith("- \"DIMM1 Power\"", Value(HwinfoSensorRegistry.OtherSubkey), StringComparison.Ordinal);
        Assert.Equal($"\"{Exe}\" --autostart", _backend.GetString(AutostartManager.RunKeyPath, AutostartManager.ValueName));
        Assert.Equal(2, _clients.Count);

        // Saved to disk.
        Assert.Equal(3, _store.Load()!.Sensors.Count);
    }

    [Fact]
    public void ApplyPasswordSemantics()
    {
        var runtime = Create(ExampleSettings(), "secret");
        runtime.Start();

        runtime.Apply(runtime.Settings!.Clone());
        Assert.Equal("secret", runtime.Password);
        Assert.True(runtime.HasPassword);

        var outcome = runtime.Apply(runtime.Settings!.Clone(), "other");
        Assert.Equal(["Device.Password"], outcome.ChangedKeys);
        Assert.Equal("other", runtime.Password);
        Assert.Equal("other", _clients[^1].Password);

        runtime.Apply(runtime.Settings!.Clone(), string.Empty);
        Assert.Null(runtime.Password);
        Assert.False(runtime.HasPassword);
        Assert.Null(_store.Load()!.Device.ProtectedPassword);
    }

    [Fact]
    public async Task PauseAndResume()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);

        runtime.Pause();
        Assert.Equal(RuntimeState.Paused, runtime.State);
        Assert.Null(runtime.LastReading);
        Assert.Equal("0", Value(HwinfoSensorRegistry.OnlineSubkey));

        await NextReading(runtime, () =>
        {
            runtime.Resume();
            _time.Advance(TimeSpan.FromSeconds(1));
        });
        Assert.Equal(RuntimeState.Running, runtime.State);
        Assert.Equal("1", Value(HwinfoSensorRegistry.OnlineSubkey));
    }

    [Fact]
    public async Task RemoveSensorsPausesAndResumeRecreatesKeys()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);
        var recreated = 0;
        runtime.SensorKeysCreated += (_, _) => recreated++;

        runtime.RemoveSensors();
        Assert.Equal(RuntimeState.Paused, runtime.State);
        Assert.DoesNotContain(_backend.Keys.Keys, k => k.StartsWith(Base, StringComparison.OrdinalIgnoreCase));

        // Nothing re-creates the keys while paused.
        _time.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        Assert.DoesNotContain(_backend.Keys.Keys, k => k.StartsWith(Base, StringComparison.OrdinalIgnoreCase));

        runtime.Resume();
        Assert.Equal(1, recreated);
        Assert.NotNull(Value(HwinfoSensorRegistry.AcSubkey));
        Assert.StartsWith("\"DC Power (Est.)\"", Value(HwinfoSensorRegistry.OtherSubkey), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyFromWizardSavesCreatesKeysAndStarts()
    {
        var runtime = Create(null);
        runtime.Start();
        var changed = 0;
        runtime.SettingsChanged += (_, _) => changed++;

        var outcome = default(ApplyOutcome);
        await NextReading(runtime, () => outcome = runtime.ApplyFromWizard(ExampleSettings(), "pw", startWithWindows: true));

        Assert.True(outcome!.Success);
        Assert.Equal(1, changed);
        Assert.Equal(RuntimeState.Running, runtime.State);
        var saved = _store.Load()!;
        Assert.True(saved.StartWithWindows);
        Assert.Equal("enc:wp", saved.Device.ProtectedPassword);
        Assert.Equal("pw", Assert.Single(_clients).Password);
        Assert.StartsWith("\"DC Power (Est.)\"", Value(HwinfoSensorRegistry.OtherSubkey), StringComparison.Ordinal);
        Assert.NotNull(_backend.GetString(AutostartManager.RunKeyPath, AutostartManager.ValueName));
    }

    [Fact]
    public void ApplyFromWizardFailureChangesNothing()
    {
        // The settings path is a directory, so saving fails.
        var store = new SettingsStore(_dir, new FakePasswordProtector());
        var runtime = new BridgeRuntime(
            null,
            store,
            new PsuDatabaseLoader(Path.Combine(_dir, "psu-database.json")),
            _updater,
            _registry,
            _autostart,
            new EnergyLogger(new EnergyCsvStore(Path.Combine(_dir, "energy")), _registry, _time, TimeZoneInfo.Utc),
            (h, p) => new FakeAppShellyClient(h, p),
            _time);
        runtime.Start();

        var outcome = runtime.ApplyFromWizard(ExampleSettings(), null, startWithWindows: true);

        Assert.False(outcome.Success);
        Assert.StartsWith("Applying the setup failed", outcome.Error, StringComparison.Ordinal);
        Assert.Equal(RuntimeState.Unconfigured, runtime.State);
        Assert.Empty(_backend.Keys);
    }

    [Fact]
    public async Task TestConnectionReportsSuccessAndErrors()
    {
        var runtime = Create(null);

        var ok = await runtime.TestConnectionAsync("192.168.1.50", null);
        Assert.True(ok.Success);
        Assert.Equal("PlugMGen3-2", ok.DeviceInfo!.Name);
        Assert.Equal(312.4, ok.Reading!.ActivePowerWatts);
        Assert.True(_clients[^1].Disposed);

        var invalid = await runtime.TestConnectionAsync("http://bad host", null);
        Assert.False(invalid.Success);
        Assert.Equal("Enter a valid IP address or host name.", invalid.Error);
    }

    [Fact]
    public async Task TestConnectionMapsAuthenticationFailure()
    {
        var store = _store;
        var runtime = new BridgeRuntime(
            null,
            store,
            new PsuDatabaseLoader(Path.Combine(_dir, "psu-database.json")),
            _updater,
            _registry,
            _autostart,
            new EnergyLogger(new EnergyCsvStore(Path.Combine(_dir, "energy")), _registry, _time, TimeZoneInfo.Utc),
            (h, p) => new FakeAppShellyClient(h, p) { Error = new ShellyAuthenticationException("401") },
            _time);

        var result = await runtime.TestConnectionAsync("192.168.1.50", "wrong");

        Assert.False(result.Success);
        Assert.Equal("The device rejected the password.", result.Error);
    }

    [Fact]
    public async Task StopFlushesAndSetsOffline()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);

        await runtime.StopAsync();

        Assert.Equal("0", Value(HwinfoSensorRegistry.OnlineSubkey));
        Assert.Equal("0.0", Value(HwinfoSensorRegistry.AcSubkey));
        Assert.True(_clients[0].Disposed);
        Assert.True(File.Exists(Path.Combine(_dir, "energy", EnergyCsvStore.StateFileName)));
    }
}
