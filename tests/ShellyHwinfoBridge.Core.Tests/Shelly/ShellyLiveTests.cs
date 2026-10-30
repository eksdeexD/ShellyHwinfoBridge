using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Tests.Shelly;

/// <summary>
/// Opt-in tests against a real plug. They return immediately unless the environment variable
/// <c>SHB_SHELLY_HOST</c> is set. The password, if any, is read from the file named by
/// <c>SHB_SHELLY_PASSWORD_FILE</c>; it is never printed or passed to an assertion.
/// </summary>
public class ShellyLiveTests
{
    private const string HostVariable = "SHB_SHELLY_HOST";
    private const string PasswordFileVariable = "SHB_SHELLY_PASSWORD_FILE";

    [Fact]
    public async Task LiveDeviceReportsEnergyTotalAndDeviceInfo()
    {
        var host = Environment.GetEnvironmentVariable(HostVariable);
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        using var client = new ShellyClient(host, ReadPassword());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var reading = await client.GetReadingAsync(timeout.Token);
        var info = await client.GetDeviceInfoAsync(timeout.Token);

        Assert.True(reading.EnergyTotalWh is >= 0, "aenergy.total missing or negative");
        Assert.True(reading.ActivePowerWatts >= 0, "apower negative");
        Assert.False(string.IsNullOrEmpty(info.Id), "GetDeviceInfo returned no id");
    }

    private static string? ReadPassword()
    {
        var file = Environment.GetEnvironmentVariable(PasswordFileVariable);
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var password = File.ReadAllText(file).Trim();
        return password.Length == 0 ? null : password;
    }
}
