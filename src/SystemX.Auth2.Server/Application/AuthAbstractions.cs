using System.Security.Claims;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>Input of a password login. The application always comes from <see cref="IApplicationContext"/>.</summary>
/// <param name="Login">User name, email or phone number.</param>
/// <param name="Password">The password. Never logged.</param>
/// <param name="IpAddress">Caller IP (for the audit log).</param>
/// <param name="UserAgent">Caller user agent (for the audit log).</param>
public sealed record PasswordLoginRequest(string Login, string Password, string? IpAddress = null, string? UserAgent = null);

/// <summary>Input of a login with a one-time code.</summary>
/// <param name="Login">Email or phone number.</param>
/// <param name="Code">The one-time code. Never logged.</param>
/// <param name="IpAddress">Caller IP (for the audit log).</param>
/// <param name="UserAgent">Caller user agent (for the audit log).</param>
public sealed record OtpLoginRequest(string Login, string Code, string? IpAddress = null, string? UserAgent = null);

/// <summary>
/// Input of a login that an external source already proved (social, OIDC, SAML, LDAP, passkey). The user is known; this request
/// only asks the one login pipeline (application, method, membership, lockout, two-factor, session) whether the user may enter.
/// </summary>
/// <param name="User">The user that the external source identified.</param>
/// <param name="Method">The login method: <c>External</c>, <c>Ldap</c> or <c>Passkey</c>.</param>
/// <param name="ProviderName">Name of the provider (becomes the <c>idp</c> claim). Null for passkeys.</param>
/// <param name="Amr">Authentication method references for the <c>amr</c> claim.</param>
/// <param name="SkipLocalTwoFactor">The source already used several factors (trusted provider MFA, passkey with user verification).</param>
/// <param name="IpAddress">Caller IP (for the audit log).</param>
/// <param name="UserAgent">Caller user agent (for the audit log).</param>
public sealed record ExternalSignInRequest(AppUser User, LoginMethod Method, string? ProviderName, IReadOnlyList<string> Amr, bool SkipLocalTwoFactor,
    string? IpAddress = null, string? UserAgent = null);

/// <summary>Result of a login step.</summary>
public sealed record LoginResult
{
    /// <summary>True when the user is fully signed in.</summary>
    public bool Succeeded { get; init; }
    /// <summary>The user, when <see cref="Succeeded"/>.</summary>
    public AppUser? User { get; init; }
    /// <summary>The application the login was for.</summary>
    public Auth2Application? Application { get; init; }
    /// <summary>Stable error code (<c>Auth2ErrorCodes</c>), when not succeeded.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Short-lived token for the second step, when <c>ErrorCode</c> is <c>two_factor_required</c>.</summary>
    public string? TwoFactorToken { get; init; }
    /// <summary>Authentication methods used (<c>pwd</c>, <c>otp</c>).</summary>
    public IReadOnlyList<string> Amr { get; init; } = [];
    /// <summary>Id of the new session (<c>sid</c> claim).</summary>
    public string? SessionId { get; init; }
    /// <summary>Name of the external provider the user came from (<c>idp</c> claim), or null for local logins.</summary>
    public string? IdentityProvider { get; init; }

    /// <summary>Failure result.</summary>
    public static LoginResult Fail(string code) => new() { ErrorCode = code };
}

/// <summary>
/// The one place that decides if a login is valid. Order: application enabled, user belongs to the application (multi mode),
/// login method allowed, lockout, password, two-factor, single active session. Consent is checked by the authorize endpoint (phase 6).
/// Wrong user, wrong password and "not registered here" all return <c>invalid_credentials</c>.
/// </summary>
public interface ILoginService
{
    /// <summary>Checks a login and password.</summary>
    Task<LoginResult> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Login with a one-time code (<c>PhoneOtp</c> or <c>EmailOtp</c>). Every failure is <c>invalid_otp</c>.
    /// A right code also proves the email or phone, so it is marked as confirmed.
    /// </summary>
    Task<LoginResult> OtpLoginAsync(OtpLoginRequest request, CancellationToken ct = default);

    /// <summary>Login of a user that an external source proved (see <see cref="ExternalSignInRequest"/>). Same rules as every other login.</summary>
    Task<LoginResult> ExternalLoginAsync(ExternalSignInRequest request, CancellationToken ct = default);

    /// <summary>Second step: checks a TOTP code or a recovery code.</summary>
    Task<LoginResult> CompleteTwoFactorAsync(string twoFactorToken, string? code, string? recoveryCode, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>True when the subject of an existing token or cookie may still sign in (enabled, active, member, not locked out).</summary>
    Task<bool> IsSubjectValidAsync(AppUser user, CancellationToken ct = default);
}

/// <summary>Builds the claims principal for tokens. Roles are the roles of the current application.</summary>
public interface ITokenIssuer
{
    /// <summary>Creates the principal of a user for the current application.</summary>
    Task<ClaimsPrincipal> CreateUserPrincipalAsync(AppUser user, IEnumerable<string> scopes, IEnumerable<string> amr, string? sessionId, CancellationToken ct = default, string? identityProvider = null);

    /// <summary>Creates the principal of a machine client (client credentials).</summary>
    Task<ClaimsPrincipal> CreateMachinePrincipalAsync(IEnumerable<string> scopes, CancellationToken ct = default);

    /// <summary>Refreshes roles and the access version of an existing user principal (used for the refresh token grant).</summary>
    Task RefreshUserClaimsAsync(ClaimsPrincipal principal, AppUser user, CancellationToken ct = default);
}

/// <summary>Single active session and session revoking.</summary>
public interface ISessionService
{
    /// <summary>
    /// Revokes all tokens and authorizations of the user in the application. Returns the number of revoked tokens.
    /// An authorization id in <paramref name="keepAuthorizationId"/> is left alone (the session that asked).
    /// </summary>
    Task<int> RevokeAllAsync(AppUser user, Auth2Application application, string reason, string? keepAuthorizationId = null, CancellationToken ct = default);

    /// <summary>Lists the open sessions of the user in the application.</summary>
    Task<IReadOnlyList<SessionInfo>> ListAsync(AppUser user, Auth2Application application, CancellationToken ct = default);

    /// <summary>Revokes one session of the user. Returns false when it is not theirs.</summary>
    Task<bool> RevokeAsync(AppUser user, Auth2Application application, string authorizationId, CancellationToken ct = default);
}

/// <summary>Checks return URLs. Open redirect protection.</summary>
public interface IReturnUrlValidator
{
    /// <summary>
    /// True when the URL is a safe local path, or an absolute URL that exactly matches one of <paramref name="allowedAbsolute"/>.
    /// </summary>
    bool IsAllowed(string? url, IEnumerable<string> allowedAbsolute);

    /// <summary>Returns the first allowed value in the order: return URL, application fallback, <paramref name="home"/>.</summary>
    string Resolve(string? returnUrl, string? applicationFallback, ApplicationSettings settings, string home = "/");
}
