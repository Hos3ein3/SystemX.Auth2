using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Audit;

public sealed class IndexModel : AdminPageModel
{
    public PagedResult<AuditEventDto> Events { get; private set; } = new([], 1, 25, 0);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? ActionFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? TargetType { get; set; }
    [BindProperty(SupportsGet = true)] public DateTimeOffset? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateTimeOffset? ToDate { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var auditReader = Svc<IAuditReader>();
        bool allProjects = IsGlobalAdmin && ProjectKey == "_global";

        Events = await auditReader.QueryAsync(
            new PageQuery(PageNumber, PageSize, Q),
            ActionFilter,
            null,
            TargetType,
            FromDate,
            ToDate,
            allProjects,
            HttpContext.RequestAborted);

        return Page();
    }
}
