using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>What the callback page must do after <see cref="ExternalFlow.CompleteAsync"/>.</summary>
public sealed record ExternalFlowResult
{
    /// <summary>The browser must go here (sign-in done, or the link was added).</summary>
    public string? RedirectTo { get; init; }
    /// <summary>The user must give the second factor (token for the two-factor page).</summary>
    public string? TwoFactorToken { get; init; }
    /// <summary>The source gave no email: the user must enter one (pending token for the confirm page).</summary>
    public string? PendingToken { get; init; }
    /// <summary>Error code when the sign-in was refused.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Where to go after the second factor or the email step.</summary>
    public string? ReturnUrl { get; init; }
}

/// <summary>
/// The browser side of redirect sign-ins: starts a challenge (keeps the sealed state in a short-lived cookie), reads the callback, runs
/// <see cref="IExternalLoginService"/> and signs the user into the hosted session. Used by the page models and the minimal routes.
/// </summary>
public sealed class ExternalFlow(
    IExternalProviderRegistry registry, IEnumerable<IExternalIdentitySource> sources, IExternalLoginService external, IExternalLinkService links,
    IAuthFlowResolver flow, IReturnUrlValidator urls, IClientIdAccessor clientId, Microsoft.Extensions.Options.IOptions<Auth2ServerOptions> options, IAuditService audit)
{
    /// <summary>Name of the state cookie.</summary>
    public const string StateCookie = ".SystemX.Auth2.ExternalState";

    private string Issuer(HttpContext http) =>
        !string.IsNullOrWhiteSpace(options.Value.Issuer) ? options.Value.Issuer.TrimEnd('/') : $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";

    /// <summary>The enabled providers of the current application, in the form the login page needs.</summary>
    public async Task<IReadOnlyList<ExternalProviderInfoDto>> ListAsync(CancellationToken ct)
    {
        var app = await flow.GetApplicationAsync(ct);
        if (!app.IsEnabled) return [];
        return [.. registry.ListForApplication(app.Id).Select(p => new ExternalProviderInfoDto(p.Name, p.Settings.DisplayName ?? p.Name, p.Type,
            p.Type == ExternalProviderType.Ldap ? null : "/Account/External/Challenge", p.Type == ExternalProviderType.Ldap))];
    }

    /// <summary>Home-realm discovery: the provider for an email (or a provider name given as hint), or null.</summary>
    public async Task<string?> DiscoverAsync(string? hint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var app = await flow.GetApplicationAsync(ct);
        var direct = registry.Get(app.Id, hint.Trim());
        if (direct is not null) return direct.Name;
        var at = hint.LastIndexOf('@');
        return at > 0 && at < hint.Length - 1 ? registry.FindByEmailDomain(app.Id, hint[(at + 1)..].Trim())?.Name : null;
    }

    /// <summary>Starts a sign-in (or a link when <paramref name="linkUserId"/> is set). Returns the provider address, or an error code.</summary>
    public async Task<(string? RedirectTo, string? ErrorCode)> StartAsync(HttpContext http, string providerName, string? returnUrl, string? hint, string? linkUserId, CancellationToken ct)
    {
        var app = await flow.GetApplicationAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var provider = app.IsEnabled ? registry.Get(app.Id, providerName ?? "") : null;
        if (provider is null || provider.Type == ExternalProviderType.Ldap) return (null, Auth2ErrorCodes.ExternalProviderDisabled);
        var source = sources.OfType<IRedirectIdentitySource>().FirstOrDefault(x => x.Handles(provider.Type));
        if (source is null) return (null, Auth2ErrorCodes.ExternalProviderDisabled);

        var safeReturn = urls.IsAllowed(returnUrl, settings.Redirects.AllowedRedirectUris) ? returnUrl! : "";
        try
        {
            var challenge = await source.ChallengeAsync(new ExternalChallengeContext(provider, Issuer(http), safeReturn, hint, linkUserId), ct);
            http.Response.Cookies.Append(StateCookie, challenge.ProtectedState, new CookieOptions
            {
                HttpOnly = true, Secure = http.Request.IsHttps, MaxAge = challenge.StateLifetime, Path = "/",
                SameSite = http.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,   // Apple answers with a cross-site POST
                IsEssential = true,
            });
            return (challenge.RedirectUri.ToString(), null);
        }
        catch (ExternalLoginException ex)
        {
            await audit.WriteAsync(new AuditEntry("external.login.failed", app.Id, Data: new Dictionary<string, object?> { ["provider"] = providerName, ["code"] = ex.ErrorCode, ["reason"] = ex.Reason }), ct);
            return (null, ex.ErrorCode);
        }
    }

    /// <summary>Handles the answer of a provider (GET query or POST form) for the provider found by the URL.</summary>
    public async Task<ExternalFlowResult> CompleteAsync(HttpContext http, string slug, string name, CancellationToken ct)
    {
        var provider = registry.FindByUrl(slug, name);
        if (provider is null) return new ExternalFlowResult { ErrorCode = Auth2ErrorCodes.ExternalProviderDisabled };
        clientId.ClientId = provider.ClientId;   // the URL decides the application; nothing else is trusted
        var app = await flow.GetApplicationAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);

        var state = http.Request.Cookies[StateCookie];
        http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/", Secure = http.Request.IsHttps, SameSite = http.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax });
        var source = sources.OfType<IRedirectIdentitySource>().FirstOrDefault(x => x.Handles(provider.Type));
        if (source is null) return new ExternalFlowResult { ErrorCode = Auth2ErrorCodes.ExternalProviderDisabled };

        var parameters = (http.Request.HasFormContentType ? http.Request.Form : (IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>>)http.Request.Query)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        ExternalCallbackResult callback;
        try { callback = await source.AuthenticateCallbackAsync(new ExternalCallbackContext(provider, Issuer(http), parameters, state), ct); }
        catch (ExternalLoginException ex)
        {
            await audit.WriteAsync(new AuditEntry(ex.Reason.StartsWith("saml", StringComparison.Ordinal) ? "saml.assertion.rejected" : "external.login.failed", app.Id,
                IpAddress: http.Connection.RemoteIpAddress?.ToString(), Data: new Dictionary<string, object?> { ["provider"] = provider.Name, ["code"] = ex.ErrorCode, ["reason"] = ex.Reason }), ct);
            return new ExternalFlowResult { ErrorCode = ex.ErrorCode };
        }

        // Linking: a signed-in user of this application adds the provider to the own account.
        if (callback.LinkUserId is not null)
        {
            var session = await http.AuthenticateAsync(Auth2Cookie.Scheme);
            var sameUser = session.Succeeded && session.Principal!.FindFirst("sub")?.Value == callback.LinkUserId
                && session.Principal.FindFirst(Auth2Claims.ApplicationId)?.Value == app.Id.ToString("D");
            if (!sameUser) return new ExternalFlowResult { ErrorCode = Auth2ErrorCodes.InvalidToken };
            var user = await http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.AppUser>>().FindByIdAsync(callback.LinkUserId);
            if (user is null) return new ExternalFlowResult { ErrorCode = Auth2ErrorCodes.InvalidToken };
            var linked = await links.LinkAsync(user, callback.Identity, ct);
            return linked.Succeeded ? new ExternalFlowResult { RedirectTo = "/Manage/ExternalLogins" } : new ExternalFlowResult { ErrorCode = linked.ErrorCode };
        }

        var outcome = await external.HandleAsync(new ExternalLoginRequest(provider, callback.Identity, callback.ReturnUrl,
            http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString()), ct);
        return await FinishAsync(http, app, settings, outcome, ct);
    }

    /// <summary>Turns an outcome into the next step: sign the user in, ask for the second factor, ask for the email, or fail.</summary>
    public async Task<ExternalFlowResult> FinishAsync(HttpContext http, Domain.Entities.Auth2Application app, ApplicationSettings settings, ExternalLoginOutcome outcome, CancellationToken ct)
    {
        if (outcome.NeedsEmail) return new ExternalFlowResult { PendingToken = outcome.PendingToken, ReturnUrl = outcome.ReturnUrl };
        var login = outcome.Login;
        if (login is { Succeeded: true })
        {
            await Auth2Cookie.SignInAsync(http, login.User!, app, settings, login.Amr, login.SessionId, login.IdentityProvider);
            return new ExternalFlowResult { RedirectTo = urls.Resolve(outcome.ReturnUrl, settings.Redirects.RedirectAfterLogin, settings) };
        }
        if (login?.ErrorCode == Auth2ErrorCodes.TwoFactorRequired) return new ExternalFlowResult { TwoFactorToken = login.TwoFactorToken, ReturnUrl = outcome.ReturnUrl };
        return new ExternalFlowResult { ErrorCode = outcome.ErrorCode ?? login?.ErrorCode ?? Auth2ErrorCodes.ExternalClaimsInvalid };
    }
}
