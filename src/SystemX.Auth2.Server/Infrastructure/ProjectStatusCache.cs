using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>
/// See <see cref="IProjectStatusCache"/>. The values live in the shared memory cache for <c>Auth2:ProjectStatusCacheSeconds</c> seconds
/// (0 turns the cache off). Other instances learn about a change through the Redis settings channel when Redis is on.
/// </summary>
public sealed class ProjectStatusCache(IServiceScopeFactory scopes, IMemoryCache cache, ISettingsInvalidationBus bus, IOptions<Auth2ServerOptions> options, ILogger<ProjectStatusCache> log) : IProjectStatusCache
{
    internal static string ProjectKey(Guid id) => $"auth2:status:{id:N}";
    private static string ApplicationKey(string clientId) => $"auth2:appstatus:{clientId}";

    private TimeSpan Window => TimeSpan.FromSeconds(Math.Max(0, options.Value.ProjectStatusCacheSeconds));

    /// <inheritdoc />
    public async Task<bool> IsProjectEnabledAsync(Guid projectId, CancellationToken ct = default)
    {
        if (Window > TimeSpan.Zero && cache.TryGetValue(ProjectKey(projectId), out bool hit)) return hit;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var enabled = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.IsEnabled).FirstOrDefaultAsync(ct);
        if (Window > TimeSpan.Zero) cache.Set(ProjectKey(projectId), enabled, Window);
        return enabled;
    }

    /// <inheritdoc />
    public async Task<bool> IsApplicationEnabledAsync(string clientId, CancellationToken ct = default)
    {
        if (Window > TimeSpan.Zero && cache.TryGetValue(ApplicationKey(clientId), out bool hit)) return hit;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var enabled = await db.Set<Auth2Application>().AsNoTracking().Where(a => a.ClientId == clientId).Select(a => a.IsEnabled).FirstOrDefaultAsync(ct);
        if (Window > TimeSpan.Zero) cache.Set(ApplicationKey(clientId), enabled, Window);
        return enabled;
    }

    /// <inheritdoc />
    public void Invalidate(Guid projectId)
    {
        cache.Remove(ProjectKey(projectId));
        _ = bus.PublishAsync(projectId).ContinueWith(t => log.LogWarning(t.Exception, "Could not tell other instances about the project status change."),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <inheritdoc />
    public void InvalidateApplication(string clientId) => cache.Remove(ApplicationKey(clientId));
}
