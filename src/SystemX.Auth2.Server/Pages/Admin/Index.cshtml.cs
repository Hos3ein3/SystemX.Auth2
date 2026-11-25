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
    private readonly IClientHealthService _clientHealth;

    public IndexModel(IAdminProjectService projectService, IClientHealthService clientHealth)
    {
        _projectService = projectService;
        _clientHealth = clientHealth;
    }

    public PagedResult<ProjectDto>? Projects { get; private set; }

    /// <summary>For every project of the page: its applications with a two-way health check and their last answers (empty when none has one).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<ClientHealthInfo>> ClientHealth { get; private set; } = new Dictionary<Guid, IReadOnlyList<ClientHealthInfo>>();
    public bool IsGlobalAdmin => User.IsInRole("GlobalAdmin") || User.IsInRole("Developer") || User.HasClaim("scope", "auth2.admin.global");

    public async Task<IActionResult> OnGetAsync(int page = 1, string? q = null)
    {
        if (!IsGlobalAdmin)
        {
            var projectKey = User.FindFirst(Auth2Claims.ProjectKey)?.Value;
            if (!string.IsNullOrEmpty(projectKey))
            {
                return Redirect($"/admin/p/{projectKey}");
            }
            return NotFound();
        }

        Projects = await _projectService.ListAsync(new PageQuery(page, 20, q), HttpContext.RequestAborted);
        ClientHealth = await _clientHealth.GetProjectsAsync([.. Projects.Items.Select(p => p.Id)], HttpContext.RequestAborted);
        return Page();
    }
}
