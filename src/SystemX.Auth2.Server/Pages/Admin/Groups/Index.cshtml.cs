using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Groups;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<GroupDto> Groups { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var groupSvc = Svc<IGroupService>();
        var all = await groupSvc.ListAsync(HttpContext.RequestAborted);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToLowerInvariant();
            all = all.Where(g => g.Name.ToLowerInvariant().Contains(filter) || (g.DisplayName?.ToLowerInvariant().Contains(filter) == true)).ToList();
        }

        Groups = all;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.DeleteAsync(name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete group.";
        else TempData["Success"] = $"Group '{name}' deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
