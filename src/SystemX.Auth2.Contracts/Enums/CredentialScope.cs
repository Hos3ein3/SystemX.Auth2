using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Where credentials (password, 2FA, lockout) are stored.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CredentialScope>))]
public enum CredentialScope
{
    /// <summary>Each application has its own credentials.</summary>
    PerApplication,
    /// <summary>All applications share one set of credentials.</summary>
    Shared,
}
