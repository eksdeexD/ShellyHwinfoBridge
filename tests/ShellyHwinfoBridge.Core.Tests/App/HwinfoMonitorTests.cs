using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.App;

public class HwinfoMonitorTests
{
    private const uint Power = 5;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 18, 40, 0, TimeSpan.Zero));
    private readonly ProcessOnlySystem _system = new();
    private byte[]? _buffer;
    private int _reads;

    private HwinfoMonitor Create() => new(
        new HwinfoLocator(_system),
        new HwinfoSharedMemoryReader(() =>
        {
            _reads++;
            return _buffer;
        }),
        _time);

    private static byte[] WithShellySensors(double other) => new HwinfoBufferBuilder()
        .Sensor("CPU [#0]: AMD Ryzen 7 7800X3D")
        .Sensor("Shelly Power")
        .Reading(0, Power, "CPU Package Power", "W", 142.3)
        .Reading(1, Power, "AC Power (Wall)", "W", 312.4)
        .Reading(1, Power, "Other Components", "W", other)
        .Build();

    [Fact]
    public void NotRunningSkipsSharedMemory()
    {
        _buffer = WithShellySensors(48.1);
        var monitor = Create();

        monitor.Refresh();

        Assert.Equal(new HwinfoMonitorSnapshot(false, false, false, false, null, null), monitor.Current);
        Assert.Equal(0, _reads);
    }

    [Fact]
    public void RunningWithSensorsVisibleReportsOtherFromHwinfo()
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
        Assert.Equal(48.1, s.OtherWatts!.Value, 6);
        Assert.Equal(@"C:\Program Files\HWiNFO64\HWiNFO64.exe", s.ExecutablePath);
        Assert.Equal(1, changes);

        monitor.Refresh();
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SharedMemoryWithoutBridgeSensorsHasNoOtherValue()
    {
        _system.Running = true;
        _buffer = new HwinfoBufferBuilder().Sensor("GPU [#0]").Reading(0, Power, "GPU Power", "W", 88.6).Build();
        var monitor = Create();

        monitor.Refresh();

        Assert.True(monitor.Current.SharedMemoryAvailable);
        Assert.False(monitor.Current.SensorsVisible);
        Assert.Null(monitor.Current.OtherWatts);
    }

    [Fact]
    public void SharedMemoryOffWhileRunning()
    {
        _system.Running = true;
        var monitor = Create();

        monitor.Refresh();

        Assert.True(monitor.Current.IsRunning);
        Assert.False(monitor.Current.SharedMemoryAvailable);
        Assert.Null(monitor.Current.OtherWatts);
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
        Assert.Null(monitor.Current.OtherWatts);
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
