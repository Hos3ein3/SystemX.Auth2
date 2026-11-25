using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Users;

public sealed class IndexModel : AdminPageModel
{
    public PagedResult<UserDto> Users { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public bool Desc { get; set; }
    [BindProperty(SupportsGet = true)] public bool? LockedOut { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userSvc = Svc<IAdminUserService>();
        Users = await userSvc.ListAsync(new PageQuery(PageNumber, PageSize, Q, Sort, Desc), LockedOut, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostLockAsync(Guid id, DateTimeOffset? untilUtc)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.LockAsync(id, untilUtc, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to lock user.";
        else TempData["Success"] = untilUtc.HasValue ? $"User locked until {untilUtc.Value:g} UTC." : "User locked indefinitely.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostUnlockAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.UnlockAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to unlock user.";
        else TempData["Success"] = "User unlocked successfully.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostReset2FaAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.ResetTwoFactorAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to reset 2FA.";
        else TempData["Success"] = "Two-factor authentication reset.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRevokeSessionsAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.RevokeSessionsAsync(id, null, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to revoke sessions.";
        else TempData["Success"] = "All sessions revoked for user.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.RemoveAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to remove user.";
        else TempData["Success"] = "User removed from project.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostBulkActionAsync(string bulkAction, List<Guid> selectedIds)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        if (selectedIds is null || selectedIds.Count == 0)
        {
            TempData["Error"] = "No users selected.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var userSvc = Svc<IAdminUserService>();
        var response = await userSvc.BulkActionAsync(new AdminBulkUserRequest(selectedIds, bulkAction), HttpContext.RequestAborted);
        TempData["Success"] = $"Bulk action '{bulkAction}' completed. Succeeded: {response.Succeeded}, Failed: {response.Failed}";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
