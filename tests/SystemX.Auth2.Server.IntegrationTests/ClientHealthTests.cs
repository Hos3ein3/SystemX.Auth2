using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The two-way health check: the Server asks the clients that gave a health address in their handshake.</summary>
public class ClientHealthTests : Phase6Base
{
    private const string Url = "http://client-a.test/auth2/health";

    /// <summary>Stands in for the client applications: answers by address, or fails like an unreachable host.</summary>
    public sealed class FakeClients : HttpMessageHandler
    {
        public ConcurrentDictionary<string, Func<HttpResponseMessage>> Answers { get; } = new();
        public ConcurrentBag<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Add(request.Method + " " + request.RequestUri);
            if (request.Headers.Authorization is not null || request.Headers.Contains("Cookie")) throw new InvalidOperationException("The Server must send no token and no cookie.");
            return Answers.TryGetValue(request.RequestUri!.ToString(), out var answer) ? Task.FromResult(answer()) : throw new HttpRequestException("no route to host");
        }

        public static HttpResponseMessage Json(HttpStatusCode code, string status) =>
            new(code) { Content = new StringContent($$"""{"status":"{{status}}","applicationName":"default","clientVersion":"1.0"}""", Encoding.UTF8, "application/json") };
    }

    protected override void Customize(IServiceCollection services)
    {
        services.AddSingleton<FakeClients>();
        services.AddHttpClient("auth2-client-health").ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<FakeClients>());
    }

    private FakeClients Clients => Factory.Services.GetRequiredService<FakeClients>();

    private async Task<HttpStatusCode> HandshakeAsync(string? healthUrl, string client = "appa")
    {
        var token = await ClientTokenAsync(client, "auth2.client.handshake");
        var response = await new Browser(Factory).PostJsonAsync(Auth2Routes.Handshake, new ClientHandshakeRequest(client, "default", "1.0", healthUrl), token);
        return response.StatusCode;
    }

    private async Task<IReadOnlyList<ClientHealthInfo>> HealthAsync(string projectKey = "appa")
    {
        await using var scope = NewScope();
        var project = (await FindProjectAsync(projectKey))!;
        return await scope.ServiceProvider.GetRequiredService<IClientHealthService>().GetProjectAsync(project.Id);
    }

    private async Task<int> CheckAllAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<IClientHealthService>().CheckAllAsync();
    }

    [Fact]
    public async Task AClientWithoutAHealthAddress_IsNeverCalled()
    {
        await PrepareAppsAsync();
        (await HandshakeAsync(null)).ShouldBe(HttpStatusCode.OK);
        (await CheckAllAsync()).ShouldBe(0);
        (await HealthAsync()).ShouldBeEmpty();
        Clients.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheHandshakeRegistersTheAddress_AndTheCheckKeepsTheAnswer()
    {
        await PrepareAppsAsync();
        (await HandshakeAsync(Url)).ShouldBe(HttpStatusCode.OK);

        var before = (await HealthAsync()).Single();
        before.ApplicationName.ShouldBe("default");
        before.State.ShouldBe(ClientHealthState.Unknown);   // registered, not asked yet
        before.CheckedAtUtc.ShouldBeNull();

        Clients.Answers[Url] = () => FakeClients.Json(HttpStatusCode.OK, "Healthy");
        (await CheckAllAsync()).ShouldBe(1);
        Clients.Asked.ShouldBe([$"GET {Url}"]);
        var after = (await HealthAsync()).Single();
        (after.State, after.Message).ShouldBe((ClientHealthState.Healthy, null));
        after.CheckedAtUtc.ShouldNotBeNull();
        after.LastHealthyAtUtc.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(200, "Degraded", ClientHealthState.Degraded)]
    [InlineData(503, "Unhealthy", ClientHealthState.Unhealthy)]
    [InlineData(200, "Unhealthy", ClientHealthState.Unhealthy)]
    [InlineData(500, "Healthy", ClientHealthState.Unhealthy)]
    [InlineData(404, "Healthy", ClientHealthState.Unhealthy)]
    public async Task TheStateFollowsTheAnswer(int code, string word, ClientHealthState expected)
    {
        await PrepareAppsAsync();
        await HandshakeAsync(Url);
        Clients.Answers[Url] = () => FakeClients.Json((HttpStatusCode)code, word);
        await CheckAllAsync();
        var info = (await HealthAsync()).Single();
        info.State.ShouldBe(expected);
        info.Message.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnUnreachableClient_IsUnreachable_AndTheLastHealthyTimeStays()
    {
        await PrepareAppsAsync();
        await HandshakeAsync(Url);
        Clients.Answers[Url] = () => FakeClients.Json(HttpStatusCode.OK, "Healthy");
        await CheckAllAsync();
        var healthyAt = (await HealthAsync()).Single().LastHealthyAtUtc;

        Clients.Answers.Clear();   // the host is gone
        await CheckAllAsync();
        var info = (await HealthAsync()).Single();
        info.State.ShouldBe(ClientHealthState.Unreachable);
        info.Message.ShouldBe("The client could not be reached.");
        info.LastHealthyAtUtc.ShouldBe(healthyAt);
    }

    [Fact]
    public async Task ABodyThatIsNotOurs_OnA200_IsHealthy_AndAHugeBodyIsNotRead()
    {
        await PrepareAppsAsync();
        await HandshakeAsync(Url);
        Clients.Answers[Url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        await CheckAllAsync();
        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Healthy);

        Clients.Answers[Url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 100_000)) };
        await CheckAllAsync();
        (await HealthAsync()).Single().State.ShouldBe(ClientHealthState.Healthy);
    }

    [Fact]
    public async Task ALaterOneWayHandshake_SwitchesTheCheckOff()
    {
        await PrepareAppsAsync();
        await HandshakeAsync(Url);
        (await HealthAsync()).Count.ShouldBe(1);
        await HandshakeAsync(null);
        (await HealthAsync()).ShouldBeEmpty();
        (await CheckAllAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("ftp://client-a.test/health")]
    [InlineData("http://user:secret@client-a.test/health")]
    [InlineData("http://client-a.test/health#fragment")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public async Task AnAddressThatIsNotAPlainHttpAddress_IsNotRegistered(string url)
    {
        await PrepareAppsAsync();
        (await HandshakeAsync(url)).ShouldBe(HttpStatusCode.OK);   // the handshake itself still works
        (await HealthAsync()).ShouldBeEmpty();
        (await CheckAllAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task EveryProject_SeesOnlyItsOwnApplications()
    {
        await PrepareAppsAsync();
        await HandshakeAsync(Url, "appa");
        await HandshakeAsync("http://client-b.test/auth2/health", "appb");
        (await HealthAsync("appa")).Select(h => h.ApplicationId).Count().ShouldBe(1);
        (await HealthAsync("appb")).Select(h => h.ApplicationId).Count().ShouldBe(1);
        (await HealthAsync("appa")).Single().ApplicationId.ShouldNotBe((await HealthAsync("appb")).Single().ApplicationId);
    }

    [Fact]
    public async Task TheDashboardOfTheProject_ShowsTheClientsNextToTheJobs()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=_global", new Dictionary<string, string> { ["Input.Login"] = "globaladmin", ["Input.Password"] = "Admin@12345!ChangeMe" })).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.OK);

        (await (await b.GetAsync("/admin/p/appa?ClientId=_global")).Content.ReadAsStringAsync()).ShouldNotContain("Clients:");   // no two-way client: no badge

        await HandshakeAsync(Url);
        Clients.Answers[Url] = () => FakeClients.Json(HttpStatusCode.OK, "Healthy");
        await CheckAllAsync();
        var healthy = await (await b.GetAsync("/admin/p/appa?ClientId=_global")).Content.ReadAsStringAsync();
        healthy.ShouldContain("Clients: Healthy");
        healthy.ShouldContain("default: Healthy");   // the details are in the tooltip

        Clients.Answers[Url] = () => FakeClients.Json(HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await CheckAllAsync();
        (await (await b.GetAsync("/admin/p/appa?ClientId=_global")).Content.ReadAsStringAsync()).ShouldContain("Clients: 1 of 1 not healthy");
    }
}
