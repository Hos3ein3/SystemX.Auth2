using Microsoft.Extensions.Diagnostics.HealthChecks;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>See <see cref="IInfrastructureStatusService"/>.</summary>
internal sealed class InfrastructureStatusService(HealthCheckService checks) : IInfrastructureStatusService
{
    public async Task<IReadOnlyList<InfrastructureStatus>> ReadAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        HealthReport report;
        try { report = await checks.CheckHealthAsync(r => r.Name is "database" or "redis", timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
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
