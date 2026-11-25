using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin;

public sealed class DashboardModel : AdminPageModel
{
    /// <summary>PostgreSQL and Redis, as the health checks <c>database</c> and <c>redis</c> of the server see them. Filled for global admins only (it is about the server, not about one project).</summary>
    public IReadOnlyList<InfrastructureStatus> Infrastructure { get; private set; } = [];

    public AdminDashboardMetrics? Metrics { get; private set; }

    /// <summary>The applications of this project with a two-way health check, and their last answers. Empty when none has one.</summary>
    public IReadOnlyList<ClientHealthInfo> ClientHealth { get; private set; } = [];

    /// <summary>The worst state of <see cref="ClientHealth"/>: the one badge of the header.</summary>
    public ClientHealthState? ClientHealthSummary => Application.ClientHealthSummary.Of(ClientHealth);

    /// <summary>The refresh button of the <c>Clients</c> badge: asks the two-way clients of this project now, then shows the page again. (The PostgreSQL and Redis badges are read every time the page opens, so their button only reloads it.)</summary>
    public async Task<IActionResult> OnPostRefreshClientsAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        await Svc<IClientHealthService>().CheckProjectAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var dashboardSvc = Svc<IAdminDashboardService>();
        Metrics = await dashboardSvc.GetMetricsAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        ClientHealth = await Svc<IClientHealthService>().GetProjectAsync(CurrentProject.Id, HttpContext.RequestAborted);
        if (IsGlobalAdmin) Infrastructure = await Svc<IInfrastructureStatusService>().ReadAsync(HttpContext.RequestAborted);
        return Page();
    }
}
