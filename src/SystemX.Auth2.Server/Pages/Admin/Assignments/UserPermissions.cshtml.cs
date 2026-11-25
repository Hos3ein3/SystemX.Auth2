using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Assignments;

public sealed class UserPermissionsModel : AdminPageModel
{
    public record UserPermItem(Guid UserId, string UserName, string? DisplayName, Guid PermissionId, string PermissionKey, DateTimeOffset GrantedAtUtc);

    public IReadOnlyList<UserPermItem> Items { get; private set; } = [];
    public IReadOnlyList<AppUser> AvailableUsers { get; private set; } = [];
    public IReadOnlyList<Permission> AvailablePermissions { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? UserFilter { get; set; }
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

        // Available active users in project
        AvailableUsers = await (from up in db.UserProjects.AsNoTracking()
                                join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                                where up.ProjectId == projectId && u.IsActive
                                orderby u.UserName
                                select u)
                               .Take(100)
                               .ToListAsync(HttpContext.RequestAborted);

        var globalProj = await db.Projects.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == ProjectKeyRules.GlobalProjectKey, HttpContext.RequestAborted);
        var globalId = globalProj?.Id;

        // Available permissions (project specific + global if global project)
        AvailablePermissions = await db.Permissions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => (CurrentProject.IsGlobalProject ? (p.ProjectId == projectId || (globalId != null && p.ProjectId == globalId.Value)) : p.ProjectId == projectId) && !p.IsObsolete)
            .OrderBy(p => p.Key)
            .ToListAsync(HttpContext.RequestAborted);

        var query = from up in db.UserPermissions.AsNoTracking()
                    join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                    join p in db.Permissions.IgnoreQueryFilters().AsNoTracking() on up.PermissionId equals p.Id
                    where up.ProjectId == projectId
                    select new { up.UserId, UserName = u.UserName ?? u.Email ?? "", u.DisplayName, up.PermissionId, PermissionKey = p.Key, up.GrantedAtUtc };

        if (!string.IsNullOrWhiteSpace(UserFilter))
        {
            var uFilter = UserFilter.Trim().ToUpperInvariant();
            query = query.Where(x => x.UserName.ToUpper().Contains(uFilter) || (x.DisplayName != null && x.DisplayName.ToUpper().Contains(uFilter)));
        }

        if (!string.IsNullOrWhiteSpace(PermissionKey))
        {
            var pFilter = PermissionKey.Trim();
            query = query.Where(x => x.PermissionKey == pFilter);
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToUpperInvariant();
            query = query.Where(x => x.UserName.ToUpper().Contains(filter) || x.PermissionKey.ToUpper().Contains(filter));
        }

        TotalCount = await query.CountAsync(HttpContext.RequestAborted);
        var rows = await query.OrderBy(x => x.UserName).ThenBy(x => x.PermissionKey)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync(HttpContext.RequestAborted);
        Items = [.. rows.Select(x => new UserPermItem(x.UserId, x.UserName.Length > 0 ? x.UserName : x.UserId.ToString(), x.DisplayName, x.PermissionId, x.PermissionKey, x.GrantedAtUtc))];
        return Page();
    }

    public async Task<IActionResult> OnPostGrantAsync(string? userIdentifier, string? userIdentifierManual, string? permissionKey, string? permissionKeyManual)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedUser = !string.IsNullOrWhiteSpace(userIdentifierManual) ? userIdentifierManual.Trim() : userIdentifier?.Trim();
        var selectedPerm = !string.IsNullOrWhiteSpace(permissionKeyManual) ? permissionKeyManual.Trim() : permissionKey?.Trim();

        if (string.IsNullOrWhiteSpace(selectedUser) || string.IsNullOrWhiteSpace(selectedPerm))
        {
            TempData["Error"] = "Both User and Permission are required.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var userManager = Svc<UserManager<AppUser>>();
        AppUser? user = null;
        if (Guid.TryParse(selectedUser, out var uid))
            user = await userManager.FindByIdAsync(uid.ToString());
        if (user is null && selectedUser.Contains('@'))
            user = await userManager.FindByEmailAsync(selectedUser);
        if (user is null)
            user = await userManager.FindByNameAsync(selectedUser);

        if (user is null)
        {
            TempData["Error"] = $"User '{selectedUser}' not found.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var db = Svc<Auth2DbContext>();
        if (!CurrentProject!.IsGlobalProject && !await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == CurrentProject.Id, HttpContext.RequestAborted))
        {
            db.UserProjects.Add(new UserProject { UserId = user.Id, ProjectId = CurrentProject.Id, JoinedAtUtc = DateTimeOffset.UtcNow, Status = UserProjectStatus.Active, LockoutEnabled = true });
            await db.SaveChangesAsync(HttpContext.RequestAborted);
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.GrantPermissionAsync(user, selectedPerm, ct: HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to grant direct permission.";
        else
            TempData["Success"] = $"Granted direct permission '{selectedPerm}' to user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid userId, string permissionKey)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.RevokePermissionAsync(user, permissionKey, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to revoke direct permission.";
        else
            TempData["Success"] = $"Revoked direct permission '{permissionKey}' from user '{user.UserName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
