using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Permissions;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string Key { get; set; } = "";
    public PermissionDto? Permission { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var perms = await accessSvc.ListPermissionsAsync(HttpContext.RequestAborted);
        Permission = perms.FirstOrDefault(p => string.Equals(p.Key, Key, StringComparison.OrdinalIgnoreCase));

        if (Permission is null) return NotFound();
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
}
