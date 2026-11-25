using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Applications;

namespace SystemX.Auth2.Client.HealthChecks;

/// <summary>
/// Answers the Server's health question (two-way health check): is this application healthy and ready? It runs <b>all</b> the health checks of the application
/// (database, queue, ... and the <c>auth2</c> check) and gives one word. The answer has no details: it is public, like any health endpoint.
/// </summary>
internal sealed class ClientHealthReporter(HealthCheckService checks, IOptions<Auth2ClientOptions> client)
{
    /// <summary>The answer and the HTTP status: 200 for Healthy and Degraded, 503 for Unhealthy.</summary>
    public async Task<(ClientHealthResponse Body, int StatusCode)> ReportAsync(CancellationToken ct)
    {
        var report = await checks.CheckHealthAsync(ct);
        var word = report.Status switch { HealthStatus.Healthy => "Healthy", HealthStatus.Degraded => "Degraded", _ => "Unhealthy" };
        return (new ClientHealthResponse(word, client.Value.ApplicationName, client.Value.ClientVersion), report.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
    }
}
