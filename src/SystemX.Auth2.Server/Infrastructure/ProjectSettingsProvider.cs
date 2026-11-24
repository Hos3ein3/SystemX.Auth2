using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>
/// Reads settings through the current scope's DbContext (so it sees the caller's open transaction)
/// and caches them in the shared memory cache. A change is announced to other instances through <see cref="ISettingsInvalidationBus"/> (Redis pub/sub).
/// </summary>
public sealed class ProjectSettingsProvider(Auth2DbContext db, IMemoryCache cache, ISettingsInvalidationBus bus, ILogger<ProjectSettingsProvider> log) : IProjectSettingsProvider
{
    internal static string CacheKey(Guid id) => $"auth2:settings:{id:N}";
    private static string Key(Guid id) => CacheKey(id);

    /// <inheritdoc />
    public async Task<ProjectSettings> GetAsync(Guid projectId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(projectId), out ProjectSettings? hit) && hit is not null) return hit;

        var settings = await db.Projects.AsNoTracking()
            .Where(a => a.Id == projectId).Select(a => a.Settings).FirstOrDefaultAsync(ct)
            ?? throw new ProjectNotFoundException("Application not found.");
        cache.Set(Key(projectId), settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    /// <inheritdoc />
    public void Invalidate(Guid projectId)
    {
        cache.Remove(Key(projectId));
        // Other instances (Redis pub/sub) drop their copy too. A failure here only means they refresh after the cache time (10 minutes).
        _ = bus.PublishAsync(projectId).ContinueWith(t => log.LogWarning(t.Exception, "Could not tell other instances about the settings change."),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
