using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages;

public sealed class NotFoundModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        ErrorCode = null;
        ErrorMessage = null;
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
