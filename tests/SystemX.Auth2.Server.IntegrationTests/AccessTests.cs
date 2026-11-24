using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class AccessModelTests : Phase6Base
{
    private async Task<T> WithAsync<T>(string clientId, Func<IAccessService, IGroupService, UserManager<AppUser>, Task<T>> action)
    {
        await using var scope = ScopeFor(clientId);
        var sp = scope.ServiceProvider;
        return await action(sp.GetRequiredService<IAccessService>(), sp.GetRequiredService<IGroupService>(), sp.GetRequiredService<UserManager<AppUser>>());
    }

    private Task<EffectiveAccess> AccessOfAsync(string clientId, string userName = "sara") =>
        WithAsync(clientId, async (access, _, users) => await access.GetEffectiveAccessAsync((await users.FindByNameAsync(userName))!));

    private async Task SeedAccessAsync()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await WithAsync("appa", async (a, g, u) =>
        {
            foreach (var k in new[] { "orders.read", "orders.write", "reports.view", "billing.pay", "admin.users" })
                (await a.CreatePermissionAsync(new PermissionDefinition(k))).Succeeded.ShouldBeTrue();
            (await a.CreateRoleAsync("Editor")).Succeeded.ShouldBeTrue();
            (await a.CreateRoleAsync("Auditor")).Succeeded.ShouldBeTrue();
            (await a.AddRolePermissionAsync("Editor", "orders.write")).Succeeded.ShouldBeTrue();
            (await a.AddRolePermissionAsync("Auditor", "reports.view")).Succeeded.ShouldBeTrue();
            (await g.CreateAsync("Finance")).Succeeded.ShouldBeTrue();
            return 0;
        });
    }

    [Fact]
    public async Task EffectivePermissions_AreTheUnionOfDirectRoleGroupRoleAndGroupPermissions()
    {
        await SeedAccessAsync();
        await WithAsync("appa", async (a, g, u) =>
        {
            var sara = (await u.FindByNameAsync("sara"))!;
            (await a.GrantPermissionAsync(sara, "orders.read")).Succeeded.ShouldBeTrue();          // direct
            (await a.AssignRoleAsync(sara, "Editor")).Succeeded.ShouldBeTrue();                    // via role -> orders.write
            (await g.AddRoleAsync("finance", "Auditor")).Succeeded.ShouldBeTrue();                 // group role -> reports.view
            (await g.AddPermissionAsync("finance", "billing.pay")).Succeeded.ShouldBeTrue();       // group permission
            (await g.AddUserAsync("finance", sara)).Succeeded.ShouldBeTrue();
            return 0;
        });

        var access = await AccessOfAsync("appa");
        access.Permissions.ShouldBe(["billing.pay", "orders.read", "orders.write", "reports.view"]);
        access.Roles.ShouldBe(["Auditor", "Editor"]);   // Auditor comes from the group
        access.Groups.ShouldBe(["finance"]);
    }

    [Fact]
    public async Task SameRoleName_InTwoApplications_HasSeparatePermissions_AndAccessDoesNotLeak()
    {
        await SeedAccessAsync();
        var sara = await UserAsync("sara@example.com");
        await JoinAsync(sara, B);
        await WithAsync("appa", async (a, g, u) =>
        {
            var user = (await u.FindByNameAsync("sara"))!;
            await a.AssignRoleAsync(user, "Editor");
            return 0;
        });
        await WithAsync("appb", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("books.read"));
            (await a.CreateRoleAsync("Editor")).Succeeded.ShouldBeTrue();   // same name as in App A
            await a.AddRolePermissionAsync("Editor", "books.read");
            (await a.AddRolePermissionAsync("Editor", "orders.write")).ErrorCode.ShouldBe(Auth2ErrorCodes.NotFound);   // A's permission does not exist in B
            await a.AssignRoleAsync((await u.FindByNameAsync("sara"))!, "Editor");
            return 0;
        });

        (await AccessOfAsync("appa")).Permissions.ShouldBe(["orders.write"]);
        (await AccessOfAsync("appb")).Permissions.ShouldBe(["books.read"]);
    }

    [Fact]
    public async Task NotAMemberOfTheApplication_HasNoAccess_AndCannotBeGranted()
    {
        await SeedAccessAsync();
        var outsider = await WithAsync("appb", async (a, g, u) =>
        {
            var user = new AppUser { UserName = "otto", Email = "otto@example.com" };
            (await u.CreateAsync(user, PassB)).Succeeded.ShouldBeTrue();
            return user;
        });
        await WithAsync("appa", async (a, g, u) =>
        {
            (await a.GrantPermissionAsync(outsider, "orders.read")).ErrorCode.ShouldBe(Auth2ErrorCodes.NotFound);
            (await a.AssignRoleAsync(outsider, "Editor")).ErrorCode.ShouldBe(Auth2ErrorCodes.NotFound);
            (await g.AddUserAsync("finance", outsider)).ErrorCode.ShouldBe(Auth2ErrorCodes.NotFound);
            (await a.GetEffectiveAccessAsync(outsider)).Permissions.ShouldBeEmpty();
            return 0;
        });
    }

    [Fact]
    public async Task EveryAccessChange_RaisesTheVersion_AndTheNextReadSeesIt()
    {
        await SeedAccessAsync();
        var seen = new List<int>();
        async Task Step(Func<IAccessService, IGroupService, AppUser, Task> change, Action<EffectiveAccess> expect)
        {
            await WithAsync("appa", async (a, g, u) => { await change(a, g, (await u.FindByNameAsync("sara"))!); return 0; });
            var access = await AccessOfAsync("appa");
            expect(access);
            seen.Add(access.AccessVersion);
        }

        seen.Add((await AccessOfAsync("appa")).AccessVersion);
        await Step((a, g, u) => a.GrantPermissionAsync(u, "orders.read"), x => x.Permissions.ShouldContain("orders.read"));
        await Step((a, g, u) => a.RevokePermissionAsync(u, "orders.read"), x => x.Permissions.ShouldNotContain("orders.read"));
        await Step((a, g, u) => a.AssignRoleAsync(u, "Editor"), x => x.Roles.ShouldContain("Editor"));
        await Step((a, g, u) => a.AddRolePermissionAsync("Editor", "orders.read"), x => x.Permissions.ShouldContain("orders.read"));   // application version
        await Step((a, g, u) => a.RemoveRolePermissionAsync("Editor", "orders.read"), x => x.Permissions.ShouldNotContain("orders.read"));
        await Step((a, g, u) => g.AddUserAsync("finance", u), x => x.Groups.ShouldContain("finance"));
        await Step((a, g, u) => g.AddPermissionAsync("finance", "billing.pay"), x => x.Permissions.ShouldContain("billing.pay"));
        await Step((a, g, u) => g.RemovePermissionAsync("finance", "billing.pay"), x => x.Permissions.ShouldNotContain("billing.pay"));
        await Step((a, g, u) => g.AddRoleAsync("finance", "Auditor"), x => x.Roles.ShouldContain("Auditor"));
        await Step((a, g, u) => g.RemoveRoleAsync("finance", "Auditor"), x => x.Roles.ShouldNotContain("Auditor"));
        await Step((a, g, u) => g.RemoveUserAsync("finance", u), x => x.Groups.ShouldBeEmpty());
        await Step((a, g, u) => a.RemoveRoleAsync(u, "Editor"), x => x.Roles.ShouldBeEmpty());
        await Step((a, g, u) => a.DeleteRoleAsync("Auditor"), x => { });

        seen.Zip(seen.Skip(1), (before, after) => after > before).ShouldAllBe(x => x);   // strictly growing
    }

    [Fact]
    public async Task NoChange_DoesNotRaiseTheVersion_AndTheSecondReadIsServedFromTheCache()
    {
        await SeedAccessAsync();
        await WithAsync("appa", async (a, g, u) => await a.GrantPermissionAsync((await u.FindByNameAsync("sara"))!, "orders.read"));
        var first = await AccessOfAsync("appa");
        await WithAsync("appa", async (a, g, u) =>
        {
            var sara = (await u.FindByNameAsync("sara"))!;
            (await a.GrantPermissionAsync(sara, "orders.read")).Succeeded.ShouldBeTrue();    // already granted
            (await a.RevokePermissionAsync(sara, "billing.pay")).Succeeded.ShouldBeTrue();   // never granted
            (await a.CreatePermissionAsync(new PermissionDefinition("orders.read"))).Succeeded.ShouldBeTrue();
            return 0;
        });
        var second = await AccessOfAsync("appa");
        second.AccessVersion.ShouldBe(first.AccessVersion);
        ReferenceEquals(first, second).ShouldBeTrue();   // same cached object
    }

    [Fact]
    public async Task DeletingAPermission_RemovesItFromEveryone()
    {
        await SeedAccessAsync();
        await WithAsync("appa", async (a, g, u) =>
        {
            var sara = (await u.FindByNameAsync("sara"))!;
            await a.GrantPermissionAsync(sara, "orders.read");
            await a.AddRolePermissionAsync("Editor", "orders.read");
            await g.AddPermissionAsync("finance", "orders.read");
            (await a.DeletePermissionAsync("orders.read")).Succeeded.ShouldBeTrue();
            (await a.DeletePermissionAsync("orders.read")).ErrorCode.ShouldBe(Auth2ErrorCodes.NotFound);
            return 0;
        });
        (await AccessOfAsync("appa")).Permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task ObsoletePermission_StaysEffective_ButIsMarked()
    {
        await SeedAccessAsync();
        await WithAsync("appa", async (a, g, u) =>
        {
            await a.GrantPermissionAsync((await u.FindByNameAsync("sara"))!, "orders.read");
            await a.SetPermissionObsoleteAsync("ORDERS.READ", true);
            (await a.ListPermissionsAsync()).Single(p => p.Key == "orders.read").IsObsolete.ShouldBeTrue();
            return 0;
        });
        (await AccessOfAsync("appa")).Permissions.ShouldBe(["orders.read"]);
    }

    [Fact]
    public async Task InvalidKeys_AreRejected_AndKeysAreNormalized()
    {
        await SeedAccessAsync();
        await WithAsync("appa", async (a, g, u) =>
        {
            (await a.CreatePermissionAsync(new PermissionDefinition("Bad Key"))).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidPermissionKey);
            (await a.CreatePermissionAsync(new PermissionDefinition(new string('a', 101)))).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidPermissionKey);
            (await a.CreatePermissionAsync(new PermissionDefinition("  Users.Delete  "))).Succeeded.ShouldBeTrue();
            (await a.ListPermissionsAsync()).Select(p => p.Key).ShouldContain("users.delete");
            (await g.CreateAsync("Not Valid")).ErrorCode.ShouldBe(Auth2ErrorCodes.ValidationFailed);
            (await g.CreateAsync("FINANCE")).ErrorCode.ShouldBe(Auth2ErrorCodes.Conflict);
            return 0;
        });
    }

    [Fact]
    public async Task ParallelGrants_CreateOneRow()
    {
        await SeedAccessAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => WithAsync("appa", async (a, g, u) =>
            (await a.GrantPermissionAsync((await u.FindByNameAsync("sara"))!, "orders.read")).Succeeded)));
        results.ShouldAllBe(x => x);
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserPermissions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Groups_AreSeparatePerApplication_AndChangesAreAudited()
    {
        await SeedAccessAsync();
        await WithAsync("appb", async (a, g, u) =>
        {
            (await g.CreateAsync("finance")).Succeeded.ShouldBeTrue();   // same name as in App A: fine
            (await g.ListAsync()).Select(x => x.Name).ShouldBe(["finance"]);
            return 0;
        });
        await WithAsync("appa", async (a, g, u) =>
        {
            (await g.AddPermissionAsync("finance", "billing.pay")).Succeeded.ShouldBeTrue();
            (await g.ListAsync()).Single().Permissions.ShouldBe(["billing.pay"]);
            return 0;
        });
        (await AuditAsync("group.permission_added")).Count.ShouldBe(1);
        (await AuditAsync("group.created")).Count.ShouldBe(2);
        (await AuditAsync("permission.created")).Count.ShouldBe(5);
    }

    [Fact]
    public async Task SystemRoles_CannotBeDeleted()
    {
        await SeedAccessAsync();
        await using var scope = ScopeFor("appa");
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        (await roles.CreateAsync(new AppRole("Owner") { ProjectId = A.Id, IsSystem = true })).Succeeded.ShouldBeTrue();
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().DeleteRoleAsync("Owner")).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);
    }
}

