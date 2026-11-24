using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.HealthChecks;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Client.IntegrationTests;

public class MultiServerHandshakeTests : ClientTestBase
{
    [Fact]
    public async Task Handshake_Ok_FillsTheState_WithModeIssuerAndCookieLifetime()
    {
        await UpdateSettingsAsync(s => { s.Sessions.CookieLifetimeMinutes = 90; s.Sessions.SingleActiveSession = true; });
        await using var client = BuildClient("blog");
        var service = client.GetRequiredService<HandshakeHostedService>();
        await service.StartAsync(default);

        var state = client.GetRequiredService<Auth2ClientState>();
        state.Status.ShouldBe(HandshakeStatus.Ok);
        state.LastResponse!.ServerMode.ShouldBe(ServerMode.MultiProject);
        state.LastResponse.ProjectKey.ShouldBe("blog");
        state.LastResponse.CookieLifetime.ShouldBe(TimeSpan.FromMinutes(90));
        state.LastResponse.SingleActiveSession.ShouldBeTrue();
        state.LastResponse.ProjectId.ShouldBe(ProjectId);
        (await new Auth2ClientHealthCheck(state).CheckHealthAsync(new(), default)).Status.ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
    }

    [Fact]
    public async Task Handshake_ApplicationDeletedOnTheServer_IsRejected_AndThrowsByDefault()
    {
        await using var client = BuildClient("blog");
        await client.GetRequiredService<IAuth2ApiClientService>().HandshakeAsync();   // gets and caches the machine token while the application exists
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            // an application is deleted through the OpenIddict manager: its tokens go with it, so the cached token and the client id are both finished
            var apps = scope.ServiceProvider.GetRequiredService<Server.Application.IApplicationService>();
            await apps.DeleteAsync((await apps.ListAsync(ProjectId)).Single().Id);
        }
        var result = await client.GetRequiredService<IAuth2ApiClientService>().HandshakeAsync();
        result.IsSuccess.ShouldBeFalse();
        client.GetRequiredService<Auth2ClientState>().Status.ShouldBe(HandshakeStatus.Rejected);

        var ex = await Should.ThrowAsync<Auth2StartupException>(() => client.GetRequiredService<HandshakeHostedService>().StartAsync(default));
        ex.Status.ShouldBe(HandshakeStatus.Rejected);
    }

    [Fact]
    public async Task Handshake_WrongApplicationName_IsRejected_WithTheMismatchCode()
    {
        await using var client = BuildClient("blog", o => o.ApplicationName = "Not The Default");
        var result = await client.GetRequiredService<IAuth2ApiClientService>().HandshakeAsync();
        result.Error!.Code.ShouldBe(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
        client.GetRequiredService<Auth2ClientState>().Status.ShouldBe(HandshakeStatus.Rejected);
    }

    [Fact]
    public async Task Handshake_ProjectKeyOfAnotherApplication_IsRejected()
    {
        await using (var scope = Server.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Server.Application.IProjectService>().CreateWithApplicationAsync("shop", "Shop", null, null);
        await using var client = BuildClient("shop");   // the secret is Blog's
        (await client.GetRequiredService<IAuth2ApiClientService>().HandshakeAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
    }

    [Fact]
    public async Task Handshake_WrongSecret_IsRejected_WithoutLeakingIt()
    {
        await using var client = BuildClient("blog", o => o.ClientSecret = "definitely-wrong-secret");
        var outcome = await client.GetRequiredService<HandshakeRunner>().RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Rejected);
        outcome.Message!.ShouldNotContain("definitely-wrong-secret");
    }

    [Fact]
    public async Task ServerMode_ClientWithoutProjectKey_AgainstAMultiServer_IsRejected_WithAClearMessage()
    {
        await using var client = BuildClient(projectKey: null);
        var outcome = await client.GetRequiredService<HandshakeRunner>().RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Rejected);
        outcome.Message.ShouldBe(HandshakeRunner.MultiServerMessage);
    }

    [Fact]
    public async Task ServerDown_Continue_StartsAnyway_Throw_StopsTheApplication()
    {
        await using var down = BuildClient("blog", o => o.OnServerUnavailable = StartupFailureBehavior.Continue, handler: new DownHandler());
        var service = down.GetRequiredService<HandshakeHostedService>();
        await service.StartAsync(default);
        down.GetRequiredService<Auth2ClientState>().Status.ShouldBe(HandshakeStatus.ServerUnavailable);
        await service.StopAsync(default);

        await using var strict = BuildClient("blog", o => o.OnServerUnavailable = StartupFailureBehavior.Throw, handler: new DownHandler());
        (await Should.ThrowAsync<Auth2StartupException>(() => strict.GetRequiredService<HandshakeHostedService>().StartAsync(default))).Status.ShouldBe(HandshakeStatus.ServerUnavailable);
    }

    [Fact]
    public async Task TheHostedService_RunsInARealHost()
    {
        using var host = Host.CreateDefaultBuilder().ConfigureServices(s =>
        {
            s.AddAuth2Client("http://localhost", o => { o.ApplicationName = "default"; o.ClientId = "blog"; o.ClientSecret = ClientSecret; }).ServerWithMultiProject("blog").EnableHealthCheck();
            foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
                s.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Server.Server.CreateHandler());
        }).Build();
        await host.StartAsync();
        host.Services.GetRequiredService<Auth2ClientState>().IsReady.ShouldBeTrue();
        await host.StopAsync();
    }
}

