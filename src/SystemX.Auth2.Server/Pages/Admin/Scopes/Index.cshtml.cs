using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Scopes;

public sealed class IndexModel : AdminPageModel
{
    public PagedResult<AdminScopeDto> Scopes { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? Owner { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var scopeSvc = Svc<IAdminScopeService>();
        Scopes = await scopeSvc.ListAsync(new PageQuery(PageNumber, PageSize, Q), Owner, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var scopeSvc = Svc<IAdminScopeService>();
        var result = await scopeSvc.DeleteAsync(id, IsGlobalAdmin, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete scope.";
        else TempData["Success"] = "Scope deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
