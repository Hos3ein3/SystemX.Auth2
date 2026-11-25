using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Assignments;

public sealed class RolePermissionsModel : AdminPageModel
{
    public record RolePermItem(Guid RoleId, string RoleName, Guid PermissionId, string PermissionKey);

    public IReadOnlyList<RolePermItem> Items { get; private set; } = [];
    public IReadOnlyList<AppRole> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<Permission> AvailablePermissions { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? PermissionKey { get; set; }
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
        else if (Request.Query.TryGetValue("page", out var pVal) && int.TryParse(pVal, out var p) && p > 0)
        {
            PageNumber = p;
        }

        var db = Svc<Auth2DbContext>();
        var projectId = CurrentProject!.Id;

        // Roles available in this project (or global roles if global project)
        AvailableRoles = await db.Roles.AsNoTracking()
            .Where(r => r.ProjectId == projectId || (r.ProjectId == null && CurrentProject.IsGlobalProject))
            .OrderBy(r => r.Name)
            .ToListAsync(HttpContext.RequestAborted);

        var globalProj = await db.Projects.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == ProjectKeyRules.GlobalProjectKey, HttpContext.RequestAborted);
        var globalId = globalProj?.Id;

        // Permissions available (project specific + global if global project)
        AvailablePermissions = await db.Permissions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => (CurrentProject.IsGlobalProject ? (p.ProjectId == projectId || (globalId != null && p.ProjectId == globalId.Value)) : p.ProjectId == projectId) && !p.IsObsolete)
            .OrderBy(p => p.Key)
            .ToListAsync(HttpContext.RequestAborted);

        var query = from rp in db.RolePermissions.AsNoTracking()
                    join r in db.Roles.AsNoTracking() on rp.RoleId equals r.Id
                    join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on rp.PermissionId equals p.Id
                    where CurrentProject.IsGlobalProject ? (r.ProjectId == projectId || r.ProjectId == null) : r.ProjectId == projectId
                    select new { rp.RoleId, RoleName = r.Name ?? "", rp.PermissionId, PermissionKey = p.Key };

        if (!string.IsNullOrWhiteSpace(Role))
        {
            var rFilter = Role.Trim();
            query = query.Where(x => x.RoleName == rFilter);
        }

        if (!string.IsNullOrWhiteSpace(PermissionKey))
        {
            var pFilter = PermissionKey.Trim();
            query = query.Where(x => x.PermissionKey == pFilter);
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToUpperInvariant();
            query = query.Where(x => x.RoleName.ToUpper().Contains(filter) || x.PermissionKey.ToUpper().Contains(filter));
        }

        TotalCount = await query.CountAsync(HttpContext.RequestAborted);
        var rows = await query.OrderBy(x => x.RoleName).ThenBy(x => x.PermissionKey)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync(HttpContext.RequestAborted);
        Items = [.. rows.Select(x => new RolePermItem(x.RoleId, x.RoleName.Length > 0 ? x.RoleName : x.RoleId.ToString(), x.PermissionId, x.PermissionKey))];
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string? roleName, string? roleNameManual, string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedRole = !string.IsNullOrWhiteSpace(roleNameManual) ? roleNameManual.Trim() : roleName?.Trim();
        var selectedPerm = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();

        if (string.IsNullOrWhiteSpace(selectedRole) || string.IsNullOrWhiteSpace(selectedPerm))
        {
            TempData["Error"] = "Both Role and Permission are required.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.AddRolePermissionAsync(selectedRole, selectedPerm, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign permission to role.";
        else
            TempData["Success"] = $"Assigned permission '{selectedPerm}' to role '{selectedRole}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRemoveAsync(string roleName, string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RemoveRolePermissionAsync(roleName, permissionKey, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove permission from role.";
        else
            TempData["Success"] = $"Removed permission '{permissionKey}' from role '{roleName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
