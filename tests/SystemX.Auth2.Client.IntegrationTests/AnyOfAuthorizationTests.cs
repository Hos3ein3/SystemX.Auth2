using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SystemX.Auth2.Client.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Client.IntegrationTests;

/// <summary>"Any of" rules (roles, permissions, groups, scopes together) against the real Server.</summary>
public class AnyOfAuthorizationTests : ClientAppTestBase
{
    private static void Map(IEndpointRouteBuilder e)
    {
        e.MapGet("/role-or-perm", () => "ok").WithRoleOrPermission(["Admin"], ["orders.write"]);
        e.MapGet("/role-or-group", () => "ok").WithRoleOrGroup(["Admin"], ["finance"]);
        e.MapGet("/perm-or-group", () => "ok").WithPermissionOrGroup(["orders.write"], ["finance"]);
        e.MapGet("/scope-or-role", () => "ok").WithScopeOrRole(["auth2.groups.read"], ["Admin"]);
        e.MapGet("/any-of", () => "ok").WithAnyOf(roles: ["Admin"], permissions: ["orders.write"], groups: ["finance"], scopes: ["auth2.groups.read"]);
        // the attribute object as endpoint metadata (an attribute on a lambda would be found by the assembly scan of other tests)
        e.MapGet("/attr", () => "ok").RequireAuthorization(new Auth2RoleOrPermissionAttribute { Roles = ["Admin"], Permissions = ["orders.write"] });
        e.MapGet("/chained", () => "ok").WithRoleOrPermission(["Admin"], ["orders.write"]).WithGroup("finance");   // AND of two rules
        e.MapGet("/role-perm-only", () => "ok").WithRoleOrPermission(["Admin"], ["orders.write"]).WithOnlyRolePermission();
    }

    private async Task StartWithUsersAsync()
    {
        await StartAppAsync(Map);
        await CreateUserAsync("sara");
        await AccessAsync("sara", async (a, g, u) => await a.CreatePermissionAsync(new PermissionDefinition("orders.write")));
    }

    [Fact]
    public async Task ARoleOrAPermission_EitherOneIsEnough_NeitherIsRefused()
    {
        await StartWithUsersAsync();
        (await GetAsync("/role-or-perm")).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync("/role-or-perm", await LoginAsync())).ShouldBe(HttpStatusCode.Forbidden);

        await AccessAsync("sara", async (a, g, u) => await a.GrantPermissionAsync(u, "orders.write"));
        var byPermission = await LoginAsync();
        (await GetAsync("/role-or-perm", byPermission)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/attr", byPermission)).ShouldBe(HttpStatusCode.OK);        // the attribute is the same rule

        await CreateUserAsync("dan");
        await AccessAsync("dan", async (a, g, u) =>
        {
            await a.CreateRoleAsync("Admin");
            await a.AssignRoleAsync(u, "Admin");
        });
        var byRole = await LoginAsync("dan");
        (await GetAsync("/role-or-perm", byRole)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/attr", byRole)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/role-or-group", byRole)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/perm-or-group", byRole)).ShouldBe(HttpStatusCode.Forbidden);   // the role does not give the permission here
    }

