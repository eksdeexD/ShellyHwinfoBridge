using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ShellyHwinfoBridge.Core.Settings;

/// <summary>Encrypts and decrypts the stored device password.</summary>
public interface IPasswordProtector
{
    /// <summary>Encrypts a password to a storable string.</summary>
    /// <param name="plainText">The password.</param>
    string Protect(string plainText);

    /// <summary>Decrypts a stored password.</summary>
    /// <param name="protectedText">The stored value.</param>
    /// <exception cref="CryptographicException">The value cannot be decrypted (e.g. another user's data).</exception>
    string Unprotect(string protectedText);
}

/// <summary>DPAPI (CurrentUser scope) password protection; output is base64.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiPasswordProtector : IPasswordProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ShellyHwinfoBridge.DevicePassword");

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        byte[] data;
        try
        {
            data = Convert.FromBase64String(protectedText);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The stored password is not valid base64.", ex);
        }

        return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser));
    }
}
