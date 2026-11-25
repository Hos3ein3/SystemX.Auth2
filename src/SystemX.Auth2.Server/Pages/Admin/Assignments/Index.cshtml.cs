using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Assignments;

public sealed class IndexModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "user_permissions";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    // DTO records for display
    public record UserPermView(Guid UserId, string UserName, string? DisplayName, Guid PermissionId, string PermissionKey, DateTimeOffset GrantedAtUtc);
    public record RolePermView(Guid RoleId, string RoleName, Guid PermissionId, string PermissionKey);
    public record UserGroupView(Guid UserId, string UserName, string? DisplayName, Guid GroupId, string GroupName, string? GroupDisplayName);
    public record GroupRoleView(Guid GroupId, string GroupName, string? GroupDisplayName, Guid RoleId, string RoleName);
    public record GroupPermView(Guid GroupId, string GroupName, string? GroupDisplayName, Guid PermissionId, string PermissionKey);

    public IReadOnlyList<UserPermView> UserPermissions { get; private set; } = [];
    public IReadOnlyList<RolePermView> RolePermissions { get; private set; } = [];
    public IReadOnlyList<UserGroupView> UserGroups { get; private set; } = [];
    public IReadOnlyList<GroupRoleView> GroupRoles { get; private set; } = [];
    public IReadOnlyList<GroupPermView> GroupPermissions { get; private set; } = [];

    // Lookup options for dropdowns
    public IReadOnlyList<Permission> AvailablePermissions { get; private set; } = [];
    public IReadOnlyList<AppRole> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<Group> AvailableGroups { get; private set; } = [];
    public IReadOnlyList<AppUser> AvailableUsers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var db = Svc<Auth2DbContext>();
        var projectId = CurrentProject!.Id;
        var searchUpper = Search?.Trim().ToUpperInvariant();

        var globalProj = await db.Projects.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == ProjectKeyRules.GlobalProjectKey, HttpContext.RequestAborted);
        var globalId = globalProj?.Id;

        // 1. Available lookup data for this project
        AvailablePermissions = await db.Permissions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => (CurrentProject.IsGlobalProject ? (p.ProjectId == projectId || (globalId != null && p.ProjectId == globalId.Value)) : p.ProjectId == projectId) && !p.IsObsolete)
            .OrderBy(p => p.Key)
            .ToListAsync(HttpContext.RequestAborted);

        AvailableRoles = await db.Roles.AsNoTracking()
            .Where(r => CurrentProject.IsGlobalProject ? (r.ProjectId == projectId || r.ProjectId == null) : r.ProjectId == projectId)
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
                               .Take(100)
                               .ToListAsync(HttpContext.RequestAborted);

        // 2. Load tab-specific or all assignment lists
        // Each list is selected into an anonymous type (EF cannot sort or filter on a property of a record built in the query),
        // filtered and sorted in SQL, then turned into the view records. An id stands in for a missing name.
        // User Permissions
        var upQuery = from up in db.UserPermissions.AsNoTracking()
                      join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                      join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on up.PermissionId equals p.Id
                      where up.ProjectId == projectId && (CurrentProject.IsGlobalProject || p.ProjectId == projectId)
                      select new { up.UserId, Name = u.UserName ?? u.Email ?? "", u.DisplayName, up.PermissionId, p.Key, up.GrantedAtUtc };

        if (!string.IsNullOrEmpty(searchUpper))
            upQuery = upQuery.Where(x => x.Name.ToUpper().Contains(searchUpper) || x.Key.ToUpper().Contains(searchUpper));

        UserPermissions = [.. (await upQuery.OrderBy(x => x.Name).ThenBy(x => x.Key).ToListAsync(HttpContext.RequestAborted))
            .Select(x => new UserPermView(x.UserId, x.Name.Length > 0 ? x.Name : x.UserId.ToString(), x.DisplayName, x.PermissionId, x.Key, x.GrantedAtUtc))];

        // Role Permissions
        var rpQuery = from rp in db.RolePermissions.AsNoTracking()
                      join r in db.Roles.AsNoTracking() on rp.RoleId equals r.Id
                      join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on rp.PermissionId equals p.Id
                      where CurrentProject.IsGlobalProject ? (r.ProjectId == projectId || r.ProjectId == null) : r.ProjectId == projectId
                      select new { rp.RoleId, Name = r.Name ?? "", rp.PermissionId, p.Key };

        if (!string.IsNullOrEmpty(searchUpper))
            rpQuery = rpQuery.Where(x => x.Name.ToUpper().Contains(searchUpper) || x.Key.ToUpper().Contains(searchUpper));

        RolePermissions = [.. (await rpQuery.OrderBy(x => x.Name).ThenBy(x => x.Key).ToListAsync(HttpContext.RequestAborted))
            .Select(x => new RolePermView(x.RoleId, x.Name.Length > 0 ? x.Name : x.RoleId.ToString(), x.PermissionId, x.Key))];

        // User Groups
        var ugQuery = from ug in db.UserGroups.AsNoTracking()
                      join u in db.Users.AsNoTracking() on ug.UserId equals u.Id
                      join g in db.Groups.AsNoTracking() on ug.GroupId equals g.Id
                      where g.ProjectId == projectId && (CurrentProject.IsGlobalProject || db.UserProjects.Any(x => x.UserId == u.Id && x.ProjectId == projectId))
                      select new { ug.UserId, Name = u.UserName ?? u.Email ?? "", u.DisplayName, ug.GroupId, GroupName = g.Name, GroupDisplayName = g.DisplayName };

        if (!string.IsNullOrEmpty(searchUpper))
            ugQuery = ugQuery.Where(x => x.Name.ToUpper().Contains(searchUpper) || x.GroupName.ToUpper().Contains(searchUpper));

        UserGroups = [.. (await ugQuery.OrderBy(x => x.Name).ThenBy(x => x.GroupName).ToListAsync(HttpContext.RequestAborted))
            .Select(x => new UserGroupView(x.UserId, x.Name.Length > 0 ? x.Name : x.UserId.ToString(), x.DisplayName, x.GroupId, x.GroupName, x.GroupDisplayName))];

        // Group Roles
        var grQuery = from gr in db.GroupRoles.AsNoTracking()
                      join g in db.Groups.AsNoTracking() on gr.GroupId equals g.Id
                      join r in db.Roles.AsNoTracking() on gr.RoleId equals r.Id
                      where g.ProjectId == projectId && (CurrentProject.IsGlobalProject ? (r.ProjectId == projectId || r.ProjectId == null) : r.ProjectId == projectId)
                      select new { gr.GroupId, GroupName = g.Name, GroupDisplayName = g.DisplayName, gr.RoleId, RoleName = r.Name ?? "" };

        if (!string.IsNullOrEmpty(searchUpper))
            grQuery = grQuery.Where(x => x.GroupName.ToUpper().Contains(searchUpper) || x.RoleName.ToUpper().Contains(searchUpper));

        GroupRoles = [.. (await grQuery.OrderBy(x => x.GroupName).ThenBy(x => x.RoleName).ToListAsync(HttpContext.RequestAborted))
            .Select(x => new GroupRoleView(x.GroupId, x.GroupName, x.GroupDisplayName, x.RoleId, x.RoleName.Length > 0 ? x.RoleName : x.RoleId.ToString()))];

        // Group Permissions
        var gpQuery = from gp in db.GroupPermissions.AsNoTracking()
                      join g in db.Groups.AsNoTracking() on gp.GroupId equals g.Id
                      join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on gp.PermissionId equals p.Id
                      where g.ProjectId == projectId && (CurrentProject.IsGlobalProject || p.ProjectId == projectId)
                      select new { gp.GroupId, GroupName = g.Name, GroupDisplayName = g.DisplayName, gp.PermissionId, p.Key };

        if (!string.IsNullOrEmpty(searchUpper))
            gpQuery = gpQuery.Where(x => x.GroupName.ToUpper().Contains(searchUpper) || x.Key.ToUpper().Contains(searchUpper));

        GroupPermissions = [.. (await gpQuery.OrderBy(x => x.GroupName).ThenBy(x => x.Key).ToListAsync(HttpContext.RequestAborted))
            .Select(x => new GroupPermView(x.GroupId, x.GroupName, x.GroupDisplayName, x.PermissionId, x.Key))];

        return Page();
    }

    // 1. User Permission Handlers
    public async Task<IActionResult> OnPostGrantUserPermissionAsync(string? userIdentifier, string? userIdentifierManual, string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedUser = !string.IsNullOrWhiteSpace(userIdentifierManual) ? userIdentifierManual.Trim() : userIdentifier?.Trim();
        var selectedPerm = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();

        if (string.IsNullOrWhiteSpace(selectedUser) || string.IsNullOrWhiteSpace(selectedPerm))
        {
            TempData["Error"] = "Both User and Permission are required.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "user_permissions" });
        }

        var user = await FindUserAsync(selectedUser);
        if (user is null)
        {
            TempData["Error"] = $"User '{selectedUser}' not found.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "user_permissions" });
        }

        var db = Svc<Auth2DbContext>();
        if (!CurrentProject!.IsGlobalProject && !await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == CurrentProject.Id, HttpContext.RequestAborted))
        {
            db.UserProjects.Add(new UserProject { UserId = user.Id, ProjectId = CurrentProject.Id, JoinedAtUtc = DateTimeOffset.UtcNow, Status = UserProjectStatus.Active, LockoutEnabled = true });
            await db.SaveChangesAsync(HttpContext.RequestAborted);
        }

        var accessSvc = Svc<IAccessService>();
        var actorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var uid) ? uid : (Guid?)null;
        var result = await accessSvc.GrantPermissionAsync(user, selectedPerm, actorUserId, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to grant user permission.";
        else
            TempData["Success"] = $"Granted permission '{selectedPerm}' directly to user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "user_permissions" });
    }

    public async Task<IActionResult> OnPostRevokeUserPermissionAsync(Guid userId, string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "user_permissions" });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RevokePermissionAsync(user, permissionKey, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to revoke user permission.";
        else
            TempData["Success"] = $"Revoked permission '{permissionKey}' from user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "user_permissions" });
    }

    // 2. Role Permission Handlers
    public async Task<IActionResult> OnPostAddRolePermissionAsync(string? roleName, string? roleNameManual, string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedRole = !string.IsNullOrWhiteSpace(roleNameManual) ? roleNameManual.Trim() : roleName?.Trim();
        var selectedPerm = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();

        if (string.IsNullOrWhiteSpace(selectedRole) || string.IsNullOrWhiteSpace(selectedPerm))
        {
            TempData["Error"] = "Both Role and Permission are required.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "role_permissions" });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.AddRolePermissionAsync(selectedRole, selectedPerm, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to add role permission.";
        else
            TempData["Success"] = $"Assigned permission '{selectedPerm}' to role '{selectedRole}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "role_permissions" });
    }

    public async Task<IActionResult> OnPostRemoveRolePermissionAsync(string roleName, string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RemoveRolePermissionAsync(roleName, permissionKey, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove role permission.";
        else
            TempData["Success"] = $"Removed permission '{permissionKey}' from role '{roleName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "role_permissions" });
    }

    // 3. User Group Handlers
    public async Task<IActionResult> OnPostAddUserGroupAsync(string? userIdentifier, string? userIdentifierManual, string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedUser = !string.IsNullOrWhiteSpace(userIdentifierManual) ? userIdentifierManual.Trim() : userIdentifier?.Trim();

        if (string.IsNullOrWhiteSpace(selectedUser) || string.IsNullOrWhiteSpace(groupName))
        {
            TempData["Error"] = "Both User and Group are required.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "user_groups" });
        }

        var user = await FindUserAsync(selectedUser);
        if (user is null)
        {
            TempData["Error"] = $"User '{selectedUser}' not found.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "user_groups" });
        }

        var db = Svc<Auth2DbContext>();
        if (!CurrentProject!.IsGlobalProject && !await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == CurrentProject.Id, HttpContext.RequestAborted))
        {
            db.UserProjects.Add(new UserProject { UserId = user.Id, ProjectId = CurrentProject.Id, JoinedAtUtc = DateTimeOffset.UtcNow, Status = UserProjectStatus.Active, LockoutEnabled = true });
            await db.SaveChangesAsync(HttpContext.RequestAborted);
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddUserAsync(groupName, user, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to add user to group.";
        else
            TempData["Success"] = $"Added user '{user.UserName}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "user_groups" });
    }

    public async Task<IActionResult> OnPostRemoveUserGroupAsync(Guid userId, string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "user_groups" });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveUserAsync(groupName, user, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove user from group.";
        else
            TempData["Success"] = $"Removed user '{user.UserName}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "user_groups" });
    }

    // 4. Group Role Handlers
    public async Task<IActionResult> OnPostAddGroupRoleAsync(string groupName, string? roleName, string? roleNameManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedRole = !string.IsNullOrWhiteSpace(roleNameManual) ? roleNameManual.Trim() : roleName?.Trim();

        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(selectedRole))
        {
            TempData["Error"] = "Both Group and Role are required.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "group_roles" });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddRoleAsync(groupName, selectedRole, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign role to group.";
        else
            TempData["Success"] = $"Assigned role '{selectedRole}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "group_roles" });
    }

    public async Task<IActionResult> OnPostRemoveGroupRoleAsync(string groupName, string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveRoleAsync(groupName, roleName, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove role from group.";
        else
            TempData["Success"] = $"Removed role '{roleName}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "group_roles" });
    }

    // 5. Group Permission Handlers
    public async Task<IActionResult> OnPostAddGroupPermissionAsync(string groupName, string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedPerm = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();

        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(selectedPerm))
        {
            TempData["Error"] = "Both Group and Permission are required.";
            return RedirectToPage(new { projectKey = ProjectKey, tab = "group_permissions" });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddPermissionAsync(groupName, selectedPerm, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to assign permission to group.";
        else
            TempData["Success"] = $"Assigned permission '{selectedPerm}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "group_permissions" });
    }

    public async Task<IActionResult> OnPostRemoveGroupPermissionAsync(string groupName, string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemovePermissionAsync(groupName, permissionKey, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove permission from group.";
        else
            TempData["Success"] = $"Removed permission '{permissionKey}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey, tab = "group_permissions" });
    }

    private async Task<AppUser?> FindUserAsync(string identifier)
    {
        var clean = identifier.Trim();
        var userManager = Svc<UserManager<AppUser>>();
        var db = Svc<Auth2DbContext>();

        AppUser? user = null;
        if (Guid.TryParse(clean, out var id))
            user = await userManager.FindByIdAsync(id.ToString());

        if (user is null && clean.Contains('@'))
            user = await userManager.FindByEmailAsync(clean);

        if (user is null)
            user = await userManager.FindByNameAsync(clean);

        if (user is not null)
        {
            var isMember = await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == CurrentProject!.Id);
            if (!isMember && !IsGlobalAdmin) return null;
        }

        return user;
    }
}
