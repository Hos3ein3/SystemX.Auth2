using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Roles;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<RoleDto> Roles { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var accessSvc = Svc<IAccessService>();
        var allRoles = await accessSvc.ListRolesAsync(HttpContext.RequestAborted);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToLowerInvariant();
            Roles = allRoles.Where(r => r.Name.ToLowerInvariant().Contains(filter) || (r.Description?.ToLowerInvariant().Contains(filter) == true)).ToList();
        }
        else
        {
            Roles = allRoles;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.DeleteRoleAsync(name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete role.";
        else TempData["Success"] = $"Role '{name}' deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
