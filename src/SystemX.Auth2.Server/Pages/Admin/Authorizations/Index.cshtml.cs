using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Authorizations;

public sealed class IndexModel : AdminPageModel
{
    public PagedResult<AdminAuthorizationDto> Authorizations { get; private set; } = new([], 1, 25, 0);
    public Dictionary<string, string> Usernames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        if (Request.Query.TryGetValue("pageNumber", out var pnVal) && int.TryParse(pnVal, out var pn) && pn > 0)
        {
            PageNumber = pn;
        }
        else if (Request.Query.TryGetValue("page", out var pVal) && int.TryParse(pVal, out var p) && p > 0)
        {
            PageNumber = p;
        }

        var authSvc = Svc<IAdminAuthorizationService>();
        Authorizations = await authSvc.ListAsync(new PageQuery(PageNumber, PageSize, Q), null, Status, HttpContext.RequestAborted);

        var userGuids = Authorizations.Items
            .Select(a => a.Subject)
            .Where(s => !string.IsNullOrEmpty(s) && Guid.TryParse(s, out _))
            .Select(s => Guid.Parse(s!))
            .Distinct()
            .ToList();

        if (userGuids.Count > 0)
        {
            var db = Svc<Auth2DbContext>();
            var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => userGuids.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.UserName ?? u.Email ?? u.DisplayName ?? u.Id.ToString() })
                .ToListAsync(HttpContext.RequestAborted);

            foreach (var u in users)
            {
                Usernames[u.Id.ToString()] = u.Name;
            }
        }

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
