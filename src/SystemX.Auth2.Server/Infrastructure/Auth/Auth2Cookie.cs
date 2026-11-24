using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// The login session of the hosted UI (the browser cookie). It remembers who signed in and for which application (claim <c>app</c>).
/// The authorize endpoint uses it only when <c>app</c> matches the application of the request, so a login in one application
/// never silently signs the user into another application (their credentials may be different there).
/// </summary>
public static class Auth2Cookie
{
    /// <summary>Authentication scheme name.</summary>
    public const string Scheme = "Auth2.Session";

    /// <summary>Cookie name.</summary>
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
    public static async Task SignInAsync(HttpContext http, AppUser user, Auth2Application app, ApplicationSettings settings, IEnumerable<string> amr, string? sessionId, string? identityProvider = null)
    {
        var identity = new ClaimsIdentity(Scheme, "name", "role");
        identity.AddClaim(new Claim("sub", user.Id.ToString("D")));
        identity.AddClaim(new Claim("name", user.UserName ?? ""));
        identity.AddClaim(new Claim(Auth2Claims.ApplicationId, app.Id.ToString("D")));
        identity.AddClaim(new Claim(Auth2Claims.ClientId, app.ClientId));
        if (sessionId is not null) identity.AddClaim(new Claim(Auth2Claims.SessionId, sessionId));
        foreach (var a in amr) identity.AddClaim(new Claim(Auth2Claims.Amr, a));
        if (!string.IsNullOrEmpty(identityProvider)) identity.AddClaim(new Claim(Auth2Claims.IdentityProvider, identityProvider));

        var props = new AuthenticationProperties { IsPersistent = false };
        if (settings.Sessions.CookieLifetimeMinutes is { } minutes)
        {
            props.IsPersistent = true;
            props.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
        }
        await http.SignInAsync(Scheme, new ClaimsPrincipal(identity), props);
    }
}
