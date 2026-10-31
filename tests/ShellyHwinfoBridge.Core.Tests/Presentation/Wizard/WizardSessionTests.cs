using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

public class WizardSessionTests
{
    [Fact]
    public async Task WalksAllStepsWithoutWritingAnythingBeforeApply()
    {
        using var _ = new CultureScope("en-US");
        var f = new WizardFixture();
        var session = f.Session();

        Assert.Equal(WizardStep.Hwinfo, session.Current);
        Assert.True(session.IsFirstRun);
        Assert.False(session.CanGoBack);
        Assert.True(await session.GoNextAsync());

        session.Shelly.SelectDevice(f.Devices[0]);
        Assert.True(await session.GoNextAsync());
        Assert.Equal(WizardStep.Psu, session.Current);
        Assert.Equal(226.9, session.Psu.MeasuredVolts);

        Assert.False(session.CanGoNext);
        session.Psu.Select("seasonic-prime-fanless-tx-700");
        Assert.True(await session.GoNextAsync());

        Assert.Equal(WizardStep.Sensors, session.Current);
        Assert.False(session.Sensors.IsManual);
        Assert.True(await session.GoNextAsync());

        Assert.Equal(WizardStep.Apply, session.Current);
        Assert.Equal("Apply", session.NextText);
        Assert.Empty(f.Runtime.WizardApplied);
        Assert.Empty(f.Runtime.Applied);
        Assert.Equal(f.Status, f.Dependencies.DetectHwinfo());
        Assert.Equal(0, f.Restarts);
        Assert.Equal(0, f.Starts);
        Assert.Equal(new[] { WizardStep.Hwinfo, WizardStep.Shelly, WizardStep.Psu, WizardStep.Sensors }, session.Completed.Order());
    }

    [Fact]
    public async Task ApplyHandsTheDraftToTheRuntimeAndThenOffersFinish()
    {
        using var _ = new CultureScope("en-US");
        var f = new WizardFixture();
        var session = await AtApply(f);

        Assert.True(session.Apply.StartWithWindows);
        Assert.True(await session.GoNextAsync());

        var (draft, password, startWithWindows) = Assert.Single(f.Runtime.WizardApplied);
        Assert.Equal("192.168.1.42", draft.Device.Host);
        Assert.Equal("PlugMGen3-2", draft.Device.Name);
        Assert.Equal("S3PL-10112EU", draft.Device.Model);
        Assert.Equal("seasonic-prime-fanless-tx-700", draft.Psu.DatabaseId);
        Assert.Equal(["CPU Package Power", "GPU Power", "DIMM1 Power", "DIMM3 Power"], draft.Sensors.Select(s => s.Name));
        Assert.Null(password);
        Assert.True(startWithWindows);

        Assert.True(session.IsApplied);
        Assert.Equal(WizardStep.Verify, session.Current);
        Assert.Equal("Verify in HWiNFO", session.PageTitle);
        Assert.False(session.CanGoBack);
        Assert.True(session.CanGoNext);
        Assert.Equal("Finish", session.NextText);
        Assert.Contains(WizardStep.Apply, session.Completed);
        Assert.Equal(HwinfoPrimaryAction.Restart, session.Apply.PrimaryAction);

        // Step 6 starts verifying at once.
        Assert.True(session.Apply.IsVerifyRunning);
        Assert.False(await session.GoNextAsync());
        Assert.Equal(WizardStep.Verify, session.Current);
        Assert.Single(f.Runtime.WizardApplied);
        Assert.Equal(6, WizardSession.StepLabels.Count);
    }

    [Fact]
    public async Task FailedApplyStaysOnTheStepAndCanBeRetried()
    {
        var f = new WizardFixture();
        var session = await AtApply(f);
        f.Runtime.WizardApplyError = "Applying the setup failed: access denied";

        Assert.False(await session.GoNextAsync());
        Assert.False(session.IsApplied);
        Assert.Equal("Applying the setup failed: access denied", session.Apply.ApplyError);
        Assert.True(session.CanGoBack);
        Assert.Equal("Apply", session.NextText);

        Assert.Equal(WizardStep.Apply, session.Current);

        f.Runtime.WizardApplyError = null;
        Assert.True(await session.GoNextAsync());
        Assert.Null(session.Apply.ApplyError);
        Assert.True(session.IsApplied);
        Assert.Equal(WizardStep.Verify, session.Current);
    }