public class MeEndpointTests : Phase6Base
{
    private async Task<(string UserToken, string ClientToken)> StartAsync()
    {
        await PrepareAppsAsync();
        var client = await ClientTokenAsync("appa", "auth2.register auth2.login");
        await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("sara", "sara@example.com", null, PassA, FirstName: "Sara", LastName: "Jones"), client);
        var tokens = await UserTokensAsync("sara", PassA);
        return (tokens.Str("access_token"), client);
    }

    private async Task<JsonElement> MeAsync(string token)
    {
        var r = await new Browser(Factory).GetAsync(Auth2Routes.Me, token);
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task GrantAsync(Func<IAccessService, IGroupService, AppUser, Task> change)
    {
        await using var scope = ScopeFor("appa");
        var sara = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("sara"))!;   // same scope: one tracked instance
        await change(scope.ServiceProvider.GetRequiredService<IAccessService>(), scope.ServiceProvider.GetRequiredService<IGroupService>(), sara);
    }

    [Fact]
    public async Task Me_ReturnsTheUserWithRolesPermissionsGroupsScopesAndVersion()
    {
        var (token, _) = await StartAsync();
        await GrantAsync(async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.CreateRoleAsync("Editor");
            await a.AddRolePermissionAsync("Editor", "orders.read");
            await a.AssignRoleAsync(u, "Editor");
            await g.CreateAsync("finance");
            await g.AddUserAsync("finance", u);
        });

        var me = await MeAsync(token);
        me.Str("userName").ShouldBe("sara");
        me.Str("email").ShouldBe("sara@example.com");
        me.Values("roles").ShouldBe(["Editor"]);
        me.Values("permissions").ShouldBe(["orders.read"]);
        me.Values("groups").ShouldBe(["finance"]);
        me.Values("scopes").ShouldContain("auth2.me");
        me.GetProperty("accessVersion").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Me_SeesChangesWithTheSameToken_AndTheVersionGrows()
    {
        var (token, _) = await StartAsync();
        var before = await MeAsync(token);
        before.Values("permissions").ShouldBeEmpty();
        await GrantAsync(async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.GrantPermissionAsync(u, "orders.read");
        });
        var after = await MeAsync(token);
        after.Values("permissions").ShouldBe(["orders.read"]);
        after.GetProperty("accessVersion").GetInt32().ShouldBeGreaterThan(before.GetProperty("accessVersion").GetInt32());
    }

    [Fact]
    public async Task TokenCarriesEffectiveRoles_AndAVersion_ButNoPermissionList()
    {
        await StartAsync();
        await GrantAsync(async (a, g, u) =>
        {
            await a.CreateRoleAsync("Auditor");
            await g.CreateAsync("finance");
            await g.AddRoleAsync("finance", "Auditor");
            await g.AddUserAsync("finance", u);
            await a.CreatePermissionAsync(new PermissionDefinition("reports.view"));
            await a.GrantPermissionAsync(u, "reports.view");
        });
        var payload = HttpExtensions.JwtPayload((await UserTokensAsync("sara", PassA)).Str("access_token"));
        payload.Values("role").ShouldBe(["Auditor"]);   // role of the group
        payload.Values(Auth2Claims.AccessVersion).Single().ShouldNotBeNullOrEmpty();
        payload.ToString().ShouldNotContain("reports.view");
        var me = await MeAsync((await UserTokensAsync("sara", PassA)).Str("access_token"));
        payload.Values(Auth2Claims.AccessVersion).Single().ShouldBe(me.GetProperty("accessVersion").GetInt32().ToString());
    }

    [Fact]
    public async Task Me_IgnoresAUserIdInTheRequest()
    {
        var (token, _) = await StartAsync();
        var other = Guid.NewGuid();
        var r = await new Browser(Factory).GetAsync($"{Auth2Routes.Me}?userId={other}&id={other}", token);
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Str("userName").ShouldBe("sara");
    }

    [Fact]
    public async Task Me_RejectsMissingBadAndMachineTokens_AndTokensWithoutTheScope()
    {
        var (_, client) = await StartAsync();
        var b = new Browser(Factory);
        var none = await b.GetAsync(Auth2Routes.Me);
        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonDocument.Parse(await none.Content.ReadAsStringAsync()).RootElement.Str("code").ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await b.GetAsync(Auth2Routes.Me, "not-a-token")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await b.GetAsync(Auth2Routes.Me, client)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);   // machine token

        var noScope = (await UserTokensAsync("sara", PassA, scope: "openid")).Str("access_token");
        var r = await b.GetAsync(Auth2Routes.Me, noScope);
        r.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Str("code").ShouldBe(Auth2ErrorCodes.InsufficientScope);
    }

    [Fact]
    public async Task Me_OfAnotherApplicationToken_ShowsOnlyThatApplicationsAccess()
    {
        var (tokenA, _) = await StartAsync();
        var sara = await UserAsync("sara@example.com");
        await JoinAsync(sara, B);
        await GrantAsync(async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.GrantPermissionAsync(u, "orders.read");
        });
        await using (var scope = ScopeFor("appb"))
            await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().AddPasswordAsync((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("sara"))!, PassB);
        var tokenB = (await UserTokensAsync("sara", PassB, "appb")).Str("access_token");

        (await MeAsync(tokenA)).Values("permissions").ShouldBe(["orders.read"]);
        (await MeAsync(tokenB)).Values("permissions").ShouldBeEmpty();
    }
}
