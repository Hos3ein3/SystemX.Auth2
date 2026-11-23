using System.Security.Claims;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>Input of a password login. The application always comes from <see cref="IApplicationContext"/>.</summary>
/// <param name="Login">User name, email or phone number.</param>
/// <param name="Password">The password. Never logged.</param>
/// <param name="IpAddress">Caller IP (for the audit log).</param>
/// <param name="UserAgent">Caller user agent (for the audit log).</param>
public sealed record PasswordLoginRequest(string Login, string Password, string? IpAddress = null, string? UserAgent = null);

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

    /// <summary>Second step: checks a TOTP code or a recovery code.</summary>
    Task<LoginResult> CompleteTwoFactorAsync(string twoFactorToken, string? code, string? recoveryCode, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>True when the subject of an existing token or cookie may still sign in (enabled, active, member, not locked out).</summary>
    Task<bool> IsSubjectValidAsync(AppUser user, CancellationToken ct = default);
}

/// <summary>Builds the claims principal for tokens. Roles are the roles of the current application.</summary>
public interface ITokenIssuer
{
    /// <summary>Creates the principal of a user for the current application.</summary>
    Task<ClaimsPrincipal> CreateUserPrincipalAsync(AppUser user, IEnumerable<string> scopes, IEnumerable<string> amr, string? sessionId, CancellationToken ct = default);

    /// <summary>Creates the principal of a machine client (client credentials).</summary>
    Task<ClaimsPrincipal> CreateMachinePrincipalAsync(IEnumerable<string> scopes, CancellationToken ct = default);

    /// <summary>Refreshes roles and the access version of an existing user principal (used for the refresh token grant).</summary>
    Task RefreshUserClaimsAsync(ClaimsPrincipal principal, AppUser user, CancellationToken ct = default);
}

/// <summary>Single active session and session revoking.</summary>
public interface ISessionService
{
    /// <summary>Revokes all tokens and authorizations of the user in the application. Returns the number of revoked tokens.</summary>
    Task<int> RevokeAllAsync(AppUser user, Auth2Application application, string reason, CancellationToken ct = default);
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
