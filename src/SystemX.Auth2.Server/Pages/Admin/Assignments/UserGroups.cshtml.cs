using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Assignments;

public sealed class UserGroupsModel : AdminPageModel
{
    public record UserGroupItem(Guid UserId, string UserName, string? DisplayName, Guid GroupId, string GroupName, string? GroupDisplayName);

    public IReadOnlyList<UserGroupItem> Items { get; private set; } = [];
    public IReadOnlyList<AppUser> AvailableUsers { get; private set; } = [];
    public IReadOnlyList<Group> AvailableGroups { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? UserFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? GroupName { get; set; }
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

        // Available groups in project
        AvailableGroups = await db.Groups.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Name)
            .ToListAsync(HttpContext.RequestAborted);

        var query = from ug in db.UserGroups.AsNoTracking()
                    join u in db.Users.AsNoTracking() on ug.UserId equals u.Id
                    join g in db.Groups.AsNoTracking() on ug.GroupId equals g.Id
                    where g.ProjectId == projectId
                    select new { ug.UserId, UserName = u.UserName ?? u.Email ?? "", u.DisplayName, ug.GroupId, GroupName = g.Name, GroupDisplayName = g.DisplayName };

        if (!string.IsNullOrWhiteSpace(UserFilter))
        {
            var uFilter = UserFilter.Trim().ToUpperInvariant();
            query = query.Where(x => x.UserName.ToUpper().Contains(uFilter) || (x.DisplayName != null && x.DisplayName.ToUpper().Contains(uFilter)));
        }

        if (!string.IsNullOrWhiteSpace(GroupName))
        {
            var gFilter = GroupName.Trim();
            query = query.Where(x => x.GroupName == gFilter);
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToUpperInvariant();
            query = query.Where(x => x.UserName.ToUpper().Contains(filter) || x.GroupName.ToUpper().Contains(filter));
        }

        TotalCount = await query.CountAsync(HttpContext.RequestAborted);
        var rows = await query.OrderBy(x => x.UserName).ThenBy(x => x.GroupName)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync(HttpContext.RequestAborted);
        Items = [.. rows.Select(x => new UserGroupItem(x.UserId, x.UserName.Length > 0 ? x.UserName : x.UserId.ToString(), x.DisplayName, x.GroupId, x.GroupName, x.GroupDisplayName))];
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string? userIdentifier, string? userIdentifierManual, string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var selectedUser = !string.IsNullOrWhiteSpace(userIdentifierManual) ? userIdentifierManual.Trim() : userIdentifier?.Trim();

        if (string.IsNullOrWhiteSpace(selectedUser) || string.IsNullOrWhiteSpace(groupName))
        {
            TempData["Error"] = "Both User and Group are required.";
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

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddUserAsync(groupName.Trim(), user, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to add user to group.";
        else
            TempData["Success"] = $"Added user '{user.UserName}' to group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid userId, string groupName)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveUserAsync(groupName, user, HttpContext.RequestAborted);

        if (!result.Succeeded)
            TempData["Error"] = result.ErrorCode ?? "Failed to remove user from group.";
        else
            TempData["Success"] = $"Removed user '{user.UserName}' from group '{groupName}'.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
