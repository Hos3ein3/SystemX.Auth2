using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Auth;

namespace SystemX.Auth2.Server.Application;

/// <summary>The admin side of external providers, mappings, links and passkeys. Always works on one application.</summary>
public interface IAdminExternalService
{
    /// <summary>All provider types with their fields.</summary>
    IReadOnlyList<ExternalProviderTypeDto> Types();
    /// <summary>The providers of an application (enabled or not), without secrets.</summary>
    Task<IReadOnlyList<ExternalProviderAdminDto>?> ListProvidersAsync(Guid applicationId, CancellationToken ct = default);
    /// <summary>Adds a provider. <paramref name="body"/> is the provider JSON with the secret in plain fields (<c>clientSecret</c>, <c>privateKey</c>, <c>bindPassword</c>).</summary>
    Task<(OperationResult Result, ExternalProviderAdminDto? Provider)> CreateProviderAsync(Guid applicationId, string body, bool isGlobalAdmin, CancellationToken ct = default);
    /// <summary>Changes a provider. An empty or missing secret keeps the old one. <paramref name="ifMatchVersion"/> is the settings version of the application.</summary>
    Task<(OperationResult Result, ExternalProviderAdminDto? Provider)> UpdateProviderAsync(Guid applicationId, string name, string body, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default);
    /// <summary>Replaces the secret.</summary>
    Task<OperationResult> RotateSecretAsync(Guid applicationId, string name, RotateProviderSecretRequest request, CancellationToken ct = default);
    /// <summary>Removes a provider. The links of users stay in the database.</summary>
    Task<OperationResult> DeleteProviderAsync(Guid applicationId, string name, CancellationToken ct = default);
    /// <summary>Tests a provider without signing anybody in.</summary>
    Task<(OperationResult Result, ExternalProviderTestResult? Test)> TestProviderAsync(Guid applicationId, string name, CancellationToken ct = default);
    /// <summary>LDAP: looks up a user name (no password).</summary>
    Task<(OperationResult Result, LdapTestUserResult? Found)> LdapTestUserAsync(Guid applicationId, string name, string userName, CancellationToken ct = default);
    /// <summary>SAML: reads the identity provider metadata again now.</summary>
    Task<(OperationResult Result, ExternalProviderTestResult? Test)> SamlRefreshMetadataAsync(Guid applicationId, string name, CancellationToken ct = default);
    /// <summary>SAML: makes a new service provider certificate. Returns only the public part.</summary>
    Task<(OperationResult Result, SamlSpCertificateDto? Certificate)> SamlGenerateCertificateAsync(Guid applicationId, string name, CancellationToken ct = default);

    /// <summary>Mappings of the application.</summary>
    Task<IReadOnlyList<ExternalMappingDto>> ListMappingsAsync(Guid applicationId, CancellationToken ct = default);
    /// <summary>Adds a mapping.</summary>
    Task<(OperationResult Result, ExternalMappingDto? Mapping)> CreateMappingAsync(Guid applicationId, ExternalMappingRequest request, CancellationToken ct = default);
    /// <summary>Changes a mapping.</summary>
    Task<(OperationResult Result, ExternalMappingDto? Mapping)> UpdateMappingAsync(Guid applicationId, Guid id, ExternalMappingRequest request, CancellationToken ct = default);
    /// <summary>Removes a mapping.</summary>
    Task<OperationResult> DeleteMappingAsync(Guid applicationId, Guid id, CancellationToken ct = default);

    /// <summary>The email domains of every provider.</summary>
    Task<IReadOnlyList<EmailDomainRouteDto>?> ListEmailDomainRoutesAsync(Guid applicationId, CancellationToken ct = default);
    /// <summary>Replaces the email domains of all providers at once. A domain belongs to one provider.</summary>
    Task<OperationResult> SetEmailDomainRoutesAsync(Guid applicationId, IReadOnlyList<EmailDomainRouteDto> routes, CancellationToken ct = default);

    /// <summary>External logins of a user of the current application.</summary>
    Task<(OperationResult Result, IReadOnlyList<UserExternalLoginDto> Logins)> ListUserLoginsAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Removes an external login of a user (support case). A global admin may force it when it is the last way to sign in.</summary>
    Task<OperationResult> RemoveUserLoginAsync(Guid userId, string provider, bool force, CancellationToken ct = default);
    /// <summary>Passkeys of a user of the current application.</summary>
    Task<(OperationResult Result, IReadOnlyList<PasskeyDto> Passkeys)> ListUserPasskeysAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Removes a passkey of a user (for example a lost device).</summary>
    Task<OperationResult> RemoveUserPasskeyAsync(Guid userId, string passkeyId, CancellationToken ct = default);

    /// <summary>The blocked authenticator models (from the configuration and from the database).</summary>
    Task<BlockedAaguidsDto> GetBlockedAaguidsAsync(CancellationToken ct = default);
    /// <summary>Replaces the blocked authenticator models that are kept in the database.</summary>
    Task<OperationResult> SetBlockedAaguidsAsync(IReadOnlyList<Guid> aaguids, CancellationToken ct = default);
}

/// <summary>Small server-wide values that an admin may change at run time (kept in the database).</summary>
public interface IServerValues
{
    /// <summary>A value, or null.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    /// <summary>Sets a value.</summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