public class SingleServerHandshakeTests : ClientTestBase
{
    protected override bool Single => true;

    [Fact]
    public async Task ClientWithoutProjectKey_AgainstASingleServer_Works()
    {
        await using var client = BuildClient(projectKey: null);
        await client.GetRequiredService<HandshakeHostedService>().StartAsync(default);
        var state = client.GetRequiredService<Auth2ClientState>();
        state.Status.ShouldBe(HandshakeStatus.Ok);
        state.LastResponse!.ServerMode.ShouldBe(ServerMode.SingleProject);
        state.LastResponse.ProjectKey.ShouldBeNull();
    }

    [Fact]
    public async Task ClientWithAProjectKey_AgainstASingleServer_Works_ButIsToldThatTheKeyIsIgnored()
    {
        await using var client = BuildClient("default");
        var outcome = await client.GetRequiredService<HandshakeRunner>().RunAsync();
        outcome.Status.ShouldBe(HandshakeStatus.Ok);
        outcome.Message.ShouldBe(HandshakeRunner.SingleServerMessage);   // a warning, not an error
        outcome.Response!.ProjectKey.ShouldBeNull();
    }
}

public class ClientServicesAgainstServerTests : ClientTestBase
{
    private const string Password = "Pass-12345!x";

    private async Task<ServiceProvider> StartAsync()
    {
        await UpdateSettingsAsync(s =>
        {
            s.App.AllowSelfUiLogin = true;
            s.Methods.AllowedRegisterMethods.Add(LoginMethod.EmailPassword);
        });
        return BuildClient("blog", configure: b => b.EnableResilience());
    }

