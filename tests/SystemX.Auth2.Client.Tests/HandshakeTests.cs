using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.HealthChecks;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Tests;

public class HandshakeRunnerTests
{
    private const string Ok = """{"applicationId":"0198a1b2-0000-7000-8000-000000000001","projectKey":"blog","applicationName":"Blog","serverMode":"MultiProject","issuer":"https://auth.test","settingsVersion":1,"cookieLifetime":"01:30:00","singleActiveSession":false}""";
    private const string OkSingle = """{"applicationId":"0198a1b2-0000-7000-8000-000000000001","projectKey":null,"applicationName":"Blog","serverMode":"SingleProject","issuer":"https://auth.test","settingsVersion":1,"cookieLifetime":null,"singleActiveSession":false}""";

    private static (HandshakeRunner Runner, FakeServer Server) Start(Func<HttpResponseMessage> handshake, string? projectKey = "blog", Action<Client.Options.Auth2ClientOptions>? options = null)
    {
        var server = new FakeServer
        {
            Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/connect/token" ? FakeServer.Token() : handshake()),
        };
        var sp = TestHost.Build(server, b => { if (projectKey is not null) b.ServerWithMultiProject(projectKey); }, options: options);
        return (sp.GetRequiredService<HandshakeRunner>(), server);
    }

    private static HttpResponseMessage Err(HttpStatusCode s, string code) => FakeServer.Json(s, $$"""{"code":"{{code}}","message":"x"}""");

    [Fact]
    public async Task Ok_ReturnsTheServerAnswer_AndSendsKeyNameVersionWithTheMachineToken()
    {
        var (runner, server) = Start(() => FakeServer.Json(HttpStatusCode.OK, Ok));
        var outcome = await runner.RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Ok);
        outcome.Response!.ServerMode.ShouldBe(ServerMode.MultiProject);
        outcome.Response.CookieLifetime.ShouldBe(TimeSpan.FromMinutes(90));
        var call = server.Calls.Single(c => c.Path == Auth2Routes.Handshake);
        call.Authorization.ShouldBe("Bearer machine-token");
        call.Body.ShouldContain("\"projectKey\":\"blog\"");
        call.Body.ShouldContain("\"applicationName\":\"Blog\"");
        server.Calls[0].Body.ShouldContain("scope=auth2.client.handshake");
    }

    [Fact]
    public async Task NoProjectKey_AgainstASingleServer_IsOk() =>
        (await Start(() => FakeServer.Json(HttpStatusCode.OK, OkSingle), projectKey: null).Runner.RunAsync()).Status.ShouldBe(HandshakeStatus.Ok);

    [Fact]
    public async Task ProjectKey_AgainstASingleServer_IsIgnored_WithAWarningMessage()
    {
        var outcome = await Start(() => FakeServer.Json(HttpStatusCode.OK, OkSingle)).Runner.RunAsync();   // client has a key: the server ignores it
        outcome.Status.ShouldBe(HandshakeStatus.Ok);
        outcome.Message.ShouldBe(HandshakeRunner.SingleServerMessage);
        outcome.Message!.ShouldContain("ignored");
    }

    [Fact]
    public async Task NoProjectKey_AgainstAMultiServer_IsRejected_WithTheRightMessage()
    {
        var outcome = await Start(() => FakeServer.Json(HttpStatusCode.OK, Ok), projectKey: null).Runner.RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Rejected);
        outcome.Message.ShouldBe(HandshakeRunner.MultiServerMessage);
        outcome.Message!.ShouldContain("ServerWithMultiProject(projectKey)");
    }

    [Fact]
    public async Task ServerModeMismatch409_GetsTheFriendlyMessageForTheClientsSide()
    {
        var withKey = await Start(() => Err(HttpStatusCode.Conflict, Auth2ErrorCodes.ServerModeMismatch)).Runner.RunAsync();
        withKey.Message!.ShouldContain("Remove ServerWithMultiProject");
        var withoutKey = await Start(() => Err(HttpStatusCode.Conflict, Auth2ErrorCodes.ServerModeMismatch), projectKey: null).Runner.RunAsync();
        withoutKey.Message!.ShouldContain("Call ServerWithMultiProject(projectKey)");
        withKey.Status.ShouldBe(HandshakeStatus.Rejected);
    }

    [Theory]
    [InlineData(404, HandshakeStatus.NotFound)]
    [InlineData(401, HandshakeStatus.Rejected)]
    [InlineData(403, HandshakeStatus.Rejected)]
    [InlineData(409, HandshakeStatus.Rejected)]
    [InlineData(500, HandshakeStatus.ServerUnavailable)]
    [InlineData(503, HandshakeStatus.ServerUnavailable)]
    [InlineData(429, HandshakeStatus.ServerUnavailable)]
    public async Task StatusCodes_MapToHandshakeStatus(int code, HandshakeStatus expected) =>
        (await Start(() => FakeServer.Json((HttpStatusCode)code, "{}")).Runner.RunAsync()).Status.ShouldBe(expected);

    [Fact]
    public async Task NetworkError_IsServerUnavailable()
    {
        var server = new FakeServer { Respond = (_, _) => throw new HttpRequestException("down") };
        var runner = TestHost.Build(server).GetRequiredService<HandshakeRunner>();
        (await runner.RunAsync()).Status.ShouldBe(HandshakeStatus.ServerUnavailable);
    }

    [Fact]
    public async Task SlowServer_TimesOut_AsServerUnavailable()
    {
        var server = new FakeServer { Respond = async (_, _) => { await Task.Delay(Timeout.Infinite, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)); return FakeServer.Token(); } };
        var runner = TestHost.Build(server, options: o => o.HandshakeTimeout = TimeSpan.FromMilliseconds(100)).GetRequiredService<HandshakeRunner>();
        (await runner.RunAsync()).Status.ShouldBe(HandshakeStatus.ServerUnavailable);
    }

    [Fact]
    public async Task WrongSecret_AtTheTokenEndpoint_IsRejected_AndTheMessageHasNoSecret()
    {
        var server = new FakeServer { Respond = (_, _) => Task.FromResult(FakeServer.Json(HttpStatusCode.BadRequest, """{"error":"invalid_client"}""")) };
        var outcome = await TestHost.Build(server).GetRequiredService<HandshakeRunner>().RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Rejected);
        outcome.Message!.ShouldNotContain(TestHost.Secret);
    }

    [Fact]
    public async Task GarbageAnswer_IsRejected()
    {
        var outcome = await Start(() => FakeServer.Json(HttpStatusCode.OK, "<html>")).Runner.RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Rejected);
    }
}

