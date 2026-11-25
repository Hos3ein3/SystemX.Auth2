using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Contracts.Access;

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