    [Fact]
    public async Task AGroup_IsEnoughForTheGroupVariants()
    {
        await StartWithUsersAsync();
        await AccessAsync("sara", async (a, g, u) =>
        {
            await g.CreateAsync("finance");
            await g.AddUserAsync("finance", u);
        });
        var token = await LoginAsync();
        (await GetAsync("/role-or-group", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/perm-or-group", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/any-of", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/role-or-perm", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AScopeOfTheToken_IsEnough_ForAProgram_WhileAPersonNeedsTheRole()
    {
        await StartWithUsersAsync();
        // a program (client token) has the scope and no person
        (await GetAsync("/scope-or-role", await MachineTokenAsync("auth2.groups.read"))).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/any-of", await MachineTokenAsync("auth2.groups.read"))).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/scope-or-role", await MachineTokenAsync("auth2.permissions.sync"))).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/role-or-perm", await MachineTokenAsync("auth2.groups.read"))).ShouldBe(HttpStatusCode.Forbidden);   // a program has no roles or permissions

        (await GetAsync("/scope-or-role", await LoginAsync())).ShouldBe(HttpStatusCode.Forbidden);   // a person without the scope or the role
    }

    [Fact]
    public async Task ChainedWithAnotherRule_BothMustPass()
    {
        await StartWithUsersAsync();
        await AccessAsync("sara", async (a, g, u) => await a.GrantPermissionAsync(u, "orders.write"));
        (await GetAsync("/chained", await LoginAsync())).ShouldBe(HttpStatusCode.Forbidden);   // the any-of passes, the group is missing

        await AccessAsync("sara", async (a, g, u) =>
        {
            await g.CreateAsync("finance");
            await g.AddUserAsync("finance", u);
        });
        (await GetAsync("/chained", await LoginAsync())).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ThePermissionSourceOfTheEndpoint_AppliesToThePermissionPart()
    {
        await StartWithUsersAsync();
        await AccessAsync("sara", async (a, g, u) => await a.GrantPermissionAsync(u, "orders.write"));   // a direct grant
        (await GetAsync("/role-perm-only", await LoginAsync())).ShouldBe(HttpStatusCode.Forbidden);

        await CreateUserAsync("dan");
        await AccessAsync("dan", async (a, g, u) =>
        {
            await a.CreateRoleAsync("Editor");
            await a.AddRolePermissionAsync("Editor", "orders.write");
            await a.AssignRoleAsync(u, "Editor");   // the permission comes from a role
        });
        (await GetAsync("/role-perm-only", await LoginAsync("dan"))).ShouldBe(HttpStatusCode.OK);
    }
}

/// <summary>An API that expects the audience of a scope resource accepts exactly the tokens that have the scope.</summary>
public class ScopeResourceAudienceTests : ClientAppTestBase
{
    private const string Scope = "orders.api";

    private async Task StartAsync()
    {
        await StartAppAsync(e => e.MapGet("/orders", () => "ok").WithAuth2(), api: o => o.Audience = "orders-api");   // this API is the resource "orders-api"
        await UpdateSettingsAsync(s => s.AllowedScopes.Add(Scope));
        await using (var scope = ServerScope())
            (await scope.ServiceProvider.GetRequiredService<IAdminScopeService>().CreateAsync(new SystemX.Auth2.Contracts.Admin.CreateScopeRequest(Scope, "Orders API", null, ["orders-api"]), true)).Result.Succeeded.ShouldBeTrue();
        await CreateUserAsync();
    }

    private async Task<string> TokenAsync(params string[] scopes)
    {
        var login = await ApiClient.GetRequiredService<IAuth2ApiClientService>().LoginAsync(new LoginRequest("sara", Password, scopes));
        login.IsSuccess.ShouldBeTrue(login.Error?.Code);
        return login.Value!.AccessToken!;
    }

    [Fact]
    public async Task OnlyTokensWithTheScope_HaveTheAudienceOfTheApi()
    {
        await StartAsync();
        (await GetAsync("/orders", await TokenAsync("openid", "auth2.me", Scope))).ShouldBe(HttpStatusCode.OK);        // aud = blog, orders-api
        (await GetAsync("/orders", await TokenAsync("openid", "auth2.me"))).ShouldBe(HttpStatusCode.Unauthorized);       // aud = blog only: not for this API
        (await GetAsync("/orders")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AMachineTokenWithTheScope_IsAcceptedToo()
    {
        await StartAsync();
        (await GetAsync("/orders", await MachineTokenAsync(Scope))).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/orders", await MachineTokenAsync("auth2.client.handshake"))).ShouldBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>A background job (generic host, no web server) that calls an API as a program: the configuration of the Worker sample.</summary>
public class WorkerJobTests : ClientAppTestBase
{
    private const string WorkerId = "worker-sample";

    private async Task<(ServiceProvider Worker, HttpClient Api)> StartAsync()
    {
        // The API: an application with machine-only and scope rules.
        await StartAppAsync(e =>
        {
            e.MapGet("/api/machine-only", () => "machine").WithMachineOnly();
            e.MapGet("/api/jobs", () => "jobs").WithMachineOnly().WithScope("jobs.run");
        });

        // The scope "jobs.run" names the API as its resource, so the worker's token is accepted by the API (whose audience is its client id "blog").
        await UpdateSettingsAsync(s => s.AllowedScopes.Add("jobs.run"));
        string secret;
        await using (var scope = ServerScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAdminScopeService>().CreateAsync(new SystemX.Auth2.Contracts.Admin.CreateScopeRequest("jobs.run", null, null, [ClientId]), true);
            var created = await scope.ServiceProvider.GetRequiredService<IApplicationService>().CreateAsync(ProjectId,
                new ApplicationSpec("WorkerSample", WorkerId, AllowedScopes: ["auth2.client.handshake", "auth2.permissions.sync", "jobs.run"]));
            secret = created.ClientSecret;
        }

        // The job, as the sample configures it.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuth2Client("http://localhost", o => { o.ApplicationName = "WorkerSample"; o.ClientId = WorkerId; o.ClientSecret = secret; o.HandshakeOnStartup = false; })
            .ServerWithMultiProject("blog")
            .EnableMachineToMachine(o => o.Scopes = ["jobs.run"])
            .EnableResilience();
        foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Server.Server.CreateHandler());
        services.AddHttpClient("api", c => c.BaseAddress = new Uri("http://api.test/")).ConfigurePrimaryHttpMessageHandler(() => App.GetTestServer().CreateHandler())
            .AddAuth2MachineToken("jobs.run");
        services.AddHttpClient("api-without-the-scope", c => c.BaseAddress = new Uri("http://api.test/")).ConfigurePrimaryHttpMessageHandler(() => App.GetTestServer().CreateHandler())
            .AddAuth2MachineToken("auth2.permissions.sync");
        var worker = services.BuildServiceProvider();
        return (worker, worker.GetRequiredService<IHttpClientFactory>().CreateClient("api"));
    }

    [Fact]
    public async Task TheJob_CallsAnApiWithAMachineToken_AndTheTokenIsCached()
    {
        var (worker, api) = await StartAsync();
        await using var _ = worker;

        (await api.GetAsync("/api/machine-only")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetAsync("/api/jobs")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = worker.GetRequiredService<SystemX.Auth2.Client.Machine.IAuth2MachineTokenProvider>();
        var token = await tokens.GetTokenAsync(["jobs.run"]);
        new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).Audiences.ShouldBe([WorkerId, ClientId]);
        (await tokens.GetTokenAsync(["jobs.run"])).ShouldBe(token);   // one token for many calls
    }

    [Fact]
    public async Task ATokenWithoutTheScope_IsNotForThatApi()
    {
        var (worker, _) = await StartAsync();
        await using var _w = worker;
        var other = worker.GetRequiredService<IHttpClientFactory>().CreateClient("api-without-the-scope");
        (await other.GetAsync("/api/machine-only")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // audience: only the worker, not the API
    }

    [Fact]
    public async Task TheJob_CanAskTheAuth2Server_AsAProgram_ForWhatItsScopesAllow()
    {
        var (worker, _) = await StartAsync();
        await using var _w = worker;
        var auth2 = worker.GetRequiredService<IAuth2ApiClientService>();
        (await auth2.GetPermissionsAsync()).IsSuccess.ShouldBeTrue();                  // auth2.permissions.sync is allowed
        (await auth2.GetRolesAsync()).Error!.Code.ShouldNotBeNullOrEmpty();            // auth2.roles.sync is not: a result, not an exception
    }

    [Fact]
    public async Task AWrongSecret_IsATransientFreeError_NotAHang()
    {
        var (worker, _) = await StartAsync();
        await using var _w = worker;
        var wrong = new ServiceCollection();
        wrong.AddLogging();
        wrong.AddAuth2Client("http://localhost", o => { o.ApplicationName = "WorkerSample"; o.ClientId = WorkerId; o.ClientSecret = "wrong"; o.HandshakeOnStartup = false; }).ServerWithMultiProject("blog");
        foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
            wrong.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Server.Server.CreateHandler());
        await using var bad = wrong.BuildServiceProvider();
        var ex = await Should.ThrowAsync<Auth2Exception>(() => bad.GetRequiredService<SystemX.Auth2.Client.Machine.IAuth2MachineTokenProvider>().GetTokenAsync(["jobs.run"]));
        ex.IsTransient.ShouldBeFalse();   // retrying will not help: the secret is wrong
    }
}
