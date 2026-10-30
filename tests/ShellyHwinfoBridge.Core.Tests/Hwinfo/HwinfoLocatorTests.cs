using ShellyHwinfoBridge.Core.Hwinfo.Locator;

namespace ShellyHwinfoBridge.Core.Tests.Hwinfo;

public class HwinfoLocatorTests
{
    private const string Exe = @"C:\Program Files\HWiNFO64\HWiNFO64.EXE";
    private const string InstallerKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO® 64_is1";

    [Fact]
    public void FindsInstallerEntryByDisplayIcon()
    {
        var system = new FakeSystem { Files = { Exe } };
        system.SetValue(RegistryRoot.LocalMachine64, InstallerKey, "DisplayName", "HWiNFO® 64");
        system.SetValue(RegistryRoot.LocalMachine64, InstallerKey, "DisplayIcon", "\"" + Exe + "\",0");

        var status = new HwinfoLocator(system).Detect();

        Assert.Equal(new HwinfoStatus(true, false, Exe, HwinfoPathSource.UninstallEntry), status);
    }

    [Fact]
    public void FallsBackToInstallLocation()
    {
        var system = new FakeSystem { Files = { @"D:\Apps\HWiNFO\HWiNFO64.exe" } };
        var key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}";
        system.SetValue(RegistryRoot.LocalMachine32, key, "DisplayName", "HWiNFO64 Version 8.10");
        system.SetValue(RegistryRoot.LocalMachine32, key, "InstallLocation", @"D:\Apps\HWiNFO\");

        Assert.Equal(@"D:\Apps\HWiNFO\HWiNFO64.exe", new HwinfoLocator(system).ExePath);
    }

