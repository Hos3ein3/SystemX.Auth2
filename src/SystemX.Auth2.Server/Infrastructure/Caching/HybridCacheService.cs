using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Operations;

namespace SystemX.Auth2.Server.Infrastructure.Caching;

/// <summary>
/// See <see cref="IHybridCacheService"/>. Redis holds, for every key, a number <c>{prefix}:hc:{key}:ver</c> (raised by an invalidation, never expires)
/// and a hash <c>{prefix}:hc:{key}:data</c> with the version the value belongs to and the value as JSON (expires). A value is written only when the version
/// is still the one it was read under (one Lua script), so a value loaded before an invalidation can never be stored after it.
/// </summary>
public sealed class HybridCacheService(IMemoryCache memory, IServiceProvider services, ILogger<HybridCacheService> log) : IHybridCacheService
{
    /// <summary>The version of a copy made without Redis (or while Redis was failing). Redis versions are never negative.</summary>
    private const long LocalVersion = -1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        AllowOutOfOrderMetadataProperties = true,   // the same options as the settings in the database
    };

    // Writes the value only when the version in Redis is still the one the caller read it under.
    private const string StoreScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then current = '0' end
        if current == ARGV[1] then
          redis.call('HSET', KEYS[2], 'ver', ARGV[1], 'json', ARGV[2])
          redis.call('PEXPIRE', KEYS[2], ARGV[3])
          return 1
        end
        return 0
        """;

    private sealed record Entry(long Version, object Value);

    private RedisConnection? Redis => services.GetService(typeof(RedisConnection)) as RedisConnection;

    private static string MemoryKey(string key) => "hc:" + key;

    /// <inheritdoc />
    public async ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, HybridCacheEntryOptions? options = null, CancellationToken ct = default) where T : class
    {
        options ??= new HybridCacheEntryOptions();
        var redis = Redis;
        if (redis is null) return await FromMemoryOrFactoryAsync(key, factory, options, ct);

        long version;
        try
        {
            var raw = await redis.Database.StringGetAsync(redis.Key($"hc:{key}:ver"));
            version = raw.HasValue && long.TryParse(raw.ToString(), out var parsed) ? parsed : 0;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            log.LogWarning(ex, "Redis did not answer the version of the cache key {Key}. The cache works from memory.", key);
            return await FromMemoryOrFactoryAsync(key, factory, options, ct);
        }

        // Same version as the copy in memory: no database, no value from Redis.
        if (memory.TryGetValue(MemoryKey(key), out Entry? hit) && hit is not null && hit.Version == version && hit.Value is T cached) return cached;

        // Another instance may have loaded this version already.
        try
        {
            var stored = await redis.Database.HashGetAsync(redis.Key($"hc:{key}:data"), ["ver", "json"]);
            if (stored[0].HasValue && stored[1].HasValue && long.TryParse(stored[0].ToString(), out var storedVersion) && storedVersion == version
                && JsonSerializer.Deserialize<T>(stored[1].ToString(), Json) is { } shared)
            {
                Remember(key, version, shared, options);
                return shared;
            }
        }
        catch (Exception ex) when (IsRedisFailure(ex) || ex is JsonException)
        {
            log.LogWarning(ex, "The shared copy of the cache key {Key} could not be read. It is loaded from the source.", key);
        }

        var created = await factory(ct);
        try
        {
            await redis.Database.ScriptEvaluateAsync(StoreScript,
                [redis.Key($"hc:{key}:ver"), redis.Key($"hc:{key}:data")],
                [version.ToString(), JsonSerializer.Serialize(created, Json), (long)options.Expiration.TotalMilliseconds]);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            log.LogWarning(ex, "The cache key {Key} could not be shared through Redis. Other instances will load it themselves.", key);
        }
        Remember(key, version, created, options);
        return created;
    }

    /// <inheritdoc />
    public void Invalidate(string key)
    {
        memory.Remove(MemoryKey(key));
        var redis = Redis;
        if (redis is null) return;
        try
        {
            redis.Database.StringIncrement(redis.Key($"hc:{key}:ver"));   // synchronous on purpose: the next read must already see the new version
            redis.Database.KeyDelete(redis.Key($"hc:{key}:data"), CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (IsRedisFailure(ex)) { log.LogWarning(ex, "Redis could not be told that the cache key {Key} changed. Other instances read it again when their copy expires.", key); }
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(string key, CancellationToken ct = default)
    {
        memory.Remove(MemoryKey(key));
        var redis = Redis;
        if (redis is null) return;
        try
        {
            await redis.Database.StringIncrementAsync(redis.Key($"hc:{key}:ver"));
            await redis.Database.KeyDeleteAsync(redis.Key($"hc:{key}:data"), CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (IsRedisFailure(ex)) { log.LogWarning(ex, "Redis could not be told that the cache key {Key} changed. Other instances read it again when their copy expires.", key); }
    }

    private async ValueTask<T> FromMemoryOrFactoryAsync<T>(string key, Func<CancellationToken, Task<T>> factory, HybridCacheEntryOptions options, CancellationToken ct) where T : class
    {
        if (memory.TryGetValue(MemoryKey(key), out Entry? hit) && hit is not null && hit.Version == LocalVersion && hit.Value is T cached) return cached;
        var created = await factory(ct);
        Remember(key, LocalVersion, created, options);
        return created;
    }

    private void Remember(string key, long version, object value, HybridCacheEntryOptions options) =>
        memory.Set(MemoryKey(key), new Entry(version, value), options.Expiration);

    private static bool IsRedisFailure(Exception ex) => ex is RedisException or TimeoutException or ObjectDisposedException or InvalidOperationException;
}
