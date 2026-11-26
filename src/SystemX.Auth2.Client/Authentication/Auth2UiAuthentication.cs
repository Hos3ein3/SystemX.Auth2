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

        var me = await api.WithTokens(accessToken).GetMeAsync(ct);
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
                if (ctx.Principal is null) return;
                CopyAccessTokenClaims(ctx.Principal, ctx.TokenEndpointResponse?.AccessToken);
                await loader.OnSignedInAsync(ctx.Principal, ctx.TokenEndpointResponse?.AccessToken, ctx.HttpContext.RequestAborted);
            },
        };
    }

    /// <summary>
    /// The identity token carries the person, but not the <b>scopes</b> and the facts about the application and the session (<c>client_id</c>, <c>application_id</c>,
    /// <c>project_id</c>, <c>projectkey</c>, <c>sid</c>): those are in the <b>access</b> token. Without them the cookie of a hosted-UI sign-in would have no scopes,
    /// so <c>ICurrentClient.Scopes</c>, <c>[Auth2Scope]</c> and <c>WithScope</c> would refuse everybody and <c>ICurrentClient.ClientId</c> would be empty.
    /// They are copied into the cookie at sign-in, as the Self UI does. A claim that the identity token already has is left alone.
    /// </summary>
    private static void CopyAccessTokenClaims(ClaimsPrincipal principal, string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken) || principal.Identity is not ClaimsIdentity identity) return;
        JsonWebToken jwt;
        try { jwt = new JsonWebToken(accessToken); }
        catch (ArgumentException) { return; }   // not a readable JWT (for example an encrypted token): nothing to copy

        if (!identity.HasClaim(c => c.Type is "scope" or "scp"))
            foreach (var claim in jwt.Claims.Where(c => c.Type is "scope" or "scp")) identity.AddClaim(new Claim("scope", claim.Value));
        foreach (var type in new[] { Auth2Claims.ClientId, Auth2Claims.ApplicationId, Auth2Claims.ProjectId, Auth2Claims.ProjectKey, Auth2Claims.SessionId })
            if (!identity.HasClaim(c => c.Type == type) && jwt.Claims.FirstOrDefault(c => c.Type == type) is { } found) identity.AddClaim(new Claim(type, found.Value));
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
