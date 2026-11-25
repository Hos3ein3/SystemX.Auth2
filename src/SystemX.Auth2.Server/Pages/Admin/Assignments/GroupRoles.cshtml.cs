using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Assignments;

public sealed class GroupRolesModel : AdminPageModel
{
    public record GroupRoleItem(Guid GroupId, string GroupName, string? GroupDisplayName, Guid RoleId, string RoleName);

    public IReadOnlyList<GroupRoleItem> Items { get; private set; } = [];
    public IReadOnlyList<Group> AvailableGroups { get; private set; } = [];
    public IReadOnlyList<AppRole> AvailableRoles { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? GroupName { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
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

        // Available groups in project
        AvailableGroups = await db.Groups.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Name)
            .ToListAsync(HttpContext.RequestAborted);

        // Available roles in project (or global roles if global project)
        AvailableRoles = await db.Roles.AsNoTracking()
            .Where(r => r.ProjectId == projectId || (r.ProjectId == null && CurrentProject.IsGlobalProject))
            .OrderBy(r => r.Name)
            .ToListAsync(HttpContext.RequestAborted);

        var query = from gr in db.GroupRoles.AsNoTracking()
                    join g in db.Groups.AsNoTracking() on gr.GroupId equals g.Id
                    join r in db.Roles.AsNoTracking() on gr.RoleId equals r.Id
                    where g.ProjectId == projectId && (CurrentProject.IsGlobalProject ? (r.ProjectId == projectId || r.ProjectId == null) : r.ProjectId == projectId)
                    select new { gr.GroupId, GroupName = g.Name, GroupDisplayName = g.DisplayName, gr.RoleId, RoleName = r.Name ?? "" };

        if (!string.IsNullOrWhiteSpace(GroupName))
        {
            var gFilter = GroupName.Trim();
            query = query.Where(x => x.GroupName == gFilter);
        }

        if (!string.IsNullOrWhiteSpace(Role))
        {
            var rFilter = Role.Trim().ToUpperInvariant();
            query = query.Where(x => x.RoleName.ToUpper() == rFilter);
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToUpperInvariant();
            query = query.Where(x => x.GroupName.ToUpper().Contains(filter) || x.RoleName.ToUpper().Contains(filter));
        }

        TotalCount = await query.CountAsync(HttpContext.RequestAborted);
        var skip = Math.Max(0, (PageNumber - 1) * PageSize);
        var rows = await query.OrderBy(x => x.GroupName).ThenBy(x => x.RoleName)
            .Skip(skip)
            .Take(PageSize)
            .ToListAsync(HttpContext.RequestAborted);
        Items = [.. rows.Select(x => new GroupRoleItem(x.GroupId, x.GroupName, x.GroupDisplayName, x.RoleId, x.RoleName.Length > 0 ? x.RoleName : x.RoleId.ToString()))];
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string groupName, string? roleName, string? roleNameManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedRole = !string.IsNullOrWhiteSpace(roleNameManual) ? roleNameManual.Trim() : roleName?.Trim();

        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(selectedRole))
        {
            TempData["Error"] = "Both Group and Role are required.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddRoleAsync(groupName.Trim(), selectedRole, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign role to group.";
        else
            TempData["Success"] = $"Assigned role '{selectedRole}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRemoveAsync(string groupName, string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveRoleAsync(groupName, roleName, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove role from group.";
        else
            TempData["Success"] = $"Removed role '{roleName}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
