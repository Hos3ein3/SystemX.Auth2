using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Roles;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string Name { get; set; } = "";
    public RoleDto? Role { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var roles = await accessSvc.ListRolesAsync(HttpContext.RequestAborted);
        Role = roles.FirstOrDefault(r => string.Equals(r.Name, Name, StringComparison.OrdinalIgnoreCase));

        if (Role is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.DeleteRoleAsync(Name, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete role.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        TempData["Success"] = $"Role '{Name}' deleted.";
        return Redirect($"/admin/p/{ProjectKey}/roles");
    }
}
