using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Permissions;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<PermissionDto> Permissions { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
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

        TotalCount = all.Count;
        Permissions = all.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
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
