using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;

namespace SystemX.Auth2.Server.Tests;

/// <summary>The hybrid cache without Redis: the memory of the instance, with the same rules.</summary>
public class HybridCacheServiceTests
{
    private sealed record Box(int Value);

    private static HybridCacheService Cache() =>
        new(new MemoryCache(new MemoryCacheOptions()), new ServiceCollection().BuildServiceProvider(), NullLogger<HybridCacheService>.Instance);

    [Fact]
    public async Task TheFactory_RunsOnce_ForTheSameKey()
    {
        var cache = Cache();
        var calls = 0;
        Task<Box> Make(CancellationToken _) { calls++; return Task.FromResult(new Box(calls)); }

        var first = await cache.GetOrCreateAsync("k", Make);
        var second = await cache.GetOrCreateAsync("k", Make);
        calls.ShouldBe(1);
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task DifferentKeys_AreSeparate()
    {
        var cache = Cache();
        (await cache.GetOrCreateAsync("a", _ => Task.FromResult(new Box(1)))).Value.ShouldBe(1);
        (await cache.GetOrCreateAsync("b", _ => Task.FromResult(new Box(2)))).Value.ShouldBe(2);
    }

    [Fact]
    public async Task Invalidate_MakesTheNextReadCallTheFactoryAgain()
    {
        var cache = Cache();
        var calls = 0;
        Task<Box> Make(CancellationToken _) => Task.FromResult(new Box(++calls));

        (await cache.GetOrCreateAsync("k", Make)).Value.ShouldBe(1);
        cache.Invalidate("k");
        (await cache.GetOrCreateAsync("k", Make)).Value.ShouldBe(2);
        await cache.InvalidateAsync("k");
        (await cache.GetOrCreateAsync("k", Make)).Value.ShouldBe(3);
    }

    [Fact]
    public async Task AFailingFactory_IsNotCached()
    {
        var cache = Cache();
        var calls = 0;
        Task<Box> Make(CancellationToken _) => ++calls == 1 ? throw new InvalidOperationException("db down") : Task.FromResult(new Box(calls));

        await Should.ThrowAsync<InvalidOperationException>(async () => await cache.GetOrCreateAsync("k", Make));
        (await cache.GetOrCreateAsync("k", Make)).Value.ShouldBe(2);
    }

    [Fact]
    public async Task AValue_ExpiresAfterTheTime()
    {
        var cache = Cache();
        var calls = 0;
        Task<Box> Make(CancellationToken _) => Task.FromResult(new Box(++calls));
        var options = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMilliseconds(60) };

        await cache.GetOrCreateAsync("k", Make, options);
        await Task.Delay(250);
        await cache.GetOrCreateAsync("k", Make, options);
        calls.ShouldBe(2);
    }
}
