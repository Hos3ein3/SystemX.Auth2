using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Admin.Users;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public AdminUserView? UserView { get; private set; }
    public IReadOnlyList<SessionDto> Sessions { get; private set; } = [];

    public EffectiveAccess? Access { get; private set; }
    public IReadOnlyList<RoleDto> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<PermissionDto> AvailablePermissions { get; private set; } = [];
    public IReadOnlyList<GroupDto> AvailableGroups { get; private set; } = [];

    [BindProperty] public UpdateProfileInput Input { get; set; } = new();

    public sealed class UpdateProfileInput
    {
        [StringLength(100)] public string? FirstName { get; set; }
        [StringLength(100)] public string? LastName { get; set; }
        [StringLength(200)] public string? DisplayName { get; set; }
        [StringLength(10)] public string? PreferredCulture { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userSvc = Svc<IAdminUserService>();
        UserView = await userSvc.GetAsync(Id, HttpContext.RequestAborted);
        if (UserView is null) return NotFound();

        Input = new UpdateProfileInput
        {
            FirstName = UserView.User.FirstName,
            LastName = UserView.User.LastName,
            DisplayName = UserView.User.DisplayName,
            IsActive = UserView.User.IsActive
        };

        var (_, sessions) = await userSvc.ListSessionsAsync(Id, HttpContext.RequestAborted);
        Sessions = sessions;

        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(Id.ToString());
        if (appUser is not null)
        {
            var accessSvc = Svc<IAccessService>();
            var groupSvc = Svc<IGroupService>();
            Access = await accessSvc.GetEffectiveAccessAsync(appUser, HttpContext.RequestAborted);
            AvailableRoles = await accessSvc.ListRolesAsync(HttpContext.RequestAborted);
            AvailablePermissions = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);
            AvailableGroups = await groupSvc.ListAsync(HttpContext.RequestAborted);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(Guid id)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();

        var userSvc = Svc<IAdminUserService>();
        var (result, updated) = await userSvc.UpdateAsync(id, new AdminUpdateUserRequest(
            Input.FirstName, Input.LastName, Input.DisplayName, Input.PreferredCulture, Input.IsActive
        ), null, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to update user profile.";
        else TempData["Success"] = "User profile updated.";

        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostLockAsync(Guid id, DateTimeOffset? untilUtc)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.LockAsync(id, untilUtc, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to lock user.";
        else TempData["Success"] = untilUtc.HasValue ? $"User locked until {untilUtc.Value:g} UTC." : "User locked indefinitely.";
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostUnlockAsync(Guid id)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.UnlockAsync(id, HttpContext.RequestAborted);
        TempData["Success"] = "User unlocked.";
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostReset2FaAsync(Guid id)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.ResetTwoFactorAsync(id, HttpContext.RequestAborted);
        TempData["Success"] = "Two-factor authentication reset.";
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostRevokeSessionAsync(Guid id, string sessionId)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.RevokeSessionsAsync(id, sessionId, HttpContext.RequestAborted);
        TempData["Success"] = "Session revoked.";
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostAssignRoleAsync(Guid id, string roleName)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(id.ToString());
        if (appUser is not null)
        {
            var accessSvc = Svc<IAccessService>();
            var r = await accessSvc.AssignRoleAsync(appUser, roleName, HttpContext.RequestAborted);
            if (!r.Succeeded) TempData["Error"] = r.ErrorCode ?? "Failed to assign role.";
            else TempData["Success"] = $"Role '{roleName}' assigned.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostRemoveRoleAsync(Guid id, string roleName)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(id.ToString());
        if (appUser is not null)
        {
            var accessSvc = Svc<IAccessService>();
            var r = await accessSvc.RemoveRoleAsync(appUser, roleName, HttpContext.RequestAborted);
            if (!r.Succeeded) TempData["Error"] = r.ErrorCode ?? "Failed to remove role.";
            else TempData["Success"] = $"Role '{roleName}' removed.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostGrantPermissionAsync(Guid id, string? permissionKey, string? permissionKeyManual)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();

        var key = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            TempData["Error"] = "Please select or enter a permission key.";
            return RedirectToPage(new { projectKey = ProjectKey, id });
        }

        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(id.ToString());
        if (appUser is not null)
        {
            var accessSvc = Svc<IAccessService>();
            var r = await accessSvc.GrantPermissionAsync(appUser, key, ct: HttpContext.RequestAborted);
            if (!r.Succeeded) TempData["Error"] = r.ErrorCode ?? "Failed to grant direct permission.";
            else TempData["Success"] = $"Direct permission '{key}' granted.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostRevokePermissionAsync(Guid id, string permissionKey)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(id.ToString());
        if (appUser is not null)
        {
            var accessSvc = Svc<IAccessService>();
            var r = await accessSvc.RevokePermissionAsync(appUser, permissionKey, HttpContext.RequestAborted);
            if (!r.Succeeded) TempData["Error"] = r.ErrorCode ?? "Failed to revoke direct permission.";
            else TempData["Success"] = $"Direct permission '{permissionKey}' revoked.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostAddGroupAsync(Guid id, string groupName)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(id.ToString());
        if (appUser is not null)
        {
            var groupSvc = Svc<IGroupService>();
            var r = await groupSvc.AddUserAsync(groupName, appUser, HttpContext.RequestAborted);
            if (!r.Succeeded) TempData["Error"] = r.ErrorCode ?? "Failed to add user to group.";
            else TempData["Success"] = $"User added to group '{groupName}'.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostRemoveGroupAsync(Guid id, string groupName)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userManager = Svc<UserManager<AppUser>>();
        var appUser = await userManager.FindByIdAsync(id.ToString());
        if (appUser is not null)
        {
            var groupSvc = Svc<IGroupService>();
            var r = await groupSvc.RemoveUserAsync(groupName, appUser, HttpContext.RequestAborted);
            if (!r.Succeeded) TempData["Error"] = r.ErrorCode ?? "Failed to remove user from group.";
            else TempData["Success"] = $"User removed from group '{groupName}'.";
        }
        return RedirectToPage(new { projectKey = ProjectKey, id });
    }

    public async Task<IActionResult> OnPostRemoveFromProjectAsync(Guid id)
    {
        if (id == Guid.Empty) id = Id;
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.RemoveAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not remove user from project.";
            return RedirectToPage(new { projectKey = ProjectKey, id });
        }
        TempData["Success"] = "User removed from project.";
        return Redirect($"/admin/p/{ProjectKey}/users");
    }
}
