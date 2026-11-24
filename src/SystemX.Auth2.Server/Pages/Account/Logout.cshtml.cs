using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>Signs the user out of the hosted UI session. Where to go next: valid return URL, then <c>RedirectAfterLogout</c>, then the home page.</summary>
public sealed class LogoutModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>Loads the application. The UI shows an "are you sure" form.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        Response.StatusCode = StatusCodes.Status200OK;   // a missing application is fine here
        return Page();
    }

    /// <summary>Signs out.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        var found = await LoadProjectAsync();
        Response.StatusCode = StatusCodes.Status200OK;
        await HttpContext.SignOutAsync(Auth2Cookie.Scheme);
        return Redirect(ResolveRedirect(found ? Settings.Redirects.RedirectAfterLogout : null));
    }
}
