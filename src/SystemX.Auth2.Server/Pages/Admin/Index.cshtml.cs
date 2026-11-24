using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin;

public sealed class IndexModel : PageModel
{
    private readonly IAdminProjectService _projectService;

    public IndexModel(IAdminProjectService projectService)
    {
        _projectService = projectService;
    }

    public PagedResult<ProjectDto>? Projects { get; private set; }
    public bool IsGlobalAdmin => User.IsInRole("GlobalAdmin") || User.IsInRole("Developer") || User.HasClaim("scope", "auth2.admin.global");

    public async Task<IActionResult> OnGetAsync(int page = 1, string? q = null)
    {
        if (!IsGlobalAdmin)
        {
            var projectKey = User.FindFirst("project_key")?.Value;
            if (!string.IsNullOrEmpty(projectKey))
            {
                return Redirect($"/admin/p/{projectKey}");
            }
            return Forbid();
        }

        Projects = await _projectService.ListAsync(new PageQuery(page, 20, q), HttpContext.RequestAborted);
        return Page();
    }
}
