namespace SystemX.Auth2.Server.Application;

/// <summary>How long and how a value is kept by <see cref="IHybridCacheService"/>.</summary>
public sealed class HybridCacheEntryOptions
{
    /// <summary>How long a copy may live in the memory of one instance and in Redis. After that it is read again from the source. Default 10 minutes.</summary>
    public TimeSpan Expiration { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// A two-level cache for values that are read all the time and changed rarely (the settings of a project, for example).
/// <b>Level one</b> is the memory of the instance; <b>level two</b> is Redis (when <c>Auth2:Redis:ConnectionString</c> is set), shared by every instance.
/// <para>
/// Every key has a small <b>version</b> in Redis. A read asks Redis only for that number: when it equals the version of the copy in memory the copy is returned
/// (no database, no value sent over the network). When it differs, the value is read from Redis; when Redis has no value for the current version, the factory
/// (the database) is called once and its result is written to Redis for every other instance. <see cref="Invalidate"/> raises the version, so every instance
/// reloads on its next read, at once, without a message to each of them.
/// </para>
/// <para>Without Redis the cache is the memory of the instance only. When Redis fails, the cache does not: it logs and works from memory and the factory.</para>
/// </summary>
public interface IHybridCacheService
{
    /// <summary>The value for <paramref name="key"/>: from memory, else from Redis, else from <paramref name="factory"/> (which is then stored). An exception of the factory is not cached.</summary>
    ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, HybridCacheEntryOptions? options = null, CancellationToken ct = default) where T : class;

    /// <summary>The value changed: drops the copy of this instance and raises the version in Redis, so every instance reads it again. Returns after Redis has the new version.</summary>
    void Invalidate(string key);

    /// <summary><see cref="Invalidate"/> for asynchronous callers.</summary>
    ValueTask InvalidateAsync(string key, CancellationToken ct = default);
}