    [Fact]
    public void IgnoresOtherProductsAndThe32BitEdition()
    {
        var system = new FakeSystem { Files = { @"C:\HWiNFO32\HWiNFO32.exe" } };
        system.SetValue(RegistryRoot.LocalMachine64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO32_is1", "InstallLocation", @"C:\HWiNFO32");
        system.SetValue(RegistryRoot.LocalMachine64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\7-Zip", "DisplayName", "7-Zip");

        var status = new HwinfoLocator(system).Detect();

        Assert.False(status.IsInstalled);
        Assert.Null(status.ExecutablePath);
    }

    [Fact]
    public void RunningProcessPathWinsOverRegistry()
    {
        const string portable = @"E:\Portable\HWiNFO64.exe";
        var system = new FakeSystem { Files = { Exe } };
        system.SetValue(RegistryRoot.LocalMachine64, InstallerKey, "DisplayIcon", Exe);
        system.Processes.Add(new RunningProcess(42, portable));

        var status = new HwinfoLocator(system).Detect();

        Assert.True(status.IsRunning);
        Assert.Equal(portable, status.ExecutablePath);
        Assert.Equal(HwinfoPathSource.RunningProcess, status.PathSource);
    }

    [Fact]
    public void EntryWithMissingFileStillCountsAsInstalled()
    {
        var system = new FakeSystem();
        system.SetValue(RegistryRoot.CurrentUser, InstallerKey, "DisplayIcon", Exe);

        Assert.Equal(new HwinfoStatus(true, false, null, HwinfoPathSource.None), new HwinfoLocator(system).Detect());
    }

    [Fact]
    public void UserSettingsKeyIsEvidenceOnlyWithHwinfosOwnContent()
    {
        var system = new FakeSystem();
        system.AddKey(RegistryRoot.CurrentUser, @"Software\HWiNFO64\Sensors");
        Assert.False(new HwinfoLocator(system).IsInstalled);

        system.AddKey(RegistryRoot.CurrentUser, @"Software\HWiNFO64\OSD");
        Assert.True(new HwinfoLocator(system).IsInstalled);
    }

    [Fact]
    public void UserSettingsPathValueIsUsed()
    {
        const string portable = @"E:\Tools\HWiNFO64.exe";
        var system = new FakeSystem { Files = { portable } };
        system.SetValue(RegistryRoot.CurrentUser, @"Software\HWiNFO64", "SomePath", portable);

        Assert.Equal(new HwinfoStatus(true, false, portable, HwinfoPathSource.UserSettings), new HwinfoLocator(system).Detect());
    }

    [Theory]
    [InlineData("\"C:\\A B\\HWiNFO64.exe\",0", "C:\\A B\\HWiNFO64.exe")]
    [InlineData("C:\\A\\HWiNFO64.exe,0", "C:\\A\\HWiNFO64.exe")]
    [InlineData("C:\\A\\HWiNFO64.exe", "C:\\A\\HWiNFO64.exe")]
    [InlineData("  ", null)]
    public void ParsesDisplayIcon(string value, string? expected) => Assert.Equal(expected, HwinfoLocator.ParseIconPath(value));

    [Fact]
    public async Task RestartClosesGracefullyThenRelaunches()
    {
        var system = new FakeSystem { Files = { Exe } };
        system.Processes.Add(new RunningProcess(7, Exe));

        var result = await new HwinfoLocator(system).RestartAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal(HwinfoRestartResult.Restarted, result);
        Assert.Equal(["close 7", "wait 7", "start " + Exe], system.Log);
    }

    [Fact]
    public async Task RestartKillsWhenCloseIsIgnored()
    {
        var system = new FakeSystem { CloseWorks = false };
        system.Processes.Add(new RunningProcess(7, Exe));

        var result = await new HwinfoLocator(system).RestartAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal(HwinfoRestartResult.Restarted, result);
        Assert.Equal(["close 7", "kill 7", "wait 7", "start " + Exe], system.Log);
    }

    [Fact]
    public async Task RestartReportsWhenItCannotClose()
    {
        var system = new FakeSystem { CloseWorks = false, KillWorks = false };
        system.Processes.Add(new RunningProcess(7, Exe));

        Assert.Equal(HwinfoRestartResult.CouldNotClose, await new HwinfoLocator(system).RestartAsync(TimeSpan.FromMilliseconds(1)));
        Assert.DoesNotContain(system.Log, l => l.StartsWith("start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartDoesNothingWithoutProcessOrPath()
    {
        Assert.Equal(HwinfoRestartResult.NotRunning, await new HwinfoLocator(new FakeSystem()).RestartAsync());

        var system = new FakeSystem();
        system.Processes.Add(new RunningProcess(7, null));
        Assert.Equal(HwinfoRestartResult.ExecutableNotFound, await new HwinfoLocator(system).RestartAsync());
        Assert.Empty(system.Log);
    }

    [Fact]
    public async Task RestartReportsFailedRelaunch()
    {
        var system = new FakeSystem { StartWorks = false };
        system.Processes.Add(new RunningProcess(7, Exe));

        Assert.Equal(HwinfoRestartResult.RelaunchFailed, await new HwinfoLocator(system).RestartAsync());
    }

    private sealed class FakeSystem : IHwinfoSystem
    {
        private readonly Dictionary<(RegistryRoot, string), Dictionary<string, string>> _keys = [];

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RunningProcess> Processes { get; } = [];

        public List<string> Log { get; } = [];

        public bool CloseWorks { get; set; } = true;

        public bool KillWorks { get; set; } = true;

        public bool StartWorks { get; set; } = true;

        public void AddKey(RegistryRoot root, string path)
        {
            var parts = path.Split('\\');
            for (var i = 1; i <= parts.Length; i++)
            {
                _keys.TryAdd((root, string.Join('\\', parts[..i]).ToLowerInvariant()), new(StringComparer.OrdinalIgnoreCase));
            }
        }

        public void SetValue(RegistryRoot root, string path, string name, string value)
        {
            AddKey(root, path);
            _keys[(root, path.ToLowerInvariant())][name] = value;
        }

        public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path)
        {
            var prefix = path.ToLowerInvariant() + "\\";
            return _keys.Keys
                .Where(k => k.Item1 == root && k.Item2.StartsWith(prefix, StringComparison.Ordinal) && !k.Item2[prefix.Length..].Contains('\\'))
                .Select(k => k.Item2[prefix.Length..])
                .ToList();
        }

        public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path) =>
            _keys.TryGetValue((root, path.ToLowerInvariant()), out var v) ? v.Keys.ToList() : [];

        public string? GetString(RegistryRoot root, string path, string name) =>
            _keys.TryGetValue((root, path.ToLowerInvariant()), out var v) && v.TryGetValue(name, out var s) ? s : null;

        public bool FileExists(string path) => Files.Contains(path);

        public IReadOnlyList<RunningProcess> GetProcesses(string processName) =>
            processName.Equals("HWiNFO64", StringComparison.OrdinalIgnoreCase) ? Processes.ToList() : [];

        public bool RequestClose(int processId)
        {
            Log.Add($"close {processId}");
            return CloseWorks;
        }

        public Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Log.Add($"wait {processId}");
            return Task.FromResult(true);
        }

        public bool Kill(int processId)
        {
            Log.Add($"kill {processId}");
            return KillWorks;
        }

        public bool Start(string executablePath)
        {
            Log.Add($"start {executablePath}");
            return StartWorks;
        }
    }
}
