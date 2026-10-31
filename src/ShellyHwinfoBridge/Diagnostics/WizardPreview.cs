using System.Net;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>Wizard screens with the design brief's example data (for <c>--render-screens</c>).</summary>
internal static class WizardPreview
{
    /// <summary>
    /// Builds a wizard positioned at <paramref name="step"/>; <paramref name="tweak"/> adjusts the session first.
    /// <paramref name="afterApply"/>, when set, replaces <paramref name="snapshot"/> once the setup was applied.
    /// <paramref name="database"/> replaces the three-entry preview database.
    /// </summary>
    public static WizardForm At(WizardStep step, bool firstRun = true, Action<WizardSession>? tweak = null, Snapshot snapshot = Snapshot.Desktop, Snapshot? afterApply = null, PsuDatabase? database = null)
    {
        var runtime = new PreviewRuntime();
        WizardSession? session = null;
        var dependencies = new WizardDependencies(
            runtime,
            () => new HwinfoStatus(true, true, @"C:\Program Files\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.RunningProcess),
            () => (session is { IsApplied: true } && afterApply is { } applied ? applied : snapshot) switch
            {
                Snapshot.None => null,
                Snapshot.WithBridge => Sensors(withBridge: true),
                _ => Sensors(withBridge: false),
            },
            Discover,
            _ => Task.FromResult(HwinfoRestartResult.CouldNotClose),
            () => true,
            TimeProvider.System);

        var settings = firstRun ? null : SampleData.Settings();
        session = new WizardSession(settings, null, database ?? Database(), dependencies, SampleData.Culture);
        session.Shelly.DiscoverAsync().GetAwaiter().GetResult();
        if (step > WizardStep.Hwinfo)
        {
            session.GoNextAsync().GetAwaiter().GetResult();
        }

        if (step > WizardStep.Shelly)
        {
            session.Shelly.FindAsync().GetAwaiter().GetResult();
            session.Shelly.SelectDevice(session.Shelly.Devices[0]);
            session.GoNextAsync().GetAwaiter().GetResult();
        }

        if (step > WizardStep.Psu)
        {
            session.Psu.Select("seasonic-prime-fanless-tx-700");
            session.GoNextAsync().GetAwaiter().GetResult();
        }

        if (step > WizardStep.Sensors)
        {
            session.GoNextAsync().GetAwaiter().GetResult();
        }

        tweak?.Invoke(session);
        return new WizardForm(session);
    }

    /// <summary>The preview's network: the design image's plug and a second one (no mDNS, no network).</summary>
    internal static Task<IReadOnlyList<DiscoveredShelly>> Discover(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DiscoveredShelly>>(
        [
            new("shellyplugmg3-1", "PlugMGen3-2", "S3PL-10112EU", "PlugMG3", 3, IPAddress.Parse("192.168.1.42"), 80, false),
            new("shellyplugsg3-2", "PlugSG3-1", "S3PL-00112EU", "PlugSG3", 3, IPAddress.Parse("192.168.1.57"), 80, false),
        ]);

    /// <summary>What the preview's shared memory returns.</summary>
    internal enum Snapshot
    {
        /// <summary>The design image's sensors.</summary>
        Desktop,

        /// <summary>The design image's sensors plus the bridge's own four sensors.</summary>
        WithBridge,

        /// <summary>Shared memory off.</summary>
        None,
    }

    /// <summary>Three Seasonic entries as in the design image (TX-700 is the real seed entry).</summary>
    internal static PsuDatabase Database()
    {
        var real = PsuDatabaseLoader.LoadEmbedded();
        var tx700 = real.Psus.First(p => p.Id == "seasonic-prime-fanless-tx-700");
        PsuEntry Copy(string id, string model, int watts, string rating, string report) => new()
        {
            Id = id,
            Brand = tx700.Brand,
            Series = "Prime",
            Model = model,
            Wattage = watts,
            Rating80Plus = rating,
            RatingCybenetics = rating,
            Source = new PsuSource { Name = "Cybenetics", ReportId = report, Url = tx700.Source?.Url ?? string.Empty },
            Curves = tx700.Curves,
        };

        return new PsuDatabase
        {
            SchemaVersion = 1,
            Updated = real.Updated,
            Psus = [tx700, Copy("preview-tx-750", "Prime TX-750", 750, "Titanium", "20PS1620A"), Copy("preview-px-750", "Prime PX-750", 750, "Platinum", "20PS1588A")],
        };
    }

    /// <summary>The id of the preview's 230 V-only entry.</summary>
    internal const string SingleVoltageId = "preview-single-voltage";

    /// <summary>The embedded database plus the preview's 230 V-only entry (settings and main window previews).</summary>
    internal static PsuDatabase DatabaseWithSingleVoltage()
    {
        var real = PsuDatabaseLoader.LoadEmbedded();
        return new PsuDatabase
        {
            SchemaVersion = real.SchemaVersion,
            Updated = real.Updated,
            Psus = [.. real.Psus, SingleVoltageEntry(real.Psus.First(p => p.Id == "seasonic-prime-fanless-tx-700"))],
        };
    }

    /// <summary>A unit tested at 230 V only (the TX-700's 230 V curve under another name), for the curve note.</summary>
    private static PsuEntry SingleVoltageEntry(PsuEntry template) => new()
    {
        Id = SingleVoltageId,
        Brand = template.Brand,
        Series = "Prime",
        Model = "Prime EU-700",
        Wattage = 700,
        Rating80Plus = "Titanium",
        RatingCybenetics = "Titanium",
        Source = template.Source,
        Curves = new Dictionary<string, List<double[]>> { ["230"] = template.Curves!["230"] },
    };

    internal static HwinfoSnapshot Sensors(bool withBridge)
    {
        static HwinfoReading W(int sensor, string device, string label, double value) =>
            new(HwinfoReadingType.Power, sensor, 0, device, label, string.Empty, "W", value, value, value, value);

        const string cpu = "CPU [#0]: Intel Core i9-14900K";
        const string gpu = "GPU [#0]: NVIDIA GeForce RTX 4080";
        const string memory = "Memory";
        var readings = new List<HwinfoReading>
        {
            W(0, cpu, "CPU Package Power", 142.3),
            W(0, cpu, "IA Cores Power", 118.0),
            W(0, cpu, "GT Cores Power", 2.1),
            W(1, gpu, "GPU Power", 88.6),
            W(2, memory, "DIMM1 Power", 8.1),
            W(2, memory, "DIMM3 Power", 7.7),
        };
        if (withBridge)
        {
            readings.Add(W(9, SensorNaming.DeviceName, SensorNaming.PcAcName, 312.4));
            readings.Add(W(9, SensorNaming.DeviceName, SensorNaming.PcDcName, 294.8));
            readings.Add(W(9, SensorNaming.DeviceName, SensorNaming.PcLossName, 17.6));
            readings.Add(W(9, SensorNaming.DeviceName, SensorNaming.PcOtherName, 48.1));
        }

        return new HwinfoSnapshot(2, 1, SampleData.NowUtc, [], readings);
    }
}