public class HandshakeHostedServiceTests
{
    private const string Ok = """{"applicationId":"0198a1b2-0000-7000-8000-000000000001","projectKey":"blog","applicationName":"Blog","serverMode":"MultiProject","issuer":"https://auth.test","settingsVersion":1,"cookieLifetime":null,"singleActiveSession":false}""";

    /// <summary>A server whose handshake answers come from a queue (the last one repeats).</summary>
    private static (IHostedService Service, Auth2ClientState State, FakeServer Server, FakeTimeProvider Time) Start(
        IEnumerable<Func<HttpResponseMessage>> answers, Action<Client.Options.Auth2ClientOptions>? options = null)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(answers);
        var server = new FakeServer
        {
            Respond = (r, _) =>
            {
                if (r.RequestUri!.AbsolutePath == "/connect/token") return Task.FromResult(FakeServer.Token());
                var next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                return Task.FromResult(next());
            },
        };
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sp = TestHost.Build(server, b => b.ServerWithMultiProject("blog"), time, options);
        return (sp.GetRequiredService<HandshakeHostedService>(), sp.GetRequiredService<Auth2ClientState>(), server, time);
    }

    private static HttpResponseMessage Up() => FakeServer.Json(HttpStatusCode.OK, Ok);
    private static HttpResponseMessage Down() => FakeServer.Json(HttpStatusCode.ServiceUnavailable, "{}");
    private static HttpResponseMessage NotFound() => FakeServer.Json(HttpStatusCode.NotFound, """{"code":"project_not_found"}""");
    private static HttpResponseMessage Rejected() => FakeServer.Json(HttpStatusCode.Forbidden, """{"code":"forbidden"}""");

    private static async Task Until(Func<bool> done, FakeTimeProvider time, TimeSpan step)
    {
        for (var i = 0; i < 200 && !done(); i++) { time.Advance(step); await Task.Delay(10); }
    }

    [Fact]
    public async Task Ok_SetsTheState_AndSyncServicesCanContinue()
    {
        var (service, state, _, _) = Start([Up]);
        var waiting = state.WaitUntilReadyAsync();
        waiting.IsCompleted.ShouldBeFalse();
        await service.StartAsync(default);
        state.Status.ShouldBe(HandshakeStatus.Ok);
        state.LastResponse!.ApplicationName.ShouldBe("Blog");
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task HandshakeOnStartupOff_DoesNothing()
    {
        var (service, state, server, _) = Start([Up], o => o.HandshakeOnStartup = false);
        await service.StartAsync(default);
        state.Status.ShouldBeNull();
        server.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unavailable_WithThrow_StopsTheApplication()
    {
        var (service, state, _, _) = Start([Down], o => o.OnServerUnavailable = StartupFailureBehavior.Throw);
        var ex = await Should.ThrowAsync<Auth2StartupException>(() => service.StartAsync(default));
        ex.Status.ShouldBe(HandshakeStatus.ServerUnavailable);
        state.Status.ShouldBe(HandshakeStatus.ServerUnavailable);
    }

    [Fact]
    public async Task Unavailable_WithContinue_Starts_ThenRetriesInTheBackgroundWithBackoff_UntilItWorks()
    {
        var (service, state, server, time) = Start([Down, Down, Down, Up], o => { o.OnServerUnavailable = StartupFailureBehavior.Continue; });
        await service.StartAsync(default);                          // the application starts
        state.Status.ShouldBe(HandshakeStatus.ServerUnavailable);
        server.CountTo(Auth2Routes.Handshake).ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(1)); await Task.Delay(50);
        server.CountTo(Auth2Routes.Handshake).ShouldBe(1);          // first wait is 2 s
        await Until(() => server.CountTo(Auth2Routes.Handshake) == 2, time, TimeSpan.FromSeconds(1));
        var afterSecond = time.GetUtcNow();
        await Until(() => server.CountTo(Auth2Routes.Handshake) == 3, time, TimeSpan.FromSeconds(1));
        (time.GetUtcNow() - afterSecond).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3));   // second wait is 4 s (it doubles)
        await Until(() => state.IsReady, time, TimeSpan.FromSeconds(1));
        state.Status.ShouldBe(HandshakeStatus.Ok);
        await service.StopAsync(default);
    }

    [Fact]
    public async Task Retries_StopAfterTheMaximum_AndTheApplicationKeepsRunning()
    {
        var (service, state, server, time) = Start([Down], o => o.HandshakeMaxRetries = 3);
        await service.StartAsync(default);
        await Until(() => server.CountTo(Auth2Routes.Handshake) >= 4, time, TimeSpan.FromSeconds(1));
        for (var i = 0; i < 60; i++) { time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(5); }
        server.CountTo(Auth2Routes.Handshake).ShouldBe(4);          // 1 + 3 retries, then nothing
        state.Status.ShouldBe(HandshakeStatus.ServerUnavailable);
        await service.StopAsync(default);
    }

    [Fact]
    public async Task TheDelay_NeverGrowsBeyondTheMaximum()
    {
        var (service, _, server, time) = Start([Down], o => { o.HandshakeMaxRetries = 6; o.HandshakeInitialDelay = TimeSpan.FromSeconds(2); o.HandshakeMaxDelay = TimeSpan.FromSeconds(5); });
        await service.StartAsync(default);
        var start = time.GetUtcNow();
        await Until(() => server.CountTo(Auth2Routes.Handshake) >= 7, time, TimeSpan.FromSeconds(1));
        // waits: 2 + 4 + 5 + 5 + 5 + 5 = 26 s
        (time.GetUtcNow() - start).ShouldBeLessThan(TimeSpan.FromSeconds(32));
        await service.StopAsync(default);
    }

    [Theory]
    [InlineData(StartupFailureBehavior.Throw)]
    public async Task NotFound_AndRejected_FollowOnApplicationNotFound_Throw(StartupFailureBehavior behavior)
    {
        foreach (var answer in new Func<HttpResponseMessage>[] { NotFound, Rejected })
        {
            var (service, state, _, _) = Start([answer], o => o.OnProjectNotFound = behavior);
            var ex = await Should.ThrowAsync<Auth2StartupException>(() => service.StartAsync(default));
            ex.Status.ShouldBeOneOf(HandshakeStatus.NotFound, HandshakeStatus.Rejected);
            state.IsReady.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task NotFound_WithContinue_LogsAndStarts_WithoutRetrying()
    {
        var (service, state, server, time) = Start([NotFound], o => o.OnProjectNotFound = StartupFailureBehavior.Continue);
        await service.StartAsync(default);
        state.Status.ShouldBe(HandshakeStatus.NotFound);
        for (var i = 0; i < 30; i++) { time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(5); }
        server.CountTo(Auth2Routes.Handshake).ShouldBe(1);
    }

    [Fact]
    public async Task AConfigurationErrorFoundDuringRetries_StopsTheRetries_ButDoesNotKillTheApplication()
    {
        var (service, state, server, time) = Start([Down, Rejected], o => o.OnServerUnavailable = StartupFailureBehavior.Continue);
        await service.StartAsync(default);
        await Until(() => state.Status == HandshakeStatus.Rejected, time, TimeSpan.FromSeconds(1));
        state.Status.ShouldBe(HandshakeStatus.Rejected);
        var calls = server.CountTo(Auth2Routes.Handshake);
        for (var i = 0; i < 30; i++) { time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(5); }
        server.CountTo(Auth2Routes.Handshake).ShouldBe(calls);
        await service.StopAsync(default);
    }

    [Fact]
    public async Task HealthCheck_IsHealthyOnlyWhenTheHandshakeWorked()
    {
        var (service, state, _, _) = Start([Up]);
        var check = new Auth2ClientHealthCheck(state);
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Degraded);   // not finished
        await service.StartAsync(default);
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
        state.Set(HandshakeStatus.Rejected, null, "bad");
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description!.ShouldContain("Rejected");
    }
}
