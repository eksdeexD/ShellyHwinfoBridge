using System.Globalization;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;

namespace ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

public class ShellyStepModelTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static ShellyStepModel Model(Func<string, string?, CancellationToken, Task<ConnectionTestResult>> test, params DiscoveredShelly[] devices) =>
        new(_ => Task.FromResult<IReadOnlyList<DiscoveredShelly>>(devices), test, EnUs);

    [Fact]
    public async Task DiscoveryListsPlugsAndSelectingOneFillsTheHost()
    {
        var plug = WizardFixture.Plug("PlugMGen3-2", "192.168.1.42");
        var m = Model((_, _, _) => Task.FromResult(TestResults.Ok()), plug);
        Assert.Equal(string.Empty, m.DeviceListEmptyText);

        await m.DiscoverAsync();
        Assert.True(m.HasDiscovered);
        Assert.Equal("1 plug found.", m.DiscoveryStatusText);
        Assert.Equal(-1, m.SelectedDeviceIndex);

        m.SelectDevice(plug);
        Assert.Equal("192.168.1.42", m.Host);
        Assert.Equal(0, m.SelectedDeviceIndex);
        Assert.Equal("PlugMGen3-2", m.DeviceName);
        Assert.Equal("S3PL-10112EU", m.DeviceModel);
    }

    [Fact]
    public async Task SetupQuestionLeadsToTheGuideOrToDiscovery()
    {
        var discoveries = 0;
        var m = new ShellyStepModel(
            _ =>
            {
                discoveries++;
                return Task.FromResult<IReadOnlyList<DiscoveredShelly>>([WizardFixture.Plug("PlugMGen3-2", "192.168.1.42")]);
            },
            (_, _, _) => Task.FromResult(TestResults.Ok()),
            EnUs);
        var changes = 0;
        m.Changed += (_, _) => changes++;

        Assert.Equal(ShellySetupMode.Unanswered, m.SetupMode);
        Assert.False(m.IsReady);
        Assert.False(m.ShouldDiscoverOnShow);

        m.ShowGuide();
        Assert.Equal(ShellySetupMode.Guide, m.SetupMode);
        Assert.False(m.ShouldDiscoverOnShow);
        Assert.Equal(0, discoveries);
        Assert.Equal(1, changes);
        m.ShowGuide();
        Assert.Equal(1, changes);

        // "Done, search now" switches to discovery and searches.
        await m.FindAsync();
        Assert.Equal(ShellySetupMode.Ready, m.SetupMode);
        Assert.True(m.IsReady);
        Assert.Equal(1, discoveries);
        Assert.Single(m.Devices);
        Assert.False(m.ShouldDiscoverOnShow);
    }

    [Fact]
    public void YesStartsDiscoveryAndAConfiguredHostSkipsTheQuestion()
    {
        var m = Model((_, _, _) => Task.FromResult(TestResults.Ok()));
        var pending = new TaskCompletionSource<IReadOnlyList<DiscoveredShelly>>();
        var slow = new ShellyStepModel(_ => pending.Task, (_, _, _) => Task.FromResult(TestResults.Ok()), EnUs);

        var find = slow.FindAsync();
        Assert.True(slow.IsReady);
        Assert.True(slow.IsDiscovering);
        Assert.False(slow.ShouldDiscoverOnShow);
        pending.SetResult([]);
        Assert.True(find.IsCompleted);

        m.LoadFrom(new ShellyDeviceSettings(), null);
        Assert.Equal(ShellySetupMode.Unanswered, m.SetupMode);
        m.LoadFrom(new ShellyDeviceSettings { Host = "192.168.1.42" }, null);
        Assert.Equal(ShellySetupMode.Ready, m.SetupMode);
        Assert.True(m.ShouldDiscoverOnShow);
    }

    [Fact]
    public void SetupGuideHasTheSixSpecStepsWithBoldNames()
    {
        Assert.Equal(6, ShellyStepModel.GuideSteps.Count);
        Assert.Contains(new TextRun("ShellyPlugSG3-xxxxxxxxxxxx", true), InlineMarkup.Parse(ShellyStepModel.GuideSteps[0]));
        Assert.Contains(new TextRun("http://<ip>", true), InlineMarkup.Parse(ShellyStepModel.GuideSteps[3]));
        Assert.Contains(new TextRun("Settings › Wi-Fi › Access Point", true), InlineMarkup.Parse(ShellyStepModel.GuideSteps[3]));
        Assert.Contains(new TextRun("TCP port 80", true), InlineMarkup.Parse(ShellyStepModel.GuideSteps[4]));
        Assert.Equal("Plug the PC's power cable into the Shelly.", ShellyStepModel.GuideSteps[5]);
        Assert.All(ShellyStepModel.GuideSteps, s => Assert.DoesNotContain("**", InlineMarkup.Strip(s), StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedDiscoveryShowsTheEmptyText()
    {
        var m = new ShellyStepModel(_ => Task.FromException<IReadOnlyList<DiscoveredShelly>>(new IOException("no network")), (_, _, _) => Task.FromResult(TestResults.Ok()), EnUs);
        await m.DiscoverAsync();
        Assert.Empty(m.Devices);
        Assert.Equal("No plugs found. Type the IP address on the right.", m.DeviceListEmptyText);
    }

    [Fact]
    public async Task SuccessfulTestShowsTheReadingAndDevice()
    {
        (string Host, string? Password)? called = null;
        var m = Model((h, p, _) =>
        {
            called = (h, p);
            return Task.FromResult(TestResults.Ok());
        });
        m.Host = " 192.168.1.42 ";
        m.Password = "pw";

        Assert.Equal(UiFormat.Dash, m.PowerText);
        Assert.True(await m.TestAsync());
        Assert.Equal(("192.168.1.42", (string?)"pw"), called);
        Assert.Equal(ShellyTestState.Succeeded, m.TestState);
        Assert.True(m.IsTestCurrent);
        Assert.Equal("312.4 W", m.PowerText);
        Assert.Equal("226.9 V", m.VoltageText);
        Assert.Equal("1.38 A", m.CurrentText);
        Assert.Equal(226.9, m.MeasuredVolts);
        Assert.Equal(new WizardStatusLine(Tone.Success, "Connected to PlugMGen3-2 (S3PL-10112EU)."), m.ResultLine);

        var device = new ShellyDeviceSettings();
        m.ApplyTo(device);
        Assert.Equal("192.168.1.42", device.Host);
        Assert.Equal("PlugMGen3-2", device.Name);
        Assert.Null(device.ProtectedPassword);

        m.Password = "other";
        Assert.False(m.IsTestCurrent);
        Assert.Equal(ShellyTestState.Idle, m.TestState);
        Assert.Null(m.MeasuredVolts);
    }

    [Fact]
    public async Task FailedTestShowsTheErrorAndOffersContinueAnyway()
    {
        var m = Model((_, _, _) => Task.FromResult(TestResults.Failed("The device rejected the password.")));
        m.Host = "192.168.1.42";

        Assert.False(await m.TestAsync());
        Assert.Equal(new WizardStatusLine(Tone.Error, "The device rejected the password."), m.ResultLine);
        Assert.True(m.OfferContinueAnyway);
        m.SkipTest = true;
        Assert.False(m.OfferContinueAnyway);

        m.Host = "192.168.1.43";
        Assert.False(m.SkipTest);
        Assert.Equal(ShellyTestState.Idle, m.TestState);
        Assert.Null(m.ResultLine);
    }

    [Fact]
    public async Task TimeoutBecomesAFriendlyError()
    {
        var m = Model((_, _, ct) => Task.FromCanceled<ConnectionTestResult>(new CancellationToken(true)));
        m.Host = "192.168.1.42";
        Assert.False(await m.TestAsync());
        Assert.Equal("No answer within 3 seconds.", m.ErrorText);
        Assert.Equal(TimeSpan.FromSeconds(3), ShellyStepModel.TestTimeout);
    }

    [Fact]
    public async Task InvalidHostFailsWithoutCallingTheDevice()
    {
        var calls = 0;
        var m = Model((_, _, _) =>
        {
            calls++;
            return Task.FromResult(TestResults.Ok());
        });
        m.Host = "not a host/";
        Assert.False(await m.TestAsync());
        Assert.Equal(0, calls);
        Assert.Equal("Enter a valid IP address or host name.", m.ErrorText);
    }
}

public class RestartAndVerifyTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(HwinfoRestartResult.Restarted, Tone.Success, "")]
    [InlineData(HwinfoRestartResult.CouldNotClose, Tone.Warning, "Retry,Verify")]
    [InlineData(HwinfoRestartResult.RelaunchFailed, Tone.Warning, "StartHwinfo,Verify")]
    [InlineData(HwinfoRestartResult.ExecutableNotFound, Tone.Warning, "Verify")]
    [InlineData(HwinfoRestartResult.NotRunning, Tone.Warning, "StartHwinfo,Verify")]
    public void AdviceCoversEveryRestartResult(HwinfoRestartResult result, Tone tone, string actions)
    {
        var advice = RestartAdvice.From(result, canStart: true);
        Assert.Equal(tone, advice.Tone);
        Assert.Equal(actions, string.Join(",", advice.Actions));
        Assert.False(string.IsNullOrWhiteSpace(advice.Text));
    }

    [Fact]
    public void AdviceTexts()
    {
        Assert.Equal(
            "HWiNFO is running as administrator, so this app can't close it. Exit HWiNFO from its tray icon, start it again, then click Verify.",
            RestartAdvice.From(HwinfoRestartResult.CouldNotClose, true).Text);
        Assert.Equal("HWiNFO was closed but could not be started again. Start it manually, then click Verify.", RestartAdvice.From(HwinfoRestartResult.RelaunchFailed, true).Text);
        Assert.Equal("Restart HWiNFO manually, then click Verify.", RestartAdvice.From(HwinfoRestartResult.ExecutableNotFound, true).Text);
        Assert.Equal("HWiNFO is not running. Start it, then click Verify.", RestartAdvice.From(HwinfoRestartResult.NotRunning, true).Text);
        Assert.Equal([RestartAction.Verify], RestartAdvice.From(HwinfoRestartResult.NotRunning, canStart: false).Actions);
    }

    [Fact]
    public void VerifyWithoutSharedMemoryUsesThePollerValues()
    {
        var result = VerifyEvaluator.Evaluate(null, new PowerReading(312.4, 312.4, 294.8, 17.6, InputVoltage.V230), EnUs);

        Assert.False(result.SharedMemoryAvailable);
        Assert.False(result.AllFound);
        Assert.Equal(VerifyEvaluator.SharedMemoryOffText, result.Message);
        Assert.Equal(["312.4 W", "294.8 W", "17.6 W", "—"], result.Tiles.Select(t => t.Value));
        Assert.Equal(["AC", "DC", "Loss", "Other"], result.Tiles.Select(t => t.Caption));
    }

    [Fact]
    public void VerifyFindsTheFourSensorsOnShellyPower()
    {
        var result = VerifyEvaluator.Evaluate(Snapshots.Desktop(Snapshots.Bridge()), null, EnUs);

        Assert.True(result.AllFound);
        Assert.Equal(["PC Power (AC)", "PC Power (DC)", "PC PSU Loss", "PC Other Components"], result.Items.Select(i => i.Label));
        Assert.False(result.Implausible);
        Assert.Null(result.Message);
        Assert.Equal("48.1 W", result.Tiles[3].Value);
    }

    [Fact]
    public void VerifyReportsMissingSensors()
    {
        var result = VerifyEvaluator.Evaluate(Snapshots.Desktop(Snapshots.Bridge(other: null)), null, EnUs);
        Assert.False(result.AllFound);
        Assert.Equal([true, true, true, false], result.Items.Select(i => i.Found));

        var none = VerifyEvaluator.Evaluate(Snapshots.Desktop(), null, EnUs);
        Assert.All(none.Items, i => Assert.False(i.Found));
        Assert.True(none.SharedMemoryAvailable);
    }

    [Theory]
    [InlineData(-12.0, true)]
    [InlineData(0.0, false)]
    [InlineData(147.4, false)]
    [InlineData(147.5, true)]
    public void OtherIsImplausibleWhenNegativeOrMoreThanHalfOfDc(double other, bool implausible)
    {
        var result = VerifyEvaluator.Evaluate(Snapshots.Of(Snapshots.Bridge(dc: 294.8, other: other)), null, EnUs);
        Assert.Equal(implausible, result.Implausible);
    }

    [Fact]
    public void ImplausibleTextUsesATypographicMinus()
    {
        var result = VerifyEvaluator.Evaluate(Snapshots.Of(Snapshots.Bridge(other: -12)), null, EnUs);
        Assert.Equal("Other Components looks implausible (\u221212.0 W). Check the sensor selection; a sensor may be counted twice or missing.", result.Message);
    }

    [Fact]
    public async Task RestartedStartsTheVerificationWhichConfirms()
    {
        var f = new WizardFixture { Snapshot = Snapshots.Desktop(Snapshots.Bridge()) };
        var m = Applied(f);

        Assert.Equal(HwinfoPrimaryAction.Restart, m.PrimaryAction);
        Assert.Equal("HWiNFO is running. Restart it so it shows the Shelly Power sensors.", m.HwinfoText);
        await m.RestartHwinfoAsync();

        Assert.Equal(1, f.Restarts);
        Assert.Equal(Tone.Success, m.Advice!.Tone);
        Assert.Equal(VerifyPhase.Confirmed, m.VerifyPhase);
        Assert.True(m.IsVerifyRunning);
        Assert.Equal(new WizardStatusLine(Tone.Success, "Verified: all Shelly Power sensors are visible in HWiNFO."), m.VerifyStatus);
        Assert.False(m.CanVerify);
    }

    [Fact]
    public void VerifyIsOfferedAgainWhenTheSensorsGoAway()
    {
        var f = new WizardFixture { Snapshot = Snapshots.Desktop(Snapshots.Bridge()) };
        var m = Applied(f);
        Assert.True(m.CanVerify);
        m.StartVerify();
        Assert.Equal(VerifyPhase.Confirmed, m.VerifyPhase);
        Assert.False(m.CanVerify);

        f.Snapshot = Snapshots.Desktop();
        m.VerifyTick();
        Assert.Equal(VerifyPhase.Checking, m.VerifyPhase);
        Assert.True(m.CanVerify);

        f.Snapshot = Snapshots.Desktop(Snapshots.Bridge());
        m.VerifyTick();
        Assert.Equal(VerifyPhase.Confirmed, m.VerifyPhase);
        Assert.False(m.CanVerify);
    }

    [Fact]
    public void MissingSharedMemoryIsPendingDuringTheGracePeriod()
    {
        var f = new WizardFixture { Snapshot = null };
        var m = Applied(f);
        m.StartVerify();
        Assert.Equal(new WizardStatusLine(Tone.Pending, ApplyStepModel.CheckingHwinfoText), m.VerifyStatus);

        f.Time.Advance(TimeSpan.FromSeconds(19));
        m.VerifyTick();
        Assert.Equal(Tone.Pending, m.VerifyStatus!.Tone);

        f.Time.Advance(TimeSpan.FromSeconds(1));
        m.VerifyTick();
        Assert.Equal(new WizardStatusLine(Tone.Warning, VerifyEvaluator.SharedMemoryOffText), m.VerifyStatus);

        // A restart starts a new grace period.
        m.StartVerify();
        Assert.Equal(Tone.Pending, m.VerifyStatus!.Tone);
    }

    [Fact]
    public async Task CouldNotCloseOffersRetryAndDoesNotVerify()
    {
        var f = new WizardFixture { RestartResult = HwinfoRestartResult.CouldNotClose };
        var m = Applied(f);
        await m.RestartHwinfoAsync();

        Assert.Equal(RestartAdvice.CouldNotCloseText, m.Advice!.Text);
        Assert.Equal(VerifyPhase.Idle, m.VerifyPhase);
        Assert.False(m.IsRestarting);
    }

    [Fact]
    public void VerifyTimesOutAfter90SecondsAndCanRunAgain()
    {
        var f = new WizardFixture();
        var m = Applied(f);
        m.StartVerify();
        Assert.Equal(VerifyPhase.Checking, m.VerifyPhase);
        Assert.Equal(new WizardStatusLine(Tone.Secondary, "Looking for the Shelly Power sensors in HWiNFO…"), m.VerifyStatus);

        f.Time.Advance(TimeSpan.FromSeconds(89));
        m.VerifyTick();
        Assert.Equal(VerifyPhase.Checking, m.VerifyPhase);

        f.Time.Advance(TimeSpan.FromSeconds(1));
        m.VerifyTick();
        Assert.Equal(VerifyPhase.TimedOut, m.VerifyPhase);
        Assert.False(m.IsVerifyRunning);
        Assert.Equal(Tone.Warning, m.VerifyStatus!.Tone);

        f.Snapshot = Snapshots.Desktop(Snapshots.Bridge(other: 200));
        m.StartVerify();
        Assert.Equal(VerifyPhase.Confirmed, m.VerifyPhase);
        Assert.Equal(Tone.Warning, m.VerifyStatus!.Tone);
        Assert.StartsWith("Other Components looks implausible (200.0 W)", m.VerifyStatus.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void StartHwinfoWhenNotRunning()
    {
        var f = new WizardFixture { Status = new HwinfoStatus(true, false, @"C:\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.UninstallEntry), Snapshot = null };
        var m = Applied(f);
        Assert.Equal(HwinfoPrimaryAction.Start, m.PrimaryAction);

        m.StartHwinfo();
        Assert.Equal(1, f.Starts);
        Assert.Equal(VerifyPhase.Checking, m.VerifyPhase);
        Assert.Equal(new WizardStatusLine(Tone.Pending, ApplyStepModel.CheckingHwinfoText), m.VerifyStatus);
        f.Time.Advance(ApplyStepModel.VerifyGracePeriod);
        m.VerifyTick();
        Assert.Equal(new WizardStatusLine(Tone.Warning, VerifyEvaluator.SharedMemoryOffText), m.VerifyStatus);

        f.StartWorks = false;
        m.StartHwinfo();
        Assert.Equal([RestartAction.Verify], m.Advice!.Actions);
    }

    [Fact]
    public void NotInstalledOffersNoButton()
    {
        var f = new WizardFixture { Status = new HwinfoStatus(false, false, null, HwinfoPathSource.None) };
        var m = Applied(f);
        Assert.Equal(HwinfoPrimaryAction.None, m.PrimaryAction);
        Assert.StartsWith("HWiNFO was not found.", m.HwinfoText, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveAdviceCompletesAPendingAdvice()
    {
        var pending = RestartAdvice.Checking("HWiNFO restarted.");
        Assert.Equal("HWiNFO restarted. Checking for the Shelly Power sensors…", pending.Text);
        Assert.Equal(pending, RestartAdvice.From(HwinfoRestartResult.Restarted, canStart: true));

        Assert.Same(pending, VerifyEvaluator.ResolveAdvice(pending, sensorsFound: false, gracePeriodOver: false));

        var found = VerifyEvaluator.ResolveAdvice(pending, sensorsFound: true, gracePeriodOver: true)!;
        Assert.Equal(Tone.Success, found.Tone);
        Assert.Equal("HWiNFO restarted. Shelly Power sensors found.", found.Text);
        Assert.Empty(found.Actions);

        var missing = VerifyEvaluator.ResolveAdvice(pending, sensorsFound: false, gracePeriodOver: true)!;
        Assert.Equal(Tone.Warning, missing.Tone);
        Assert.Equal("HWiNFO restarted. " + RestartAdvice.SensorsNotFoundText, missing.Text);
        Assert.Equal([RestartAction.Verify], missing.Actions);

        // A final advice stays as it is.
        var couldNotClose = RestartAdvice.From(HwinfoRestartResult.CouldNotClose, canStart: true);
        Assert.Same(couldNotClose, VerifyEvaluator.ResolveAdvice(couldNotClose, sensorsFound: true, gracePeriodOver: true));
        Assert.Null(VerifyEvaluator.ResolveAdvice(null, sensorsFound: true, gracePeriodOver: true));
    }

    [Fact]
    public void ReviewHwinfoTurnsAClosedHwinfoIntoAWarning()
    {
        var running = new HwinfoStatus(true, true, @"C:\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.RunningProcess);
        var closed = new HwinfoStatus(true, false, @"C:\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.UninstallEntry);
        var pending = RestartAdvice.Checking("HWiNFO restarted.");

        var notRunning = VerifyEvaluator.ReviewHwinfo(pending, closed)!;
        Assert.Equal(Tone.Warning, notRunning.Tone);
        Assert.Equal(RestartAdvice.NotRunningText, notRunning.Text);
        Assert.Equal([RestartAction.StartHwinfo, RestartAction.Verify], notRunning.Actions);
        Assert.Equal([RestartAction.Verify], VerifyEvaluator.ReviewHwinfo(null, closed with { ExecutablePath = null })!.Actions);

        Assert.Same(pending, VerifyEvaluator.ReviewHwinfo(pending, running));
        Assert.Null(VerifyEvaluator.ReviewHwinfo(null, running));
        Assert.Same(pending, VerifyEvaluator.ReviewHwinfo(pending, null));

        var again = VerifyEvaluator.ReviewHwinfo(notRunning, running)!;
        Assert.Equal("HWiNFO is running. Checking for the Shelly Power sensors…", again.Text);
        Assert.Equal("HWiNFO is running.", again.PendingLead);
    }

    [Fact]
    public async Task RestartAdviceResolvesWhenTheSensorsAreFound()
    {
        var f = new WizardFixture { Snapshot = Snapshots.Desktop(Snapshots.Bridge()) };
        var m = Applied(f);
        await m.RestartHwinfoAsync();

        Assert.Equal(new RestartAdvice(Tone.Success, "HWiNFO restarted. Shelly Power sensors found.", []) { PendingLead = "HWiNFO restarted." }, m.Advice);
    }

    [Fact]
    public async Task RestartAdviceWarnsWhenTheSensorsStayMissingAfterTheGracePeriod()
    {
        var f = new WizardFixture { Snapshot = Snapshots.Desktop() };
        var m = Applied(f);
        await m.RestartHwinfoAsync();
        Assert.Equal("HWiNFO restarted. Checking for the Shelly Power sensors…", m.Advice!.Text);

        f.Time.Advance(ApplyStepModel.VerifyGracePeriod);
        m.VerifyTick();
        Assert.Equal(Tone.Warning, m.Advice!.Tone);
        Assert.Equal("HWiNFO restarted. " + RestartAdvice.SensorsNotFoundText, m.Advice.Text);

        // Still looking: the sensors showing up later turn it green.
        f.Snapshot = Snapshots.Desktop(Snapshots.Bridge());
        m.VerifyTick();
        Assert.Equal("HWiNFO restarted. Shelly Power sensors found.", m.Advice!.Text);
        Assert.Equal(Tone.Success, m.Advice.Tone);

        // After the timeout without sensors the warning stays.
        var g = new WizardFixture { Snapshot = Snapshots.Desktop() };
        var n = Applied(g);
        await n.RestartHwinfoAsync();
        g.Time.Advance(ApplyStepModel.VerifyTimeout);
        n.VerifyTick();
        Assert.Equal(VerifyPhase.TimedOut, n.VerifyPhase);
        Assert.Equal(Tone.Warning, n.Advice!.Tone);
    }

    [Fact]
    public async Task VerifyAgainDropsTheGreenCheckWhenHwinfoWasClosed()
    {
        var f = new WizardFixture { Snapshot = Snapshots.Desktop(Snapshots.Bridge()) };
        var m = Applied(f);
        await m.RestartHwinfoAsync();
        Assert.Equal(Tone.Success, m.Advice!.Tone);

        f.Status = new HwinfoStatus(true, false, @"C:\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.UninstallEntry);
        f.Snapshot = null;
        m.VerifyAgain();
        Assert.Equal(Tone.Warning, m.Advice!.Tone);
        Assert.Equal(RestartAdvice.NotRunningText, m.Advice.Text);
        Assert.Equal(HwinfoPrimaryAction.Start, m.PrimaryAction);
        Assert.Equal(VerifyPhase.Checking, m.VerifyPhase);

        // HWiNFO started by hand: the next Verify waits for the sensors again and resolves.
        f.Status = new HwinfoStatus(true, true, @"C:\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.RunningProcess);
        f.Snapshot = Snapshots.Desktop(Snapshots.Bridge());
        m.VerifyAgain();
        Assert.Equal(Tone.Success, m.Advice!.Tone);
        Assert.Equal("HWiNFO is running. Shelly Power sensors found.", m.Advice.Text);
    }

    private static ApplyStepModel Applied(WizardFixture f)
    {
        var m = new ApplyStepModel(f.Dependencies, startWithWindows: false, EnUs);
        Assert.True(m.Apply(BriefExample.Settings(), null));
        Assert.False(Assert.Single(f.Runtime.WizardApplied).StartWithWindows);
        return m;
    }
}