    [Fact]
    public async Task Register_Login_Me_Refresh_ChangePassword_Logout_WorkEndToEnd()
    {
        await using var client = await StartAsync();
        var api = client.GetRequiredService<IAuth2ApiClientService>();

        var registered = await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", null, Password));
        registered.IsSuccess.ShouldBeTrue(registered.Error?.Code);
        registered.Value!.UserWasCreated.ShouldBeTrue();
        (await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", null, "Different-Pass-1!"))).Value!.UserWasCreated.ShouldBeFalse();   // idempotent

        var login = await api.LoginAsync(new LoginRequest("sara", Password));
        login.IsSuccess.ShouldBeTrue(login.Error?.Code);
        var tokens = login.Value!;
        tokens.AccessToken.ShouldNotBeNullOrEmpty();
        tokens.TwoFactorRequired.ShouldBeFalse();

        var me = await api.GetMeAsync(tokens.AccessToken!);
        me.Value!.UserName.ShouldBe("sara");

        var refreshed = await api.RefreshAsync(tokens.RefreshToken!);
        refreshed.IsSuccess.ShouldBeTrue(refreshed.Error?.Code);

        (await api.ChangePasswordAsync(refreshed.Value!.AccessToken!, new ChangePasswordRequest(Password, "New-Pass-98765!y"))).IsSuccess.ShouldBeTrue();
        (await api.LoginAsync(new LoginRequest("sara", Password))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        var again = await api.LoginAsync(new LoginRequest("sara", "New-Pass-98765!y"));
        (await api.LogoutAsync(again.Value!.RefreshToken!)).IsSuccess.ShouldBeTrue();
        (await api.RefreshAsync(again.Value.RefreshToken!)).IsSuccess.ShouldBeFalse();   // revoked
        // Using an old refresh token again is reuse: the server ends the whole chain (that is the server's rule, see authentication-flows.md).
        (await api.RefreshAsync(refreshed.Value!.RefreshToken!)).IsSuccess.ShouldBeTrue();
        (await api.RefreshAsync(refreshed.Value.RefreshToken!)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task WrongPassword_AndUnknownUser_GiveTheSameResult()
    {
        await using var client = await StartAsync();
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", null, Password));
        var wrong = await api.LoginAsync(new LoginRequest("sara", "nope-nope-1A!"));
        var unknown = await api.LoginAsync(new LoginRequest("nobody", "nope-nope-1A!"));
        wrong.Error!.Code.ShouldBe(unknown.Error!.Code);
    }

    [Fact]
    public async Task PermissionAndRoleSync_IsIdempotent_AndPullReadsTheCatalogs()
    {
        await using var client = await StartAsync();
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        var request = new PermissionSyncRequest([new PermissionDefinition("orders.read", "Read orders"), new PermissionDefinition("orders.write")]);

        var first = await api.SyncPermissionsAsync(request);
        first.Value!.Added.ShouldBe(2);
        var second = await api.SyncPermissionsAsync(request);
        (second.Value!.Added, second.Value.Unchanged).ShouldBe((0, 2));

        var roles = await api.SyncRolesAsync(new RoleSyncRequest([new RoleDefinition("Editor", null, "Edits", ["orders.read"])]));
        roles.Value!.Added.ShouldBe(1);

        (await api.GetPermissionsAsync()).Value!.Select(p => p.Key).ShouldBe(["orders.read", "orders.write"]);
        (await api.GetRolesAsync()).Value!.Single().Permissions.ShouldBe(["orders.read"]);
        (await api.GetGroupsAsync()).Value!.ShouldBeEmpty();

        (await api.SyncPermissionsAsync(new PermissionSyncRequest([new PermissionDefinition("bad key")]))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidPermissionKey);
    }

    [Fact]
    public async Task SyncNotAllowed_IsAResult_NotAnException()
    {
        await using var client = await StartAsync();
        await UpdateSettingsAsync(s => s.Clients.PermissionSyncAllowed = false);
        (await client.GetRequiredService<IAuth2ApiClientService>().SyncPermissionsAsync(new PermissionSyncRequest([new PermissionDefinition("a.b")])))
            .Error!.Code.ShouldBe(Auth2ErrorCodes.SyncNotAllowed);
    }

    [Fact]
    public async Task MachineTokenIsCached_AcrossManyCalls()
    {
        await using var client = await StartAsync();
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        for (var i = 0; i < 5; i++) (await api.GetPermissionsAsync()).IsSuccess.ShouldBeTrue();
        // five reads, one token: all five worked and the token endpoint is not rate limited
        var token = await client.GetRequiredService<Machine.IAuth2MachineTokenProvider>().GetTokenAsync([Auth2Scopes.PermissionsSync]);
        (await client.GetRequiredService<Machine.IAuth2MachineTokenProvider>().GetTokenAsync([Auth2Scopes.PermissionsSync])).ShouldBe(token);
    }
}
