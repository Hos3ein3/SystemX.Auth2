using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Groups;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string Name { get; set; } = "";
    public GroupDto? Group { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var groups = await groupSvc.ListAsync(HttpContext.RequestAborted);
        Group = groups.FirstOrDefault(g => string.Equals(g.Name, Name, StringComparison.OrdinalIgnoreCase));

        if (Group is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAddRoleAsync(string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.AddRoleAsync(Name, roleName, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to assign role to group.";
        else TempData["Success"] = $"Role '{roleName}' added to group.";
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostRemoveRoleAsync(string roleName)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.RemoveRoleAsync(Name, roleName, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = "Failed to remove role from group.";
        else TempData["Success"] = $"Role '{roleName}' removed from group.";
        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.DeleteAsync(Name, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete group.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        TempData["Success"] = $"Group '{Name}' deleted.";
        return Redirect($"/admin/p/{ProjectKey}/groups");
    }
}
