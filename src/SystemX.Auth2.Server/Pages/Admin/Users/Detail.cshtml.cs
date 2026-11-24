using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Users;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public AdminUserView? UserView { get; private set; }
    public IReadOnlyList<SessionDto> Sessions { get; private set; } = [];

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
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var userSvc = Svc<IAdminUserService>();
        var (result, updated) = await userSvc.UpdateAsync(Id, new AdminUpdateUserRequest(
            Input.FirstName, Input.LastName, Input.DisplayName, Input.PreferredCulture, Input.IsActive
        ), null, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to update user profile.";
        else TempData["Success"] = "User profile updated.";

        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostLockAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.LockAsync(Id, null, HttpContext.RequestAborted);
        TempData["Success"] = "User locked.";
        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostUnlockAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.UnlockAsync(Id, HttpContext.RequestAborted);
        TempData["Success"] = "User unlocked.";
        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostReset2FaAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.ResetTwoFactorAsync(Id, HttpContext.RequestAborted);
        TempData["Success"] = "Two-factor authentication reset.";
        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostRevokeSessionAsync(string sessionId)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        await userSvc.RevokeSessionsAsync(Id, sessionId, HttpContext.RequestAborted);
        TempData["Success"] = "Session revoked.";
        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostRemoveFromProjectAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var userSvc = Svc<IAdminUserService>();
        var result = await userSvc.RemoveAsync(Id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not remove user from project.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }
        TempData["Success"] = "User removed from project.";
        return Redirect($"/admin/p/{ProjectKey}/users");
    }
}
