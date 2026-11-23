using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>Encrypts and decrypts the secrets of external providers (client secrets, bind passwords, private keys). Keys are the shared Data Protection keys.</summary>
public interface IExternalSecretProtector
{
    /// <summary>Encrypts a secret. An empty text gives null.</summary>
    string? Protect(string? plain);
    /// <summary>Decrypts a secret. Null or empty gives null. A broken value throws <see cref="CryptographicException"/>.</summary>
    string? Unprotect(string? protectedValue);
    /// <summary>Returns a copy of the provider without secrets, safe to show or log.</summary>
    ExternalProviderSettings Redact(ExternalProviderSettings settings);
}

/// <inheritdoc />
public sealed class ExternalSecretProtector(IDataProtectionProvider provider) : IExternalSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("SystemX.Auth2.ExternalProviderSecrets.v1");

    /// <inheritdoc />
    public string? Protect(string? plain) => string.IsNullOrEmpty(plain) ? null : _protector.Protect(plain);

    /// <inheritdoc />
    public string? Unprotect(string? protectedValue) => string.IsNullOrEmpty(protectedValue) ? null : _protector.Unprotect(protectedValue);

    /// <inheritdoc />
    public ExternalProviderSettings Redact(ExternalProviderSettings settings)
    {
        var copy = Persistence.SettingsJson.CloneProvider(settings);
        switch (copy)
        {
            case OidcProviderSettings o: o.ClientSecretProtected = null; o.PrivateKeyProtected = null; break;
            case SamlProviderSettings m: m.SpSigningCertificateProtected = null; break;
            case LdapProviderSettings l: l.BindPasswordProtected = null; break;
        }
        return copy;
    }
}
