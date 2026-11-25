using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Roles;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<RoleDto> Roles { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    public int TotalCount { get; private set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        if (Request.Query.TryGetValue("pageNumber", out var pnVal) && int.TryParse(pnVal, out var pn) && pn > 0)
        {
            PageNumber = pn;
        }

        var app = CurrentProject!;
        var db = Svc<Persistence.Auth2DbContext>();
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Roles.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.ProjectId == app.Id || r.ProjectId == null)
            .OrderBy(r => r.Name),
            HttpContext.RequestAborted);
        var ids = rows.Select(r => r.Id).ToList();
        var perms = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            (from rp in db.RolePermissions.IgnoreQueryFilters().AsNoTracking()
             join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on rp.PermissionId equals p.Id
             where ids.Contains(rp.RoleId)
             select new { rp.RoleId, p.Key }),
            HttpContext.RequestAborted);

        var allRoles = rows.Select(r => new RoleDto(
            r.Name!,
            r.Description,
            r.ProjectId is null,
            r.IsSystem,
            perms.Where(p => p.RoleId == r.Id).Select(p => p.Key).OrderBy(k => k).ToList()
        )).ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToLowerInvariant();
            allRoles = allRoles.Where(r => r.Name.ToLowerInvariant().Contains(filter) || (r.Description?.ToLowerInvariant().Contains(filter) == true)).ToList();
        }

        TotalCount = allRoles.Count;
        Roles = allRoles.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.DeleteRoleAsync(name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete role.";
        else TempData["Success"] = $"Role '{name}' deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
