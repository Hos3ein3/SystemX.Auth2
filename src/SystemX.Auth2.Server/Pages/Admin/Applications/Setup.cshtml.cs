using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Applications;

public sealed class SetupModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public ClientSetupDto? Setup { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var appSvc = Svc<IAdminApplicationService>();
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var (result, setup) = await appSvc.GetSetupAsync(CurrentProject!.Id, Id, serverUrl, HttpContext.RequestAborted);

        if (!result.Succeeded || setup is null) return NotFound();
        Setup = setup;
        return Page();
    }
}
