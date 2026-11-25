using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Groups;

public sealed class DetailModel : AdminPageModel
{
    public sealed record GroupMemberDto(Guid Id, string UserName, string? Email, string DisplayName);

    [BindProperty(SupportsGet = true)] public string Name { get; set; } = "";
    public GroupDto? Group { get; private set; }
    public IReadOnlyList<GroupMemberDto> Members { get; private set; } = [];
    public IReadOnlyList<RoleDto> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<PermissionDto> AvailablePermissions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var groups = await groupSvc.ListAsync(HttpContext.RequestAborted);
        Group = groups.FirstOrDefault(g => string.Equals(g.Name, Name, StringComparison.OrdinalIgnoreCase));

        if (Group is null) return NotFound();

        var accessSvc = Svc<IAccessService>();
        AvailableRoles = await accessSvc.ListRolesAsync(HttpContext.RequestAborted);
        AvailablePermissions = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);

        var db = Svc<Auth2DbContext>();
        var norm = Name.ToLowerInvariant();
        var members = await (from ug in db.UserGroups.AsNoTracking()
                         join g in db.Groups.AsNoTracking() on ug.GroupId equals g.Id
                         join u in db.Users.AsNoTracking() on ug.UserId equals u.Id
                         where g.ProjectId == CurrentProject!.Id && g.Name.ToLower() == norm
                         select new { u.Id, u.UserName, u.Email, u.FirstName, u.LastName })
                         .ToListAsync(HttpContext.RequestAborted);
        Members = [.. members.Select(m => new GroupMemberDto(m.Id, m.UserName ?? m.Email ?? m.Id.ToString(), m.Email, $"{m.FirstName} {m.LastName}".Trim()))];

        return Page();
    }

    public async Task<IActionResult> OnPostAddRoleAsync(string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddRoleAsync(Name, roleName, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to assign role to group.";
        else TempData["Success"] = $"Role '{roleName}' added to group.";
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemoveRoleAsync(string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveRoleAsync(Name, roleName, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove role from group.";
        else TempData["Success"] = $"Role '{roleName}' removed from group.";
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
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

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddPermissionAsync(Name, key, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to assign permission to group.";
        else TempData["Success"] = $"Permission '{key}' added to group.";
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemovePermissionAsync(string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemovePermissionAsync(Name, permissionKey, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove permission from group.";
        else TempData["Success"] = $"Permission '{permissionKey}' removed from group.";
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostAddUserAsync(string userIdentifier)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        if (string.IsNullOrWhiteSpace(userIdentifier))
        {
            TempData["Error"] = "Please provide an email or username.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(userIdentifier.Trim()) ??
                   await userManager.FindByNameAsync(userIdentifier.Trim());

        if (user is null && Guid.TryParse(userIdentifier.Trim(), out var uid))
        {
            user = await userManager.FindByIdAsync(uid.ToString());
        }

        if (user is null)
        {
            TempData["Error"] = $"User '{userIdentifier}' not found.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddUserAsync(Name, user, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to add user to group.";
        else TempData["Success"] = $"User '{user.UserName ?? user.Email}' added to group '{Name}'.";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemoveUserAsync(Guid userId)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            var groupSvc = Svc<IGroupService>();
            var result = await groupSvc.RemoveUserAsync(Name, user, HttpContext.RequestAborted);
            if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove user from group.";
            else TempData["Success"] = $"User removed from group '{Name}'.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.DeleteAsync(Name, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete group.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        TempData["Success"] = $"Group '{Name}' deleted.";
        return Redirect($"/admin/p/{ProjectKey}/groups");
    }
}
