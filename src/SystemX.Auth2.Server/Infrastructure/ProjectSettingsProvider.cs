using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>
/// Reads settings through the current scope's DbContext (so it sees the caller's open transaction) and keeps them in <see cref="IHybridCacheService"/>:
/// memory on the instance, Redis shared by all instances. A read asks Redis only for the version of the settings; the database is read when nothing is cached
/// for the current version. A change raises the version (<see cref="Invalidate"/>), so every instance reads the new settings on its next call.
/// </summary>
public sealed class ProjectSettingsProvider(Auth2DbContext db, IHybridCacheService cache, ISettingsInvalidationBus bus, ILogger<ProjectSettingsProvider> log) : IProjectSettingsProvider
{
    internal static string CacheKey(Guid id) => $"settings:{id:N}";

    /// <inheritdoc />
    public async Task<ProjectSettings> GetAsync(Guid projectId, CancellationToken ct = default) =>
        await cache.GetOrCreateAsync(CacheKey(projectId), async token =>
            await db.Projects.AsNoTracking().Where(a => a.Id == projectId).Select(a => a.Settings).FirstOrDefaultAsync(token)
                ?? throw new ProjectNotFoundException("Application not found."),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) }, ct);

    /// <inheritdoc />
    public void Invalidate(Guid projectId)
    {
        cache.Invalidate(CacheKey(projectId));   // drops this instance's copy and raises the version in Redis for all instances
        cache.InvalidateProject(projectId);      // the project row has the settings version and the name: read again too
        // The status cache of the other instances still listens to this message. A failure only means they refresh after their cache time.
        _ = bus.PublishAsync(projectId).ContinueWith(t => log.LogWarning(t.Exception, "Could not tell other instances about the settings change."),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
