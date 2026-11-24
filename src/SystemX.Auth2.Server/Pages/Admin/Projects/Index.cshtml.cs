using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Projects;

public sealed class IndexModel(IAdminProjectService projectService) : PageModel
{
    public PagedResult<ProjectDto> Projects { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public bool Desc { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        Projects = await projectService.ListAsync(new PageQuery(PageNumber, PageSize, Q, Sort, Desc), HttpContext.RequestAborted);
        return Page();
    }
}
