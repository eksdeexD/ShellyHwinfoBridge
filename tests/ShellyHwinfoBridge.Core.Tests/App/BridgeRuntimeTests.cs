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
    private const string Base = SensorNaming.BaseKeyPath;
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
    private readonly FakeComponents _components = new();
    private readonly Dictionary<string, ShellyReading> _readingsByHost = new(StringComparer.OrdinalIgnoreCase);

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
                if (_readingsByHost.TryGetValue(host, out var reading))
                {
                    client.Reading = reading;
                }

                lock (_clients)
                {
                    _clients.Add(client);
                }

                return client;
            },
            _time,
            components: _components);
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

    private string? Name(string subkey) => _backend.GetString(Base + "\\" + subkey, "Name");

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
        Assert.Equal("312.4", Value("Power0"));
        Assert.Equal("1", Value("Other2"));
        Assert.Equal(SensorValueFormatter.FormatWatts(reading.Power.DcWatts - 250), Value("Power3"));
        Assert.Equal(OtherSource.SharedMemory, runtime.Other.Source);
        Assert.Equal("PC Power (AC)", Name("Power0"));
        Assert.Equal("0.00", Value("Other0"));
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
        Assert.Equal("0", Value("Other2"));

        await NextReading(runtime, () =>
        {
            runtime.Resume();
            _time.Advance(TimeSpan.FromSeconds(1));
        });
        Assert.Equal(RuntimeState.Running, runtime.State);
        Assert.Equal("1", Value("Other2"));
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
        Assert.NotNull(Value("Power0"));
        Assert.NotNull(Value("Power3"));
        Assert.Equal("PC Other Components", Name("Power3"));
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
        Assert.NotNull(Value("Power3"));
        Assert.Equal("PC Other Components", Name("Power3"));
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

        Assert.Equal("0", Value("Other2"));
        Assert.Equal("0.0", Value("Power0"));
        Assert.True(_clients[0].Disposed);
        Assert.True(File.Exists(Path.Combine(_dir, "energy", EnergyCsvStore.StateFileName)));
    }

    private const string MonitorId = "11111111-1111-1111-1111-111111111111";
    private const string LampId = "22222222-2222-2222-2222-222222222222";

    private static AppSettings MultiSettings(bool lampIncluded = false)
    {
        var settings = ExampleSettings();
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = MonitorId, Label = "Monitor", Host = "192.168.1.50" });
        settings.AdditionalDevices.Add(new AdditionalDeviceSettings { Id = LampId, Label = "Lamp", Host = "192.168.1.51", IncludeInTotals = lampIncluded });
        return settings;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static Task<PowerReadingEventArgs> NextReadingFrom(BridgeRuntime runtime, string deviceId, Action trigger)
    {
        var tcs = new TaskCompletionSource<PowerReadingEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, PowerReadingEventArgs e)
        {
            if (e.DeviceId == deviceId)
            {
                runtime.ReadingReceived -= Handler;
                tcs.TrySetResult(e);
            }
        }

        runtime.ReadingReceived += Handler;
        trigger();
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AllDevicesArePolledStaggeredAndTotalSumsIncludedDevices()
    {
        _readingsByHost["192.168.1.50"] = new ShellyReading(31.2, 230, 0.2, 500);
        _readingsByHost["192.168.1.51"] = new ShellyReading(10, 230, 0.05, 100);
        var runtime = Create(MultiSettings());
        _registry.EnsureKeys();

        await NextReadingFrom(runtime, AppSettings.PrimaryDeviceId, runtime.Start);
        Assert.Equal(3, _clients.Count);

        // Three devices on a 1000 ms interval: the monitor starts at 333 ms, the lamp at 666 ms.
        Assert.Equal("0", Value("Other3"));
        await Task.Delay(50);
        var monitor = NextReadingFrom(runtime, MonitorId, () => _time.Advance(TimeSpan.FromMilliseconds(340)));
        await monitor;
        Assert.Equal("0", Value("Other4"));
        await Task.Delay(50);
        await NextReadingFrom(runtime, LampId, () => _time.Advance(TimeSpan.FromMilliseconds(340)));

        Assert.Equal("Monitor Power (AC)", Name("Power4"));
        Assert.Equal("31.2", Value("Power4"));
        Assert.Equal("10.0", Value("Power5"));
        Assert.Equal("Total Power (AC)", Name("Power6"));
        Assert.Equal("343.6", Value("Power6"));
        Assert.Equal("1", Value("Other3"));
        Assert.Equal("1", Value("Other4"));
        Assert.Equal(343.6, runtime.TotalWatts!.Value, 6);
        Assert.Equal([AppSettings.PrimaryDeviceId, MonitorId, LampId], runtime.Devices.Select(d => d.Id));
        Assert.Equal(31.2, runtime.Devices[1].AcWatts!.Value, 6);
    }

    [Fact]
    public async Task AnOfflineAdditionalDeviceNeverAffectsThePc()
    {
        var runtime = Create(MultiSettings());
        _registry.EnsureKeys();
        await NextReadingFrom(runtime, AppSettings.PrimaryDeviceId, runtime.Start);
        await Task.Delay(50);
        await NextReadingFrom(runtime, MonitorId, () => _time.Advance(TimeSpan.FromMilliseconds(340)));
        var monitorClient = _clients.Single(c => c.Host == "192.168.1.50");
        monitorClient.Error = new ShellyException("unreachable");

        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(30);
            _time.Advance(TimeSpan.FromMilliseconds(500));
        }

        await WaitUntil(() => runtime.Devices[1].State == OnlineState.Offline);
        await WaitUntil(() => Value("Other3") == "0");
        Assert.Equal("0.0", Value("Power4"));
        Assert.Equal(OnlineState.Online, runtime.OnlineState);
        Assert.Equal(RuntimeState.Running, runtime.State);
        await WaitUntil(() => Value("Other2") == "1");
        Assert.Equal("312.4", Value("Power0"));
    }

    [Fact]
    public async Task AddingAndRemovingDevicesChangesTheLayoutAndRaisesTheRestartNotice()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);
        var notices = 0;
        runtime.SensorKeysCreated += (_, _) => notices++;

        var outcome = runtime.Apply(MultiSettings());
        Assert.Contains("AdditionalDevices", outcome.ChangedKeys);
        Assert.Equal(1, notices);
        Assert.Equal("Lamp Online", Name("Other4"));
        Assert.Equal(3, _clients.Count);

        var fewer = runtime.Settings!.Clone();
        fewer.AdditionalDevices.RemoveAt(0);
        runtime.Apply(fewer);
        Assert.Equal(2, notices);
        Assert.Equal("Lamp Power (AC)", Name("Power4"));
        Assert.Equal("Total Power (AC)", Name("Power5"));
        Assert.Null(Name("Power6"));
        Assert.Null(Name("Other4"));
        await WaitUntil(() => _clients.Single(c => c.Host == "192.168.1.50").Disposed);

        // Changing nothing that affects the layout raises no notice.
        var same = runtime.Settings!.Clone();
        same.PollIntervalMs = 2000;
        runtime.Apply(same);
        Assert.Equal(2, notices);
    }

    [Fact]
    public void CustomSensorNamesAreWrittenAndRaiseTheNotice()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        runtime.Start();
        var notices = 0;
        runtime.SensorKeysCreated += (_, _) => notices++;

        var renamed = runtime.Settings!.Clone();
        renamed.SensorNames[SensorKey.PcAc.Id] = "Wall Power";
        var outcome = runtime.Apply(renamed);

        Assert.Contains("SensorNames", outcome.ChangedKeys);
        Assert.Equal("Wall Power", Name("Power0"));
        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task OtherComponentsHoldsTheLastValueWhenAComponentIsMissingOrTheResultIsNegative()
    {
        _registry.EnsureKeys();
        var runtime = Create(ExampleSettings());
        var first = await NextReading(runtime, runtime.Start);
        var expected = SensorValueFormatter.FormatWatts(first.Power.DcWatts - 250);
        Assert.Equal(expected, Value("Power3"));

        // A missing component: the value is held.
        _components.Values["GPU Power"] = null;
        await NextReading(runtime, () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(expected, Value("Power3"));

        // A spike makes the result negative: held too.
        _components.Values["GPU Power"] = 900;
        await NextReading(runtime, () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(expected, Value("Power3"));

        _components.Values["GPU Power"] = 50;
        var fresh = await NextReading(runtime, () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(SensorValueFormatter.FormatWatts(fresh.Power.DcWatts - 150), Value("Power3"));
    }

    [Fact]
    public async Task OtherComponentsStaysZeroWithoutASource()
    {
        _registry.EnsureKeys();
        _components.Source = OtherSource.None;
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);

        Assert.Equal("0.0", Value("Power3"));
        Assert.Equal(OtherSource.None, runtime.Other.Source);
        Assert.False(runtime.Other.HasValue);
    }

    [Fact]
    public async Task AdditionalDevicePasswordsAreProtectedAndUsed()
    {
        var runtime = Create(ExampleSettings());
        await NextReading(runtime, runtime.Start);

        runtime.Apply(MultiSettings(), devicePasswords: new Dictionary<string, string> { [MonitorId] = "mon" });

        Assert.Equal("mon", runtime.GetDevicePassword(MonitorId));
        Assert.Equal("enc:nom", _store.Load()!.FindAdditionalDevice(MonitorId)!.ProtectedPassword);
        Assert.Equal("mon", _clients.Single(c => c.Host == "192.168.1.50").Password);
        Assert.Null(runtime.GetDevicePassword(LampId));
    }

    [Fact]
    public void StaggerOffsetsSpreadDevicesOverTheInterval()
    {
        var interval = TimeSpan.FromMilliseconds(1000);
        Assert.Equal(TimeSpan.Zero, BridgeRuntime.StaggerOffset(0, 4, interval));
        Assert.Equal(TimeSpan.FromMilliseconds(250), BridgeRuntime.StaggerOffset(1, 4, interval));
        Assert.Equal(TimeSpan.FromMilliseconds(750), BridgeRuntime.StaggerOffset(3, 4, interval));
        Assert.Equal(TimeSpan.Zero, BridgeRuntime.StaggerOffset(0, 1, interval));
    }

    private sealed class FakeComponents : IComponentSensorSource
    {
        public OtherSource Source { get; set; } = OtherSource.SharedMemory;

        public Dictionary<string, double?> Values { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CPU Package Power"] = 100,
            ["GPU Power"] = 150,
            ["DIMM1 Power"] = 5,
        };

        public ComponentReadings Read(IReadOnlyList<SensorSelection> sensors) =>
            Source == OtherSource.None
                ? new ComponentReadings(OtherSource.None, [], true, sensors.Select(s => s.Name).ToList())
                : new ComponentReadings(Source, sensors.Select(s => Values.GetValueOrDefault(s.Name)).ToList(), true, []);
    }
}
