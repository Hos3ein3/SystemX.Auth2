using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SystemX.Auth2.Server.Application;

/// <summary>One piece of infrastructure the server depends on, as a badge of the admin console shows it.</summary>
/// <param name="Label">The name shown to the admin (<c>PostgreSQL</c>, <c>Redis</c>).</param>
/// <param name="Status">The result of its health check, or null when the piece is not used (Redis that is not configured).</param>
/// <param name="Detail">A short text for the tooltip.</param>
public sealed record InfrastructureStatus(string Label, HealthStatus? Status, string Detail);

/// <summary>Runs the health checks of PostgreSQL (<c>database</c>) and Redis (<c>redis</c>) now, for the admin console.</summary>
public interface IInfrastructureStatusService
{
    /// <summary>PostgreSQL first, then Redis. A check that does not answer in 3 seconds is <see cref="HealthStatus.Unhealthy"/> (PostgreSQL) so a dead service cannot hang the page.</summary>
    Task<IReadOnlyList<InfrastructureStatus>> ReadAsync(CancellationToken ct = default);
}
