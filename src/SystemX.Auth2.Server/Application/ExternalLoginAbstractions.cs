using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Application;

/// <summary>What <see cref="IExternalLoginService"/> needs to decide a sign-in that an external source proved.</summary>
/// <param name="Provider">The provider (from the registry).</param>
/// <param name="Identity">The checked identity.</param>
/// <param name="ReturnUrl">Where to go afterwards.</param>
/// <param name="IpAddress">Caller IP, for the audit log.</param>
/// <param name="UserAgent">Caller user agent, for the audit log.</param>
/// <param name="IsDirectoryLogin">True for LDAP: the method is <c>Ldap</c> and the amr is <c>pwd</c>.</param>
public sealed record ExternalLoginRequest(RegisteredProvider Provider, ExternalIdentity Identity, string ReturnUrl, string? IpAddress = null, string? UserAgent = null, bool IsDirectoryLogin = false);

/// <summary>The decision about an external sign-in.</summary>
public sealed record ExternalLoginOutcome
{
    /// <summary>The login result when a user was found or made. Null when <see cref="ErrorCode"/> or <see cref="NeedsEmail"/> is set.</summary>
    public LoginResult? Login { get; init; }
    /// <summary>Stable error code when the sign-in was refused before the login pipeline.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>The source gave no email and the application needs one: the user must enter and confirm it (<see cref="PendingToken"/>).</summary>
    public bool NeedsEmail { get; init; }
    /// <summary>Sealed, short-lived copy of the identity for the email step.</summary>
    public string? PendingToken { get; init; }
    /// <summary>A new user was created.</summary>
    public bool Registered { get; init; }
    /// <summary>A login was added to an existing user.</summary>
    public bool Linked { get; init; }
    /// <summary>Where to go afterwards.</summary>
    public string ReturnUrl { get; init; } = "/";
}

/// <summary>The one decision table for OIDC, SAML and LDAP sign-ins: find a link, link by verified email, register, map claims and groups, sign in.</summary>
public interface IExternalLoginService
{
    /// <summary>Decides and signs in.</summary>
    Task<ExternalLoginOutcome> HandleAsync(ExternalLoginRequest request, CancellationToken ct = default);

    /// <summary>Finishes a sign-in that needed an email (without proof: the caller must send the confirmation mail first).</summary>
    Task<ExternalLoginOutcome> CompleteWithEmailAsync(string pendingToken, string email, CancellationToken ct = default);

    /// <summary>Sends the confirmation mail for the email step. Returns false when the token is not valid.</summary>
    Task<bool> SendEmailConfirmationAsync(string pendingToken, string email, string confirmUrlBase, CancellationToken ct = default);

    /// <summary>Opens the link of a confirmation mail and finishes the sign-in.</summary>
    Task<ExternalLoginOutcome> ConfirmEmailAsync(string confirmationToken, CancellationToken ct = default);
}

/// <summary>Applies the mappings of an application to a user at login.</summary>
public interface IExternalMappingService
{
    /// <summary>
    /// Adds the roles, groups and permissions that the identity earns, and removes only those that some mapping of this provider mentions
    /// but the identity no longer earns. Things that no mapping mentions are never touched. Returns how many changes were made.
    /// </summary>
    Task<int> ApplyAsync(Guid applicationId, string providerName, AppUser user, ExternalIdentity identity, CancellationToken ct = default);
}

/// <summary>Lists, links and unlinks the external logins of a user in the current application.</summary>
public interface IExternalLinkService
{
    /// <summary>The links of the user.</summary>
    Task<IReadOnlyList<ExternalLinkInfo>> ListAsync(AppUser user, CancellationToken ct = default);
    /// <summary>Adds a link. Fails with <c>external_claims_invalid</c> when the identity belongs to another user already.</summary>
    Task<OperationResult> LinkAsync(AppUser user, ExternalIdentity identity, CancellationToken ct = default);
    /// <summary>Removes a link. Fails with <c>cannot_remove_last_login_method</c> when it is the last way to sign in.</summary>
    Task<OperationResult> UnlinkAsync(AppUser user, string providerName, string providerKey, CancellationToken ct = default);
}

/// <summary>One external link of a user.</summary>
public sealed record ExternalLinkInfo(string Provider, string ProviderKey, string? DisplayName);
