namespace SystemX.Auth2.Server.Application;

/// <summary>
/// Short-lived cache of "is this project / application enabled". It is used where a token is validated, so a token of a project
/// that was disabled is refused at the next validation. The cache window is <c>Auth2:ProjectStatusCacheSeconds</c> (default 10 seconds).
/// A change made on this instance clears the cache at once, and through Redis (when it is on) on all instances.
/// </summary>
public interface IProjectStatusCache
{
    /// <summary>True when the project exists and is enabled.</summary>
    Task<bool> IsProjectEnabledAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>True when the application (client id) exists and is enabled.</summary>
    Task<bool> IsApplicationEnabledAsync(string clientId, CancellationToken ct = default);

    /// <summary>Forgets the cached value of a project (and tells the other instances).</summary>
    void Invalidate(Guid projectId);

    /// <summary>Forgets the cached value of an application.</summary>
    void InvalidateApplication(string clientId);
}
