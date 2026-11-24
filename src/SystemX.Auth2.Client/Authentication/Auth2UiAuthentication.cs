using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Authentication;

/// <summary>Names of the authentication schemes the client adds.</summary>
public static class Auth2AuthSchemes
{
    /// <summary>The default scheme when cookies are used: bearer token when the request has one, else the cookie.</summary>
    public const string Policy = "Auth2";
    /// <summary>The sign-in cookie.</summary>
    public const string Cookie = "Auth2.Cookie";
    /// <summary>The OpenID Connect sign-in (hosted UI).</summary>
    public const string Oidc = "Auth2.Oidc";
}

/// <summary>Which authentication features are on. Filled by the <c>Use...</c> methods of the builder.</summary>
internal sealed class Auth2AuthFlags
{
    public bool HasBearer { get; set; }
    public bool HasCookie { get; set; }
    public bool BlazorDefaults { get; set; }
    public UiMode Mode { get; set; } = UiMode.None;
}

/// <summary>Marks an endpoint as an API endpoint: a failed sign-in answers 401 / 403 and never redirects.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class Auth2ApiEndpointAttribute : Attribute;

internal static class Auth2Http
{
    /// <summary>
    /// True for requests that must get 401 / 403 and no redirect: a bearer token, a JSON client (<c>Accept</c>), an AJAX call, an API controller,
    /// or an endpoint marked with <see cref="Auth2ApiEndpointAttribute"/> / <c>AsAuth2Api()</c>.
    /// </summary>
    public static bool IsApiRequest(HttpContext http)
    {
        var request = http.Request;
        if (HasBearer(request)) return true;
        if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)) return true;
        var endpoint = http.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IApiBehaviorMetadata>() is not null || endpoint?.Metadata.GetMetadata<Auth2ApiEndpointAttribute>() is not null) return true;

        var accept = request.Headers.Accept.ToString();
        if (accept.Length == 0) return false;
        var wantsJson = accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) || accept.Contains("+json", StringComparison.OrdinalIgnoreCase);
        var wantsHtml = accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        return wantsJson && !wantsHtml;
    }

    public static bool HasBearer(HttpRequest request) => request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    /// <summary>A return URL is safe when it is a path on this site (not <c>//host</c>, not <c>/\host</c>, not absolute).</summary>
    public static string SafeReturnUrl(string? returnUrl, string fallback = "/") =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\')) && !returnUrl.Contains('\n') && !returnUrl.Contains('\r')
            ? returnUrl : fallback;
}

/// <summary>Does the work that belongs to "a user just signed in": the access version claim, and loading access data by <c>AccessMode</c>.</summary>
internal sealed class ClientAccessLoader(IAuth2ApiClientService api, ICurrentUserStore store, IOptions<Auth2CookieOptions> cookie, IOptions<Auth2CurrentUserOptions> user,
    IOptions<Auth2ClientOptions> client, ILogger<ClientAccessLoader> log)
{
    public async Task OnSignedInAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        if (principal.Identity is not ClaimsIdentity identity) return;

        // The access version (pv) lives in the access token. Keep it, so the cache knows when the data is old.
        if (accessToken is not null && !identity.HasClaim(c => c.Type == Auth2Claims.AccessVersion))
        {
            try
            {
                var pv = new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims.FirstOrDefault(c => c.Type == Auth2Claims.AccessVersion)?.Value;
                if (pv is not null) identity.AddClaim(new Claim(Auth2Claims.AccessVersion, pv));
            }
            catch (ArgumentException) { /* not a JWT: no version */ }
        }

        var mode = cookie.Value.AccessMode;
        if (mode is AccessLoadMode.Lazy or AccessLoadMode.None || accessToken is null) return;

        var me = await api.GetMeAsync(accessToken, ct);
        if (!me.IsSuccess)
        {
            log.LogWarning("The access data of the user could not be loaded at sign-in ({Code}). It will be loaded when it is needed.", me.Error!.Code);
            return;
        }
        if (mode == AccessLoadMode.OnSignIn)
        {
            var sub = principal.FindFirst("sub")?.Value;
            if (sub is not null) await store.SetAsync($"auth2:access:{client.Value.ClientId}:{sub}", Auth2Access.From(me.Value!), user.Value.CacheTtl, ct);
        }
        else if (mode == AccessLoadMode.RolesInCookie)
        {
            foreach (var old in identity.FindAll("role").ToList()) identity.RemoveClaim(old);
            foreach (var role in me.Value!.Roles) identity.AddClaim(new Claim("role", role));
        }
    }
}

