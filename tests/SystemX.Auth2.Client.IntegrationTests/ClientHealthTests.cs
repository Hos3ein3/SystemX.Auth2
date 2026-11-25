using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Client.IntegrationTests;

/// <summary>The one-way and the two-way health check of the Client, against the real Server.</summary>
public class ClientHealthCheckTests : ClientAppTestBase
{
    private bool _databaseUp = true;

    private async Task StartTwoWayAsync(Action<Auth2HealthCheckOptions>? options = null)
    {
        await StartAppAsync(_ => { }, b => b.EnableHealthCheck(o =>
        {
            o.Mode = Auth2HealthCheckMode.TwoWay;
            o.PublicUrl = "http://client-app.test";
            options?.Invoke(o);
        }), s => s.AddHealthChecks().AddCheck("database", () => _databaseUp ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("down")), handshake: true);
        Server.Clients.Target = App.GetTestServer().CreateHandler();   // the Server's questions reach the application under test
        await WaitForRegistrationAsync();
    }

    private async Task WaitForRegistrationAsync()
    {
        for (var i = 0; i < 100 && (await HealthAsync()).Count == 0; i++) await Task.Delay(100);   // the handshake runs in the background
    }

    private async Task<IReadOnlyList<ClientHealthInfo>> HealthAsync()
    {
        await using var scope = Server.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IClientHealthService>().GetProjectAsync(ProjectId);
    }

    private async Task<int> CheckAsync()
    {
        await using var scope = Server.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IClientHealthService>().CheckAllAsync();
    }

    [Fact]
    public async Task TwoWay_TheClientAnswers_AndTheServerKeepsTheState()
    {
        await StartTwoWayAsync();

        var direct = await Http.GetAsync("/auth2/health");            // what the Server asks
        direct.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await direct.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("applicationName").GetString().ShouldBe("default");

        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Unknown);   // registered by the handshake, not asked yet
        (await CheckAsync()).ShouldBe(1);
        var info = (await HealthAsync()).Single();
        (info.ApplicationName, info.State).ShouldBe(("default", ClientHealthState.Healthy));
    }

    [Fact]
    public async Task TwoWay_WhenTheApplicationIsNotHealthy_TheServerSeesIt()
    {
        await StartTwoWayAsync();
        await CheckAsync();
        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Healthy);

        _databaseUp = false;                                           // any health check of the application counts, not only the auth2 one
        (await Http.GetAsync("/auth2/health")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await CheckAsync();
        var info = (await HealthAsync()).Single();
        info.State.ShouldBe(ClientHealthState.Unhealthy);
        info.LastHealthyAtUtc.ShouldNotBeNull();

        _databaseUp = true;
        await CheckAsync();
        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Healthy);
    }

    [Fact]
    public async Task TwoWay_AClientThatIsStopped_IsUnreachable()
    {
        await StartTwoWayAsync();
        await CheckAsync();
        Server.Clients.Target = new RefusingHandler();
        await CheckAsync();
        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Unreachable);
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("connection refused");
    }

    [Fact]
    public async Task TwoWay_TheAnswerHasNoDetails()
    {
        await StartTwoWayAsync();
        _databaseUp = false;
        var text = await (await Http.GetAsync("/auth2/health")).Content.ReadAsStringAsync();
        text.ShouldNotContain("down");        // the description of a failing check stays inside the application
        text.ShouldNotContain("database");
        text.ShouldNotContain("blog");        // and no client id
    }

    [Fact]
    public async Task TwoWay_ThePathCanBeChanged()
    {
        await StartTwoWayAsync(o => o.Path = "/ops/ready");
        (await Http.GetAsync("/ops/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Http.GetAsync("/auth2/health")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await CheckAsync();
        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Healthy);
    }

    [Fact]
    public async Task OneWay_IsTheDefault_NothingIsMapped_AndTheServerNeverCalls()
    {
        await StartAppAsync(_ => { }, b => b.EnableHealthCheck(), handshake: true);
        Server.Clients.Target = App.GetTestServer().CreateHandler();
        await Task.Delay(1500);   // the handshake has run
        (await Http.GetAsync("/auth2/health")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await HealthAsync()).ShouldBeEmpty();
        (await CheckAsync()).ShouldBe(0);
    }
}

public class HealthCheckOptionsTests
{
    private static string[] Errors(Auth2HealthCheckOptions o) => [.. (new Auth2HealthCheckOptionsValidator().Validate(null, o).Failures ?? [])];

    [Fact]
    public void OneWay_IsTheDefault_AndHasNoHealthUrl()
    {
        var o = new Auth2HealthCheckOptions();
        (o.Mode, o.Path, o.HealthUrl).ShouldBe((Auth2HealthCheckMode.OneWay, "/auth2/health", null));
        Errors(o).ShouldBeEmpty();
        new Auth2HealthCheckOptions { PublicUrl = "https://app.example.com" }.HealthUrl.ShouldBeNull();   // an address alone does not make it two-way
    }

    [Fact]
    public void TwoWay_BuildsTheHealthUrl_FromThePublicUrlAndThePath()
    {
        new Auth2HealthCheckOptions { Mode = Auth2HealthCheckMode.TwoWay, PublicUrl = "https://app.example.com" }.HealthUrl.ShouldBe("https://app.example.com/auth2/health");
        new Auth2HealthCheckOptions { Mode = Auth2HealthCheckMode.TwoWay, PublicUrl = "http://sample-mvc:5001/", Path = "/ops/ready" }.HealthUrl.ShouldBe("http://sample-mvc:5001/ops/ready");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("shop.example.com")]
    [InlineData("ftp://shop.example.com")]
    [InlineData("https://user:pw@shop.example.com")]
    [InlineData("https://shop.example.com/?a=1")]
    [InlineData("https://shop.example.com/#top")]
    public void TwoWay_NeedsAGoodPublicUrl(string? url) =>
        Errors(new Auth2HealthCheckOptions { Mode = Auth2HealthCheckMode.TwoWay, PublicUrl = url }).ShouldNotBeEmpty();

    [Theory]
    [InlineData("auth2/health")]
    [InlineData("")]
    [InlineData("/a?b=1")]
    [InlineData("/a#b")]
    public void ThePathMustStartWithASlash_AndHaveNoQuery(string path) =>
        Errors(new Auth2HealthCheckOptions { Path = path }).ShouldNotBeEmpty();

    [Fact]
    public void AGoodTwoWayConfiguration_HasNoError() =>
        Errors(new Auth2HealthCheckOptions { Mode = Auth2HealthCheckMode.TwoWay, PublicUrl = "https://shop.example.com", Path = "/ops/ready" }).ShouldBeEmpty();
}
