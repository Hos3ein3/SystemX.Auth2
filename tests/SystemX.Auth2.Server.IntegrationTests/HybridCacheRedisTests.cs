using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Infrastructure.Operations;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// The hybrid cache with a real Redis. Every "instance" has its own memory (its own <see cref="MemoryCache"/>) and shares the Redis, like two Server instances.
/// </summary>
public class HybridCacheRedisTests
{
    private sealed class Instance : IDisposable
    {
        public Instance(string connectionString, string prefix)
        {
            Connection = new RedisConnection(Options.Create(new Auth2ServerOptions { Redis = { ConnectionString = connectionString, KeyPrefix = prefix } }));
            var services = new ServiceCollection().AddSingleton(Connection).BuildServiceProvider();
            Cache = new HybridCacheService(new MemoryCache(new MemoryCacheOptions()), services, NullLogger<HybridCacheService>.Instance);
        }

        public RedisConnection Connection { get; }
        public HybridCacheService Cache { get; }
        public void Dispose() => Connection.Dispose();
    }

    private static async Task<(Instance A, Instance B)> TwoInstancesAsync()
    {
        var connection = await SharedRedis.ConnectionStringAsync();
        var prefix = "hc-" + Guid.NewGuid().ToString("N")[..8];   // own keys: tests never meet each other
        return (new Instance(connection, prefix), new Instance(connection, prefix));
    }

    [Fact]
    public async Task AnotherInstance_ReadsTheValueFromRedis_WithoutCallingTheSource()
    {
        var (a, b) = await TwoInstancesAsync();
        using var _a = a; using var _b = b;
        var sourceCalls = 0;
        Task<ProjectSettings> Source(CancellationToken _) { sourceCalls++; return Task.FromResult(new ProjectSettings { Password = { MinLength = 17 } }); }

        (await a.Cache.GetOrCreateAsync("settings:x", Source)).Password.MinLength.ShouldBe(17);
        var fromRedis = await b.Cache.GetOrCreateAsync("settings:x", Source);
        fromRedis.Password.MinLength.ShouldBe(17);
        sourceCalls.ShouldBe(1);   // the second instance did not touch the source
    }

    [Fact]
    public async Task TheValue_IsSavedLikeTheSettingsInTheDatabase_EnumsAndAll()
    {
        var (a, b) = await TwoInstancesAsync();
        using var _a = a; using var _b = b;
        await a.Cache.GetOrCreateAsync("settings:y", _ => Task.FromResult(new ProjectSettings { Registration = { JoinMode = JoinMode.InviteOnly } }));
        (await b.Cache.GetOrCreateAsync("settings:y", _ => Task.FromResult(new ProjectSettings()))).Registration.JoinMode.ShouldBe(JoinMode.InviteOnly);
    }

    [Fact]
    public async Task AnInvalidation_OnOneInstance_IsSeenByTheOtherOnItsNextRead()
    {
        var (a, b) = await TwoInstancesAsync();
        using var _a = a; using var _b = b;
        var current = 1;
        Task<ProjectSettings> Source(CancellationToken _) => Task.FromResult(new ProjectSettings { Password = { MinLength = current } });

        (await a.Cache.GetOrCreateAsync("settings:z", Source)).Password.MinLength.ShouldBe(1);
        (await b.Cache.GetOrCreateAsync("settings:z", Source)).Password.MinLength.ShouldBe(1);   // b now has a copy in its memory

        current = 2;
        a.Cache.Invalidate("settings:z");

        (await b.Cache.GetOrCreateAsync("settings:z", Source)).Password.MinLength.ShouldBe(2);   // b saw only the version change
        (await a.Cache.GetOrCreateAsync("settings:z", Source)).Password.MinLength.ShouldBe(2);
    }

    [Fact]
    public async Task AValueLoadedBeforeAnInvalidation_IsNeverStoredAfterIt()
    {
        var (a, b) = await TwoInstancesAsync();
        using var _a = a; using var _b = b;
        var value = 1;
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        async Task<ProjectSettings> Slow(CancellationToken _)
        {
            var read = value;            // the "database read" happens first ...
            started.SetResult();
            await release.Task;          // ... and the answer is slow
            return new ProjectSettings { Password = { MinLength = read } };
        }

        var loading = a.Cache.GetOrCreateAsync("settings:race", Slow).AsTask();
        await started.Task;
        value = 2;
        b.Cache.Invalidate("settings:race");   // the data changed while a was still loading the old value
        release.SetResult();
        await loading;

        // a's old value must not be in Redis for the new version: b loads the new one from the source
        (await b.Cache.GetOrCreateAsync("settings:race", _ => Task.FromResult(new ProjectSettings { Password = { MinLength = value } }))).Password.MinLength.ShouldBe(2);
    }

    [Fact]
    public async Task WhenRedisIsDown_TheCacheStillWorks_FromMemoryAndTheSource()
    {
        using var broken = new Instance("localhost:1,connectTimeout=200,syncTimeout=200,abortConnect=false", "hc-down");
        var calls = 0;
        Task<ProjectSettings> Source(CancellationToken _) => Task.FromResult(new ProjectSettings { Password = { MinLength = ++calls } });

        (await broken.Cache.GetOrCreateAsync("settings:down", Source)).Password.MinLength.ShouldBe(1);
        (await broken.Cache.GetOrCreateAsync("settings:down", Source)).Password.MinLength.ShouldBe(1);   // from memory
        broken.Cache.Invalidate("settings:down");                                                            // does not throw
        (await broken.Cache.GetOrCreateAsync("settings:down", Source)).Password.MinLength.ShouldBe(2);
    }
}
