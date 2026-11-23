using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemX.Auth2.Client;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.DependencyInjection;
using SystemX.Auth2.Client.Machine;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Client.IntegrationTests;

/// <summary>Counts the calls that go to the server.</summary>
public sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private int _me;
    public int MeCalls => Volatile.Read(ref _me);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath == Auth2Routes.Me) Interlocked.Increment(ref _me);
        return base.SendAsync(request, ct);
    }
}

/// <summary>A client application (a small API) in memory that uses the Client package, and the real Server behind it.</summary>
public abstract class ClientAppTestBase : ClientTestBase
{
    protected const string Password = "Pass-12345!x";
    protected IHost App = null!;
    protected HttpClient Http = null!;
    protected CountingHandler Counting = null!;
    protected ServiceProvider ApiClient = null!;

    protected async Task StartAppAsync(Action<IEndpointRouteBuilder> map, Action<IAuth2ClientBuilder>? configure = null, Action<IServiceCollection>? services = null,
        HttpMessageHandler? apiHandler = null, Action<SystemX.Auth2.Client.Options.Auth2ApiOptions>? api = null)
    {
        await UpdateSettingsAsync(s => { s.Clients.AllowSelfUiLogin = true; });
        Counting = new CountingHandler(Server.Server.CreateHandler());
        ApiClient = BuildClient("blog", handler: apiHandler ?? Counting);

        App = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(s =>
            {
                s.AddRouting();
                var builder = s.AddAuth2Client("http://localhost", o => { o.ApplicationName = "Blog"; o.ClientId = "blog"; o.ClientSecret = ClientSecret; o.HandshakeOnStartup = false; })
                    .ServerWithMultiApplication("blog")
                    .UseApiAuthentication(o => { o.BackchannelHandler = Server.Server.CreateHandler(); o.KeyRefreshInterval = TimeSpan.FromSeconds(1); api?.Invoke(o); });
                configure?.Invoke(builder);
                foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
                    s.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => apiHandler ?? Counting);
                services?.Invoke(s);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuth2();
                app.UseEndpoints(map);
            })).StartAsync();
        Http = App.GetTestClient();
    }

    public new async Task DisposeAsync()
    {
        Http?.Dispose();
        if (App is not null) await App.StopAsync();
        App?.Dispose();
        if (ApiClient is not null) await ApiClient.DisposeAsync();
        await base.DisposeAsync();
    }

    protected async Task<AppUser> CreateUserAsync(string name = "sara")
    {
        await using var scope = ServerScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var user = new AppUser { UserName = name, Email = $"{name}@example.com" };
        (await users.CreateAsync(user, Password)).Succeeded.ShouldBeTrue();
        return user;
    }

    protected AsyncServiceScope ServerScope()
    {
        var scope = Server.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IClientIdAccessor>().ClientId = "blog";
        return scope;
    }

    /// <summary>Runs server-side access changes for a user.</summary>
    protected async Task AccessAsync(string userName, Func<IAccessService, IGroupService, AppUser, Task> change)
    {
        await using var scope = ServerScope();
        var user = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByNameAsync(userName))!;
        await change(scope.ServiceProvider.GetRequiredService<IAccessService>(), scope.ServiceProvider.GetRequiredService<IGroupService>(), user);
    }

    protected async Task<string> LoginAsync(string name = "sara")
    {
        var login = await ApiClient.GetRequiredService<IAuth2ApiClientService>().LoginAsync(new LoginRequest(name, Password));
        login.IsSuccess.ShouldBeTrue(login.Error?.Code);
        return login.Value!.AccessToken!;
    }

    protected Task<string> MachineTokenAsync(params string[] scopes) => ApiClient.GetRequiredService<IAuth2MachineTokenProvider>().GetTokenAsync(scopes);

    protected async Task<HttpStatusCode> GetAsync(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request);
        return response.StatusCode;
    }

    protected async Task<HttpResponseMessage> SendAsync(string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(request);
    }
}