internal sealed class Auth2CookieConfigurator(IOptions<Auth2CookieOptions> cookie, Auth2ClientState state, TimeProvider clock) : IConfigureNamedOptions<CookieAuthenticationOptions>
{
    public void Configure(CookieAuthenticationOptions options) { }

    public void Configure(string? name, CookieAuthenticationOptions options)
    {
        if (name != Auth2AuthSchemes.Cookie) return;
        var c = cookie.Value;
        options.Cookie.Name = c.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = c.SameSite;
        options.Cookie.SecurePolicy = c.SecurePolicy;
        options.SlidingExpiration = c.SlidingExpiration;
        options.ExpireTimeSpan = c.ExpireTimeSpan ?? TimeSpan.FromHours(8);
        options.LoginPath = c.LoginPath;
        options.AccessDeniedPath = c.AccessDeniedPath;
        options.ReturnUrlParameter = "returnUrl";
        options.Events = new CookieAuthenticationEvents
        {
            // The lifetime of the cookie comes from the server (read at the handshake) unless you set ExpireTimeSpan yourself.
            OnSigningIn = ctx =>
            {
                if (c.ExpireTimeSpan is null && state.LastResponse?.CookieLifetime is { } lifetime && lifetime > TimeSpan.Zero)
                {
                    ctx.Properties.ExpiresUtc = clock.GetUtcNow().Add(lifetime);
                    ctx.Properties.IsPersistent = true;
                }
                return Task.CompletedTask;
            },
            // Browsers are sent to the sign-in page. APIs and AJAX calls get 401 / 403, never a redirect.
            OnRedirectToLogin = ctx =>
            {
                if (Auth2Http.IsApiRequest(ctx.HttpContext)) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                if (Auth2Http.IsApiRequest(ctx.HttpContext)) ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                else ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            },
        };
    }
}

internal sealed class Auth2OidcConfigurator(IOptions<Auth2ClientOptions> client, IOptions<Auth2HostedUiOptions> ui, IOptions<Auth2CookieOptions> cookie, ClientAccessLoader loader)
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options) { }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != Auth2AuthSchemes.Oidc) return;
        var c = client.Value;
        var u = ui.Value;
        options.SignInScheme = Auth2AuthSchemes.Cookie;
        options.Authority = c.ServerUrl.TrimEnd('/');
        options.ClientId = c.ClientId;
        options.ClientSecret = c.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;   // authorization code with PKCE: the browser never sees a token
        options.ResponseMode = OpenIdConnectResponseMode.Query;   // the code comes back in the address: no cross-site POST, so Lax cookies and http development work
        options.UsePkce = true;
        options.RequireHttpsMetadata = u.RequireHttpsMetadata ?? c.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        options.SaveTokens = cookie.Value.SaveTokens;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.CallbackPath = u.CallbackPath;
        options.SignedOutCallbackPath = u.SignedOutCallbackPath;
        options.BackchannelHttpHandler = u.BackchannelHandler;
        options.Scope.Clear();
        foreach (var s in u.Scopes) options.Scope.Add(s);
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";

        // OnTokenValidated: ASP.NET calls this when the server's identity token has been checked and before the cookie is made.
        // We use it to keep the access version and to load the user's access data (see AccessMode).
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async ctx =>
            {
                if (ctx.Principal is not null) await loader.OnSignedInAsync(ctx.Principal, ctx.TokenEndpointResponse?.AccessToken, ctx.HttpContext.RequestAborted);
            },
        };
    }
}

