using System.Security.Cryptography;
using System.Text;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Identity;

/// <summary>
/// Salted SHA-256 hash, format <c>v1.{salt}.{hash}</c>. Good for high-entropy or short-lived secrets (recovery codes, OTP).
/// Not for passwords: those use the Identity password hasher.
/// </summary>
public sealed class Sha256SecretHasher : ISecretHasher
{
    private const string Prefix = "v1";

    /// <inheritdoc />
    public string Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return $"{Prefix}.{Convert.ToHexString(salt)}.{Convert.ToHexString(Compute(salt, secret))}";
    }

    /// <inheritdoc />
    public bool Verify(string secret, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Compute(Convert.FromHexString(parts[1]), secret), Convert.FromHexString(parts[2]));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Compute(byte[] salt, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var data = new byte[salt.Length + bytes.Length];
        salt.CopyTo(data, 0);
        bytes.CopyTo(data, salt.Length);
        return SHA256.HashData(data);
    }
}
