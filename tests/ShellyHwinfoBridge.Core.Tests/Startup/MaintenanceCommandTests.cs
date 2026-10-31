using System.Runtime.Versioning;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Startup;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Startup;

public class StartupCommandParsingTests
{
    [Theory]
    [InlineData(new string[0], StartupCommand.None)]
    [InlineData(new[] { "--exit" }, StartupCommand.Exit)]
    [InlineData(new[] { "/EXIT" }, StartupCommand.Exit)]
    [InlineData(new[] { "--remove-sensors" }, StartupCommand.RemoveSensors)]
    [InlineData(new[] { "/Remove-Sensors" }, StartupCommand.RemoveSensors)]
    [InlineData(new[] { "--exit", "--remove-sensors" }, StartupCommand.RemoveSensors)]
    [InlineData(new[] { "--remove-sensors", "--exit" }, StartupCommand.RemoveSensors)]
    [InlineData(new[] { "--autostart", "--unknown" }, StartupCommand.None)]
    [InlineData(new[] { "--removesensors", "exit" }, StartupCommand.None)]
    public void ParsesTheCommand(string[] args, StartupCommand expected) =>
        Assert.Equal(expected, StartupArguments.Parse(args).Command);

    [Fact]
    public void CommandsKeepTheOtherSwitches()
    {
        var parsed = StartupArguments.Parse([" --autostart ", "--exit"]);

        Assert.Equal(new StartupArguments(Autostart: true, Minimized: true, StartupCommand.Exit), parsed);
    }

    [Fact]
    public void NullArgumentsParseToDefaults() =>
        Assert.Equal(new StartupArguments(false, false), StartupArguments.Parse(null));
}

public class AutostartEnsureCurrentTests
{
    private const string Exe = @"C:\Program Files\Shelly HWiNFO Bridge\ShellyHwinfoBridge.exe";

    [Fact]
    public void WritesTheEntryWhenMissing()
    {
        var registry = new InMemoryRegistryBackend();
        var manager = new AutostartManager(registry, Exe);

        Assert.False(manager.IsCurrent);
        Assert.True(manager.EnsureCurrent());
        Assert.True(manager.IsCurrent);
        Assert.Equal(manager.CommandLine, registry.GetString(AutostartManager.RunKeyPath, AutostartManager.ValueName));
    }

    [Fact]
    public void RewritesAnEntryForAnotherPath()
    {
        var registry = new InMemoryRegistryBackend();
        new AutostartManager(registry, @"C:\Portable\ShellyHwinfoBridge.exe").Enable();
        var manager = new AutostartManager(registry, Exe);

        Assert.True(manager.EnsureCurrent());
        Assert.Equal($"\"{Exe}\" --autostart", registry.GetString(AutostartManager.RunKeyPath, AutostartManager.ValueName));
    }

    [Fact]
    public void RewritesAnEntryWithoutTheAutostartSwitch()
    {
        var registry = new InMemoryRegistryBackend();
        registry.SetString(AutostartManager.RunKeyPath, AutostartManager.ValueName, $"\"{Exe}\"");
        var manager = new AutostartManager(registry, Exe);

        Assert.False(manager.IsStale);
        Assert.False(manager.IsCurrent);
        Assert.True(manager.EnsureCurrent());
        Assert.True(manager.IsCurrent);
    }

    [Fact]
    public void LeavesACurrentEntryAlone()
    {
        var registry = new InMemoryRegistryBackend();
        var upper = $"\"{Exe.ToUpperInvariant()}\" --autostart";
        registry.SetString(AutostartManager.RunKeyPath, AutostartManager.ValueName, upper);
        var manager = new AutostartManager(registry, Exe);

        Assert.False(manager.EnsureCurrent());
        Assert.Equal(upper, registry.GetString(AutostartManager.RunKeyPath, AutostartManager.ValueName));
    }
}

public class MaintenanceCommandRunnerTests
{
    private const string OtherGroup = @"Software\HWiNFO64\Sensors\Custom\Other";
    private readonly InMemoryRegistryBackend _backend = new();
    private readonly List<TimeSpan> _exitRequests = [];

    public MaintenanceCommandRunnerTests()
    {
        new HwinfoSensorRegistry(_backend).EnsureKeys();
    }

    [Fact]
    public void NoneDoesNothing()
    {
        var runner = CreateRunner(ExitRequestResult.Exited);

        Assert.Equal(MaintenanceCommandRunner.Success, runner.Run(StartupCommand.None));
        Assert.Empty(_exitRequests);
        Assert.True(HasGroup());
    }

    [Theory]
    [InlineData(ExitRequestResult.NotRunning, MaintenanceCommandRunner.Success)]
    [InlineData(ExitRequestResult.Exited, MaintenanceCommandRunner.Success)]
    [InlineData(ExitRequestResult.NotSupported, MaintenanceCommandRunner.InstanceStillRunning)]
    [InlineData(ExitRequestResult.TimedOut, MaintenanceCommandRunner.InstanceStillRunning)]
    public void ExitAsksTheInstanceAndKeepsTheSensors(ExitRequestResult result, int expected)
    {
        var runner = CreateRunner(result);

        Assert.Equal(expected, runner.Run(StartupCommand.Exit));
        Assert.Equal([MaintenanceCommandRunner.ExitTimeout], _exitRequests);
        Assert.True(HasGroup());
    }

