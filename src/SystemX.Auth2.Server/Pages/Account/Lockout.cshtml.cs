using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>Shown when the account is locked. It does not say how long: that would help an attacker.</summary>
public sealed class LockoutModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>Loads the application (for the brand name and the support email).</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        await LoadApplicationAsync();
        Response.StatusCode = StatusCodes.Status200OK;
        return Page();
    }
}
