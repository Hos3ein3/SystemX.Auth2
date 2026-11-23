namespace SystemX.Auth2.Server.Infrastructure.OpenIddict;

/// <summary>One certificate file.</summary>
public sealed class CertificateFileOptions
{
    /// <summary>Path of the PFX file.</summary>
    public string Path { get; set; } = "";
    /// <summary>Password of the PFX file. Keep it in a secret store.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Signing and encryption keys. Section <c>Auth2:Keys</c>. When certificate files are configured they are used (the one valid the longest signs, all validate).
/// Otherwise the server keeps its own keys in the database (the key ring): created on first start, protected with Data Protection,
/// shared by all instances, rotated with the <c>rotate-keys</c> command.
/// </summary>
public sealed class Auth2KeyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth2:Keys";

    /// <summary>Certificates used to sign tokens.</summary>
    public List<CertificateFileOptions> SigningCertificates { get; set; } = [];

    /// <summary>Certificates used to encrypt tokens (refresh tokens, authorization codes).</summary>
    public List<CertificateFileOptions> EncryptionCertificates { get; set; } = [];

    /// <summary>Days a new key is valid (the certificate validity).</summary>
    public int KeyLifetimeDays { get; set; } = 730;

    /// <summary>
    /// Days an old key stays in use for checking and decrypting after a rotation. Must be longer than the longest refresh token,
    /// or users lose their sessions. Default 35 (the default refresh token lasts 30 days).
    /// </summary>
    public int RetiredKeyDays { get; set; } = 35;

    /// <summary>Seconds between checks for keys changed by another instance.</summary>
    public int RefreshSeconds { get; set; } = 30;

    /// <summary>True when at least one signing and one encryption certificate are set.</summary>
    public bool HasCertificates => SigningCertificates.Count > 0 && EncryptionCertificates.Count > 0;
}
