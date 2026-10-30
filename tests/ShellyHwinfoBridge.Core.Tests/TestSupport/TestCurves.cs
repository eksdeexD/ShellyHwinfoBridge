using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;

namespace ShellyHwinfoBridge.Core.Tests.TestSupport;

internal static class TestCurves
{
    public static PsuEntry Tx700 => PsuDatabaseLoader.LoadEmbedded().Psus.Single(p => p.Id == "seasonic-prime-fanless-tx-700");

    public static PsuCurve Tx700At230 => Tx700.GetCurve(InputVoltage.V230)!;

    public static PsuCurve Simple => new([new CurvePoint(100, 80), new CurvePoint(200, 180)]);
}
