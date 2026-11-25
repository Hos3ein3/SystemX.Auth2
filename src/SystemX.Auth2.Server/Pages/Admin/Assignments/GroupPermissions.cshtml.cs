using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Assignments;

public sealed class GroupPermissionsModel : AdminPageModel
{
    public record GroupPermItem(Guid GroupId, string GroupName, string? GroupDisplayName, Guid PermissionId, string PermissionKey);

    public IReadOnlyList<GroupPermItem> Items { get; private set; } = [];
    public IReadOnlyList<Group> AvailableGroups { get; private set; } = [];
    public IReadOnlyList<Permission> AvailablePermissions { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var db = Svc<Auth2DbContext>();
        var projectId = CurrentProject!.Id;

        // Available groups in project
        AvailableGroups = await db.Groups.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Name)
            .ToListAsync(HttpContext.RequestAborted);

        var globalProj = await db.Projects.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == ProjectKeyRules.GlobalProjectKey, HttpContext.RequestAborted);
        var globalId = globalProj?.Id;

        // Available permissions (project specific + global if global project)
        AvailablePermissions = await db.Permissions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => (CurrentProject.IsGlobalProject ? (p.ProjectId == projectId || (globalId != null && p.ProjectId == globalId.Value)) : p.ProjectId == projectId) && !p.IsObsolete)
            .OrderBy(p => p.Key)
            .ToListAsync(HttpContext.RequestAborted);

        var query = from gp in db.GroupPermissions.AsNoTracking()
                    join g in db.Groups.AsNoTracking() on gp.GroupId equals g.Id
                    join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on gp.PermissionId equals p.Id
                    where g.ProjectId == projectId && (CurrentProject.IsGlobalProject || p.ProjectId == projectId)
                    select new { gp.GroupId, GroupName = g.Name, GroupDisplayName = g.DisplayName, gp.PermissionId, PermissionKey = p.Key };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToUpperInvariant();
            query = query.Where(x => x.GroupName.ToUpper().Contains(filter) || x.PermissionKey.ToUpper().Contains(filter));
        }

        var rows = await query.OrderBy(x => x.GroupName).ThenBy(x => x.PermissionKey).ToListAsync(HttpContext.RequestAborted);
        Items = [.. rows.Select(x => new GroupPermItem(x.GroupId, x.GroupName, x.GroupDisplayName, x.PermissionId, x.PermissionKey))];
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string groupName, string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedPerm = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();

        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(selectedPerm))
        {
            TempData["Error"] = "Both Group and Permission are required.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddPermissionAsync(groupName.Trim(), selectedPerm, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign permission to group.";
        else
            TempData["Success"] = $"Assigned permission '{selectedPerm}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRemoveAsync(string groupName, string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemovePermissionAsync(groupName, permissionKey, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove permission from group.";
        else
            TempData["Success"] = $"Removed permission '{permissionKey}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
