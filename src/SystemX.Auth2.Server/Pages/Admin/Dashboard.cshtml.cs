using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin;

/// <summary>One piece of infrastructure the server depends on, as the badge of the dashboard shows it.</summary>
/// <param name="Label">The name shown to the admin (<c>PostgreSQL</c>, <c>Redis</c>).</param>
/// <param name="Status">The result of its health check, or null when the piece is not used (Redis that is not configured).</param>
/// <param name="Detail">A short text for the tooltip.</param>
public sealed record InfrastructureStatus(string Label, HealthStatus? Status, string Detail);

public sealed class DashboardModel : AdminPageModel
{
    /// <summary>PostgreSQL and Redis, as the health checks <c>database</c> and <c>redis</c> of the server see them. Filled for global admins only (it is about the server, not about one project).</summary>
    public IReadOnlyList<InfrastructureStatus> Infrastructure { get; private set; } = [];

    public AdminDashboardMetrics? Metrics { get; private set; }

    /// <summary>The applications of this project with a two-way health check, and their last answers. Empty when none has one.</summary>
    public IReadOnlyList<ClientHealthInfo> ClientHealth { get; private set; } = [];

    /// <summary>The worst state of <see cref="ClientHealth"/>: the one badge of the header.</summary>
    public ClientHealthState? ClientHealthSummary => ClientHealth.Count == 0 ? null
        : ClientHealth.Any(c => c.State is ClientHealthState.Unhealthy or ClientHealthState.Unreachable) ? ClientHealthState.Unhealthy
        : ClientHealth.Any(c => c.State == ClientHealthState.Degraded) ? ClientHealthState.Degraded
        : ClientHealth.Any(c => c.State == ClientHealthState.Unknown) ? ClientHealthState.Unknown
        : ClientHealthState.Healthy;

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
        if (IsGlobalAdmin) Infrastructure = await ReadInfrastructureAsync();
        return Page();
    }

    /// <summary>Runs the two infrastructure checks now, with a short time limit so a dead service cannot hang the page.</summary>
    private async Task<IReadOnlyList<InfrastructureStatus>> ReadInfrastructureAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        HealthReport report;
        try { report = await Svc<HealthCheckService>().CheckHealthAsync(r => r.Name is "database" or "redis", timeout.Token); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            return [new InfrastructureStatus("PostgreSQL", HealthStatus.Unhealthy, "No answer in 3 seconds."), new InfrastructureStatus("Redis", null, "")];
        }

        static InfrastructureStatus Of(string label, HealthReport report, string name, string offText) =>
            report.Entries.TryGetValue(name, out var e)
                ? new InfrastructureStatus(label, e.Status, $"{e.Status} in {e.Duration.TotalMilliseconds:0} ms" + (string.IsNullOrEmpty(e.Description) ? "" : $" - {e.Description}"))
                : new InfrastructureStatus(label, null, offText);
        return [Of("PostgreSQL", report, "database", "No health check."), Of("Redis", report, "redis", "Redis is not configured (Auth2:Redis:ConnectionString is empty): the server uses the database instead.")];
    }
}
