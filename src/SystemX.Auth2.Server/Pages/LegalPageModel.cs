#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages;

/// <summary>
/// Base of the public text pages (<c>/Privacy</c> and <c>/Terms</c>). Anybody may read them, also without a sign-in and without an application.
/// When the application is known (<c>?ClientId=</c>, or a single-project server) the page shows its brand name and support address;
/// when it is not known the page still works, with the default name.
/// </summary>
public abstract class LegalPageModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>The address people can write to (setting <c>Localization.SupportEmail</c>), or null.</summary>
    public string? SupportEmail => string.IsNullOrWhiteSpace(Settings.Localization.SupportEmail) ? null : Settings.Localization.SupportEmail.Trim();

    /// <summary>The name of the operator's service: the brand of the project, else the default name.</summary>
    public string ServiceName => BrandName ?? "this service";

    /// <summary>Where the "back" link goes: the sign-in page of the application, or the home page.</summary>
    public string BackUrl => ProjectFound && !string.IsNullOrEmpty(ClientIdForLinks) ? Url.Page("/Account/Login", new { ClientId = ClientIdForLinks }) ?? "/" : "/";

    public async Task<IActionResult> OnGetAsync()
    {
        // An unknown application is not an error here: the text is the same for everybody.
        if (!await LoadProjectAsync())
        {
            ErrorCode = null;
            ErrorMessage = null;
            Response.StatusCode = StatusCodes.Status200OK;
        }
        return Page();
    }
}
