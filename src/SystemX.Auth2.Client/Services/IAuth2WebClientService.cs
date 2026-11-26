using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Services;

/// <summary>
/// Everything a website with its <b>own forms</b> (Self UI) needs from the Auth2 Server, in one service: signing in and out, and the account pages of the signed-in person
/// (password, user name, email, phone, two-factor), plus the forms for people who are not signed in (register, forgot password, confirm email). It is
/// <see cref="IAuth2ApiClientService"/> for web applications: the same calls, but it works with the <b>session cookie</b>.
/// <list type="bullet">
/// <item>Sign-in methods make the cookie and keep the tokens in it (<c>Auth2CookieOptions.SaveTokens</c>).</item>
/// <item>Methods with an <see cref="HttpContext"/> act for the <b>signed-in person</b>: the service takes the access token from the cookie, so your code never handles a token.
/// When the access token is old (<c>invalid_token</c>) the service refreshes it <b>once</b> with the refresh token of the cookie, saves the new tokens in the cookie and asks again;
/// several requests that need it at the same moment share one refresh (a refresh token works once).</item>
/// <item>Methods without an <see cref="HttpContext"/> are for people who are not signed in.</item>
/// <item>Like the API service, nothing throws for a server error: the answer is a <see cref="Result"/> with the error code.</item>
/// </list>
/// A program (a job, an API, a back end that holds tokens itself) uses <see cref="IAuth2ApiClientService"/>.
/// </summary>
public interface IAuth2WebClientService
{
    /// <summary>
    /// Signs a user in with login and password (password grant; the server must allow <c>AllowSelfUiLogin</c>) and makes the cookie.
    /// When two-factor is needed the result succeeds with <c>TwoFactorRequired = true</c> and <b>no cookie is made</b>: call <see cref="TwoFactorLoginAsync"/> next.
    /// </summary>
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, bool isPersistent = false, CancellationToken ct = default);

    /// <summary>Second step of a login. Makes the cookie when it works.</summary>
    Task<Result<LoginResponse>> TwoFactorLoginAsync(TwoFactorRequest request, bool isPersistent = false, CancellationToken ct = default);

    /// <summary>Signs a user in with a one-time code. Makes the cookie when it works.</summary>
    Task<Result<LoginResponse>> OtpLoginAsync(string login, string code, bool isPersistent = false, CancellationToken ct = default);

    /// <summary>Ends the server session (revokes the refresh token) and removes the cookie. For the hosted UI use the <c>/auth2/logout</c> route, which also signs out on the server.</summary>
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends the browser to sign in: the hosted UI starts the sign-in on the server; the Self UI goes to your <c>LoginPath</c>. After sign-in the user returns to
    /// <paramref name="returnUrl"/> (a path on this site; anything else is ignored).
    /// </summary>
    Task ChallengeAsync(string? returnUrl = null);

    // ---------------------------------------------------------------- people who are not signed in

    /// <summary>Registers a person (no cookie is made: they sign in next, or confirm their email first).</summary>
    Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>Sends a one-time code by email or SMS (for <see cref="OtpLoginAsync"/>). The answer is the same whether the account exists or not.</summary>
    Task<Result> RequestOtpAsync(OtpRequest request, CancellationToken ct = default);

    /// <summary>Starts the reset of a forgotten password (the person gets a link or code).</summary>
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    /// <summary>Sets a new password with the token or code of <see cref="ForgotPasswordAsync"/>.</summary>
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    /// <summary>Confirms an email address with the code or token of the message.</summary>
    Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default);

    /// <summary>Sends the confirmation message again.</summary>
    Task<Result> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request, CancellationToken ct = default);

    /// <summary>Confirms a phone number with the code of the SMS.</summary>
    Task<Result> ConfirmPhoneAsync(ConfirmPhoneRequest request, CancellationToken ct = default);

    /// <summary>Finishes joining this application for a person who already has an account on the server (with the proof they were sent).</summary>
    Task<Result> CompleteJoinAsync(JoinCompleteRequest request, CancellationToken ct = default);

    /// <summary>The external sign-in providers (Google, GitHub, ...) this application shows, for your own login page.</summary>
    Task<Result<IReadOnlyList<ExternalProviderInfoDto>>> GetExternalProvidersAsync(CancellationToken ct = default);

    // ---------------------------------------------------------------- the signed-in person (the access token comes from the cookie)

    /// <summary>The signed-in person: profile, roles, permissions and groups, read from the server.</summary>
    Task<Result<Contracts.Users.CurrentUserInfo>> GetMeAsync(CancellationToken ct = default);

    /// <summary>
    /// Refreshes the tokens of the session now: uses the refresh token of the cookie and saves the new tokens in it. Other methods do it by themselves when the access token is old;
    /// call this to keep a long session alive or to pick up new roles. Fails when the cookie has no refresh token or the server refuses it (sign in again).
    /// </summary>
    Task<Result<LoginResponse>> RefreshAsync(CancellationToken ct = default);

    /// <summary>Changes the password of the signed-in person. The current password is needed when they have one.</summary>
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>Changes the user name of the signed-in person (the project rules apply; the password is needed when they have one).</summary>
    Task<Result> ChangeUserNameAsync(ChangeUserNameRequest request, CancellationToken ct = default);

    /// <summary>Asks to change the email address: a link is sent <b>to the new address</b>.</summary>
    Task<Result> RequestEmailChangeAsync(ChangeEmailRequest request, CancellationToken ct = default);

    /// <summary>Confirms the new email address with the token of the link.</summary>
    Task<Result> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest request, CancellationToken ct = default);

    /// <summary>Asks to change the phone number: a code is sent <b>to the new number</b>.</summary>
    Task<Result> RequestPhoneChangeAsync(ChangePhoneNumberRequest request, CancellationToken ct = default);

    /// <summary>Confirms the new phone number with the code of the SMS.</summary>
    Task<Result> ConfirmPhoneChangeAsync(ConfirmPhoneChangeRequest request, CancellationToken ct = default);

    /// <summary>Makes the key for the authenticator app. Show <c>SharedKey</c> and <c>AuthenticatorUri</c> (as a QR code), then call <see cref="EnableTwoFactorAsync"/>.</summary>
    Task<Result<TwoFactorSetupResponse>> SetupTwoFactorAsync(CancellationToken ct = default);

    /// <summary>Turns two-factor on with the first code of the app. The recovery codes in the answer are shown once.</summary>
    Task<Result<TwoFactorEnableResponse>> EnableTwoFactorAsync(TwoFactorEnableRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks one code of the authenticator app for the signed-in person, for example before a restricted action of your own page. A code works once.
    /// Errors: <c>totp_required</c>, <c>invalid_otp</c>, <c>totp_setup_required</c> (or success when <see cref="TwoFactorVerifyRequest.Optional"/> and the person has no two-factor), <c>locked_out</c>.
    /// </summary>
    Task<Result> VerifyTotpAsync(TwoFactorVerifyRequest request, CancellationToken ct = default);

    /// <summary>Turns two-factor off. The password is needed. Refused when the application requires two-factor.</summary>
    Task<Result> DisableTwoFactorAsync(TwoFactorDisableRequest request, CancellationToken ct = default);

    /// <summary>Makes new recovery codes (the old ones stop working). Needs a code of the authenticator app. They are shown once.</summary>
    Task<Result<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync(RecoveryCodesRequest request, CancellationToken ct = default);

    // ---------------------------------------------------------------- roles, permissions, groups and scopes (an admin is signed in)

    /// <summary>
    /// The people, roles, permissions, groups and scopes of the project: create, read, change and delete, and who has what, with the token of the signed-in <b>admin</b>
    /// (session scope <c>auth2.admin</c>). The same calls as <see cref="IAuth2ApiClientService.Admin"/>; the token is the one of the cookie, renewed once when it is old. See <see cref="IAuth2AdminClientService"/>.
    /// </summary>
    IAuth2AdminClientService Admin { get; }
}
