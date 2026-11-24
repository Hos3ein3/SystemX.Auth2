using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>
/// Reads the rules of the current application. Identity options are global and cannot change per request,
/// so validators and managers ask this resolver instead.
/// </summary>
public interface IAuthFlowResolver
{
    /// <summary>The current application.</summary>
    Task<Project> GetProjectAsync(CancellationToken ct = default);

    /// <summary>Settings of the current project.</summary>
    Task<ProjectSettings> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>The calling application (from the client id of the request), or null when the request has none.</summary>
    Task<ClientApplicationInfo?> GetClientAsync(CancellationToken ct = default);

    /// <summary>Project settings with the application overrides applied (the effective values for this request).</summary>
    Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>
    /// The shared gate. Returns <c>project_disabled</c> or <c>application_disabled</c> when the request must be refused, else null.
    /// Every sign-in path asks this one method. The public answer is always <c>access_denied</c>; the real reason goes to the audit log.
    /// </summary>
    Task<string?> CheckGateAsync(CancellationToken ct = default);

    /// <summary>Where credentials live for the current application. Single mode always gives <see cref="CredentialScope.Shared"/>.</summary>
    Task<CredentialScope> GetCredentialScopeAsync(CancellationToken ct = default);
}

/// <summary>
/// Where the credential state (password, security stamp, 2FA, lockout, TOTP key, recovery codes) of a user is read and written.
/// Two strategies: the user row, or the <c>user_projects</c> row of the current project.
/// </summary>
public interface ICredentialState
{
    /// <summary>Password hash.</summary>
    Task<string?> GetPasswordHashAsync(AppUser user, CancellationToken ct);
    /// <summary>Sets the password hash.</summary>
    Task SetPasswordHashAsync(AppUser user, string? hash, CancellationToken ct);
    /// <summary>Security stamp.</summary>
    Task<string?> GetSecurityStampAsync(AppUser user, CancellationToken ct);
    /// <summary>Sets the security stamp.</summary>
    Task SetSecurityStampAsync(AppUser user, string stamp, CancellationToken ct);
    /// <summary>Is TOTP two-factor on.</summary>
    Task<bool> GetTwoFactorEnabledAsync(AppUser user, CancellationToken ct);
    /// <summary>Turns two-factor on or off.</summary>
    Task SetTwoFactorEnabledAsync(AppUser user, bool enabled, CancellationToken ct);
    /// <summary>End of lockout.</summary>
    Task<DateTimeOffset?> GetLockoutEndAsync(AppUser user, CancellationToken ct);
    /// <summary>Sets the end of lockout.</summary>
    Task SetLockoutEndAsync(AppUser user, DateTimeOffset? end, CancellationToken ct);
    /// <summary>Failed attempts so far.</summary>
    Task<int> GetAccessFailedCountAsync(AppUser user, CancellationToken ct);
    /// <summary>Adds one failed attempt and returns the new count.</summary>
    Task<int> IncrementAccessFailedCountAsync(AppUser user, CancellationToken ct);
    /// <summary>Sets failed attempts to zero.</summary>
    Task ResetAccessFailedCountAsync(AppUser user, CancellationToken ct);
    /// <summary>Can this user be locked out.</summary>
    Task<bool> GetLockoutEnabledAsync(AppUser user, CancellationToken ct);
    /// <summary>Sets whether the user can be locked out.</summary>
    Task SetLockoutEnabledAsync(AppUser user, bool enabled, CancellationToken ct);
    /// <summary>TOTP key (decrypted).</summary>
    Task<string?> GetAuthenticatorKeyAsync(AppUser user, CancellationToken ct);
    /// <summary>Saves the TOTP key (encrypted at rest).</summary>
    Task SetAuthenticatorKeyAsync(AppUser user, string? key, CancellationToken ct);
    /// <summary>Replaces all recovery codes. Only hashes are stored.</summary>
    Task ReplaceRecoveryCodesAsync(AppUser user, IEnumerable<string> codes, CancellationToken ct);
    /// <summary>Uses one recovery code. True when the code was valid and unused.</summary>
    Task<bool> RedeemRecoveryCodeAsync(AppUser user, string code, CancellationToken ct);
    /// <summary>Number of unused recovery codes.</summary>
    Task<int> CountRecoveryCodesAsync(AppUser user, CancellationToken ct);
    /// <summary>Called before a new user is saved, so pending credential rows can be saved in the same transaction.</summary>
    void BeforeUserCreated(AppUser user);
}

/// <summary>Chooses the <see cref="ICredentialState"/> strategy for the current application.</summary>
public interface ICredentialStateProvider
{
    /// <summary>Returns the strategy for the current application.</summary>
    Task<ICredentialState> GetAsync(CancellationToken ct = default);
}

/// <summary>Hashes secrets such as recovery codes and OTP codes. Never reversible.</summary>
public interface ISecretHasher
{
    /// <summary>Hashes a secret with a random salt.</summary>
    string Hash(string secret);
    /// <summary>Checks a secret in constant time.</summary>
    bool Verify(string secret, string hash);
}

/// <summary>Gives localized texts from the resx files. Order: selected culture, then English, then the key itself.</summary>
public interface IAuth2Localizer
{
    /// <summary>Text for an error code (<c>Auth2ErrorCodes</c>) or an Identity error key.</summary>
    string Error(string key, params object[] args);
    /// <summary>Text for a validation key.</summary>
    string Validation(string key, params object[] args);
}