    [Theory]
    [InlineData(ExitRequestResult.NotRunning, MaintenanceCommandRunner.Success)]
    [InlineData(ExitRequestResult.Exited, MaintenanceCommandRunner.Success)]
    [InlineData(ExitRequestResult.TimedOut, MaintenanceCommandRunner.InstanceStillRunning)]
    public void RemoveSensorsExitsTheInstanceThenDeletesTheGroup(ExitRequestResult result, int expected)
    {
        _backend.SetString(OtherGroup, "Name", "keep");
        var runner = CreateRunner(result);

        Assert.Equal(expected, runner.Run(StartupCommand.RemoveSensors));
        Assert.Single(_exitRequests);
        Assert.False(HasGroup());
        Assert.True(_backend.KeyExists(OtherGroup));
    }

    [Fact]
    public void RemoveSensorsSucceedsWhenTheGroupIsAlreadyGone()
    {
        var runner = new MaintenanceCommandRunner(_ => ExitRequestResult.NotRunning, new HwinfoSensorRegistry(new InMemoryRegistryBackend()));

        Assert.Equal(MaintenanceCommandRunner.Success, runner.Run(StartupCommand.RemoveSensors));
    }

    [Fact]
    public void RemoveSensorsReportsADeleteFailure()
    {
        var runner = new MaintenanceCommandRunner(_ => ExitRequestResult.NotRunning, new HwinfoSensorRegistry(new DeleteFailingBackend()));

        Assert.Equal(MaintenanceCommandRunner.RemoveFailed, runner.Run(StartupCommand.RemoveSensors));
    }

    private bool HasGroup() =>
        _backend.Keys.Keys.Any(k => k.StartsWith(SensorNaming.BaseKeyPath, StringComparison.OrdinalIgnoreCase));

    private MaintenanceCommandRunner CreateRunner(ExitRequestResult result) =>
        new(
            timeout =>
            {
                _exitRequests.Add(timeout);
                return result;
            },
            new HwinfoSensorRegistry(_backend));

    private sealed class DeleteFailingBackend : IRegistryBackend
    {
        private readonly InMemoryRegistryBackend _inner = new();

        public bool KeyExists(string path) => _inner.KeyExists(path);

        public void CreateKey(string path) => _inner.CreateKey(path);

        public string? GetString(string path, string name) => _inner.GetString(path, name);

        public IReadOnlyDictionary<string, string> GetStringValues(string path) => _inner.GetStringValues(path);

        public IReadOnlyList<string> GetSubKeyNames(string path) => _inner.GetSubKeyNames(path);

        public void SetString(string path, string name, string value) => _inner.SetString(path, name, value);

        public void DeleteValue(string path, string name) => _inner.DeleteValue(path, name);

        public void DeleteTree(string path) => throw new UnauthorizedAccessException("denied");
    }
}

[SupportedOSPlatform("windows")]
public class SingleInstanceExitTests
{
    private static string UniqueName() => "ShellyHwinfoBridge.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void NotRunningWhenThereIsNoPrimary() =>
        Assert.Equal(ExitRequestResult.NotRunning, SingleInstanceCoordinator.RequestExit(TimeSpan.FromSeconds(1), UniqueName()));

    [Fact]
    public void PrimaryThatExitsOnRequestReportsExited()
    {
        var name = UniqueName();
        var primary = new SingleInstanceCoordinator(name);
        var count = 0;
        primary.ExitRequested += (_, _) =>
        {
            Interlocked.Increment(ref count);

            // Dispose off the wait callback, as the app does at the end of its exit sequence.
            Task.Run(primary.Dispose);
        };

        Assert.Equal(ExitRequestResult.Exited, SingleInstanceCoordinator.RequestExit(TimeSpan.FromSeconds(10), name));
        Assert.Equal(1, Volatile.Read(ref count));
        using var next = new SingleInstanceCoordinator(name);
        Assert.True(next.IsPrimary);
    }

    [Fact]
    public void PrimaryThatIgnoresTheRequestTimesOut()
    {
        var name = UniqueName();
        using var primary = new SingleInstanceCoordinator(name);
        using var requested = new ManualResetEventSlim();
        primary.ExitRequested += (_, _) => requested.Set();

        Assert.Equal(ExitRequestResult.TimedOut, SingleInstanceCoordinator.RequestExit(TimeSpan.FromMilliseconds(300), name));
        Assert.True(requested.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(primary.IsPrimary);
    }

    [Fact]
    public void InstanceWithoutExitEventIsNotSupported()
    {
        var name = UniqueName();
        using var olderVersionMutex = new Mutex(false, $@"Local\{name}.SingleInstance");

        Assert.Equal(ExitRequestResult.NotSupported, SingleInstanceCoordinator.RequestExit(TimeSpan.FromSeconds(1), name));
    }

    [Fact]
    public void ActivationDoesNotRaiseExit()
    {
        var name = UniqueName();
        using var primary = new SingleInstanceCoordinator(name);
        using var activated = new ManualResetEventSlim();
        var exits = 0;
        primary.ActivationRequested += (_, _) => activated.Set();
        primary.ExitRequested += (_, _) => Interlocked.Increment(ref exits);

        using (var second = new SingleInstanceCoordinator(name))
        {
            Assert.True(second.SignalPrimary());
        }

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref exits));
    }
}
