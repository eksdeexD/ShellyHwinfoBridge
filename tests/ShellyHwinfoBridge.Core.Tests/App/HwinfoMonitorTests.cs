using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.App;

public class HwinfoMonitorTests
{
    private const uint Power = 5;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 18, 40, 0, TimeSpan.Zero));
    private readonly ProcessOnlySystem _system = new();
    private readonly InMemoryRegistryBackend _registry = new();
    private byte[]? _buffer;
    private int _reads;

    private HwinfoMonitor Create() => new(
        new HwinfoLocator(_system),
        new HwinfoSharedMemoryReader(() =>
        {
            _reads++;
            return _buffer;
        }),
        _time,
        gadget: new HwinfoGadgetRegistryReader(_registry));

    private static byte[] WithShellySensors(double other) => new HwinfoBufferBuilder()
        .Sensor("CPU [#0]: AMD Ryzen 7 7800X3D")
        .Sensor("Shelly Power")
        .Reading(0, Power, "CPU Package Power", "W", 142.3)
        .Reading(1, Power, "PC Power (AC)", "W", 312.4)
        .Reading(1, Power, "PC Other Components", "W", other)
        .Build();

    private static readonly IReadOnlyList<SensorSelection> Selected =
    [
        new SensorSelection { Name = "CPU Package Power", Category = SensorCategory.Cpu },
        new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu },
    ];

    private void Export(int index, string sensor, string label, string raw)
    {
        var n = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _registry.SetString(HwinfoGadgetRegistryReader.DefaultKeyPath, "Sensor" + n, sensor);
        _registry.SetString(HwinfoGadgetRegistryReader.DefaultKeyPath, "Label" + n, label);
        _registry.SetString(HwinfoGadgetRegistryReader.DefaultKeyPath, "Value" + n, raw + " W");
        _registry.SetString(HwinfoGadgetRegistryReader.DefaultKeyPath, "ValueRaw" + n, raw);
    }

    [Fact]
    public void NotRunningSkipsSharedMemoryAndReadsNoComponents()
    {
        _buffer = WithShellySensors(48.1);
        var monitor = Create();

        monitor.Refresh();

        Assert.Equal(new HwinfoMonitorSnapshot(false, false, false, false, null), monitor.Current);
        Assert.Equal(0, _reads);
        var components = monitor.Read(Selected);
        Assert.False(components.HwinfoRunning);
        Assert.Equal(OtherSource.None, components.Source);
        Assert.Equal(0, _reads);
    }

    [Fact]
    public void RunningWithSharedMemoryReadsTheSelectedComponents()
    {
        _system.Running = true;
        _buffer = WithShellySensors(48.1);
        var monitor = Create();
        var changes = 0;
        monitor.Changed += (_, _) => changes++;

        monitor.Refresh();

        var s = monitor.Current;
        Assert.True(s.IsInstalled);
        Assert.True(s.IsRunning);
        Assert.True(s.SharedMemoryAvailable);
        Assert.True(s.SensorsVisible);
        Assert.Equal(OtherSource.SharedMemory, s.ComponentSource);
        Assert.Equal(@"C:\Program Files\HWiNFO64\HWiNFO64.exe", s.ExecutablePath);
        Assert.Equal(1, changes);

        var components = monitor.Read(Selected);
        Assert.Equal(OtherSource.SharedMemory, components.Source);
        Assert.Equal(142.3, components.Values[0]!.Value, 6);
        Assert.Null(components.Values[1]);
        Assert.Empty(components.MissingGadgetSensors);

        monitor.Refresh();
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SharedMemoryWithoutBridgeSensorsIsNotVisible()
    {
        _system.Running = true;
        _buffer = new HwinfoBufferBuilder().Sensor("GPU [#0]").Reading(0, Power, "GPU Power", "W", 88.6).Build();
        var monitor = Create();

        monitor.Refresh();

        Assert.True(monitor.Current.SharedMemoryAvailable);
        Assert.False(monitor.Current.SensorsVisible);
        Assert.Equal(88.6, monitor.Read(Selected).Values[1]!.Value, 6);
    }

    [Fact]
    public void SharedMemoryOffWhileRunning()
    {
        _system.Running = true;
        var monitor = Create();

        monitor.Refresh();

        Assert.True(monitor.Current.IsRunning);
        Assert.False(monitor.Current.SharedMemoryAvailable);
        Assert.Equal(OtherSource.None, monitor.Current.ComponentSource);
    }

    [Fact]
    public void SharedMemoryIsPreferredOverTheGadgetExport()
    {
        _system.Running = true;
        _buffer = WithShellySensors(48.1);
        Export(0, "CPU [#0]: AMD Ryzen 7 7800X3D", "CPU Package Power", "99.0");
        var monitor = Create();
        monitor.Refresh();

        var components = monitor.Read(Selected);
        Assert.Equal(OtherSource.SharedMemory, components.Source);
        Assert.Equal(142.3, components.Values[0]!.Value, 6);
    }

    [Fact]
    public void FallsBackToTheGadgetExportWhenItReportsEverySelectedSensor()
    {
        _system.Running = true;
        Export(0, "CPU [#0]: AMD Ryzen 7 7800X3D", "CPU Package Power", "140.5");
        Export(3, "GPU [#0]: NVIDIA GeForce RTX 4080", "GPU Power", "88.0");
        Export(4, "Shelly Power", "PC Power (AC)", "312.4");
        var monitor = Create();

        monitor.Refresh();

        var s = monitor.Current;
        Assert.False(s.SharedMemoryAvailable);
        Assert.True(s.SensorsVisible);
        Assert.Equal(OtherSource.Gadget, s.ComponentSource);
        var components = monitor.Read(Selected);
        Assert.Equal(OtherSource.Gadget, components.Source);
        Assert.Equal([140.5, 88.0], components.Values.Select(v => v!.Value));
    }

    [Fact]
    public void AnIncompleteGadgetExportIsNoSourceAndNamesTheMissingSensors()
    {
        _system.Running = true;
        Export(0, "CPU [#0]: AMD Ryzen 7 7800X3D", "CPU Package Power", "140.5");
        var monitor = Create();
        monitor.Refresh();

        var components = monitor.Read(Selected);

        Assert.Equal(OtherSource.None, components.Source);
        Assert.True(components.HwinfoRunning);
        Assert.Equal(["GPU Power"], components.MissingGadgetSensors);
    }

    [Fact]
    public void TheBridgeOwnReadingsAreNeverUsedAsComponents()
    {
        _system.Running = true;
        Export(0, "Shelly Power", "GPU Power", "1.0");
        Export(1, "CPU [#0]", "CPU Package Power", "140.5");
        var monitor = Create();
        monitor.Refresh();

        Assert.Equal(["GPU Power"], monitor.Read(Selected).MissingGadgetSensors);
    }

    [Fact]
    public void IgnoresAStaleGadgetExportWhileHwinfoIsNotRunning()
    {
        Export(0, "CPU [#0]", "CPU Package Power", "140.5");
        Export(1, "GPU [#0]", "GPU Power", "88.0");
        var monitor = Create();

        monitor.Refresh();

        Assert.Equal(OtherSource.None, monitor.Current.ComponentSource);
        Assert.Equal(OtherSource.None, monitor.Read(Selected).Source);
    }

    [Fact]
    public void NoSourceWithoutSharedMemoryOrExport()
    {
        _system.Running = true;
        var monitor = Create();

        monitor.Refresh();

        Assert.Equal(OtherSource.None, monitor.Current.ComponentSource);
        Assert.Equal(["CPU Package Power", "GPU Power"], monitor.Read(Selected).MissingGadgetSensors);
    }

    [Fact]
    public void ReadsSharedMemoryEveryTwoSecondsWhenActiveAndFiveWhenIdle()
    {
        _system.Running = true;
        _buffer = WithShellySensors(48.1);
        var monitor = Create();

        monitor.Tick();
        Assert.Equal(1, _reads);
        var processChecks = _system.ProcessCalls;

        _time.Advance(TimeSpan.FromSeconds(1));
        monitor.Tick();
        Assert.Equal(1, _reads);

        _time.Advance(TimeSpan.FromSeconds(1));
        monitor.Tick();
        Assert.Equal(2, _reads);
        Assert.Equal(processChecks, _system.ProcessCalls);

        monitor.Active = false;
        for (var i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            monitor.Tick();
        }

        Assert.Equal(2, _reads);
        _time.Advance(TimeSpan.FromSeconds(1));
        monitor.Tick();
        Assert.Equal(3, _reads);

        // The process list was checked once more after 5 s.
        Assert.True(_system.ProcessCalls > processChecks);
    }

    [Fact]
    public void ProcessExitIsNoticedWithinFiveSeconds()
    {
        _system.Running = true;
        _buffer = WithShellySensors(48.1);
        var monitor = Create();
        monitor.Tick();
        Assert.True(monitor.Current.SensorsVisible);

        _system.Running = false;
        _time.Advance(HwinfoMonitor.ProcessInterval);
        monitor.Tick();

        Assert.False(monitor.Current.IsRunning);
        Assert.False(monitor.Current.SharedMemoryAvailable);
        Assert.Equal(OtherSource.None, monitor.Current.ComponentSource);
    }

    private sealed class ProcessOnlySystem : IHwinfoSystem
    {
        public bool Running { get; set; }

        public int ProcessCalls { get; private set; }

        public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path) => [];

        public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path) => [];

        public string? GetString(RegistryRoot root, string path, string name) => null;

        public bool FileExists(string path) => false;

        public IReadOnlyList<RunningProcess> GetProcesses(string processName)
        {
            ProcessCalls++;
            return Running ? [new RunningProcess(42, @"C:\Program Files\HWiNFO64\HWiNFO64.exe")] : [];
        }

        public bool RequestClose(int processId) => false;

        public Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(false);

        public bool Kill(int processId) => false;

        public bool Start(string executablePath) => false;
    }
}
