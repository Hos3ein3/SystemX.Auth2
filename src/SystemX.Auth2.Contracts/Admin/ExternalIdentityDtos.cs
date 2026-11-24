using System.Text.Json;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Contracts.Admin;

/// <summary>One setting field of a provider type (for a form in the admin UI). <c>LabelKey</c> and <c>HelpKey</c> are resource keys.</summary>
public sealed record ProviderFieldDto(string Name, string LabelKey, string Kind, bool Required, string? Default, string? HelpKey);

/// <summary>A provider type with its fields, defaults and the page that explains the setup.</summary>
public sealed record ExternalProviderTypeDto(ExternalProviderType Type, string DisplayName, string SettingsKind, IReadOnlyList<ProviderFieldDto> Fields,
    IReadOnlyList<string> DefaultScopes, string SetupDocsAnchor, bool EmailVerifiedByDefault);

/// <summary>
/// A provider of an application as the admin API shows it. Secrets are never included: <c>SecretIsSet</c> says which secrets exist.
/// <c>Settings</c> is the settings object (with the <c>kind</c> field) without any <c>*Protected</c> value.
/// <c>Urls</c> are the addresses to enter at the provider (callback, or ACS, SLO and metadata).
/// </summary>
public sealed record ExternalProviderAdminDto(string Name, ExternalProviderType Type, bool Enabled, string? DisplayName, JsonElement Settings,
    IReadOnlyDictionary<string, bool> SecretIsSet, IReadOnlyDictionary<string, string> Urls, string RegistrationName, IReadOnlyList<string> Warnings);

/// <summary>Result of a provider test. <c>Details</c> never contain secrets.</summary>
public sealed record ExternalProviderTestResult(bool Ok, string? ErrorCode, string? Reason, IReadOnlyDictionary<string, string> Details);

/// <summary>The public part of a new SAML service provider certificate.</summary>
public sealed record SamlSpCertificateDto(string CertificatePem, DateTimeOffset NotAfter, string Thumbprint);

/// <summary>New secret values. Fill only the one that fits the provider type: <c>ClientSecret</c> (OIDC), <c>PrivateKey</c> (Apple), <c>BindPassword</c> (LDAP).</summary>
public sealed record RotateProviderSecretRequest(string? ClientSecret, string? PrivateKey, string? BindPassword);

/// <summary>A user name to look up in the directory (no password).</summary>
public sealed record LdapTestUserRequest(string UserName);

/// <summary>What a directory lookup found.</summary>
public sealed record LdapTestUserResult(string Dn, string Subject, IReadOnlyDictionary<string, string[]> Attributes, IReadOnlyList<string> Groups);

/// <summary>A group, claim or attribute to role, group or permission mapping.</summary>
public sealed record ExternalMappingDto(Guid Id, string? ProviderName, string SourceType, string SourceName, string MatchType, string SourceValue, string TargetType, Guid TargetId);

/// <summary>Create or change a mapping. <c>SourceType</c> is <c>Group</c>, <c>Claim</c> or <c>Attribute</c>; <c>MatchType</c> is <c>Exact</c> or <c>Prefix</c>; <c>TargetType</c> is <c>Role</c>, <c>Group</c> or <c>Permission</c>.</summary>
public sealed record ExternalMappingRequest(string? ProviderName, string SourceType, string SourceName, string MatchType, string SourceValue, string TargetType, Guid TargetId);

/// <summary>One provider and its email domains (home-realm discovery).</summary>
public sealed record EmailDomainRouteDto(string ProviderName, IReadOnlyList<string> Domains);

/// <summary>The external login of a user (the provider key is the id at the provider).</summary>
public sealed record UserExternalLoginDto(string Provider, string ProviderKey, string? DisplayName);

/// <summary>The authenticator models (AAGUID) that cannot be registered as passkeys.</summary>
public sealed record BlockedAaguidsDto(IReadOnlyList<Guid> Aaguids);
