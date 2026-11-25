using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Infrastructure.Operations;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// <c>IAuthFlowResolver.GetProjectAsync</c>, <c>GetSettingsAsync</c> and <c>GetClientAsync</c> (what every page reads first) come from Redis when it is on:
/// the values are in Redis, a request does not read the database for them, and a change drops them.
/// </summary>
public class AuthFlowCacheRedisTests : Phase6Base
{
    protected override bool UseRedis => true;

    private Task<(string Prefix, string[] Keys)> RedisKeysAsync()
    {
        var redis = Factory.Services.GetRequiredService<RedisConnection>();
        var server = redis.Multiplexer.GetServer(redis.Multiplexer.GetEndPoints()[0]);
        return Task.FromResult((redis.Prefix, (string[])[.. server.Keys(pattern: redis.Key("hc:*")).Select(k => (string)k!)]));
    }

    private async Task ReadAllAsync()
    {
        await using var scope = ScopeFor("appa");
        var flow = scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>();
        await flow.GetProjectAsync();
        await flow.GetSettingsAsync();
        await flow.GetClientAsync();
    }

    [Fact]
    public async Task TheProjectTheSettingsAndTheClient_AreStoredInRedis()
    {
        await PrepareAppsAsync();
        await ReadAllAsync();

        var (prefix, keys) = await RedisKeysAsync();
        keys.ShouldContain(k => k.StartsWith($"{prefix}:hc:project:") && k.EndsWith(":data"));
        keys.ShouldContain(k => k.StartsWith($"{prefix}:hc:settings:") && k.EndsWith(":data"));
        keys.ShouldContain($"{prefix}:hc:client:appa:data");
        keys.ShouldContain($"{prefix}:hc:project-ref:client:appa:data");
        keys.ShouldContain(k => k.EndsWith(":ver"));   // the version that every request checks
    }

    [Fact]
    public async Task ARequest_DoesNotReadTheDatabaseForThem_UntilTheyAreInvalidated()
    {
        await PrepareAppsAsync();
        await ReadAllAsync();
        var project = (await FindProjectAsync("appa"))!;

        // change the row behind the cache's back: the cached copy must still be served
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            await db.Projects.Where(p => p.Id == project.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Renamed behind the cache"));
        }
        await using (var scope = ScopeFor("appa"))
            (await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetProjectAsync()).Name.ShouldNotBe("Renamed behind the cache");

        // an invalidation raises the version: the next request reads the new row
        Factory.Services.GetRequiredService<IHybridCacheService>().InvalidateProject(project.Id);
        await using (var scope = ScopeFor("appa"))
            (await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetProjectAsync()).Name.ShouldBe("Renamed behind the cache");
    }

    [Fact]
    public async Task SavingTheSettings_ShowsInTheNextRequest()
    {
        await PrepareAppsAsync();
        await ReadAllAsync();
        await UpdateSettingsAsync(A, s => s.Password.MinLength = 21);   // the admin path: it invalidates

        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetSettingsAsync()).Password.MinLength.ShouldBe(21);
    }

    [Fact]
    public async Task AChangeOfTheApplication_ShowsInTheNextRequest()
    {
        await PrepareAppsAsync();
        await ReadAllAsync();
        await UpdateSettingsAsync(A, s => s.App.UiMode = SystemX.Auth2.Contracts.Enums.UiMode.Self);

        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetClientAsync())!.Settings.UiMode.ShouldBe(SystemX.Auth2.Contracts.Enums.UiMode.Self);
    }
}
