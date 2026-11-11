using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

using SystemX.Auth2.Infrastructure.Caching;

namespace SystemX.Auth2.Infrastructure.DependencyInjection;

public static class CachingExtensions
{
    public static IServiceCollection AddAuth2Caching(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CacheOptions>()
            .Bind(config.GetSection(CacheOptions.SectionName))
            .Validate(o => o.Provider != CacheProvider.Redis || !string.IsNullOrWhiteSpace(o.RedisConnection),
                "Auth2:Cache:RedisConnection is required when Provider is Redis.")
            .Validate(o => o.PermissionTtlSeconds is > 0 and <= 3600,
                "Auth2:Cache:PermissionTtlSeconds must be between 1 and 3600.")
            .ValidateOnStart();

        var settings = config.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();

        if (settings.Provider == CacheProvider.Redis)
        {
            if (string.IsNullOrWhiteSpace(settings.RedisConnection))
                throw new InvalidOperationException("Auth2:Cache:RedisConnection is required when Provider is Redis.");

            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = settings.RedisConnection;
                o.InstanceName = settings.InstanceName;
            });

            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var cfg = ConfigurationOptions.Parse(settings.RedisConnection);
                cfg.AbortOnConnectFail = false;      // start even if Redis is down; retry in background
                return ConnectionMultiplexer.Connect(cfg);
            });

            // services.AddSingleton<IFlowStateStore, RedisFlowStateStore>();
            // services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        }
        else
        {
            services.AddDistributedMemoryCache();
            // services.AddSingleton<IFlowStateStore, MemoryFlowStateStore>();
            // services.AddSingleton<IRateLimitStore, MemoryRateLimitStore>();
        }

        services.AddSingleton(TimeProvider.System);
        //services.AddSingleton<IPermissionCache, DistributedPermissionCache>();
        return services;
    }
}