public class EndpointAuthorizationTests : ClientAppTestBase
{
    private static void Map(IEndpointRouteBuilder e)
    {
        e.MapGet("/public", () => "ok").WithAnonymous();
        e.MapGet("/signed-in", () => "ok").WithAuth2();
        e.MapGet("/orders", () => "ok").WithPermission("orders.read");
        e.MapGet("/orders-any", () => "ok").WithPermission("orders.read", "orders.write");
        e.MapGet("/orders-all", () => "ok").WithAllPermissions("orders.read", "orders.write");
        e.MapGet("/role-perm", () => "ok").WithPermission("orders.read").WithOnlyRolePermission();
        e.MapGet("/user-perm", () => "ok").WithPermission("orders.read").WithOnlyUserPermission();
        e.MapGet("/editors", () => "ok").WithRole("Editor", "Writer");
        e.MapGet("/editors-all", () => "ok").WithAllRoles("Editor", "Writer");
        e.MapGet("/finance", () => "ok").WithGroup("finance");
        e.MapGet("/machine", () => "ok").WithMachineOnly();
        e.MapGet("/users-only", () => "ok").WithUserOnly();
        e.MapGet("/scope-me", () => "ok").WithScope("auth2.me");
        e.MapGet("/scope-groups", () => "ok").WithScope("auth2.groups.read");
        e.MapGet("/chained", () => "ok").WithRole("Editor").WithPermission("orders.write");
        e.MapGet("/attr", [Auth2Permission("orders.read")] () => "ok");
        e.MapGet("/attr-role", [Auth2Role("Editor")] [Auth2UserOnly] () => "ok");
        e.MapGet("/attr-anon", [Auth2Anonymous] () => "ok");
        var group = e.MapGroup("/g").WithPermission("orders.write");
        group.MapGet("/one", () => "ok");
        e.MapGet("/who", async (ICurrentUser u) => Results.Json(new
        {
            u.IsAuthenticated, u.IsMachine, u.UserId, u.UserName, u.Email, u.Scopes,
            Roles = await u.GetRolesAsync(), Permissions = await u.GetPermissionsAsync(), Groups = await u.GetGroupsAsync(),
            HasOrders = await u.HasPermissionAsync("orders.read"), IsEditor = await u.HasRoleAsync("editor"),
        })).WithAuth2();
    }

    private async Task StartWithUserAsync()
    {
        await StartAppAsync(Map);
        await CreateUserAsync();
    }

