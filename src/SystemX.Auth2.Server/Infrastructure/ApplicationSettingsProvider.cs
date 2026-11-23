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
public sealed class ApplicationSettingsProvider(Auth2DbContext db, IMemoryCache cache, ISettingsInvalidationBus bus, ILogger<ApplicationSettingsProvider> log) : IApplicationSettingsProvider
{
    internal static string CacheKey(Guid id) => $"auth2:settings:{id:N}";
    private static string Key(Guid id) => CacheKey(id);

    /// <inheritdoc />
    public async Task<ApplicationSettings> GetAsync(Guid applicationId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(applicationId), out ApplicationSettings? hit) && hit is not null) return hit;

        var settings = await db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId).Select(a => a.Settings).FirstOrDefaultAsync(ct)
            ?? throw new ApplicationNotFoundException("Application not found.");
        cache.Set(Key(applicationId), settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    /// <inheritdoc />
    public void Invalidate(Guid applicationId)
    {
        cache.Remove(Key(applicationId));
        // Other instances (Redis pub/sub) drop their copy too. A failure here only means they refresh after the cache time (10 minutes).
        _ = bus.PublishAsync(applicationId).ContinueWith(t => log.LogWarning(t.Exception, "Could not tell other instances about the settings change."),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
