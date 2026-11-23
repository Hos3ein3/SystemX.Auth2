namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>What a key is for.</summary>
public enum KeyPurpose
{
    /// <summary>Signs tokens.</summary>
    Signing = 0,
    /// <summary>Encrypts refresh tokens and authorization codes.</summary>
    Encryption = 1,
}

/// <summary>
/// One key of the key ring (a self-signed certificate with its private key). The certificate file is protected with Data Protection.
/// A retired key is no longer used to sign, but still checks and decrypts until <see cref="RemoveAtUtc"/>.
/// </summary>
public class KeyRingEntry
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Purpose.</summary>
    public KeyPurpose Purpose { get; set; }
    /// <summary>PFX (base64) protected with Data Protection.</summary>
    public string ProtectedPfx { get; set; } = "";
    /// <summary>Certificate thumbprint (public).</summary>
    public string Thumbprint { get; set; } = "";
    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>End of the certificate validity. A later date wins when OpenIddict picks the key to sign with.</summary>
    public DateTimeOffset NotAfterUtc { get; set; }
    /// <summary>When the key was replaced by a newer one, or null while it is current.</summary>
    public DateTimeOffset? RetiredAtUtc { get; set; }
    /// <summary>After this time the key is deleted (null while current).</summary>
    public DateTimeOffset? RemoveAtUtc { get; set; }
}