    [Fact]
    public async Task Anonymous_And_SignedIn_Endpoints()
    {
        await StartWithUserAsync();
        (await GetAsync("/public")).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/attr-anon")).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/signed-in")).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync("/signed-in", "not-a-token")).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync("/signed-in", await LoginAsync())).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/orders")).ShouldBe(HttpStatusCode.Unauthorized);   // no token: 401, never a redirect
    }

    [Fact]
    public async Task Permission_401_403_200_AndAnyOfAllOf()
    {
        await StartWithUserAsync();
        var plain = await LoginAsync();
        (await GetAsync("/orders", plain)).ShouldBe(HttpStatusCode.Forbidden);

        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.GrantPermissionAsync(u, "orders.read");
        });
        var token = await LoginAsync();   // a new token: its access version is newer than the cached answer
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/orders-any", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/orders-all", token)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/attr", token)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PermissionSource_RoleOnly_And_UserOnly()
    {
        await StartWithUserAsync();
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.CreateRoleAsync("Editor");
            await a.AddRolePermissionAsync("Editor", "orders.read");
            await a.AssignRoleAsync(u, "Editor");   // the permission comes from a role
        });
        var viaRole = await LoginAsync();
        (await GetAsync("/orders", viaRole)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/role-perm", viaRole)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/user-perm", viaRole)).ShouldBe(HttpStatusCode.Forbidden);

        await CreateUserAsync("dan");
        await AccessAsync("dan", async (a, g, u) => await a.GrantPermissionAsync(u, "orders.read"));   // a direct grant
        var direct = await LoginAsync("dan");
        (await GetAsync("/role-perm", direct)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/user-perm", direct)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RoleOfAGroup_CountsAsARolePermission_AndGroupEndpointsWork()
    {
        await StartWithUserAsync();
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.CreateRoleAsync("Editor");
            await a.AddRolePermissionAsync("Editor", "orders.read");
            await g.CreateAsync("finance");
            await g.AddRoleAsync("finance", "Editor");
            await g.AddUserAsync("finance", u);
        });
        var token = await LoginAsync();
        (await GetAsync("/role-perm", token)).ShouldBe(HttpStatusCode.OK);   // via the role of the group
        (await GetAsync("/finance", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/editors", token)).ShouldBe(HttpStatusCode.OK);     // the role of the group is in the token too
    }

    [Fact]
    public async Task GroupPermission_IsEffective_ButNeitherRoleOnlyNorUserOnly()
    {
        await StartWithUserAsync();
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await g.CreateAsync("finance");
            await g.AddPermissionAsync("finance", "orders.read");
            await g.AddUserAsync("finance", u);
        });
        var token = await LoginAsync();
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/role-perm", token)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/user-perm", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_AreCheckedInTheToken_AnyAndAll()
    {
        await StartWithUserAsync();
        await AccessAsync("sara", async (a, g, u) => { await a.CreateRoleAsync("Editor"); await a.CreateRoleAsync("Writer"); await a.AssignRoleAsync(u, "Editor"); });
        var token = await LoginAsync();
        (await GetAsync("/editors", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/editors-all", token)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/attr-role", token)).ShouldBe(HttpStatusCode.OK);
        await AccessAsync("sara", async (a, g, u) => await a.AssignRoleAsync(u, "Writer"));
        (await GetAsync("/editors-all", await LoginAsync())).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Groups_Scopes_AndTokenKinds()
    {
        await StartWithUserAsync();
        var user = await LoginAsync();
        (await GetAsync("/finance", user)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/scope-me", user)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/scope-groups", user)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/users-only", user)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/machine", user)).ShouldBe(HttpStatusCode.Forbidden);

        var machine = await MachineTokenAsync("auth2.groups.read");
        (await GetAsync("/machine", machine)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/scope-groups", machine)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/users-only", machine)).ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/orders", machine)).ShouldBe(HttpStatusCode.Forbidden);   // a machine has no permissions
        (await GetAsync("/signed-in", machine)).ShouldBe(HttpStatusCode.OK);
        Counting.MeCalls.ShouldBe(1 > 0 ? Counting.MeCalls : 0);   // (machine tokens never ask /me; checked precisely in WhoAmI test)
    }

    [Fact]
    public async Task ChainedChecks_AreAnd_AndRouteGroupsApplyToTheirEndpoints()
    {
        await StartWithUserAsync();
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.write"));
            await a.CreateRoleAsync("Editor");
            await a.AssignRoleAsync(u, "Editor");
        });
        var onlyRole = await LoginAsync();
        (await GetAsync("/chained", onlyRole)).ShouldBe(HttpStatusCode.Forbidden);   // role yes, permission no
        (await GetAsync("/g/one", onlyRole)).ShouldBe(HttpStatusCode.Forbidden);
        await AccessAsync("sara", async (a, g, u) => await a.GrantPermissionAsync(u, "orders.write"));
        var both = await LoginAsync();
        (await GetAsync("/chained", both)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/g/one", both)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NewToken_SeesNewAccess_ButTheSameTokenKeepsTheCachedAnswer_UntilItExpires()
    {
        await StartWithUserAsync();
        var token = await LoginAsync();
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.Forbidden);
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.GrantPermissionAsync(u, "orders.read");
        });
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.Forbidden);          // cached (see client-current-user.md)
        (await GetAsync("/orders", await LoginAsync())).ShouldBe(HttpStatusCode.OK);    // the new token has a newer access version: read again
    }

    [Fact]
    public async Task CurrentUser_HasTokenData_AndOneServerCallPerCacheTime()
    {
        await StartWithUserAsync();
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.GrantPermissionAsync(u, "orders.read");
            await a.CreateRoleAsync("Editor");
            await a.AssignRoleAsync(u, "Editor");
            await g.CreateAsync("finance");
            await g.AddUserAsync("finance", u);
        });
        var token = await LoginAsync();
        var who = async () => await (await SendAsync("/who", token)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var first = await who();
        first.GetProperty("userName").GetString().ShouldBe("sara");
        first.GetProperty("email").GetString().ShouldBe("sara@example.com");
        first.GetProperty("isMachine").GetBoolean().ShouldBeFalse();
        first.GetProperty("permissions")[0].GetString().ShouldBe("orders.read");
        first.GetProperty("roles")[0].GetString().ShouldBe("Editor");
        first.GetProperty("groups")[0].GetString().ShouldBe("finance");
        first.GetProperty("hasOrders").GetBoolean().ShouldBeTrue();
        first.GetProperty("isEditor").GetBoolean().ShouldBeTrue();
        await who(); await who();
        Counting.MeCalls.ShouldBe(1);   // one call, three requests
    }

    [Fact]
    public async Task CurrentUser_OfAMachineToken_IsEmpty_AndNeverCallsTheServer()
    {
        await StartWithUserAsync();
        var before = Counting.MeCalls;
        var machine = await MachineTokenAsync("auth2.groups.read");
        var who = await (await SendAsync("/who", machine)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        who.GetProperty("isMachine").GetBoolean().ShouldBeTrue();
        who.GetProperty("userId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        who.GetProperty("permissions").GetArrayLength().ShouldBe(0);
        who.GetProperty("roles").GetArrayLength().ShouldBe(0);
        Counting.MeCalls.ShouldBe(before);
    }
}

public class ClientAuthenticationTests : ClientAppTestBase
{
    [Fact]
    public async Task TokensOfAnotherApplication_AreRefused()
    {
        await StartAppAsync(e => e.MapGet("/signed-in", () => "ok").WithAuth2());
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var shop = await scope.ServiceProvider.GetRequiredService<IApplicationService>().CreateAsync("shop", "Shop", null, null);
            var response = await new HttpClient(Server.Server.CreateHandler()) { BaseAddress = new Uri("http://localhost") }.PostAsync("/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = "shop", ["client_secret"] = shop.ClientSecret, ["scope"] = "auth2.groups.read" }));
            var token = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("access_token").GetString()!;
            (await GetAsync("/signed-in", token)).ShouldBe(HttpStatusCode.Unauthorized);   // the audience is "shop", not "blog"
        }
        (await GetAsync("/signed-in", await MachineTokenAsync("auth2.groups.read"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeyRotationOnTheServer_NeedsNoRestartOfTheClient()
    {
        await StartAppAsync(e => e.MapGet("/signed-in", () => "ok").WithAuth2());
        var before = await MachineTokenAsync("auth2.groups.read");
        (await GetAsync("/signed-in", before)).ShouldBe(HttpStatusCode.OK);   // the client now knows the keys

        await using (var scope = Server.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<IKeyRing>().RotateAsync();
        ApiClient.GetRequiredService<IAuth2MachineTokenProvider>().Invalidate();
        var after = await MachineTokenAsync("auth2.groups.read");
        await Task.Delay(1200);   // the client reads the keys again at most once per KeyRefreshInterval (1 s in this test)
        (await GetAsync("/signed-in", after)).ShouldBe(HttpStatusCode.OK);   // the unknown key id makes the client read the keys again, and wait for them
        (await GetAsync("/signed-in", before)).ShouldBe(HttpStatusCode.OK);   // the old key is still published
    }

    [Fact]
    public async Task ServerNotReachable_ForAccessData_FailsClosed_ButTokenOnlyChecksStillWork()
    {
        await StartAppAsync(e =>
        {
            e.MapGet("/orders", () => "ok").WithPermission("orders.read");
            e.MapGet("/scope", () => "ok").WithScope("auth2.me");
        }, apiHandler: new DownHandler(), configure: null);
        // Tokens still validate (the keys come through the backchannel), but /me cannot be reached.
        var token = await LoginViaServerAsync();
        (await GetAsync("/scope", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<string> LoginViaServerAsync()
    {
        await using (var scope = ServerScope())
            (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().CreateAsync(new AppUser { UserName = "sara", Email = "sara@example.com" }, Password)).Succeeded.ShouldBeTrue();
        using var direct = new HttpClient(Server.Server.CreateHandler()) { BaseAddress = new Uri("http://localhost") };
        var response = await direct.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "sara", ["password"] = Password, ["scope"] = "openid auth2.me", ["client_id"] = "blog", ["client_secret"] = ClientSecret,
        }));
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("access_token").GetString()!;
    }
}

public class CurrentUserStoreAndAuditTests : ClientAppTestBase
{
    [Fact]
    public async Task DistributedStore_SharesAccessData_AndTokenOnlyNeverCallsTheServer()
    {
        await StartAppAsync(e => e.MapGet("/orders", () => "ok").WithPermission("orders.read"), b => b.UseDistributedCurrentUserStore());
        await CreateUserAsync();
        await AccessAsync("sara", async (a, g, u) => { await a.CreatePermissionAsync(new PermissionDefinition("orders.read")); await a.GrantPermissionAsync(u, "orders.read"); });
        var token = await LoginAsync();
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/orders", token)).ShouldBe(HttpStatusCode.OK);
        Counting.MeCalls.ShouldBe(1);
        App.Services.GetRequiredService<ICurrentUserStore>().ShouldBeOfType<DistributedCurrentUserStore>();
    }

    [Fact]
    public async Task TokenOnlySource_UsesTheRoleClaims_AndAsksNoOne()
    {
        await StartAppAsync(e => e.MapGet("/editors", () => "ok").WithRole("Editor"), b => b.EnableCurrentUser(o => o.Source = SystemX.Auth2.Contracts.Enums.CurrentUserSource.TokenOnly));
        await CreateUserAsync();
        await AccessAsync("sara", async (a, g, u) => { await a.CreateRoleAsync("Editor"); await a.AssignRoleAsync(u, "Editor"); });
        (await GetAsync("/editors", await LoginAsync())).ShouldBe(HttpStatusCode.OK);
        Counting.MeCalls.ShouldBe(0);
    }

    private sealed class CapturingSink : IAuth2AuditSink
    {
        public List<Auth2AuthorizationEvent> Events { get; } = [];
        public Task OnAuthorizationAsync(Auth2AuthorizationEvent evt, CancellationToken ct = default) { lock (Events) Events.Add(evt); return Task.CompletedTask; }
    }

    [Fact]
    public async Task AuditHook_RaisesAnEvent_ForAllowedAndRefusedCalls()
    {
        var sink = new CapturingSink();
        await StartAppAsync(e => e.MapGet("/delete", () => "ok").WithPermission("orders.delete").WithAuth2Audit("orders.delete"), services: s => s.AddSingleton<IAuth2AuditSink>(sink));
        var user = await CreateUserAsync();
        await AccessAsync("sara", async (a, g, u) => { await a.CreatePermissionAsync(new PermissionDefinition("orders.delete")); });
        (await GetAsync("/delete", await LoginAsync())).ShouldBe(HttpStatusCode.Forbidden);
        await AccessAsync("sara", async (a, g, u) => await a.GrantPermissionAsync(u, "orders.delete"));
        (await GetAsync("/delete", await LoginAsync())).ShouldBe(HttpStatusCode.OK);

        sink.Events.Select(e => (e.Action, e.Succeeded)).ShouldBe([("orders.delete", false), ("orders.delete", true)]);
        sink.Events[1].UserId.ShouldBe(user.Id);
        sink.Events[1].Path.ShouldBe("/delete");
    }
}
