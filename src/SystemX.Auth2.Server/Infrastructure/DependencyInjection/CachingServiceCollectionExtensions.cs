using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of the caches of the Server.</summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the memory cache and <see cref="IHybridCacheService"/> (<see cref="HybridCacheService"/>): memory on every instance plus Redis as the shared level
    /// when <c>Auth2:Redis:ConnectionString</c> is set (the Redis connection is registered by the Server when it is). One instance for the whole application.
    /// </summary>
    public static IServiceCollection AddAuth2Caching(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IHybridCacheService, HybridCacheService>();
        return services;
    }
}
