using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Authorizations;

public sealed class IndexModel : AdminPageModel
{
    public PagedResult<AdminAuthorizationDto> Authorizations { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var authSvc = Svc<IAdminAuthorizationService>();
        Authorizations = await authSvc.ListAsync(new PageQuery(PageNumber, PageSize, Q), null, Status, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var authSvc = Svc<IAdminAuthorizationService>();
        var result = await authSvc.RevokeAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to revoke authorization.";
        else TempData["Success"] = "Authorization revoked successfully.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
