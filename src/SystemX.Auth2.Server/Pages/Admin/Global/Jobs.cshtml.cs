using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Global;

public sealed class JobsModel(IAdminJobService jobService) : PageModel
{
    public IReadOnlyList<JobStatusDto> JobStatuses { get; private set; } = [];
    public PagedResult<JobRunDto> JobRuns { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? JobName { get; set; }
    [BindProperty(SupportsGet = true)] public bool? Success { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        JobStatuses = await jobService.GetStatusAsync(HttpContext.RequestAborted);
        JobRuns = await jobService.ListRunsAsync(new PageQuery(PageNumber, PageSize), JobName, Success, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostRunNowAsync(string jobName)
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        var (result, run) = await jobService.RunNowAsync(jobName, HttpContext.RequestAborted);
        if (result.Succeeded) TempData["Success"] = $"Job '{jobName}' executed successfully. Rows affected: {run?.RowsAffected ?? 0}.";
        else TempData["Error"] = $"Job execution failed: {run?.Error ?? result.ErrorCode}";

        return RedirectToPage();
    }
}
