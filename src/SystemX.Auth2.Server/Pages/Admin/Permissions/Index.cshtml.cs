using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Permissions;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<PermissionDto> Permissions { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var all = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToLowerInvariant();
            all = all.Where(p => p.Key.ToLowerInvariant().Contains(filter) || (p.DisplayName?.ToLowerInvariant().Contains(filter) == true)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            all = all.Where(p => string.Equals(p.Group, Category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Permissions = all;
        return Page();
    }

    public async Task<IActionResult> OnPostToggleObsoleteAsync(string key, bool obsolete)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var accessSvc = Svc<IAccessService>();
        await accessSvc.SetPermissionObsoleteAsync(key, !obsolete, HttpContext.RequestAborted);
        TempData["Success"] = "Permission status updated.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string key)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.DeletePermissionAsync(key, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete permission.";
        else TempData["Success"] = $"Permission '{key}' deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
