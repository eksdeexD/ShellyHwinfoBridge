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
        Assert.Equal(["AC Power (Wall)", "DC Power (Est.)", "PSU Loss", "Other Components"], result.Items.Select(i => i.Label));
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
        Assert.Equal(new WizardStatusLine(Tone.Success, "Verified: all four Shelly Power sensors are visible in HWiNFO."), m.VerifyStatus);
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

    private static ApplyStepModel Applied(WizardFixture f)
    {
        var m = new ApplyStepModel(f.Dependencies, startWithWindows: false, EnUs);
        Assert.True(m.Apply(BriefExample.Settings(), null));
        Assert.False(Assert.Single(f.Runtime.WizardApplied).StartWithWindows);
        return m;
    }
}
