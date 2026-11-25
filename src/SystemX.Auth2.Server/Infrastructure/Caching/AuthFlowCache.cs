using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Caching;

/// <summary>
/// The keys of what every request needs first: the project, the calling application and the settings (<see cref="IProjectSettingsProvider"/>), kept in
/// <see cref="IHybridCacheService"/> (memory + Redis, checked by version). A change of the project or the application must call
/// <see cref="InvalidateProject"/> / <see cref="InvalidateClient"/>, which raises the version so every instance reads the new data on its next request.
/// </summary>
public static class AuthFlowCache
{
    /// <summary>Only the id of a project: how a project key, a client id or the single-project name finds its project. These never change, so they are only dropped when the project or application is deleted.</summary>
    public sealed record ProjectRef(Guid Id);

    /// <summary>The project row.</summary>
    public static string Project(Guid projectId) => $"project:{projectId:N}";

    /// <summary>The project of a project key.</summary>
    public static string ByProjectKey(string projectKey) => $"project-ref:key:{projectKey}";

    /// <summary>The project of the application with this client id.</summary>
    public static string ByClientId(string clientId) => $"project-ref:client:{clientId}";

    /// <summary>The project of single-project mode (by name).</summary>
    public static string Single(string name) => $"project-ref:single:{name}";

    /// <summary>The application row (as <c>ClientApplicationInfo</c>).</summary>
    public static string Client(string clientId) => $"client:{clientId}";

    /// <summary>The project (name, enabled flag, settings version) changed: every instance reads it again.</summary>
    public static void InvalidateProject(this IHybridCacheService cache, Guid projectId) => cache.Invalidate(Project(projectId));

    /// <summary>The application (name, enabled flag, scopes, settings, web server address, ...) changed: every instance reads it again.</summary>
    public static void InvalidateClient(this IHybridCacheService cache, string clientId) => cache.Invalidate(Client(clientId));

    /// <summary>The application was deleted: its row and its way to a project are dropped.</summary>
    public static void InvalidateClientCompletely(this IHybridCacheService cache, string clientId)
    {
        cache.Invalidate(Client(clientId));
        cache.Invalidate(ByClientId(clientId));
    }

    /// <summary>The project was deleted: its row and its way from a key are dropped.</summary>
    public static void InvalidateProjectCompletely(this IHybridCacheService cache, Guid projectId, string? projectKey)
    {
        cache.Invalidate(Project(projectId));
        if (projectKey is not null) cache.Invalidate(ByProjectKey(projectKey));
    }
}
