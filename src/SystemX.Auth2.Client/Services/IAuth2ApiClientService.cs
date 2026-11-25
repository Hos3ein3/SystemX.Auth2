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
/// <para>
/// <b>Language.</b> Every call carries the language of the current request (<c>Accept-Language</c>, from <c>CultureInfo.CurrentUICulture</c>; switch off with
/// <c>Auth2ClientOptions.SendRequestCulture</c>), so the error message, and the email or SMS that the call causes, are written in that language when the server has it
/// (<c>en</c>, <c>fa</c>, <c>nl</c>, <c>it</c>). Use <see cref="WithCulture"/> to choose it for some calls. The error <b>code</b> never changes with the language.
/// </para>
/// <para>
/// <b>Cookies.</b> This service never makes a cookie and never keeps a token. Login methods only return the tokens. For an application with cookie sign-in use
/// <c>IAuth2WebService</c>, which calls this service and then signs the cookie in.
/// </para>
/// </summary>
public interface IAuth2ApiClientService
{
    /// <summary>
    /// The same service for calls in another language. <paramref name="culture"/> is a language tag such as <c>fa</c> or <c>fa-IR</c>; <c>null</c> or empty goes back to the language of the current request.
    /// Nothing is changed on this instance: use the returned one.
    /// </summary>
    IAuth2ApiClientService WithCulture(string? culture);

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

    /// <summary>
    /// Reads the external sign-in providers that are on for this application (name, button text, kind, whether a user name and password form is needed).
    /// Use it with <c>UseSelfUi</c> to show the choices; then send the user to the hosted login of the Server. Secrets are never part of the list.
    /// </summary>
    Task<Result<IReadOnlyList<ExternalProviderInfoDto>>> GetExternalProvidersAsync(CancellationToken ct = default);

    /// <summary>Reads the permissions of this application.</summary>
    Task<Result<IReadOnlyList<PermissionDto>>> GetPermissionsAsync(CancellationToken ct = default);

    /// <summary>Reads the roles of this application with their permission keys.</summary>
    Task<Result<IReadOnlyList<RoleDto>>> GetRolesAsync(CancellationToken ct = default);

    /// <summary>Reads the groups of this application with their roles and permission keys.</summary>
    Task<Result<IReadOnlyList<GroupDto>>> GetGroupsAsync(CancellationToken ct = default);

    // ---- confirmations and joining (client token)

    /// <summary>Confirms an email address with the user id and the token of the link in the email. Needs the client scope <c>auth2.login</c>.</summary>
    Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default);

    /// <summary>Sends the email confirmation again. The answer is always the same. Needs the client scope <c>auth2.login</c>.</summary>
    Task<Result> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request, CancellationToken ct = default);

    /// <summary>Confirms a phone number with the code from the SMS (ask for it with <see cref="RequestOtpAsync"/>, purpose <c>confirm_phone</c>). Needs the client scope <c>auth2.login</c>.</summary>
    Task<Result> ConfirmPhoneAsync(ConfirmPhoneRequest request, CancellationToken ct = default);

    /// <summary>Finishes joining this application for a person who already has an account elsewhere: the code that proves ownership and the new password. Needs the client scope <c>auth2.register</c>.</summary>
    Task<Result> CompleteJoinAsync(JoinCompleteRequest request, CancellationToken ct = default);

    // ---- two-factor (user token with the scope auth2.me)

    /// <summary>Makes the key for the authenticator app. Show <c>SharedKey</c> and <c>AuthenticatorUri</c> (as a QR code), then call <see cref="EnableTwoFactorAsync"/>.</summary>
    Task<Result<TwoFactorSetupResponse>> SetupTwoFactorAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Turns two-factor on with the first code of the app. The recovery codes in the answer are shown once.</summary>
    Task<Result<TwoFactorEnableResponse>> EnableTwoFactorAsync(string accessToken, TwoFactorEnableRequest request, CancellationToken ct = default);

    /// <summary>Turns two-factor off. The password is needed. Refused when the application requires two-factor.</summary>
    Task<Result> DisableTwoFactorAsync(string accessToken, TwoFactorDisableRequest request, CancellationToken ct = default);

    /// <summary>Makes new recovery codes (the old ones stop working). A code from the authenticator app is needed. The codes are shown once.</summary>
    Task<Result<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync(string accessToken, RecoveryCodesRequest request, CancellationToken ct = default);

    // ---- change the own details (user token with the scope auth2.me)

    /// <summary>Changes the user name at once. A person who has a password must send it.</summary>
    Task<Result> ChangeUserNameAsync(string accessToken, ChangeUserNameRequest request, CancellationToken ct = default);

    /// <summary>Starts an email change: a link goes to the <b>new</b> address and nothing changes yet. Finish with <see cref="ConfirmEmailChangeAsync"/>.</summary>
    Task<Result> RequestEmailChangeAsync(string accessToken, ChangeEmailRequest request, CancellationToken ct = default);

    /// <summary>Finishes an email change with the new address and the token of the link. The address is replaced and marked as confirmed.</summary>
    Task<Result> ConfirmEmailChangeAsync(string accessToken, ConfirmEmailChangeRequest request, CancellationToken ct = default);

    /// <summary>Starts a phone change: an SMS code goes to the <b>new</b> number and nothing changes yet. Finish with <see cref="ConfirmPhoneChangeAsync"/>.</summary>
    Task<Result> RequestPhoneChangeAsync(string accessToken, ChangePhoneNumberRequest request, CancellationToken ct = default);

    /// <summary>Finishes a phone change with the new number and the code. The number is replaced and marked as confirmed.</summary>
    Task<Result> ConfirmPhoneChangeAsync(string accessToken, ConfirmPhoneChangeRequest request, CancellationToken ct = default);
}
