using Microsoft.Extensions.Diagnostics.HealthChecks;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.HealthChecks;

/// <summary><c>Healthy</c> when the last handshake worked, <c>Degraded</c> otherwise (the application still runs).</summary>
public sealed class Auth2ClientHealthCheck(Auth2ClientState state) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.Status == HandshakeStatus.Ok
            ? HealthCheckResult.Healthy("Connected to the Auth2 server.")
            : HealthCheckResult.Degraded(state.Status is null ? "The handshake with the Auth2 server has not finished." : $"Handshake status: {state.Status}. {state.Message}"));
}
