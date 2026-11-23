using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>Memory cache of settings. Singleton. Redis and cross-instance invalidation come in phase 10.</summary>
public sealed class ApplicationSettingsProvider(IServiceScopeFactory scopes, IMemoryCache cache) : IApplicationSettingsProvider
{
    private static string Key(Guid id) => $"auth2:settings:{id:N}";

    /// <inheritdoc />
    public async Task<ApplicationSettings> GetAsync(Guid applicationId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(applicationId), out ApplicationSettings? hit) && hit is not null) return hit;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var settings = await db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId).Select(a => a.Settings).FirstOrDefaultAsync(ct)
            ?? throw new ApplicationNotFoundException("Application not found.");
        cache.Set(Key(applicationId), settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    /// <inheritdoc />
    public void Invalidate(Guid applicationId) => cache.Remove(Key(applicationId));
}
