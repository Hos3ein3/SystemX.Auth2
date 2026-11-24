using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Localization;

/// <summary>
/// Culture order: OIDC <c>ui_locales</c> → user's saved culture → culture cookie → <c>Accept-Language</c> →
/// application <c>DefaultCulture</c> → <c>en</c>. A candidate is used only when the server and the application support it.
/// </summary>
public static class Auth2CultureResolver
{
    /// <summary>Fallback culture.</summary>
    public const string Fallback = "en";

    /// <summary>Cultures the server has resx files for.</summary>
    public static readonly IReadOnlyList<string> ServerCultures = ["en", "fa", "nl", "it"];

    /// <summary>Picks the culture. All candidates are optional.</summary>
    /// <param name="uiLocales">Space-separated <c>ui_locales</c> value.</param>
    /// <param name="userCulture">Saved culture of the user.</param>
    /// <param name="cookieCulture">Culture from the cookie.</param>
    /// <param name="acceptLanguage">Accept-Language values, best first.</param>
    /// <param name="applicationDefault">The application's default culture.</param>
    /// <param name="applicationSupported">The application's supported cultures. Empty means all server cultures.</param>
    public static string Resolve(string? uiLocales, string? userCulture, string? cookieCulture, IEnumerable<string> acceptLanguage,
        string? applicationDefault, IReadOnlyCollection<string>? applicationSupported)
    {
        string? Accept(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            var match = Match(candidate.Trim(), ServerCultures);
            if (match is null) return null;
            if (applicationSupported is { Count: > 0 } && Match(match, applicationSupported) is null) return null;
            return match;
        }

        foreach (var c in (uiLocales ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (Accept(c) is { } hit) return hit;
        if (Accept(userCulture) is { } user) return user;
        if (Accept(cookieCulture) is { } cookie) return cookie;
        foreach (var c in acceptLanguage)
            if (Accept(c) is { } hit) return hit;
        return Accept(applicationDefault) ?? Fallback;
    }

    /// <summary>Matches a language tag like <c>fa-IR</c> to a supported culture like <c>fa</c>.</summary>
    internal static string? Match(string tag, IEnumerable<string> supported)
    {
        var list = supported.ToList();
        var exact = list.FirstOrDefault(s => string.Equals(s, tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var neutral = tag.Split('-', '_')[0];
        return list.FirstOrDefault(s => string.Equals(s, neutral, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Request culture provider that uses <see cref="Auth2CultureResolver"/>.</summary>
public sealed class Auth2RequestCultureProvider : RequestCultureProvider
{
    /// <summary>Name of the culture cookie.</summary>
    public const string CookieName = ".SystemX.Auth2.Culture";

    /// <inheritdoc />
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var uiLocales = request.Query["ui_locales"].ToString();
        if (string.IsNullOrEmpty(uiLocales)) uiLocales = FromReturnUrl(request.Query["ReturnUrl"].ToString(), "ui_locales") ?? "";

        // The saved culture of a signed-in user is read from the "locale" claim.
        var userCulture = httpContext.User.FindFirst("locale")?.Value;
        var cookie = request.Cookies[CookieName];
        var accept = request.Headers.AcceptLanguage.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(';')[0].Trim()).ToList();

        string? appDefault = null;
        IReadOnlyCollection<string>? supported = null;
        try
        {
            var sp = httpContext.RequestServices;
            var clientId = request.Query["client_id"].ToString();
            if (string.IsNullOrEmpty(clientId)) clientId = FromReturnUrl(request.Query["ReturnUrl"].ToString(), "client_id") ?? "";
            if (!string.IsNullOrEmpty(clientId)) sp.GetRequiredService<IClientIdAccessor>().ClientId ??= clientId;
            var flow = sp.GetRequiredService<IAuthFlowResolver>();
            var settings = await flow.GetSettingsAsync(httpContext.RequestAborted);
            appDefault = settings.Localization.DefaultCulture;
            supported = settings.Localization.SupportedCultures;
        }
        catch (ProjectNotFoundException)
        {
            // No application known for this request: use the server defaults.
        }

        var culture = Auth2CultureResolver.Resolve(uiLocales, userCulture, cookie, accept, appDefault, supported);
        return new ProviderCultureResult(culture);
    }

    private static string? FromReturnUrl(string returnUrl, string key) => Auth.ReturnUrlQuery.Get(returnUrl, key);
}
