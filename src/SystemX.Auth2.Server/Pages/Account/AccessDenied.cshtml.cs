using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>Shown when the user may not do what they asked.</summary>
public sealed class AccessDeniedModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>Loads the application.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Page();
    }
}
