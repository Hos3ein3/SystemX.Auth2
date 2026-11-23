using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Client.Services;

/// <summary>
/// Calls the Auth2 Server. Every method returns <see cref="Result"/> or <see cref="Result{T}"/> and never throws for server errors
/// (a wrong password is a normal answer). The error code is in <c>Result.Error.Code</c>; a server that cannot be reached gives
/// <see cref="Auth2ClientErrorCodes.ServerUnavailable"/>. Calls that change something (register, login, password change) are never repeated automatically.
/// </summary>
public interface IAuth2ApiClientService
{
    /// <summary>Registers a user (or adds an existing person; see <c>RegisterResponse</c>). Needs the client scope <c>auth2.register</c>.</summary>
    Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Signs a user in with the password grant (the server must allow <c>AllowSelfUiLogin</c>).
    /// When two-factor is needed the result succeeds with <c>TwoFactorRequired = true</c> and a <c>TwoFactorToken</c>: call <see cref="TwoFactorLoginAsync"/> next.
    /// </summary>
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Second step of a login: the authenticator code or a recovery code.</summary>
    Task<Result<LoginResponse>> TwoFactorLoginAsync(TwoFactorRequest request, CancellationToken ct = default);

    /// <summary>Signs a user in with a one-time code (ask for it with <see cref="RequestOtpAsync"/> first).</summary>
    Task<Result<LoginResponse>> OtpLoginAsync(string login, string code, CancellationToken ct = default);

    /// <summary>Sends a one-time code by email or SMS. The answer is always the same, so nobody can find out who has an account.</summary>
    Task<Result> RequestOtpAsync(OtpRequest request, CancellationToken ct = default);

    /// <summary>Starts a password reset. The answer is always the same.</summary>
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    /// <summary>Finishes a password reset with the token from the email.</summary>
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    /// <summary>Changes the password of the user who owns <paramref name="accessToken"/>.</summary>
    Task<Result> ChangePasswordAsync(string accessToken, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>Gets new tokens with a refresh token. The refresh token can be used once; keep the new one.</summary>
    Task<Result<LoginResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes a refresh token (sign-out on the server).</summary>
    Task<Result> LogoutAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>The signed-in user and their roles, permissions and groups (<c>GET /api/auth2/me</c>).</summary>
    Task<Result<CurrentUserInfo>> GetMeAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Makes a handshake now and updates <c>Auth2ClientState</c>. Use it to try again after a start-up failure.</summary>
    Task<Result<ClientHandshakeResponse>> HandshakeAsync(CancellationToken ct = default);

    /// <summary>Sends permissions to the server (adds and updates, never deletes).</summary>
    Task<Result<SyncResponse>> SyncPermissionsAsync(PermissionSyncRequest request, CancellationToken ct = default);

    /// <summary>Sends roles to the server (adds and updates, never deletes).</summary>
    Task<Result<SyncResponse>> SyncRolesAsync(RoleSyncRequest request, CancellationToken ct = default);

    /// <summary>Sends groups to the server. They are only added when missing. Needs <c>Clients.GroupSyncAllowed</c> on the server.</summary>
    Task<Result<SyncResponse>> SyncGroupsAsync(GroupSyncRequest request, CancellationToken ct = default);

    /// <summary>Sends custom scopes to the server: they are registered and allowed for this application.</summary>
    Task<Result<SyncResponse>> SyncScopesAsync(ScopeSyncRequest request, CancellationToken ct = default);

    /// <summary>Reads the custom scopes this application allows.</summary>
    Task<Result<IReadOnlyList<ScopeDto>>> GetScopesAsync(CancellationToken ct = default);

    /// <summary>Reads the permissions of this application.</summary>
    Task<Result<IReadOnlyList<PermissionDto>>> GetPermissionsAsync(CancellationToken ct = default);

    /// <summary>Reads the roles of this application with their permission keys.</summary>
    Task<Result<IReadOnlyList<RoleDto>>> GetRolesAsync(CancellationToken ct = default);

    /// <summary>Reads the groups of this application with their roles and permission keys.</summary>
    Task<Result<IReadOnlyList<GroupDto>>> GetGroupsAsync(CancellationToken ct = default);
}
