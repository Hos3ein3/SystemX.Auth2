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

internal sealed class Auth2WebClientService(IAuth2ApiClientService api, ClientAccessLoader loader, IHttpContextAccessor httpAccessor, IOptions<Auth2CookieOptions> cookie, IOptions<Auth2ClientOptions> client, Microsoft.Extensions.Options.IOptions<Auth2AuthFlags> flags) : IAuth2WebClientService
{
    /// <summary>The request that is running now. These methods need one (they read and write its cookie).</summary>
    private HttpContext Http => httpAccessor.HttpContext
        ?? throw new InvalidOperationException("IAuth2WebClientService works on the current request, and there is none here. Call it from a request (an endpoint, a controller, a page), or use IAuth2ApiClientService.");

    private static readonly string[] CopiedClaims = ["sub", "preferred_username", "name", "given_name", "family_name", "email", "email_verified", "phone_number", "phone_number_verified", "locale", Auth2Claims.AccessVersion, "scope", Auth2Claims.ProjectId, Auth2Claims.ProjectKey, Auth2Claims.ApplicationId, Auth2Claims.ClientId, Auth2Claims.SessionId, Auth2Claims.IdentityProvider, Auth2Claims.Amr];

    public Task<Result<LoginResponse>> LoginAsync(LoginRequest request, bool isPersistent = false, CancellationToken ct = default) =>
        SignInAsync(api.LoginAsync(request, ct), isPersistent, ct);

    public Task<Result<LoginResponse>> TwoFactorLoginAsync(TwoFactorRequest request, bool isPersistent = false, CancellationToken ct = default) =>
        SignInAsync(api.TwoFactorLoginAsync(request, ct), isPersistent, ct);

    public Task<Result<LoginResponse>> OtpLoginAsync(string login, string code, bool isPersistent = false, CancellationToken ct = default) =>
        SignInAsync(api.OtpLoginAsync(login, code, ct), isPersistent, ct);

