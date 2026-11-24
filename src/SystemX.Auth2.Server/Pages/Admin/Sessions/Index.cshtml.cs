using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Sessions;

public sealed class IndexModel : AdminPageModel
{
    public PagedResult<AdminSessionDto> Sessions { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ip { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var sessionSvc = Svc<IAdminSessionService>();
        Sessions = await sessionSvc.ListAsync(new PageQuery(PageNumber, PageSize, Q), null, Status, Ip, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var sessionSvc = Svc<IAdminSessionService>();
        var result = await sessionSvc.RevokeAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to revoke session.";
        else TempData["Success"] = "Session revoked.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRevokeAllAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var sessionSvc = Svc<IAdminSessionService>();
        var result = await sessionSvc.RevokeAllAsync(null, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to revoke sessions.";
        else TempData["Success"] = "All sessions revoked.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
