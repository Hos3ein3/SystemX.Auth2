using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Machine;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Client.Services;

internal sealed class Auth2ApiClientService(IHttpClientFactory http, IAuth2MachineTokenProvider tokens, HandshakeRunner handshake, Auth2ClientState state,
    IOptions<Auth2ClientOptions> options, CurrentUser.IAuth2AccessTokenAccessor current) : IAuth2ApiClientService, IAuth2AdminTransport
{
    // The language chosen with WithCulture; null means "the language of the current request".
    private string? _culture;

    // Tokens chosen with WithTokens; null means "the tokens of the current request".
    private string? _accessToken;
    private string? _refreshToken;

    public IAuth2ApiClientService WithTokens(string? accessToken, string? refreshToken = null)
    {
        var copy = (Auth2ApiClientService)MemberwiseClone();
        copy._accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim();
        copy._refreshToken = string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken.Trim();
        return copy;
    }

    private const string NoAccessToken = "There is no access token: nobody is signed in on this request (or the cookie keeps no tokens: Auth2CookieOptions.SaveTokens). Use WithTokens to give one.";

    private async Task<string?> AccessTokenAsync(CancellationToken ct) => _accessToken ?? await current.GetAccessTokenAsync(ct);

    private async Task<string?> RefreshTokenAsync(CancellationToken ct) => _refreshToken ?? await current.GetRefreshTokenAsync(ct);

    /// <summary>A call for the person of the current request (or of <see cref="WithTokens"/>).</summary>
    private async Task<Result> ForPersonAsync(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct) =>
        await AccessTokenAsync(ct) is { } token ? await CallAsync(method, path, body, null, token, idempotent, ct) : Result.Failure(Auth2ErrorCodes.InvalidToken, NoAccessToken);

    private async Task<Result<T>> ForPersonAsync<T>(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct) =>
        await AccessTokenAsync(ct) is { } token ? await CallAsync<T>(method, path, body, null, token, idempotent, ct) : Result<T>.Failure(Auth2ErrorCodes.InvalidToken, NoAccessToken);

    public IAuth2ApiClientService WithCulture(string? culture)
    {
        var copy = (Auth2ApiClientService)MemberwiseClone();
        copy._culture = string.IsNullOrWhiteSpace(culture) ? null : culture.Trim();
        return copy;
    }

    public IAuth2AdminClientService Admin => new Auth2AdminClientService(this, null);

    // The admin calls (Auth2AdminClientService) go through here with the token of the current request, or the one chosen with WithTokens.
    Task<Result> IAuth2AdminTransport.SendAsync(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct) =>
        ForPersonAsync(method, path, body, idempotent, ct);

    Task<Result<T>> IAuth2AdminTransport.SendAsync<T>(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct) =>
        ForPersonAsync<T>(method, path, body, idempotent, ct);

    /// <summary>The id of the project of this application, from the last handshake (one is made when there was none).</summary>
    public async Task<Guid?> ProjectIdAsync(CancellationToken ct)
    {
        if (state.LastResponse is { } known) return known.ProjectId;
        var made = await HandshakeAsync(ct);
        return made.IsSuccess ? made.Value!.ProjectId : null;
    }

    // ---------------------------------------------------------------- accounts (machine token)

    public Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        CallAsync<RegisterResponse>(HttpMethod.Post, Auth2Routes.Register, request, [Auth2Scopes.Register], null, idempotent: false, ct);

    public Task<Result> RequestOtpAsync(OtpRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.OtpRequest, request, [Auth2Scopes.Login], null, idempotent: false, ct);

    public Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.PasswordForgot, request, [Auth2Scopes.Login], null, idempotent: false, ct);

    public Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.PasswordReset, request, [Auth2Scopes.Login], null, idempotent: false, ct);

    public Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.EmailConfirm, request, [Auth2Scopes.Login], null, idempotent: false, ct);

    public Task<Result> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.EmailConfirmResend, request, [Auth2Scopes.Login], null, idempotent: false, ct);

    public Task<Result> ConfirmPhoneAsync(ConfirmPhoneRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.PhoneConfirm, request, [Auth2Scopes.Login], null, idempotent: false, ct);

    public Task<Result> CompleteJoinAsync(JoinCompleteRequest request, CancellationToken ct = default) =>
        CallAsync(HttpMethod.Post, Auth2Routes.JoinComplete, request, [Auth2Scopes.Register], null, idempotent: false, ct);

    // ---------------------------------------------------------------- user token

    public Task<Result<TwoFactorSetupResponse>> SetupTwoFactorAsync(CancellationToken ct = default) =>
        ForPersonAsync<TwoFactorSetupResponse>(HttpMethod.Post, Auth2Routes.TwoFactorSetup, null, idempotent: false, ct);

    public Task<Result<TwoFactorEnableResponse>> EnableTwoFactorAsync(TwoFactorEnableRequest request, CancellationToken ct = default) =>
        ForPersonAsync<TwoFactorEnableResponse>(HttpMethod.Post, Auth2Routes.TwoFactorEnable, request, idempotent: false, ct);

    public Task<Result> VerifyTotpAsync(TwoFactorVerifyRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.TwoFactorVerify, request, idempotent: false, ct);

    public Task<Result> DisableTwoFactorAsync(TwoFactorDisableRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.TwoFactorDisable, request, idempotent: false, ct);

    public Task<Result<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync(RecoveryCodesRequest request, CancellationToken ct = default) =>
        ForPersonAsync<RecoveryCodesResponse>(HttpMethod.Post, Auth2Routes.TwoFactorRecoveryCodes, request, idempotent: false, ct);

    public Task<Result> ChangeUserNameAsync(ChangeUserNameRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.UserNameChange, request, idempotent: false, ct);

    public Task<Result> RequestEmailChangeAsync(ChangeEmailRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.EmailChange, request, idempotent: false, ct);

    public Task<Result> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.EmailChangeConfirm, request, idempotent: false, ct);

    public Task<Result> RequestPhoneChangeAsync(ChangePhoneNumberRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.PhoneChange, request, idempotent: false, ct);

    public Task<Result> ConfirmPhoneChangeAsync(ConfirmPhoneChangeRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.PhoneChangeConfirm, request, idempotent: false, ct);

    public Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        ForPersonAsync(HttpMethod.Post, Auth2Routes.PasswordChange, request, idempotent: false, ct);

    public Task<Result<CurrentUserInfo>> GetMeAsync(CancellationToken ct = default) =>
        ForPersonAsync<CurrentUserInfo>(HttpMethod.Get, Auth2Routes.Me, null, idempotent: true, ct);

    // ---------------------------------------------------------------- token endpoint

    public Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "password", ["username"] = request.Login, ["password"] = request.Password };
        var scope = request.Scopes is { Count: > 0 } s ? string.Join(' ', s) : "openid offline_access auth2.me";
        form["scope"] = scope;
        return TokenAsync(form, ct);
    }

    public Task<Result<LoginResponse>> TwoFactorLoginAsync(TwoFactorRequest request, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = Auth2GrantTypes.TwoFactor, ["two_factor_token"] = request.TwoFactorToken, ["scope"] = "openid offline_access auth2.me" };
        if (!string.IsNullOrEmpty(request.Code)) form["code"] = request.Code;
        if (!string.IsNullOrEmpty(request.RecoveryCode)) form["recovery_code"] = request.RecoveryCode;
        return TokenAsync(form, ct);
    }

    public Task<Result<LoginResponse>> OtpLoginAsync(string login, string code, CancellationToken ct = default) =>
        TokenAsync(new Dictionary<string, string> { ["grant_type"] = Auth2GrantTypes.Otp, ["username"] = login, ["code"] = code, ["scope"] = "openid offline_access auth2.me" }, ct);

    public async Task<Result<LoginResponse>> RefreshAsync(CancellationToken ct = default) =>
        await RefreshTokenAsync(ct) is { } refresh
            ? await TokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, ct)
            : Result<LoginResponse>.Failure(Auth2ErrorCodes.InvalidToken, "There is no refresh token: nobody is signed in on this request (or the cookie keeps no tokens). Use WithTokens to give one.");

    public async Task<Result> LogoutAsync(CancellationToken ct = default)
    {
        if (await RefreshTokenAsync(ct) is not { } refreshToken) return Result.Success();   // no refresh token: nothing to revoke
        var o = options.Value;
        var form = new Dictionary<string, string>
        {
            ["token"] = refreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = o.ClientId, ["client_secret"] = o.ClientSecret,
        };
        var (response, failure) = await SendAsync(() => WithLanguage(new HttpRequestMessage(HttpMethod.Post, Auth2Routes.Revoke.TrimStart('/')) { Content = new FormUrlEncodedContent(form) }), idempotent: true, ct);
        if (response is null) return failure!;
        using (response) return response.IsSuccessStatusCode ? Result.Success() : await FailureAsync(response, ct);
    }

    private async Task<Result<LoginResponse>> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var o = options.Value;
        form["client_id"] = o.ClientId;
        form["client_secret"] = o.ClientSecret;
        // A login must not be repeated by itself: a repeated wrong password would count twice towards the lockout.
        // (A refresh token can be used once, so a repeated refresh would also fail.)
        var (response, failure) = await SendAsync(() => WithLanguage(new HttpRequestMessage(HttpMethod.Post, Auth2Routes.Token.TrimStart('/')) { Content = new FormUrlEncodedContent(form) }), idempotent: false, ct);
        if (response is null) return Result<LoginResponse>.Failure(failure!.Error!.Code, failure.Error.Message);
        using (response)
        {
            string text;
            try { text = await response.Content.ReadAsStringAsync(ct); }
            catch (HttpRequestException) { return Result<LoginResponse>.Failure(Auth2ClientErrorCodes.ServerUnavailable, "The server answer could not be read."); }
            JsonDocument? doc = null;
            try { doc = text.Length == 0 ? null : JsonDocument.Parse(text); }
            catch (JsonException) { /* handled below */ }
            using (doc)
            {
                if (response.IsSuccessStatusCode && doc is not null)
                {
                    var root = doc.RootElement;
                    string? Str(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    return Result<LoginResponse>.Success(new LoginResponse(Str("access_token"), Str("refresh_token"), Str("id_token"),
                        root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var n) ? n : null));
                }
                var code = doc is not null && doc.RootElement.TryGetProperty("error_code", out var c) ? c.GetString() : null;
                if (code == Auth2ErrorCodes.TwoFactorRequired && doc!.RootElement.TryGetProperty("two_factor_token", out var t))
                    return Result<LoginResponse>.Success(new LoginResponse(null, null, null, null, TwoFactorRequired: true, TwoFactorToken: t.GetString()));
                var status = (int)response.StatusCode;
                if (status >= 500) return Result<LoginResponse>.Failure(Auth2ClientErrorCodes.ServerUnavailable, $"The server answered {status}.");
                if (response.StatusCode == HttpStatusCode.TooManyRequests) return Result<LoginResponse>.Failure(Auth2ErrorCodes.RateLimited, "Too many requests. Try again later.");
                return Result<LoginResponse>.Failure(code ?? (doc is not null && doc.RootElement.TryGetProperty("error", out var er) ? er.GetString() : null) ?? Auth2ErrorCodes.InvalidCredentials,
                    doc is not null && doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : null);
            }
        }
    }

    // ---------------------------------------------------------------- handshake and catalogs

    public async Task<Result<ClientHandshakeResponse>> HandshakeAsync(CancellationToken ct = default)
    {
        var outcome = await handshake.RunAsync(ct);
        state.Set(outcome.Status, outcome.Response, outcome.Message);
        return outcome.Status == HandshakeStatus.Ok
            ? Result<ClientHandshakeResponse>.Success(outcome.Response!)
            : Result<ClientHandshakeResponse>.Failure(outcome.Code ?? outcome.Status.ToString(), outcome.Message);
    }

    public Task<Result<SyncResponse>> SyncPermissionsAsync(PermissionSyncRequest request, CancellationToken ct = default) =>
        CallAsync<SyncResponse>(HttpMethod.Post, Auth2Routes.PermissionsSync, request, [Auth2Scopes.PermissionsSync], null, idempotent: true, ct);

    public Task<Result<SyncResponse>> SyncRolesAsync(RoleSyncRequest request, CancellationToken ct = default) =>
        CallAsync<SyncResponse>(HttpMethod.Post, Auth2Routes.RolesSync, request, [Auth2Scopes.RolesSync], null, idempotent: true, ct);

    public Task<Result<SyncResponse>> SyncGroupsAsync(GroupSyncRequest request, CancellationToken ct = default) =>
        CallAsync<SyncResponse>(HttpMethod.Post, Auth2Routes.GroupsSync, request, [Auth2Scopes.GroupsSync], null, idempotent: true, ct);

    public Task<Result<SyncResponse>> SyncScopesAsync(ScopeSyncRequest request, CancellationToken ct = default) =>
        CallAsync<SyncResponse>(HttpMethod.Post, Auth2Routes.ScopesSync, request, [Auth2Scopes.ScopesSync], null, idempotent: true, ct);

    public async Task<Result<IReadOnlyList<ScopeDto>>> GetScopesAsync(CancellationToken ct = default) =>
        ToList(await CallAsync<List<ScopeDto>>(HttpMethod.Get, Auth2Routes.Scopes, null, [Auth2Scopes.ScopesSync], null, idempotent: true, ct));

    public async Task<Result<IReadOnlyList<ExternalProviderInfoDto>>> GetExternalProvidersAsync(CancellationToken ct = default) =>
        ToList(await CallAsync<List<ExternalProviderInfoDto>>(HttpMethod.Get, Auth2Routes.ExternalProviders, null, [Auth2Scopes.ClientHandshake], null, idempotent: true, ct));

    public async Task<Result<IReadOnlyList<PermissionDto>>> GetPermissionsAsync(CancellationToken ct = default) =>
        ToList(await CallAsync<List<PermissionDto>>(HttpMethod.Get, Auth2Routes.Permissions, null, [Auth2Scopes.PermissionsSync], null, idempotent: true, ct));

    public async Task<Result<IReadOnlyList<RoleDto>>> GetRolesAsync(CancellationToken ct = default) =>
        ToList(await CallAsync<List<RoleDto>>(HttpMethod.Get, Auth2Routes.Roles, null, [Auth2Scopes.RolesSync], null, idempotent: true, ct));

    public async Task<Result<IReadOnlyList<GroupDto>>> GetGroupsAsync(CancellationToken ct = default) =>
        ToList(await CallAsync<List<GroupDto>>(HttpMethod.Get, Auth2Routes.Groups, null, [Auth2Scopes.GroupsRead], null, idempotent: true, ct));

    private static Result<IReadOnlyList<T>> ToList<T>(Result<List<T>> r) =>
        r.IsSuccess ? Result<IReadOnlyList<T>>.Success(r.Value ?? []) : Result<IReadOnlyList<T>>.Failure(r.Error!.Code, r.Error.Message);

    // ---------------------------------------------------------------- plumbing

    private async Task<Result> CallAsync(HttpMethod method, string path, object? body, string[]? machineScopes, string? userToken, bool idempotent, CancellationToken ct)
    {
        var (response, failure) = await SendWithAuthAsync(method, path, body, machineScopes, userToken, idempotent, ct);
        if (response is null) return failure!;
        using (response) return response.IsSuccessStatusCode ? Result.Success() : await FailureAsync(response, ct);
    }

    private async Task<Result<T>> CallAsync<T>(HttpMethod method, string path, object? body, string[]? machineScopes, string? userToken, bool idempotent, CancellationToken ct)
    {
        var (response, failure) = await SendWithAuthAsync(method, path, body, machineScopes, userToken, idempotent, ct);
        if (response is null) return Result<T>.Failure(failure!.Error!.Code, failure.Error.Message);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var f = await FailureAsync(response, ct);
                return Result<T>.Failure(f.Error!.Code, f.Error.Message);
            }
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(ct);
                return value is null ? Result<T>.Failure(Auth2ClientErrorCodes.InvalidResponse, "The server answer was empty.") : Result<T>.Success(value);
            }
            catch (JsonException) { return Result<T>.Failure(Auth2ClientErrorCodes.InvalidResponse, "The server answer could not be understood."); }
        }
    }

    private async Task<(HttpResponseMessage? Response, Result? Failure)> SendWithAuthAsync(HttpMethod method, string path, object? body, string[]? machineScopes, string? userToken, bool idempotent, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            string bearer;
            try { bearer = userToken ?? await tokens.GetTokenAsync(machineScopes, ct); }
            catch (Auth2Exception ex) { return (null, Result.Failure(ex.ErrorCode, ex.Message)); }

            var (response, failure) = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(method, path.TrimStart('/'));
                if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                return WithLanguage(request);
            }, idempotent, ct);

            // A machine token can be revoked or the secret rotated. One new token, one more try, only for calls that are safe to repeat.
            if (response is { StatusCode: HttpStatusCode.Unauthorized } && userToken is null && idempotent && attempt == 0)
            {
                response.Dispose();
                tokens.Invalidate();
                continue;
            }
            return (response, failure);
        }
    }

    /// <summary>
    /// Adds <c>Accept-Language</c>: the language chosen with <c>WithCulture</c>, else the language of the current request (<c>CurrentUICulture</c>) unless
    /// <c>SendRequestCulture</c> is off. A value that is not a language tag is not sent.
    /// </summary>
    private HttpRequestMessage WithLanguage(HttpRequestMessage request)
    {
        var tag = _culture ?? (options.Value.SendRequestCulture ? CultureInfo.CurrentUICulture.Name : null);
        if (!string.IsNullOrEmpty(tag) && tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(tag.Replace('_', '-')));
        return request;
    }

    private async Task<(HttpResponseMessage? Response, Result? Failure)> SendAsync(Func<HttpRequestMessage> build, bool idempotent, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.RequestTimeout);
        try
        {
            using var request = build();
            var client = http.CreateClient(idempotent ? Auth2ClientDefaults.ResilientHttpClientName : Auth2ClientDefaults.HttpClientName);
            // The response is buffered, so the timeout source can be released here.
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            return (response, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            return (null, Result.Failure(Auth2ClientErrorCodes.ServerUnavailable, "The server could not be reached or was too slow."));
        }
    }

    private static async Task<Result> FailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (status >= 500) return Result.Failure(Auth2ClientErrorCodes.ServerUnavailable, $"The server answered {status}.");
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            if (error is { Code.Length: > 0 }) return Result.Failure(error.Code, error.Message);
        }
        catch (JsonException) { /* not our error body */ }
        return Result.Failure(status switch
        {
            401 => Auth2ErrorCodes.InvalidToken, 403 => Auth2ErrorCodes.Forbidden, 404 => Auth2ErrorCodes.NotFound, 429 => Auth2ErrorCodes.RateLimited,
            _ => Auth2ErrorCodes.ValidationFailed,
        }, $"The server answered {status}.");
    }
}
