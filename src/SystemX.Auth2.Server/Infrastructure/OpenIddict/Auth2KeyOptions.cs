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
/// Signing and encryption keys. Section <c>Auth2:Keys</c>. Several certificates are allowed: the first valid one signs,
/// all of them validate. That is how key rotation works (see <c>security.md</c>, phase 10).
/// In Development and Testing, temporary keys are made when none are configured.
/// </summary>
public sealed class Auth2KeyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth2:Keys";

    /// <summary>Certificates used to sign tokens.</summary>
    public List<CertificateFileOptions> SigningCertificates { get; set; } = [];

    /// <summary>Certificates used to encrypt tokens (refresh tokens, authorization codes).</summary>
    public List<CertificateFileOptions> EncryptionCertificates { get; set; } = [];

    /// <summary>True when at least one signing and one encryption certificate are set.</summary>
    public bool HasCertificates => SigningCertificates.Count > 0 && EncryptionCertificates.Count > 0;
}
