using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly;

namespace ShellyHwinfoBridge.Core.Tests.TestSupport;

/// <summary>A Shelly client that returns a fixed reading (or throws) and records its use.</summary>
internal sealed class FakeAppShellyClient(string host, string? password) : IShellyClient, IDisposable
{
    public string Host { get; } = host;

    public string? Password { get; } = password;

    public ShellyReading Reading { get; set; } = new(312.4, 226.9, 1.38, 1000);

    public Exception? Error { get; set; }

    public int Calls { get; private set; }

    public bool Disposed { get; private set; }

    public Task<ShellyReading> GetReadingAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        Calls++;
        return Error is null ? Task.FromResult(Reading) : Task.FromException<ShellyReading>(Error);
    }

    public Task<ShellyDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default) =>
        Error is null
            ? Task.FromResult(new ShellyDeviceInfo("PlugMGen3-2", "S3PL-10112EU", "PlugMG3", 3, "shellyplugmg3-000000"))
            : Task.FromException<ShellyDeviceInfo>(Error);

    public void Dispose() => Disposed = true;
}

/// <summary>Test stand-in for DPAPI: reverses and prefixes the text.</summary>
internal sealed class FakePasswordProtector : IPasswordProtector
{
    public string Protect(string plainText) => "enc:" + new string(plainText.Reverse().ToArray());

    public string Unprotect(string protectedText) => new(protectedText["enc:".Length..].Reverse().ToArray());
}
