using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Roles;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string Name { get; set; } = "";
    public RoleDto? Role { get; private set; }
    public IReadOnlyList<PermissionDto> AvailablePermissions { get; private set; } = [];

    // Users and groups assigned to this role
    public record RoleUser(Guid Id, string UserName, string? DisplayName);
    public IReadOnlyList<RoleUser> AssignedUsers { get; private set; } = [];
    public IReadOnlyList<string> AssignedGroups { get; private set; } = [];

    public IReadOnlyList<AppUser> AvailableUsers { get; private set; } = [];
    public IReadOnlyList<Group> AvailableGroups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var roles = await accessSvc.ListRolesAsync(HttpContext.RequestAborted);
        Role = roles.FirstOrDefault(r => string.Equals(r.Name, Name, StringComparison.OrdinalIgnoreCase));

        if (Role is null) return NotFound();

        AvailablePermissions = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);

        var db = Svc<Auth2DbContext>();
        var roleManager = Svc<RoleManager<AppRole>>();
        var appRole = await roleManager.FindByNameAsync(Name);

        if (appRole is not null)
        {
            var roleId = appRole.Id;
            var projectId = CurrentProject!.Id;

            // Users with this role
            // Only plain columns go into SQL: a Guid.ToString() in the projection cannot be translated. The display value is made in memory.
            var rows = await (from ur in db.UserRoles.AsNoTracking()
                              join u in db.Users.AsNoTracking() on ur.UserId equals u.Id
                              join up in db.UserProjects.AsNoTracking() on u.Id equals up.UserId
                              where ur.RoleId == roleId && (up.ProjectId == projectId || IsGlobalAdmin)
                              select new { u.Id, u.UserName, u.Email, u.DisplayName })
                             .Distinct()
                             .ToListAsync(HttpContext.RequestAborted);
            AssignedUsers = [.. rows.Select(r => new RoleUser(r.Id, r.UserName ?? r.Email ?? r.Id.ToString(), r.DisplayName))
                                    .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)];

            // Groups with this role (GroupRole)
            AssignedGroups = await (from gr in db.GroupRoles.AsNoTracking()
                                    join g in db.Groups.AsNoTracking() on gr.GroupId equals g.Id
                                    where gr.RoleId == roleId && g.ProjectId == projectId
                                    select g.Name)
                                   .OrderBy(n => n)
                                   .ToListAsync(HttpContext.RequestAborted);

            // Available lookups
            AvailableUsers = await (from up in db.UserProjects.AsNoTracking()
                                    join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                                    where up.ProjectId == projectId && u.IsActive
                                    orderby u.UserName
                                    select u)
                                   .Take(50)
                                   .ToListAsync(HttpContext.RequestAborted);

            AvailableGroups = await db.Groups.AsNoTracking()
                .Where(g => g.ProjectId == projectId)
                .OrderBy(g => g.Name)
                .ToListAsync(HttpContext.RequestAborted);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddPermissionAsync(string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var key = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            TempData["Error"] = "Please select or enter a permission key.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.AddRolePermissionAsync(Name, key, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to add permission to role.";
        else TempData["Success"] = $"Permission '{key}' added to role '{Name}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemovePermissionAsync(string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RemoveRolePermissionAsync(Name, permissionKey, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove permission from role.";
        else TempData["Success"] = $"Permission '{permissionKey}' removed from role '{Name}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostAssignUserAsync(string userIdentifier)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var clean = userIdentifier.Trim();
        AppUser? user = null;
        if (Guid.TryParse(clean, out var uid))
            user = await userManager.FindByIdAsync(uid.ToString());
        if (user is null && clean.Contains('@'))
            user = await userManager.FindByEmailAsync(clean);
        if (user is null)
            user = await userManager.FindByNameAsync(clean);

        if (user is null)
        {
            TempData["Error"] = $"User '{userIdentifier}' not found.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.AssignRoleAsync(user, Name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to assign role to user.";
        else TempData["Success"] = $"Assigned role '{Name}' to user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemoveUserAsync(Guid userId)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RemoveRoleAsync(user, Name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove role from user.";
        else TempData["Success"] = $"Removed role '{Name}' from user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostAssignGroupAsync(string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddRoleAsync(groupName, Name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to assign role to group.";
        else TempData["Success"] = $"Assigned role '{Name}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemoveGroupAsync(string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveRoleAsync(groupName, Name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove role from group.";
        else TempData["Success"] = $"Removed role '{Name}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.DeleteRoleAsync(Name, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete role.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        TempData["Success"] = $"Role '{Name}' deleted.";
        return Redirect($"/admin/p/{ProjectKey}/roles");
    }
}
