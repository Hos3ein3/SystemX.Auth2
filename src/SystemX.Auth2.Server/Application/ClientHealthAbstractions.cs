namespace SystemX.Auth2.Server.Application;

/// <summary>The health of a client application, as the Server saw it at its last check (two-way health check).</summary>
public enum ClientHealthState
{
    /// <summary>Not checked yet, or the last check is too old (the checker is not running).</summary>
    Unknown,
    /// <summary>The client answered that it is healthy and ready.</summary>
    Healthy,
    /// <summary>The client answered, and says that something works only in part.</summary>
    Degraded,
    /// <summary>The client answered that it is not healthy, or answered with an error.</summary>
    Unhealthy,
    /// <summary>The Server could not reach the client (no answer, refused, too slow).</summary>
    Unreachable,
}

/// <summary>What the Server knows about the health of one client application.</summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="ApplicationName">Its name.</param>
/// <param name="State">The result of the last check (<see cref="ClientHealthState.Unknown"/> when it is too old).</param>
/// <param name="CheckedAtUtc">When the client was asked last, or null.</param>
/// <param name="LastHealthyAtUtc">The last time the answer was <see cref="ClientHealthState.Healthy"/>, or null.</param>
/// <param name="Message">A short explanation of a bad state (never holds secrets), or null.</param>
public sealed record ClientHealthInfo(Guid ApplicationId, string ApplicationName, ClientHealthState State, DateTimeOffset? CheckedAtUtc, DateTimeOffset? LastHealthyAtUtc, string? Message);

/// <summary>
/// The two-way health check: a client that sent a <c>HealthUrl</c> in its handshake is asked every minute "are you healthy and ready?", and the answer is kept
/// so the admin console can show it. A client with a one-way check (no <c>HealthUrl</c>) is never called.
/// </summary>
public interface IClientHealthService
{
    /// <summary>
    /// Remembers (or forgets, when <paramref name="healthUrl"/> is null or not a plain http or https address) where an application answers the health question.
    /// Called by the handshake. Returns true when a check is registered.
    /// </summary>
    Task<bool> RegisterAsync(Guid applicationId, string? healthUrl, CancellationToken ct = default);

    /// <summary>Asks every registered client now and keeps the answers. Returns how many clients were asked.</summary>
    Task<int> CheckAllAsync(CancellationToken ct = default);

    /// <summary>Asks the clients of one project now (the refresh button of the dashboard). Returns how many clients were asked.</summary>
    Task<int> CheckProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>The same as <see cref="GetProjectAsync"/> for many projects at once (the list of projects). A project without a two-way client is in the answer with an empty list.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ClientHealthInfo>>> GetProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);

    /// <summary>The health of the applications of a project that have a two-way check, ordered by name. Empty when none has.</summary>
    Task<IReadOnlyList<ClientHealthInfo>> GetProjectAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>One word for the clients of a project (the badge of the project list and of the dashboard).</summary>
public static class ClientHealthSummary
{
    /// <summary>The worst state of the clients; null when no client has a two-way check. Unreachable counts as Unhealthy.</summary>
    public static ClientHealthState? Of(IReadOnlyList<ClientHealthInfo> clients) =>
        clients.Count == 0 ? null
        : clients.Any(c => c.State is ClientHealthState.Unhealthy or ClientHealthState.Unreachable) ? ClientHealthState.Unhealthy
        : clients.Any(c => c.State == ClientHealthState.Degraded) ? ClientHealthState.Degraded
        : clients.Any(c => c.State == ClientHealthState.Unknown) ? ClientHealthState.Unknown
        : ClientHealthState.Healthy;

    /// <summary>The number of clients that are Degraded, Unhealthy or Unreachable.</summary>
    public static int NotHealthy(IReadOnlyList<ClientHealthInfo> clients) =>
        clients.Count(c => c.State is ClientHealthState.Unhealthy or ClientHealthState.Unreachable or ClientHealthState.Degraded);

    /// <summary>The text of the badge: <c>Healthy</c>, <c>Unknown</c> or <c>1 of 2 not healthy</c>; null when there is no client.</summary>
    public static string? Text(IReadOnlyList<ClientHealthInfo> clients) => Of(clients) switch
    {
        null => null,
        ClientHealthState.Healthy => "Healthy",
        ClientHealthState.Unknown => "Unknown",
        _ => $"{NotHealthy(clients)} of {clients.Count} not healthy",
    };

    /// <summary>One line per client for a tooltip.</summary>
    public static string Details(IReadOnlyList<ClientHealthInfo> clients) =>
        string.Join("\n", clients.Select(c => $"{c.ApplicationName}: {c.State}" + (c.CheckedAtUtc is { } at ? $" (checked {at:u})" : "") + (string.IsNullOrEmpty(c.Message) ? "" : $" - {c.Message}")));
}
