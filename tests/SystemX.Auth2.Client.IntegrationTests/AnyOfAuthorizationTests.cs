using System.Net;
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
