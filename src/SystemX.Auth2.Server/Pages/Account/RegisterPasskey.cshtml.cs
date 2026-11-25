#pragma warning disable CS1591
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Account;

public sealed class RegisterPasskeyModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    [BindProperty(SupportsGet = true)]
    public string? UserName { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        return Page();
    }
}
