using System.Net;
using Microsoft.Extensions.Time.Testing;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Shelly;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Presentation.Wizard;

/// <summary>Configurable fakes for the wizard's dependencies.</summary>
internal sealed class WizardFixture
{
    public FakeRuntime Runtime { get; } = new() { Settings = null, Password = null, State = RuntimeState.Unconfigured };

    public HwinfoStatus Status { get; set; } = new(true, true, @"C:\Program Files\HWiNFO64\HWiNFO64.exe", HwinfoPathSource.RunningProcess);

    public HwinfoSnapshot? Snapshot { get; set; } = Snapshots.Desktop();

    public List<DiscoveredShelly> Devices { get; } = [Plug("PlugMGen3-2", "192.168.1.42")];

    public HwinfoRestartResult RestartResult { get; set; } = HwinfoRestartResult.Restarted;

    public bool StartWorks { get; set; } = true;

    public int Restarts { get; private set; }

    public int Starts { get; private set; }

    public List<GadgetReading>? GadgetExport { get; set; } = [];

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 30, 18, 40, 0, TimeSpan.Zero));

    public WizardDependencies Dependencies => new(
        Runtime,
        () => Status,
        () => Snapshot,
        _ => Task.FromResult<IReadOnlyList<DiscoveredShelly>>(Devices.ToList()),
        _ =>
        {
            Restarts++;
            return Task.FromResult(RestartResult);
        },
        () =>
        {
            Starts++;
            return StartWorks;
        },
        Time,
        () => GadgetExport?.ToList());

    public static DiscoveredShelly Plug(string name, string ip) =>
        new("shellyplugmg3-" + name.ToLowerInvariant(), name, "S3PL-10112EU", "PlugMG3", 3, IPAddress.Parse(ip), 80, false);

    public WizardSession Session(bool configured = false) => configured
        ? new WizardSession(BriefExample.Settings(), "secret", BriefExample.Database(), Dependencies)
        : new WizardSession(null, null, BriefExample.Database(), Dependencies);
}

/// <summary>Synthetic HWiNFO snapshots.</summary>
internal static class Snapshots
{
    public const string Cpu = "CPU [#0]: Intel Core i9-14900K";
    public const string Gpu = "GPU [#0]: NVIDIA GeForce RTX 4080";
    public const string Memory = "Memory";

    public static HwinfoReading Watts(int sensor, string device, string label, double value) =>
        new(HwinfoReadingType.Power, sensor, 0, device, label, string.Empty, "W", value, value, value, value);

    public static HwinfoSnapshot Of(params HwinfoReading[] readings) =>
        new(2, 1, DateTimeOffset.UnixEpoch, [], readings);

    public static HwinfoSnapshot Desktop(params HwinfoReading[] extra) => Of(
    [
        Watts(0, Cpu, "CPU Package Power", 142.3),
        Watts(0, Cpu, "IA Cores Power", 118.0),
        Watts(0, Cpu, "GT Cores Power", 2.1),
        Watts(1, Gpu, "GPU Power", 88.6),
        Watts(2, Memory, "DIMM1 Power", 8.1),
        Watts(2, Memory, "DIMM3 Power", 7.7),
        new HwinfoReading(HwinfoReadingType.Temperature, 0, 0, Cpu, "CPU Package", string.Empty, "°C", 61, 60, 62, 61),
        .. extra,
    ]);

    public static HwinfoReading[] Bridge(double ac = 312.4, double dc = 294.8, double loss = 17.6, double? other = 48.1)
    {
        var list = new List<HwinfoReading>
        {
            Watts(9, "Shelly Power", "PC Power (AC)", ac),
            Watts(9, "Shelly Power", "PC Power (DC)", dc),
            Watts(9, "Shelly Power", "PC PSU Loss", loss),
        };
        if (other is double o)
        {
            list.Add(Watts(9, "Shelly Power", "PC Other Components", o));
        }

        return [.. list];
    }
}

/// <summary>A database with several brands and wattages.</summary>
internal static class TestDatabase
{
    public static PsuDatabase Create()
    {
        var curve = TestCurves.Tx700.Curves;
        PsuEntry Entry(string id, string brand, string model, int watts, string rating, string? eta, string? revision = null, string? report = "21PS0000A", string? eta115 = null, string? date = null) => new()
        {
            Id = id,
            Brand = brand,
            Series = model.Split(' ')[0],
            Model = model,
            Revision = revision,
            Wattage = watts,
            Rating80Plus = rating,
            RatingsCybenetics = eta is null && eta115 is null
                ? null
                : new Dictionary<string, string>(new[] { ("230", eta), ("115", eta115) }.Where(g => g.Item2 is not null).Select(g => KeyValuePair.Create(g.Item1, g.Item2!))),
            Source = new PsuSource { Name = "Cybenetics", ReportId = report, Url = "https://www.cybenetics.com/evaluations/psus/" + id + "/", ReportDate = date },
            Curves = curve,
        };

        return new PsuDatabase
        {
            SchemaVersion = 1,
            Psus =
            [
                Entry("tx700", "Seasonic", "Prime Fanless TX-700", 700, "Titanium", "Titanium", report: "21PS1872A", eta115: "Titanium", date: "2021-07-16"),
                Entry("tx750", "Seasonic", "Prime TX-750", 750, "Titanium", "Titanium"),
                Entry("px750", "Seasonic", "Prime PX-750", 750, "Platinum", "Platinum"),
                Entry("rm850", "Corsair", "RM850x", 850, "Gold", "Gold", "2021", eta115: "Platinum"),
                Entry("rm850old", "Corsair", "RM850x", 850, "Gold", null, "2018"),
                Entry("sf750", "Corsair", "SF750", 750, "Platinum", "Platinum", report: null),
            ],
        };
    }
}

/// <summary>A connection test that answers from a script.</summary>
internal static class TestResults
{
    public static ConnectionTestResult Ok(double volts = 226.9) =>
        new(true, new ShellyReading(312.4, volts, 1.38), new ShellyDeviceInfo("PlugMGen3-2", "S3PL-10112EU", "PlugMG3", 3), null);

    public static ConnectionTestResult Failed(string error) => new(false, null, null, error);
}

/// <summary>Builds curves for manual tests.</summary>
internal static class Points
{
    public static IReadOnlyList<CurvePoint> Valid => [new(100, 80), new(200, 180), new(400, 370)];
}