    private async Task<Result<LoginResponse>> SignInAsync(Task<Result<LoginResponse>> login, bool isPersistent, CancellationToken ct)
    {
        var result = await login;
        if (!result.IsSuccess || result.Value is not { AccessToken: { } accessToken } tokens) return result;   // failure, or the second factor is still needed

        var identity = new ClaimsIdentity(Auth2AuthSchemes.Cookie, "name", "role");
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        foreach (var claim in jwt.Claims.Where(c => CopiedClaims.Contains(c.Type))) identity.AddClaim(new Claim(claim.Type, claim.Value));
        var principal = new ClaimsPrincipal(identity);
        await loader.OnSignedInAsync(principal, accessToken, ct);   // roles in the cookie, or the store, by AccessMode

        var properties = new AuthenticationProperties { IsPersistent = isPersistent };
        if (cookie.Value.SaveTokens)
        {
            var saved = new List<AuthenticationToken> { new() { Name = "access_token", Value = accessToken } };
            if (tokens.RefreshToken is not null) saved.Add(new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken });
            if (tokens.IdToken is not null) saved.Add(new AuthenticationToken { Name = "id_token", Value = tokens.IdToken });
            properties.StoreTokens(saved);
        }
        await Http.SignInAsync(Auth2AuthSchemes.Cookie, principal, properties);
        return result;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var refresh = await Http.GetTokenAsync(Auth2AuthSchemes.Cookie, "refresh_token");
        if (!string.IsNullOrEmpty(refresh)) await api.WithTokens(null, refresh).LogoutAsync(ct);   // best effort: the cookie goes in any case
        await Http.SignOutAsync(Auth2AuthSchemes.Cookie);
    }

    // ---------------------------------------------------------------- people who are not signed in: the calls of the API service, as they are

    private IAuth2AdminClientService? _admin;
    public IAuth2AdminClientService Admin => _admin ??= new Auth2AdminClientService(new WebAdminTransport(this, api), null);

    /// <summary>The admin calls with the access token of the cookie (an old one is refreshed once, like every call of a person here).</summary>
    private sealed class WebAdminTransport(Auth2WebClientService web, IAuth2ApiClientService api) : IAuth2AdminTransport
    {
        private static IAuth2AdminTransport Of(IAuth2ApiClientService a) =>
            a as IAuth2AdminTransport ?? throw new InvalidOperationException("The admin calls need the package's own IAuth2ApiClientService.");

        public Task<Result> SendAsync(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct) =>
            web.ForPersonAsync(a => Of(a).SendAsync(method, path, body, idempotent, ct), Result.Failure, ct);

        public Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct) =>
            web.ForPersonAsync(a => Of(a).SendAsync<T>(method, path, body, idempotent, ct), Result<T>.Failure, ct);

        public Task<Guid?> ProjectIdAsync(CancellationToken ct) => Of(api).ProjectIdAsync(ct);
    }

    public Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default) => api.RegisterAsync(request, ct);
    public Task<Result> RequestOtpAsync(OtpRequest request, CancellationToken ct = default) => api.RequestOtpAsync(request, ct);
    public Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default) => api.ForgotPasswordAsync(request, ct);
    public Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default) => api.ResetPasswordAsync(request, ct);
    public Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default) => api.ConfirmEmailAsync(request, ct);
    public Task<Result> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request, CancellationToken ct = default) => api.ResendEmailConfirmationAsync(request, ct);
    public Task<Result> ConfirmPhoneAsync(ConfirmPhoneRequest request, CancellationToken ct = default) => api.ConfirmPhoneAsync(request, ct);
    public Task<Result> CompleteJoinAsync(JoinCompleteRequest request, CancellationToken ct = default) => api.CompleteJoinAsync(request, ct);
    public Task<Result<IReadOnlyList<ExternalProviderInfoDto>>> GetExternalProvidersAsync(CancellationToken ct = default) => api.GetExternalProvidersAsync(ct);

    // ---------------------------------------------------------------- the signed-in person: the access token comes from the cookie

    public Task<Result<Contracts.Users.CurrentUserInfo>> GetMeAsync(CancellationToken ct = default) => ForPersonAsync(a => a.GetMeAsync(ct), Result<Contracts.Users.CurrentUserInfo>.Failure, ct);
    public Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.ChangePasswordAsync(request, ct), Result.Failure, ct);
    public Task<Result> ChangeUserNameAsync(ChangeUserNameRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.ChangeUserNameAsync(request, ct), Result.Failure, ct);
    public Task<Result> RequestEmailChangeAsync(ChangeEmailRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.RequestEmailChangeAsync(request, ct), Result.Failure, ct);
    public Task<Result> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.ConfirmEmailChangeAsync(request, ct), Result.Failure, ct);
    public Task<Result> RequestPhoneChangeAsync(ChangePhoneNumberRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.RequestPhoneChangeAsync(request, ct), Result.Failure, ct);
    public Task<Result> ConfirmPhoneChangeAsync(ConfirmPhoneChangeRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.ConfirmPhoneChangeAsync(request, ct), Result.Failure, ct);
    public Task<Result<TwoFactorSetupResponse>> SetupTwoFactorAsync(CancellationToken ct = default) => ForPersonAsync(a => a.SetupTwoFactorAsync(ct), Result<TwoFactorSetupResponse>.Failure, ct);
    public Task<Result<TwoFactorEnableResponse>> EnableTwoFactorAsync(TwoFactorEnableRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.EnableTwoFactorAsync(request, ct), Result<TwoFactorEnableResponse>.Failure, ct);
    public Task<Result> VerifyTotpAsync(TwoFactorVerifyRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.VerifyTotpAsync(request, ct), Result.Failure, ct);
    public Task<Result> DisableTwoFactorAsync(TwoFactorDisableRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.DisableTwoFactorAsync(request, ct), Result.Failure, ct);
    public Task<Result<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync(RecoveryCodesRequest request, CancellationToken ct = default) => ForPersonAsync(a => a.RegenerateRecoveryCodesAsync(request, ct), Result<RecoveryCodesResponse>.Failure, ct);

    /// <summary>
    /// Runs a call of the API service for the signed-in person. The access token is the one saved in the cookie. A call that is answered <c>invalid_token</c> was refused at the door
    /// (nothing happened), so it is safe to ask again after one refresh. With no token in the cookie (<c>SaveTokens</c> is off, or nobody is signed in) the answer is <c>invalid_token</c>.
    /// </summary>
    internal async Task<TResult> ForPersonAsync<TResult>(Func<IAuth2ApiClientService, Task<TResult>> call, Func<string, string?, TResult> fail, CancellationToken ct) where TResult : Result
    {
        var token = await Http.GetTokenAsync(Auth2AuthSchemes.Cookie, "access_token");
        if (string.IsNullOrEmpty(token)) return fail(Auth2ErrorCodes.InvalidToken, "There is no access token in the session cookie. Is somebody signed in, and is Auth2CookieOptions.SaveTokens on?");

        var result = await call(api.WithTokens(token));
        if (result.Error?.Code != Auth2ErrorCodes.InvalidToken) return result;

        var refreshed = await RefreshAsync(ct);
        return refreshed.Value?.AccessToken is { } fresh ? await call(api.WithTokens(fresh)) : result;   // no refresh possible: the person must sign in again, the first answer says so
    }

    public async Task<Result<LoginResponse>> RefreshAsync(CancellationToken ct = default)
    {
        var auth = await Http.AuthenticateAsync(Auth2AuthSchemes.Cookie);
        var refresh = auth.Properties?.GetTokenValue("refresh_token");
        if (!auth.Succeeded || auth.Principal is null || auth.Properties is null || string.IsNullOrEmpty(refresh))
            return Result<LoginResponse>.Failure(Auth2ErrorCodes.InvalidToken, "There is no refresh token in the session cookie.");

        var result = await _refreshes.RunAsync(refresh, () => api.WithTokens(null, refresh).RefreshAsync(ct));
        if (!result.IsSuccess || result.Value?.AccessToken is not { } access) return result;

        // The new tokens go into the cookie of THIS response (every request that refreshed has its own cookie to update).
        auth.Properties.UpdateTokenValue("access_token", access);
        if (result.Value.RefreshToken is not null) auth.Properties.UpdateTokenValue("refresh_token", result.Value.RefreshToken);
        if (result.Value.IdToken is not null) auth.Properties.UpdateTokenValue("id_token", result.Value.IdToken);
        if (!Http.Response.HasStarted) await Http.SignInAsync(Auth2AuthSchemes.Cookie, auth.Principal, auth.Properties);
        return result;
    }

    // A refresh token works once. Requests of the same session that need a refresh at the same moment (or just after) must not each use it: the first asks the server, the others get
    // the same answer. The answer is kept for a minute, in this process only (with several instances, a session should stay on one, or refresh only from one place).
    private readonly RefreshGate _refreshes = new();

    private sealed class RefreshGate
    {
        private static readonly TimeSpan Keep = TimeSpan.FromMinutes(1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, Lazy<Task<Result<LoginResponse>>> Answer)> _answers = new();

        public async Task<Result<LoginResponse>> RunAsync(string refreshToken, Func<Task<Result<LoginResponse>>> ask)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var old in _answers.Where(a => now - a.Value.At > Keep).Select(a => a.Key).ToList()) _answers.TryRemove(old, out _);
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken)));   // never keep the token itself as a key
            var result = await _answers.GetOrAdd(key, _ => (now, new Lazy<Task<Result<LoginResponse>>>(ask))).Answer.Value;
            if (!result.IsSuccess) _answers.TryRemove(key, out _);   // only a refresh that worked is shared: a failure (a hiccup of the server) may be tried again
            return result;
        }
    }

    public Task ChallengeAsync(string? returnUrl = null)
    {
        var target = Auth2Http.SafeReturnUrl(returnUrl);
        if (flags.Value.Mode == UiMode.Hosted)
            return Http.ChallengeAsync(Auth2AuthSchemes.Oidc, new AuthenticationProperties { RedirectUri = target });
        Http.Response.Redirect($"{cookie.Value.LoginPath}?returnUrl={Uri.EscapeDataString(target)}");
        _ = client;
        return Task.CompletedTask;
    }
}
