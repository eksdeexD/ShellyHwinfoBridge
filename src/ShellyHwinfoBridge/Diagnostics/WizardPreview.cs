using System.Net;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
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
    /// <summary>Builds a wizard positioned at <paramref name="step"/>; <paramref name="tweak"/> adjusts the session first.</summary>
    public static WizardForm At(WizardStep step, bool firstRun = true, Action<WizardSession>? tweak = null, Snapshot snapshot = Snapshot.Desktop)
    {
        var runtime = new PreviewRuntime();
        var dependencies = new WizardDependencies(
            runtime,
            () => new HwinfoStatus(true, true, @"C:\Program Files\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.RunningProcess),
            () => snapshot switch
            {
                Snapshot.None => null,
                Snapshot.WithBridge => Sensors(withBridge: true),
                _ => Sensors(withBridge: false),
            },
            _ => Task.FromResult<IReadOnlyList<DiscoveredShelly>>(
            [
                new("shellyplugmg3-1", "PlugMGen3-2", "S3PL-10112EU", "PlugMG3", 3, IPAddress.Parse("192.168.1.42"), 80, false),
                new("shellyplugsg3-2", "Desk lamp", "S3PL-00112EU", "PlugSG3", 3, IPAddress.Parse("192.168.1.57"), 80, false),
            ]),
            _ => Task.FromResult(HwinfoRestartResult.CouldNotClose),
            () => true,
            TimeProvider.System);

        var settings = firstRun ? null : SampleData.Settings();
        var session = new WizardSession(settings, null, Database(), dependencies, SampleData.Culture);
        session.Shelly.DiscoverAsync().GetAwaiter().GetResult();
        if (step > WizardStep.Hwinfo)
        {
            session.GoNextAsync().GetAwaiter().GetResult();
        }

        if (step > WizardStep.Shelly)
        {
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
            readings.Add(W(9, "Shelly Power", "AC Power (Wall)", 312.4));
            readings.Add(W(9, "Shelly Power", "DC Power (Est.)", 294.8));
            readings.Add(W(9, "Shelly Power", "PSU Loss", 17.6));
            readings.Add(W(9, "Shelly Power", "Other Components", 48.1));
        }

        return new HwinfoSnapshot(2, 1, SampleData.NowUtc, [], readings);
    }
}
