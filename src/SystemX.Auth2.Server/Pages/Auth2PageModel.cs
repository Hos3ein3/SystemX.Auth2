using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages;

/// <summary>
/// Base class of the pages. A page finds its project (and its application) from the <c>client_id</c> query value, a <c>ClientId</c> form value,
/// or the <c>client_id</c> inside <c>ReturnUrl</c> (the authorize URL). In single-project mode the project needs no client id.
/// The pages have no HTML yet: they expose properties and flags for the UI that comes later (see docs/ui-handover.md).
/// </summary>
public abstract class Auth2PageModel(IClientIdAccessor clientIdAccessor, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text) : PageModel
{
    /// <summary>Where to go after the page is done. Only safe values are used.</summary>
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>The client id of the application (optional when <c>ReturnUrl</c> has one, or in single mode).</summary>
    [BindProperty(SupportsGet = true)] public string? ClientId { get; set; }

    /// <summary>The project, or null when it could not be found.</summary>
    public Project? Project { get; private set; }

    /// <summary>The calling application (client), or null when the request has no client id.</summary>
    public ClientApplicationInfo? Client { get; private set; }

    /// <summary>Settings of the project.</summary>
    public ProjectSettings Settings { get; private set; } = new();

    /// <summary>Project settings with the application overrides applied.</summary>
    public EffectiveSettings Effective { get; private set; } = new(new ProjectSettings(), null);

    /// <summary>False when the project could not be found.</summary>
    public bool ProjectFound => Project is not null;

    /// <summary>The client id to put into redirects to the next page (so the next page finds the same application).</summary>
    public string? ClientIdForLinks => Client?.ClientId ?? ClientId ?? ReturnUrlQuery.Get(ReturnUrl, "client_id") ?? ReturnUrlQuery.Get(ReturnUrl, "ClientId");

    /// <summary>Builds a sign-in URL that preserves ReturnUrl, ClientId, and Culture.</summary>
    public string GetLoginUrl()
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(ReturnUrl)) q.Add($"ReturnUrl={Uri.EscapeDataString(ReturnUrl)}");
        var cid = ClientIdForLinks;
        if (!string.IsNullOrEmpty(cid)) q.Add($"ClientId={Uri.EscapeDataString(cid)}");
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (!string.IsNullOrEmpty(culture) && culture != "en") q.Add($"culture={Uri.EscapeDataString(culture)}");
        return "/Account/Login" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>Localized error message for the UI, or null.</summary>
    public string? ErrorMessage { get; protected set; }

    /// <summary>Localizer service.</summary>
    public IAuth2Localizer Text => text;

    /// <summary>Stable error code for the UI, or null.</summary>
    public string? ErrorCode { get; protected set; }

    /// <summary>Brand name of the application, for titles.</summary>
    public string? BrandName => Settings.Localization.BrandName ?? Project?.Name;

    /// <summary>Finds the application of this request and loads its settings. Returns false when there is none.</summary>
    protected async Task<bool> LoadProjectAsync()
    {
        clientIdAccessor.ClientId ??= !string.IsNullOrEmpty(ClientId) ? ClientId
            : ReturnUrlQuery.Get(ReturnUrl, "client_id") ?? ReturnUrlQuery.Get(ReturnUrl, "ClientId");
        try
        {
            Project = await flow.GetProjectAsync(HttpContext.RequestAborted);
            Settings = await flow.GetSettingsAsync(HttpContext.RequestAborted);
            Client = await flow.GetClientAsync(HttpContext.RequestAborted);
            Effective = new EffectiveSettings(Settings, Client);
            return true;
        }
        catch (ProjectNotFoundException)
        {
            Fail(SystemX.Auth2.Contracts.Constants.Auth2ErrorCodes.ProjectNotFound, StatusCodes.Status404NotFound);
            return false;
        }
    }

    /// <summary>Sets the error and the HTTP status code.</summary>
    protected void Fail(string code, int statusCode = StatusCodes.Status400BadRequest)
    {
        ErrorCode = code;
        ErrorMessage = text.Error(code);
        Response.StatusCode = statusCode;
    }

    /// <summary>A service of the request. Pages use this to keep their constructors short.</summary>
    protected T Svc<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    /// <summary><see cref="ReturnUrl"/> if it is safe, otherwise null. Use it for links in emails.</summary>
    public string? SafeReturnUrl() => urls.IsAllowed(ReturnUrl, Effective.AllowedRedirectUris) ? ReturnUrl : null;

    /// <summary>Messages of every broken rule (for example the password rules), localized.</summary>
    public IReadOnlyList<string> ErrorMessages { get; protected set; } = [];

    /// <summary>Sets the error from an operation result.</summary>
    protected void Fail(Application.OperationResult result, int statusCode = StatusCodes.Status400BadRequest)
    {
        Fail(result.ErrorCode ?? SystemX.Auth2.Contracts.Constants.Auth2ErrorCodes.ValidationFailed, statusCode);
        ErrorMessages = [.. result.Errors.Select(e => e.Description)];
    }

    /// <summary>Signs the user into the hosted-UI session and returns the redirect to the first safe target.</summary>
    protected async Task<IActionResult> SignInAndRedirectAsync(LoginResult result, string? applicationFallback)
    {
        await Auth2Cookie.SignInAsync(HttpContext, result.User!, Project!, Client, Effective, result.Amr, result.SessionId, result.IdentityProvider);
        return Redirect(ResolveRedirect(applicationFallback));
    }

    /// <summary>The URL to go to after success: valid return URL, then the application fallback, then <paramref name="home"/>.</summary>
    protected string ResolveRedirect(string? applicationFallback, string home = "/") => urls.Resolve(ReturnUrl, applicationFallback, Effective, home);
}
