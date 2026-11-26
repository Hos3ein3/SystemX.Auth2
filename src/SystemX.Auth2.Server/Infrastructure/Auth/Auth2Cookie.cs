using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// The login session of the hosted UI (the browser cookie). It remembers who signed in and for which project (claim <c>project_id</c>).
/// The cookie name contains the project key, so a login in one project is never even sent to another project on the same domain
/// (the credentials of the same person may be different there). All applications of one project share the cookie: signing in through
/// one application of a project signs the user into the others (single sign-on inside the project).
/// </summary>
public static class Auth2Cookie
{
    /// <summary>Authentication scheme name.</summary>
    public const string Scheme = "Auth2.Session";

    /// <summary>Cookie name prefix. The project key is added: <c>.SystemX.Auth2.Session.{projectkey}</c>.</summary>
    public const string CookieName = ".SystemX.Auth2.Session";

    /// <summary>Adds the cookie scheme.</summary>
    public static AuthenticationBuilder AddAuth2Cookie(this AuthenticationBuilder builder)
    {
        // Secure in every real environment. Plain HTTP only in Development and Testing (the environment is known at run time).
        builder.Services.AddOptions<CookieAuthenticationOptions>(Scheme).Configure<IHostEnvironment>((o, env) =>
            o.Cookie.SecurePolicy = OpenIddict.OpenIddictSetup.IsDevelopmentLike(env) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);

        // The short-lived, single-use state of a passkey registration or sign-in (ASP.NET Core Identity keeps it in this scheme).
        builder.Services.AddOptions<CookieAuthenticationOptions>(Microsoft.AspNetCore.Identity.IdentityConstants.TwoFactorUserIdScheme).Configure<IHostEnvironment>((o, env) =>
            o.Cookie.SecurePolicy = OpenIddict.OpenIddictSetup.IsDevelopmentLike(env) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
        builder.AddCookie(Microsoft.AspNetCore.Identity.IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.Name = ".SystemX.Auth2.PasskeyState";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        return builder.AddCookie(Scheme, o =>
        {
            o.Cookie.Name = CookieName;
            o.CookieManager = new ProjectCookieManager();
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.LoginPath = "/Account/Login";
            o.LogoutPath = "/Account/Logout";
            o.AccessDeniedPath = "/Account/AccessDenied";
            o.ReturnUrlParameter = "ReturnUrl";
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
        });
    }

    /// <summary>Signs the user into the hosted UI session.</summary>
    public static async Task SignInAsync(HttpContext http, AppUser user, Project project, ClientApplicationInfo? client, EffectiveSettings effective, IEnumerable<string> amr, string? sessionId, string? identityProvider = null, IEnumerable<string>? roles = null, DateTimeOffset? stepUpAtUtc = null)
    {
        var identity = new ClaimsIdentity(Scheme, "name", "role");
        identity.AddClaim(new Claim("sub", user.Id.ToString("D")));
        identity.AddClaim(new Claim("name", user.UserName ?? ""));
        identity.AddClaim(new Claim(Auth2Claims.ProjectId, project.Id.ToString("D")));
        identity.AddClaim(new Claim(Auth2Claims.ProjectKey, project.ProjectKey));
        if (client is not null) identity.AddClaim(new Claim(Auth2Claims.ClientId, client.ClientId));
        if (sessionId is not null) identity.AddClaim(new Claim(Auth2Claims.SessionId, sessionId));
        foreach (var a in amr) identity.AddClaim(new Claim(Auth2Claims.Amr, a));
        if (!string.IsNullOrEmpty(identityProvider)) identity.AddClaim(new Claim(Auth2Claims.IdentityProvider, identityProvider));
        // When the person last gave a code of the authenticator app in this session ("sudo mode", see StepUp). Kept in the session, not in the database.
        if (stepUpAtUtc is { } stepUp) identity.AddClaim(new Claim(StepUp.Claim, stepUp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // Add user roles so User.IsInRole(...) works with the cookie session
        var userManager = http.RequestServices.GetService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var userRoles = roles ?? (userManager is not null ? await userManager.GetRolesAsync(user) : []);
        foreach (var r in userRoles)
        {
            identity.AddClaim(new Claim("role", r));
            if (ClaimTypes.Role != "role") identity.AddClaim(new Claim(ClaimTypes.Role, r));
            if (r is Auth2Roles.GlobalAdmin or Auth2Roles.Developer)
            {
                identity.AddClaim(new Claim("scope", "auth2.admin.global"));
            }
        }

        var props = new AuthenticationProperties { IsPersistent = false };
        if (effective.CookieLifetimeMinutes is { } minutes)
        {
            props.IsPersistent = true;
            props.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
        }
        await http.SignInAsync(Scheme, new ClaimsPrincipal(identity), props);
    }
}

/// <summary>
/// Makes the cookie name depend on the project of the request (<c>.SystemX.Auth2.Session.{projectkey}</c>). The key comes from the
/// project scope, which is set as soon as the request found its project. Without a project the plain name is used (nothing is ever stored under it).
/// </summary>
internal sealed class ProjectCookieManager : Microsoft.AspNetCore.Authentication.Cookies.ICookieManager
{
    private readonly Microsoft.AspNetCore.Authentication.Cookies.ChunkingCookieManager _inner = new();

    private static string Suffix(HttpContext context) =>
        (context.RequestServices.GetService<IProjectCookieKey>()?.CookieProjectKey ?? context.RequestServices.GetService<IProjectScope>()?.CurrentProjectKey) is { Length: > 0 } key ? "." + key : "";

    public string? GetRequestCookie(HttpContext context, string key) => _inner.GetRequestCookie(context, key + Suffix(context));

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) =>
        _inner.AppendResponseCookie(context, key + Suffix(context), value, options);

    public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
        _inner.DeleteCookie(context, key + Suffix(context), options);
}