    [Fact]
    public async Task ShellyNextRunsTheTestAndAFailureOffersContinueAnyway()
    {
        var f = new WizardFixture();
        f.Runtime.TestResult = TestResults.Failed("The device rejected the password.");
        var session = f.Session();
        await session.GoNextAsync();
        session.Shelly.Host = "192.168.1.50";

        Assert.False(await session.GoNextAsync());
        Assert.Equal(WizardStep.Shelly, session.Current);
        Assert.Equal(ShellyTestState.Failed, session.Shelly.TestState);
        Assert.Equal("The device rejected the password.", session.Shelly.ErrorText);
        Assert.True(session.Shelly.OfferContinueAnyway);

        Assert.True(await session.ContinueAnywayAsync());
        Assert.Equal(WizardStep.Psu, session.Current);
        Assert.Equal("192.168.1.50", session.Draft.Device.Host);
        Assert.Null(session.Psu.MeasuredVolts);
    }

    [Fact]
    public async Task ShellyNextSkipsTheTestWhenTheLastOneMatches()
    {
        var f = new WizardFixture();
        var session = f.Session();
        await session.GoNextAsync();
        session.Shelly.Host = "192.168.1.42";
        Assert.True(await session.Shelly.TestAsync());
        f.Runtime.TestResult = TestResults.Failed("should not be called");

        Assert.True(await session.GoNextAsync());
        Assert.Equal(WizardStep.Psu, session.Current);
    }

    [Fact]
    public async Task ShellyNextNeedsAValidHost()
    {
        var session = new WizardFixture().Session();
        await session.GoNextAsync();

        session.Shelly.Host = string.Empty;
        Assert.False(session.CanGoNext);
        session.Shelly.Host = "http://bad host/";
        Assert.False(session.CanGoNext);
        Assert.NotNull(session.Shelly.HostError);
        session.Shelly.Host = "plug.local";
        Assert.True(session.CanGoNext);
    }

    [Fact]
    public async Task BackIsAllowedBeforeApplyAndKeepsTheEnteredValues()
    {
        var f = new WizardFixture();
        var session = await AtApply(f);

        Assert.True(session.GoBack());
        Assert.Equal(WizardStep.Sensors, session.Current);
        session.Sensors.SetChecked(session.Sensors.Groups[0].Rows[0].Id, false);
        Assert.True(session.GoBack());
        Assert.True(session.GoBack());
        Assert.Equal("192.168.1.42", session.Shelly.Host);
        Assert.True(session.GoBack());
        Assert.Equal(WizardStep.Hwinfo, session.Current);
        Assert.False(session.GoBack());

        await session.GoNextAsync();
        await session.GoNextAsync();
        await session.GoNextAsync();
        Assert.Equal(WizardStep.Sensors, session.Current);
        Assert.False(session.Sensors.Groups[0].Rows[0].Checked);
        Assert.Empty(f.Runtime.WizardApplied);
    }

    [Fact]
    public async Task SensorsStepBlocksNextForInvalidNames()
    {
        var f = new WizardFixture { Snapshot = null };
        var session = await AtStep(f, WizardStep.Sensors);
        Assert.True(session.Sensors.IsManual);

        var id = session.Sensors.AddManual();
        session.Sensors.SetManualName(id, "GPU \"Hot\" Power");
        Assert.False(session.CanGoNext);
        session.Sensors.SetManualName(id, "GPU Power");
        Assert.True(session.CanGoNext);
    }

