using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SystemX.Auth2.Client.Machine;

namespace SystemX.Auth2.Client.Tests;

public class MachineTokenTests
{
    private static (IAuth2MachineTokenProvider Tokens, FakeServer Server, FakeTimeProvider Time) Start(Func<int, HttpResponseMessage>? answer = null)
    {
        var server = new FakeServer();
        var n = 0;
        server.Respond = (_, _) => Task.FromResult(answer?.Invoke(Interlocked.Increment(ref n)) ?? FakeServer.Token(300, "t" + Interlocked.Increment(ref n)));
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        return (TestHost.Build(server, time: time).GetRequiredService<IAuth2MachineTokenProvider>(), server, time);
    }

    [Fact]
    public async Task Token_IsCached_UntilShortlyBeforeItExpires()
    {
        var (tokens, server, time) = Start();
        var a = await tokens.GetTokenAsync(["a.read"]);
        (await tokens.GetTokenAsync(["a.read"])).ShouldBe(a);
        server.CountTo("/connect/token").ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(239));   // expires_in 300 - 60 = 240 s
        (await tokens.GetTokenAsync(["a.read"])).ShouldBe(a);
        time.Advance(TimeSpan.FromSeconds(2));
        (await tokens.GetTokenAsync(["a.read"])).ShouldNotBe(a);
        server.CountTo("/connect/token").ShouldBe(2);
    }

    [Fact]
    public async Task ShortLivedTokens_AreKeptAtLeast30Seconds()
    {
        var (tokens, server, time) = Start(_ => FakeServer.Token(expiresIn: 40));
        await tokens.GetTokenAsync(["a"]);
        time.Advance(TimeSpan.FromSeconds(29));
        await tokens.GetTokenAsync(["a"]);
        server.CountTo("/connect/token").ShouldBe(1);
        time.Advance(TimeSpan.FromSeconds(2));
        await tokens.GetTokenAsync(["a"]);
        server.CountTo("/connect/token").ShouldBe(2);
    }

    [Fact]
    public async Task ManyParallelCallers_CauseOneRequest()
    {
        var gate = new TaskCompletionSource();
        var server = new FakeServer { Respond = async (_, _) => { await gate.Task; return FakeServer.Token(); } };
        var tokens = TestHost.Build(server).GetRequiredService<IAuth2MachineTokenProvider>();
        var calls = Enumerable.Range(0, 20).Select(_ => tokens.GetTokenAsync(["a.read"])).ToList();
        await Task.Delay(100);
        gate.SetResult();
        (await Task.WhenAll(calls)).Distinct().Count().ShouldBe(1);
        server.CountTo("/connect/token").ShouldBe(1);
    }

    [Fact]
    public async Task ScopeSets_AreCachedSeparately_AndTheOrderDoesNotMatter()
    {
        var (tokens, server, _) = Start();
        var ab = await tokens.GetTokenAsync(["b", "a"]);
        (await tokens.GetTokenAsync(["a", "b"])).ShouldBe(ab);
        (await tokens.GetTokenAsync(["a"])).ShouldNotBe(ab);
        server.CountTo("/connect/token").ShouldBe(2);
        server.Calls[0].Body.ShouldContain("scope=a+b");
    }

    [Fact]
    public async Task Request_HasTheClientCredentials_ButTheErrorNeverHasTheSecret()
    {
        var (tokens, server, _) = Start(_ => FakeServer.Json(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_code":"invalid_client"}"""));
        server.Calls.Clear();
        var ex = await Should.ThrowAsync<Auth2Exception>(() => tokens.GetTokenAsync(["a"]));
        server.Calls[0].Body.ShouldContain("client_id=blog");
        server.Calls[0].Body.ShouldContain("grant_type=client_credentials");
        ex.StatusCode.ShouldBe(400);
        ex.ErrorCode.ShouldBe("invalid_client");
        ex.IsTransient.ShouldBeFalse();
        (ex.Message + ex).ShouldNotContain(TestHost.Secret);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task ServerErrors_AreTransient(int status)
    {
        var (tokens, _, _) = Start(_ => FakeServer.Json((HttpStatusCode)status, "{}"));
        (await Should.ThrowAsync<Auth2Exception>(() => tokens.GetTokenAsync(["a"]))).IsTransient.ShouldBeTrue();
    }

    [Fact]
    public async Task NetworkErrors_AreTransient_AndFailuresAreNotCached()
    {
        var server = new FakeServer();
        var fail = true;
        server.Respond = (_, _) => fail ? throw new HttpRequestException("boom") : Task.FromResult(FakeServer.Token());
        var tokens = TestHost.Build(server).GetRequiredService<IAuth2MachineTokenProvider>();
        var ex = await Should.ThrowAsync<Auth2Exception>(() => tokens.GetTokenAsync(["a"]));
        ex.IsTransient.ShouldBeTrue();
        ex.ErrorCode.ShouldBe(Auth2ClientErrorCodes.ServerUnavailable);
        fail = false;
        (await tokens.GetTokenAsync(["a"])).ShouldBe("machine-token");
    }

    [Fact]
    public async Task Invalidate_MakesTheNextCallAskAgain()
    {
        var (tokens, server, _) = Start();
        await tokens.GetTokenAsync(["a"]);
        tokens.Invalidate();
        await tokens.GetTokenAsync(["a"]);
        server.CountTo("/connect/token").ShouldBe(2);
    }

    [Fact]
    public async Task TheHandler_AddsABearerToken_AndDropsTheCacheOn401()
    {
        var server = new FakeServer();
        var tokenCalls = 0;
        server.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/connect/token"
            ? FakeServer.Token(300, "tok" + Interlocked.Increment(ref tokenCalls))
            : r.Headers.Authorization!.Parameter == "tok1" ? FakeServer.Json(HttpStatusCode.Unauthorized, "{}") : FakeServer.Json(HttpStatusCode.OK, "{}"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuth2Client("https://auth.test", o => { o.ApplicationName = "B"; o.ClientId = "b"; o.ClientSecret = "s"; });
        services.AddHttpClient(Auth2ClientDefaults.ResilientHttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
        services.AddHttpClient("orders", c => c.BaseAddress = new Uri("https://orders.test/")).AddAuth2MachineToken("orders.read").ConfigurePrimaryHttpMessageHandler(() => server);
        var client = services.BuildServiceProvider().GetRequiredService<IAuth2MachineService>().CreateClient("orders");

        (await client.GetAsync("items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // tok1 refused
        (await client.GetAsync("items")).StatusCode.ShouldBe(HttpStatusCode.OK);              // a new token (tok2) was fetched
        server.Calls.Last().Authorization.ShouldBe("Bearer tok2");
    }
}
