using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Permissions;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string Key { get; set; } = "";
    public PermissionDto? Permission { get; private set; }

    // Direct and indirect assignments
    public record AssignedUser(Guid Id, string UserName, string? DisplayName, DateTimeOffset GrantedAtUtc);
    public IReadOnlyList<string> AssignedRoles { get; private set; } = [];
    public IReadOnlyList<string> AssignedGroups { get; private set; } = [];
    public IReadOnlyList<AssignedUser> AssignedUsers { get; private set; } = [];

    // Available lookups
    public IReadOnlyList<AppRole> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<Group> AvailableGroups { get; private set; } = [];
    public IReadOnlyList<AppUser> AvailableUsers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var perms = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);
        Permission = perms.FirstOrDefault(p => string.Equals(p.Key, Key, StringComparison.OrdinalIgnoreCase));

        if (Permission is null) return NotFound();

        var db = Svc<Auth2DbContext>();
        var projectId = CurrentProject!.Id;

        // Roles with this permission (RolePermission)
        AssignedRoles = await (from rp in db.RolePermissions.AsNoTracking()
                               join r in db.Roles.AsNoTracking() on rp.RoleId equals r.Id
                               join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                               where p.ProjectId == projectId && p.Key == Permission.Key
                               select r.Name!)
                              .OrderBy(n => n)
                              .ToListAsync(HttpContext.RequestAborted);

        // Groups with this permission (GroupPermission)
        AssignedGroups = await (from gp in db.GroupPermissions.AsNoTracking()
                                join g in db.Groups.AsNoTracking() on gp.GroupId equals g.Id
                                join p in db.Permissions.AsNoTracking() on gp.PermissionId equals p.Id
                                where p.ProjectId == projectId && p.Key == Permission.Key
                                select g.Name)
                               .OrderBy(n => n)
                               .ToListAsync(HttpContext.RequestAborted);

        // Users directly granted this permission (UserPermission)
        // Plain columns go into SQL; the display name is made in memory (a Guid.ToString() cannot be translated).
        var granted = await (from up in db.UserPermissions.AsNoTracking()
                               join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                               join p in db.Permissions.AsNoTracking() on up.PermissionId equals p.Id
                               where up.ProjectId == projectId && p.Key == Permission.Key
                               select new { u.Id, u.UserName, u.Email, u.DisplayName, up.GrantedAtUtc })
                              .ToListAsync(HttpContext.RequestAborted);
        AssignedUsers = [.. granted.Select(g => new AssignedUser(g.Id, g.UserName ?? g.Email ?? g.Id.ToString(), g.DisplayName, g.GrantedAtUtc))
                                   .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)];

        // Lookups
        AvailableRoles = await db.Roles.AsNoTracking()
            .Where(r => r.ProjectId == projectId || (r.ProjectId == null && CurrentProject.IsGlobalProject))
            .OrderBy(r => r.Name)
            .ToListAsync(HttpContext.RequestAborted);

        AvailableGroups = await db.Groups.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Name)
            .ToListAsync(HttpContext.RequestAborted);

        AvailableUsers = await (from up in db.UserProjects.AsNoTracking()
                                join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                                where up.ProjectId == projectId && u.IsActive
                                orderby u.UserName
                                select u)
                               .Take(50)
                               .ToListAsync(HttpContext.RequestAborted);

        return Page();
    }

    public async Task<IActionResult> OnPostToggleObsoleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var perms = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);
        var p = perms.FirstOrDefault(x => string.Equals(x.Key, Key, StringComparison.OrdinalIgnoreCase));
        if (p is null) return NotFound();

        await accessSvc.SetPermissionObsoleteAsync(Key, !p.IsObsolete, HttpContext.RequestAborted);
        TempData["Success"] = "Permission status updated.";
        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.DeletePermissionAsync(Key, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete permission.";
            return RedirectToPage(new { projectKey = ProjectKey, key = Key });
        }

        TempData["Success"] = $"Permission '{Key}' deleted.";
        return Redirect($"/admin/p/{ProjectKey}/permissions");
    }

    // Role Permission Handlers
    public async Task<IActionResult> OnPostAssignRoleAsync(string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.AddRolePermissionAsync(roleName, Key, HttpContext.RequestAborted);
        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign to role.";
        else
            TempData["Success"] = $"Assigned permission '{Key}' to role '{roleName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }

    public async Task<IActionResult> OnPostRemoveRoleAsync(string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RemoveRolePermissionAsync(roleName, Key, HttpContext.RequestAborted);
        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove from role.";
        else
            TempData["Success"] = $"Removed permission '{Key}' from role '{roleName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }

    // Group Permission Handlers
    public async Task<IActionResult> OnPostAssignGroupAsync(string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddPermissionAsync(groupName, Key, HttpContext.RequestAborted);
        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign to group.";
        else
            TempData["Success"] = $"Assigned permission '{Key}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }

    public async Task<IActionResult> OnPostRemoveGroupAsync(string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemovePermissionAsync(groupName, Key, HttpContext.RequestAborted);
        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove from group.";
        else
            TempData["Success"] = $"Removed permission '{Key}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }

    // User Permission Handlers
    public async Task<IActionResult> OnPostGrantUserAsync(string userIdentifier)
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
            return RedirectToPage(new { projectKey = ProjectKey, key = Key });
        }

        var accessSvc = Svc<IAccessService>();
        var actorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var aId) ? aId : (Guid?)null;
        var result = await accessSvc.GrantPermissionAsync(user, Key, actorUserId, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to grant user permission.";
        else
            TempData["Success"] = $"Granted permission '{Key}' directly to user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }

    public async Task<IActionResult> OnPostRevokeUserAsync(Guid userId)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage(new { projectKey = ProjectKey, key = Key });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RevokePermissionAsync(user, Key, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to revoke user permission.";
        else
            TempData["Success"] = $"Revoked permission '{Key}' from user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, key = Key });
    }
}