/// <summary>Sign-in and sign-out for apps that show their own forms (Self UI), and for the hosted UI routes.</summary>
public interface IAuth2WebService
{
    /// <summary>
    /// Signs a user in with login and password (password grant; the server must allow <c>AllowSelfUiLogin</c>) and makes the cookie.
    /// When two-factor is needed the result succeeds with <c>TwoFactorRequired = true</c> and <b>no cookie is made</b>: call <see cref="TwoFactorLoginAsync"/> next.
    /// </summary>
    Task<Result<LoginResponse>> LoginAsync(HttpContext http, LoginRequest request, bool isPersistent = false, CancellationToken ct = default);

    /// <summary>Second step of a login. Makes the cookie when it works.</summary>
    Task<Result<LoginResponse>> TwoFactorLoginAsync(HttpContext http, TwoFactorRequest request, bool isPersistent = false, CancellationToken ct = default);

    /// <summary>Signs a user in with a one-time code. Makes the cookie when it works.</summary>
    Task<Result<LoginResponse>> OtpLoginAsync(HttpContext http, string login, string code, bool isPersistent = false, CancellationToken ct = default);

    /// <summary>Ends the server session (revokes the refresh token) and removes the cookie. For the hosted UI use the <c>/auth2/logout</c> route, which also signs out on the server.</summary>
    Task LogoutAsync(HttpContext http, CancellationToken ct = default);

    /// <summary>
    /// Sends the browser to sign in: the hosted UI starts the sign-in on the server; the Self UI goes to your <c>LoginPath</c>. After sign-in the user returns to
    /// <paramref name="returnUrl"/> (a path on this site; anything else is ignored).
    /// </summary>
    Task ChallengeAsync(HttpContext http, string? returnUrl = null);
}

internal sealed class WebService(IAuth2ApiClientService api, ClientAccessLoader loader, IOptions<Auth2CookieOptions> cookie, IOptions<Auth2ClientOptions> client, Microsoft.Extensions.Options.IOptions<Auth2AuthFlags> flags) : IAuth2WebService
{
    private static readonly string[] CopiedClaims = ["sub", "preferred_username", "name", "given_name", "family_name", "email", "email_verified", "phone_number", "locale", Auth2Claims.AccessVersion, "scope", Auth2Claims.ProjectId, Auth2Claims.ProjectKey, Auth2Claims.ApplicationId, Auth2Claims.ClientId, Auth2Claims.SessionId, Auth2Claims.IdentityProvider, Auth2Claims.Amr];

    public Task<Result<LoginResponse>> LoginAsync(HttpContext http, LoginRequest request, bool isPersistent = false, CancellationToken ct = default) =>
        SignInAsync(http, api.LoginAsync(request, ct), isPersistent, ct);

    public Task<Result<LoginResponse>> TwoFactorLoginAsync(HttpContext http, TwoFactorRequest request, bool isPersistent = false, CancellationToken ct = default) =>
        SignInAsync(http, api.TwoFactorLoginAsync(request, ct), isPersistent, ct);

    public Task<Result<LoginResponse>> OtpLoginAsync(HttpContext http, string login, string code, bool isPersistent = false, CancellationToken ct = default) =>
        SignInAsync(http, api.OtpLoginAsync(login, code, ct), isPersistent, ct);

