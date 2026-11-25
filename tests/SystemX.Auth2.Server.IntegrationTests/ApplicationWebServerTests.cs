using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The web server address and the type of an application: set by an admin or announced in the handshake (when the project allows it), and used by the health check.</summary>
public class ApplicationWebServerTests : Phase6Base
{
    protected override void Customize(IServiceCollection services)
    {
        services.AddSingleton<ClientHealthTests.FakeClients>();
        services.AddHttpClient("auth2-client-health").ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<ClientHealthTests.FakeClients>());
    }

    private async Task<(HttpStatusCode Status, ClientHandshakeResponse? Body)> HandshakeAsync(string? webServerUrl, ApplicationType? type, string? healthPath = null, int? port = null)
    {
        var token = await ClientTokenAsync("appa", "auth2.client.handshake");
        var response = await new Browser(Factory).PostJsonAsync(Auth2Routes.Handshake,
            new ClientHandshakeRequest("appa", "default", "1.0", null, port, port is null ? null : "http", healthPath, webServerUrl, type), token);
        return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ClientHandshakeResponse>() : null);
    }

    private async Task<(string? Url, ApplicationType Kind)> StoredAsync()
    {
        await using var scope = NewScope();
        var app = await scope.ServiceProvider.GetRequiredService<Server.Persistence.Auth2DbContext>().Set<Auth2Application>().AsNoTracking().SingleAsync(a => a.ClientId == "appa");
        return (app.WebServerUrl, app.Kind);
    }

    [Fact]
    public async Task ANewApplication_HasNoAddressAndTheTypeOther()
    {
        await PrepareAppsAsync();
        (await StoredAsync()).ShouldBe((null, ApplicationType.Other));
    }

    [Fact]
    public async Task TheClientMaySetTheAddressAndTheType_WhenTheProjectAllowsIt()
    {
        await PrepareAppsAsync();
        var (status, body) = await HandshakeAsync("https://shop.example.com/", ApplicationType.Web);
        status.ShouldBe(HttpStatusCode.OK);
        body!.WebServerUrl.ShouldBe("https://shop.example.com");   // normalized: no trailing slash
        body.ApplicationType.ShouldBe(ApplicationType.Web);
        body.WebServerLocked.ShouldBeFalse();
        (await StoredAsync()).ShouldBe(("https://shop.example.com", ApplicationType.Web));
    }

    [Fact]
    public async Task ACLientThatSendsNothing_KeepsWhatIsStored()
    {
        await PrepareAppsAsync();
        await HandshakeAsync("https://shop.example.com", ApplicationType.Api);
        var (_, body) = await HandshakeAsync(null, null);
        body!.WebServerUrl.ShouldBe("https://shop.example.com");
        (await StoredAsync()).ShouldBe(("https://shop.example.com", ApplicationType.Api));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://shop.example.com")]
    [InlineData("https://user:pw@shop.example.com")]
    [InlineData("https://shop.example.com/?a=1")]
    public async Task ABadAddress_IsIgnored_AndTheHandshakeStillWorks(string url)
    {
        await PrepareAppsAsync();
        var (status, body) = await HandshakeAsync(url, ApplicationType.Service);
        status.ShouldBe(HttpStatusCode.OK);
        body!.WebServerUrl.ShouldBeNull();
        (await StoredAsync()).ShouldBe((null, ApplicationType.Service));   // the type is still taken
    }

    [Fact]
    public async Task WhenTheApplicationForbidsIt_TheClientCannotChangeAnything()
    {
        await PrepareAppsAsync();
        await ForbidTheClientAsync();
        var (status, body) = await HandshakeAsync("https://evil.example.com", ApplicationType.Web);
        status.ShouldBe(HttpStatusCode.OK);
        body!.WebServerLocked.ShouldBeTrue();
        body.WebServerUrl.ShouldBeNull();
        (await StoredAsync()).ShouldBe((null, ApplicationType.Other));
    }

    [Fact]
    public async Task WhenLocked_AnAdminsAddressStays_AndTheClientIsTold()
    {
        await PrepareAppsAsync();
        await ForbidTheClientAsync();
        await SetByAdminAsync("https://admin-set.example.com", ApplicationType.Api);
        var (_, body) = await HandshakeAsync("https://client-set.example.com", ApplicationType.Web);
        body!.WebServerLocked.ShouldBeTrue();
        body.WebServerUrl.ShouldBe("https://admin-set.example.com");
        body.ApplicationType.ShouldBe(ApplicationType.Api);
        (await StoredAsync()).ShouldBe(("https://admin-set.example.com", ApplicationType.Api));
    }

    [Fact]
    public async Task AnAdminCanSetAndClearTheAddress_AndABadOneIsRefused()
    {
        await PrepareAppsAsync();
        await SetByAdminAsync("https://a.example.com/base/", ApplicationType.Service);
        (await StoredAsync()).ShouldBe(("https://a.example.com/base", ApplicationType.Service));
        await SetByAdminAsync("", null);   // empty clears; null type leaves it
        (await StoredAsync()).ShouldBe((null, ApplicationType.Service));

        await using var scope = NewScope();
        var project = (await FindProjectAsync("appa"))!;
        var apps = scope.ServiceProvider.GetRequiredService<IAdminApplicationService>();
        var app = (await apps.ListAsync(project.Id)).Single(a => a.ClientId == "appa");
        var (refused, _) = await apps.UpdateAsync(project.Id, app.Id, new UpdateApplicationRequest(app.Name, true, WebServerUrl: "javascript:alert(1)"), null);
        refused.Succeeded.ShouldBeFalse();
        (await StoredAsync()).Url.ShouldBeNull();
    }

    /// <summary>Turns off <c>AllowClientToSetWebServerUrl</c> of the application (a setting of the application, not of the project).</summary>
    private async Task ForbidTheClientAsync()
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Server.Persistence.Auth2DbContext>();
        var app = await db.Set<Auth2Application>().SingleAsync(a => a.ClientId == "appa");
        var json = System.Text.Json.JsonSerializer.Serialize(app.ClientSettings);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Server.Domain.Settings.ClientApplicationSettings>(json)!;
        copy.AllowClientToSetWebServerUrl = false;
        app.ClientSettings = copy;
        await db.SaveChangesAsync();
    }

    private async Task SetByAdminAsync(string? url, ApplicationType? type)
    {
        await using var scope = NewScope();
        var project = (await FindProjectAsync("appa"))!;
        var apps = scope.ServiceProvider.GetRequiredService<IAdminApplicationService>();
        var app = (await apps.ListAsync(project.Id)).Single(a => a.ClientId == "appa");
        var (result, _) = await apps.UpdateAsync(project.Id, app.Id, new UpdateApplicationRequest(app.Name, true, ApplicationType: type, WebServerUrl: url), null);
        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://shop.example.com", null, "https://shop.example.com/auth2/health")]
    [InlineData("https://shop.example.com/base/", "/ops/ready", "https://shop.example.com/base/ops/ready")]
    [InlineData("http://sample-mvc:5001", "/auth2/health", "http://sample-mvc:5001/auth2/health")]
    [InlineData("https://shop.example.com", "ops", null)]
    [InlineData("https://shop.example.com", "/a?b=1", null)]
    [InlineData("nonsense", null, null)]
    [InlineData(null, null, null)]
    public void TheHealthAddress_IsTheWebServerAddressPlusThePath(string? web, string? path, string? expected)
        => Server.Infrastructure.Operations.ClientHealthAddress.FromBase(web, path).ShouldBe(expected);

    [Fact]
    public async Task TheHealthCheckCallsTheWebServerAddress_NotTheHostOfTheHandshake()
    {
        await PrepareAppsAsync();
        await SetByAdminAsync("https://stable.example.com", ApplicationType.Web);
        // a client that only says its port and path (two-way, no PublicUrl): the address an admin set wins over the host of the connection
        var (status, _) = await HandshakeAsync(null, null, "/auth2/health", 5001);
        status.ShouldBe(HttpStatusCode.OK);
        var fake = Factory.Services.GetRequiredService<ClientHealthTests.FakeClients>();
        fake.Answers["https://stable.example.com/auth2/health"] = () => ClientHealthTests.FakeClients.Json(HttpStatusCode.OK, "Healthy");
        await using var scope = NewScope();
        (await scope.ServiceProvider.GetRequiredService<IClientHealthService>().CheckAllAsync()).ShouldBe(1);
        fake.Asked.ShouldBe(["GET https://stable.example.com/auth2/health"]);
    }

    [Fact]
    public async Task AOneWayClient_GetsNoHealthCheck_EvenWhenTheAddressIsKnown()
    {
        await PrepareAppsAsync();
        await HandshakeAsync("https://shop.example.com", ApplicationType.Web);   // no health path, port or url: one-way
        await using var scope = NewScope();
        var project = (await FindProjectAsync("appa"))!;
        (await scope.ServiceProvider.GetRequiredService<IClientHealthService>().GetProjectAsync(project.Id)).ShouldBeEmpty();
    }
}
