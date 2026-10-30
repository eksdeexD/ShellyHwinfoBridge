using System.Runtime.Versioning;
using ShellyHwinfoBridge.Core.Startup;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Startup;

public class AutostartManagerTests
{
    private const string Exe = @"C:\Tools\Shelly Bridge\ShellyHwinfoBridge.exe";

    [Fact]
    public void EnableWritesQuotedPathWithAutostartSwitch()
    {
        var registry = new InMemoryRegistryBackend();
        var manager = new AutostartManager(registry, Exe);

        Assert.False(manager.IsEnabled);
        manager.Enable();

        Assert.True(manager.IsEnabled);
        Assert.False(manager.IsStale);
        Assert.Equal(
            "\"C:\\Tools\\Shelly Bridge\\ShellyHwinfoBridge.exe\" --autostart",
            registry.GetString(@"Software\Microsoft\Windows\CurrentVersion\Run", "ShellyHwinfoBridge"));
    }

    [Fact]
    public void DisableRemovesOnlyOurValue()
    {
        var registry = new InMemoryRegistryBackend();
        registry.SetString(AutostartManager.RunKeyPath, "OtherApp", "other.exe");
        var manager = new AutostartManager(registry, Exe);
        manager.Apply(true);

        manager.Apply(false);
        manager.Disable();

        Assert.False(manager.IsEnabled);
        Assert.Equal("other.exe", registry.GetString(AutostartManager.RunKeyPath, "OtherApp"));
    }

    [Fact]
    public void DetectsStaleEntryAfterExeMoved()
    {
        var registry = new InMemoryRegistryBackend();
        new AutostartManager(registry, @"C:\Old\ShellyHwinfoBridge.exe").Enable();
        var manager = new AutostartManager(registry, "\"" + Exe + "\"");

        Assert.True(manager.IsStale);
        manager.Enable();
        Assert.False(manager.IsStale);
    }

    [Theory]
    [InlineData("\"C:\\A B\\x.exe\" --autostart", "C:\\A B\\x.exe")]
    [InlineData("C:\\A\\x.exe --autostart", "C:\\A\\x.exe")]
    [InlineData("C:\\A\\x.exe", "C:\\A\\x.exe")]
    public void ExtractsExecutableFromCommandLine(string commandLine, string expected) =>
        Assert.Equal(expected, AutostartManager.ExtractExecutable(commandLine));

    [Theory]
    [InlineData(new string[0], false, false)]
    [InlineData(new[] { "--autostart" }, true, true)]
    [InlineData(new[] { "/AUTOSTART" }, true, true)]
    [InlineData(new[] { "--minimized", "--unknown" }, false, true)]
    public void ParsesStartupArguments(string[] args, bool autostart, bool minimized) =>
        Assert.Equal(new StartupArguments(autostart, minimized), StartupArguments.Parse(args));
}

[SupportedOSPlatform("windows")]
public class SingleInstanceCoordinatorTests
{
    private static string UniqueName() => "ShellyHwinfoBridge.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void SecondInstanceIsNotPrimaryAndSignalsTheFirst()
    {
        var name = UniqueName();
        using var first = new SingleInstanceCoordinator(name);
        using var activated = new ManualResetEventSlim();
        var count = 0;
        first.ActivationRequested += (_, _) =>
        {
            Interlocked.Increment(ref count);
            activated.Set();
        };

        using (var second = new SingleInstanceCoordinator(name))
        {
            Assert.True(first.IsPrimary);
            Assert.False(second.IsPrimary);
            Assert.True(second.SignalPrimary());
        }

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref count));
        Assert.False(first.SignalPrimary());
    }

    [Fact]
    public void RepeatedLaunchesEachRaiseTheEvent()
    {
        var name = UniqueName();
        using var first = new SingleInstanceCoordinator(name);
        using var signal = new SemaphoreSlim(0);
        first.ActivationRequested += (_, _) => signal.Release();

        for (var i = 0; i < 3; i++)
        {
            using var other = new SingleInstanceCoordinator(name);
            Assert.True(other.SignalPrimary());
            Assert.True(signal.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void NewInstanceBecomesPrimaryAfterTheFirstExits()
    {
        var name = UniqueName();
        new SingleInstanceCoordinator(name).Dispose();

        using var next = new SingleInstanceCoordinator(name);
        Assert.True(next.IsPrimary);
    }
}
