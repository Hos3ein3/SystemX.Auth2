using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages;

/// <summary>
/// Base class of the pages. A page finds its application from the <c>client_id</c> query value, a <c>ClientId</c> form value,
/// or the <c>client_id</c> inside <c>ReturnUrl</c> (the authorize URL). In single-application mode no client id is needed.
/// The pages have no HTML yet: they expose properties and flags for the UI that comes later (see docs/ui-handover.md).
/// </summary>
public abstract class Auth2PageModel(IClientIdAccessor clientIdAccessor, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text) : PageModel
{
    /// <summary>Where to go after the page is done. Only safe values are used.</summary>
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>The client id of the application (optional when <c>ReturnUrl</c> has one, or in single mode).</summary>
    [BindProperty(SupportsGet = true)] public string? ClientId { get; set; }

    /// <summary>The application, or null when it could not be found.</summary>
    public Auth2Application? Application { get; private set; }

    /// <summary>Settings of the application.</summary>
    public ApplicationSettings Settings { get; private set; } = new();

    /// <summary>False when the application could not be found.</summary>
    public bool ApplicationFound => Application is not null;

    /// <summary>Localized error message for the UI, or null.</summary>
    public string? ErrorMessage { get; protected set; }

    /// <summary>Stable error code for the UI, or null.</summary>
    public string? ErrorCode { get; protected set; }

    /// <summary>Brand name of the application, for titles.</summary>
    public string? BrandName => Settings.Localization.BrandName ?? Application?.Name;

    /// <summary>Finds the application of this request and loads its settings. Returns false when there is none.</summary>
    protected async Task<bool> LoadApplicationAsync()
    {
        clientIdAccessor.ClientId ??= string.IsNullOrEmpty(ClientId) ? ReturnUrlQuery.Get(ReturnUrl, "client_id") : ClientId;
        try
        {
            Application = await flow.GetApplicationAsync(HttpContext.RequestAborted);
            Settings = await flow.GetSettingsAsync(HttpContext.RequestAborted);
            return true;
        }
        catch (ApplicationNotFoundException)
        {
            Fail(SystemX.Auth2.Contracts.Constants.Auth2ErrorCodes.ApplicationNotFound, StatusCodes.Status404NotFound);
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

    /// <summary>The URL to go to after success: valid return URL, then the application fallback, then <paramref name="home"/>.</summary>
    protected string ResolveRedirect(string? applicationFallback, string home = "/") => urls.Resolve(ReturnUrl, applicationFallback, Settings, home);
}
