using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin;

public sealed class DashboardModel : AdminPageModel
{
    public AdminDashboardMetrics? Metrics { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var dashboardSvc = Svc<IAdminDashboardService>();
        Metrics = await dashboardSvc.GetMetricsAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        return Page();
    }
}