    private async Task<Result<LoginResponse>> SignInAsync(HttpContext http, Task<Result<LoginResponse>> login, bool isPersistent, CancellationToken ct)
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
        await http.SignInAsync(Auth2AuthSchemes.Cookie, principal, properties);
        return result;
    }

    public async Task LogoutAsync(HttpContext http, CancellationToken ct = default)
    {
        var refresh = await http.GetTokenAsync(Auth2AuthSchemes.Cookie, "refresh_token");
        if (!string.IsNullOrEmpty(refresh)) await api.LogoutAsync(refresh, ct);   // best effort: the cookie goes in any case
        await http.SignOutAsync(Auth2AuthSchemes.Cookie);
    }

    public Task ChallengeAsync(HttpContext http, string? returnUrl = null)
    {
        var target = Auth2Http.SafeReturnUrl(returnUrl);
        if (flags.Value.Mode == UiMode.Hosted)
            return http.ChallengeAsync(Auth2AuthSchemes.Oidc, new AuthenticationProperties { RedirectUri = target });
        http.Response.Redirect($"{cookie.Value.LoginPath}?returnUrl={Uri.EscapeDataString(target)}");
        _ = client;
        return Task.CompletedTask;
    }
}

/// <summary>Helpers for Blazor. Use it in components; it reads the user from the <c>AuthenticationStateProvider</c>.</summary>
public interface IAuth2BlazorService
{
    /// <summary>The user of the current <c>AuthenticationState</c> and their access data.</summary>
    Task<Auth2BlazorUser> GetUserAsync(CancellationToken ct = default);
    /// <summary>True when the user has the permission.</summary>
    Task<bool> HasPermissionAsync(string permission, CancellationToken ct = default);
    /// <summary>True when the user has the role.</summary>
    Task<bool> HasRoleAsync(string role, CancellationToken ct = default);
    /// <summary>True when the user is in the group.</summary>
    Task<bool> IsInGroupAsync(string group, CancellationToken ct = default);
    /// <summary>Goes to the sign-in route and comes back to the current page (or <paramref name="returnUrl"/>).</summary>
    void NavigateToLogin(NavigationManager navigation, string? returnUrl = null);
    /// <summary>Goes to the sign-out route.</summary>
    void NavigateToLogout(NavigationManager navigation);
}

/// <summary>The user as a Blazor component sees it.</summary>
public sealed record Auth2BlazorUser(bool IsAuthenticated, bool IsMachine, Guid? UserId, string? UserName, string? Email, string? FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups)
{
    /// <summary>Nobody is signed in.</summary>
    public static Auth2BlazorUser Anonymous { get; } = new(false, false, null, null, null, null, [], [], []);
}

internal sealed class BlazorService(AuthenticationStateProvider state, IAuth2AccessProvider access) : IAuth2BlazorService
{
    public async Task<Auth2BlazorUser> GetUserAsync(CancellationToken ct = default)
    {
        var principal = (await state.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true) return Auth2BlazorUser.Anonymous;
        var machine = principal.FindFirst(Auth2Claims.Machine)?.Value == "true";
        var data = await access.GetAccessAsync(principal, ct);
        return new Auth2BlazorUser(true, machine, Guid.TryParse(principal.FindFirst("sub")?.Value, out var id) && !machine ? id : null,
            principal.FindFirst("preferred_username")?.Value, principal.FindFirst("email")?.Value, principal.FindFirst("name")?.Value, data.Roles, data.Permissions, data.Groups);
    }

    public async Task<bool> HasPermissionAsync(string permission, CancellationToken ct = default) =>
        (await GetUserAsync(ct)).Permissions.Contains(permission.Trim().ToLowerInvariant());

    public async Task<bool> HasRoleAsync(string role, CancellationToken ct = default) =>
        (await GetUserAsync(ct)).Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public async Task<bool> IsInGroupAsync(string group, CancellationToken ct = default) =>
        (await GetUserAsync(ct)).Groups.Contains(group, StringComparer.OrdinalIgnoreCase);

    public void NavigateToLogin(NavigationManager navigation, string? returnUrl = null)
    {
        var target = Auth2Http.SafeReturnUrl(returnUrl ?? "/" + navigation.ToBaseRelativePath(navigation.Uri));
        navigation.NavigateTo($"auth2/login?returnUrl={Uri.EscapeDataString(target)}", forceLoad: true);
    }

    public void NavigateToLogout(NavigationManager navigation) => navigation.NavigateTo("auth2/logout", forceLoad: true);
}