    [Fact]
    public async Task ReRunStartsFromTheCurrentSettingsAndKeepsThemUntouched()
    {
        var f = new WizardFixture();
        var original = BriefExample.Settings();
        var session = new WizardSession(original, "secret", BriefExample.Database(), f.Dependencies);

        Assert.False(session.IsFirstRun);
        Assert.False(session.Apply.StartWithWindows);
        Assert.Equal("192.168.1.42", session.Shelly.Host);
        Assert.Equal("secret", session.Shelly.Password);
        Assert.Equal("seasonic-prime-fanless-tx-700", session.Psu.SelectedId);
        Assert.Equal("Seasonic", session.Psu.Brand);

        session.Draft.Device.Host = "10.0.0.9";
        Assert.Equal("192.168.1.42", original.Device.Host);

        session.Shelly.Host = "192.168.1.42";
        await session.GoNextAsync();
        await session.GoNextAsync();
        await session.GoNextAsync();
        Assert.Equal(WizardStep.Sensors, session.Current);
        Assert.Equal(4, session.Sensors.Selected.Count);
        await session.GoNextAsync();
        await session.GoNextAsync();
        Assert.Equal("secret", Assert.Single(f.Runtime.WizardApplied).Password);
    }

    [Fact]
    public async Task ApplyStepSummaryDescribesTheDraft()
    {
        using var _ = new CultureScope("en-US");
        var session = await AtApply(new WizardFixture());

        var lines = session.Apply.SummaryLines.ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("PlugMGen3-2 (S3PL-10112EU) · 192.168.1.42", lines["Device"]);
        Assert.Equal("Seasonic Prime Fanless TX-700", lines["PSU"]);
        Assert.Equal("Cybenetics report 21PS1872A", lines["Curve source"]);
        Assert.Equal("230 V curve (automatic, plug measured 226.9 V)", lines["Voltage"]);
        Assert.Equal("CPU Package Power (CPU), GPU Power (GPU), DIMM1 Power (RAM), DIMM3 Power (RAM)", lines["Sensors"]);
        Assert.Equal("\"DC Power (Est.)\" - \"CPU Package Power\" - \"GPU Power\" - \"DIMM1 Power\" - \"DIMM3 Power\"", lines["Formula"]);
        Assert.True(session.Apply.SummaryLines.Single(l => l.Label == "Formula").Monospace);
    }

    [Fact]
    public void NotInstalledHwinfoIsOnlyAWarning()
    {
        var f = new WizardFixture { Status = new HwinfoStatus(false, false, null, HwinfoPathSource.None), Snapshot = null };
        var session = f.Session();

        Assert.True(session.CanGoNext);
        Assert.NotNull(session.Hwinfo.Warning);
        Assert.Equal(
            [new WizardStatusLine(Tone.Warning, "Not installed"), new WizardStatusLine(Tone.Warning, "Not running"), new WizardStatusLine(Tone.Secondary, "Shared Memory Support: start HWiNFO to check")],
            session.Hwinfo.Lines);
    }

    [Fact]
    public void HwinfoLinesReflectPathRunningAndSharedMemory()
    {
        var f = new WizardFixture();
        var model = f.Session().Hwinfo;
        Assert.Equal(
            [
                new WizardStatusLine(Tone.Success, @"Installed · C:\Program Files\HWiNFO64\HWiNFO64.exe"),
                new WizardStatusLine(Tone.Success, "Running"),
                new WizardStatusLine(Tone.Success, "Shared Memory Support is on"),
            ],
            model.Lines);

        f.Snapshot = null;
        model.Refresh();
        Assert.False(model.SharedMemoryOn);
        Assert.Equal(new WizardStatusLine(Tone.Warning, "Shared Memory Support is off"), model.Lines[2]);
        Assert.Null(model.Warning);
    }

    internal static async Task<WizardSession> AtStep(WizardFixture f, WizardStep step)
    {
        var session = f.Session();
        while (session.Current < step)
        {
            if (session.Current == WizardStep.Shelly)
            {
                session.Shelly.SelectDevice(f.Devices[0]);
            }
            else if (session.Current == WizardStep.Psu)
            {
                session.Psu.Select("seasonic-prime-fanless-tx-700");
            }

            Assert.True(await session.GoNextAsync());
        }

        return session;
    }

    private static Task<WizardSession> AtApply(WizardFixture f) => AtStep(f, WizardStep.Apply);
}
