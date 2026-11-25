using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The demo people and access data of the sample project.</summary>
public class DemoDataTests : Phase6Base
{
    private async Task<int> SeedAsync()
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();   // the seed runs without the project filters
        await scope.ServiceProvider.GetRequiredService<Auth2Seeder>().EnsureSampleProjectAndApplicationsAsync();
        // The seed of the admin works in the project _global first, in this same scope: the demo data must still go to the sample project.
        scope.ServiceProvider.GetRequiredService<IProjectSelection>().ProjectKey = "_global";
        await scope.ServiceProvider.GetRequiredService<IProjectContext>().GetRequiredAsync();
        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().EnsureAsync();
    }

    private async Task<(int Sample, int Global)> CountsAsync(Func<Auth2DbContext, Guid, IQueryable<Guid>> ids)
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var sample = (await db.Projects.SingleAsync(p => p.ProjectKey == DemoDataSeeder.ProjectKey)).Id;
        var global = (await db.Projects.SingleAsync(p => p.ProjectKey == "_global")).Id;
        return (await ids(db, sample).CountAsync(), await ids(db, global).CountAsync());
    }

    private async Task<JsonElement> MeAsync(string userName)
    {
        var b = new Browser(Factory);
        var token = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = userName, ["password"] = DemoDataSeeder.Password, ["scope"] = "openid roles auth2.me",
            ["client_id"] = "mvc-sample", ["client_secret"] = "mvc-sample-secret",
        });
        var body = await token.JsonAsync();
        token.StatusCode.ShouldBe(HttpStatusCode.OK, userName + ": " + body);
        var me = await b.GetAsync(Auth2Routes.Me, body.Str("access_token"));
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await me.JsonAsync();
    }

    private static string[] Names(JsonElement me, string property) => [.. me.GetProperty(property).EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public async Task TheFirstDemoPeople_CanSignIn_AndHaveTheirRolesGroupsAndPermissions()
    {
        await MigrateAndSeedAsync();
        await SeedAsync();

        var alice = await MeAsync("alice");
        Names(alice, "roles").ShouldBe(["Admin"]);
        Names(alice, "permissions").Length.ShouldBe(DemoDataSeeder.PermissionKeys.Count);   // the Admin has every permission

        var bob = await MeAsync("bob");
        Names(bob, "roles").ShouldBe(["Editor", "Manager"]);                       // Manager of his own, Editor through the group sales
        Names(bob, "groups").ShouldBe(["sales"]);
        Names(bob, "permissions").ShouldContain("reports.read");

        var erin = await MeAsync("erin");
        Names(erin, "roles").ShouldBe(["Auditor"]);                                // from the group finance
        Names(erin, "groups").ShouldBe(["finance"]);
        Names(erin, "permissions").ShouldBe(["orders.read", "reports.read"]);      // the role of the group and a permission of the group

        var frank = await MeAsync("frank");
        Names(frank, "roles").ShouldBeEmpty();
        Names(frank, "permissions").ShouldBe(["orders.read", "reports.read"]);     // direct grants only

        Names(await MeAsync("dave"), "permissions").ShouldBe(["orders.read"]);
    }

    [Fact]
    public async Task ThereAreAtLeastFiftyOfEverything_AndTheyAreAssigned()
    {
        await MigrateAndSeedAsync();
        await SeedAsync();

        DemoDataSeeder.PermissionKeys.Count.ShouldBeGreaterThanOrEqualTo(50);
        DemoDataSeeder.RoleNames.Count.ShouldBeGreaterThanOrEqualTo(50);
        DemoDataSeeder.GroupNames.Count.ShouldBeGreaterThanOrEqualTo(50);
        DemoDataSeeder.ScopeNames.Count.ShouldBeGreaterThanOrEqualTo(50);
        DemoDataSeeder.UserNames.Count.ShouldBeGreaterThanOrEqualTo(50);

        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var project = await db.Projects.SingleAsync(p => p.ProjectKey == DemoDataSeeder.ProjectKey);

        (await db.Permissions.CountAsync(p => p.ProjectId == project.Id)).ShouldBeGreaterThanOrEqualTo(50);
        (await db.Roles.CountAsync(r => r.ProjectId == project.Id)).ShouldBeGreaterThanOrEqualTo(50);
        (await db.Groups.CountAsync(g => g.ProjectId == project.Id)).ShouldBeGreaterThanOrEqualTo(50);
        (await db.UserProjects.CountAsync(u => u.ProjectId == project.Id)).ShouldBeGreaterThanOrEqualTo(50);

        // the scopes are registered and allowed for both sample applications
        var applications = await scope.ServiceProvider.GetRequiredService<IApplicationService>().ListAsync(project.Id);
        applications.Select(a => a.ClientId).ShouldBe(["minimal-api-sample", "mvc-sample", "worker-sample"], ignoreOrder: true);
        applications.ToDictionary(x => x.ClientId, x => x.ApplicationType).ShouldBe(new Dictionary<string, SystemX.Auth2.Contracts.Enums.ApplicationType> { ["mvc-sample"] = SystemX.Auth2.Contracts.Enums.ApplicationType.Web, ["minimal-api-sample"] = SystemX.Auth2.Contracts.Enums.ApplicationType.Api, ["worker-sample"] = SystemX.Auth2.Contracts.Enums.ApplicationType.Service }, ignoreOrder: true);
        foreach (var application in applications.Where(a => a.ClientId != "worker-sample")) DemoDataSeeder.ScopeNames.Except(application.AllowedScopes).ShouldBeEmpty();
        applications.Single(a => a.ClientId == "worker-sample").AllowedScopes.ShouldBe(["auth2.client.handshake", "auth2.permissions.sync", "auth2.roles.sync", "reports.read", "jobs.run"], ignoreOrder: true);   // the worker keeps only what it needs

        // the assignments: every demo role has permissions, every group has a role or a permission, every person has a way to get access
        foreach (var role in db.Roles.Where(r => r.ProjectId == project.Id && DemoDataSeeder.RoleNames.Contains(r.Name!)).Select(r => new { r.Name, Count = db.RolePermissions.Count(rp => rp.RoleId == r.Id) }))
            role.Count.ShouldBeGreaterThan(0, role.Name);
        foreach (var group in db.Groups.Where(g => g.ProjectId == project.Id && DemoDataSeeder.GroupNames.Contains(g.Name)).Select(g => new { g.Name, Count = db.GroupRoles.Count(x => x.GroupId == g.Id) + db.GroupPermissions.Count(x => x.GroupId == g.Id) }))
            group.Count.ShouldBeGreaterThan(0, group.Name);
        var people = await db.Users.Where(u => DemoDataSeeder.UserNames.Contains(u.UserName!)).Select(u => new
        {
            u.UserName, Ways = db.UserRoles.Count(x => x.UserId == u.Id) + db.UserGroups.Count(x => x.UserId == u.Id) + db.UserPermissions.Count(x => x.UserId == u.Id),
        }).ToListAsync();
        people.Count.ShouldBe(DemoDataSeeder.UserNames.Count);
        people.ShouldAllBe(p => p.Ways > 0);

        // a generated person signs in and has what the assignments give
        var liam = await MeAsync("liam.smith");
        (Names(liam, "roles").Length + Names(liam, "groups").Length + Names(liam, "permissions").Length).ShouldBeGreaterThan(0);
        Names(liam, "permissions").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task TheDemoData_BelongsToTheSampleProject_AndNothingOfItIsInGlobal()
    {
        await MigrateAndSeedAsync();
        await SeedAsync();

        (await CountsAsync((db, id) => db.UserProjects.Where(x => x.ProjectId == id && DemoDataSeeder.UserNames.Contains(db.Users.First(u => u.Id == x.UserId).UserName!)).Select(x => x.UserId)))
            .ShouldBe((DemoDataSeeder.UserNames.Count, 0));
        (await CountsAsync((db, id) => db.Roles.Where(r => r.ProjectId == id && DemoDataSeeder.RoleNames.Contains(r.Name!) && r.Name != "Admin").Select(r => r.Id)))
            .ShouldBe((DemoDataSeeder.RoleNames.Count - 1, 0));
        (await CountsAsync((db, id) => db.Groups.Where(g => g.ProjectId == id && DemoDataSeeder.GroupNames.Contains(g.Name)).Select(g => g.Id)))
            .ShouldBe((DemoDataSeeder.GroupNames.Count, 0));
        (await CountsAsync((db, id) => db.Permissions.Where(p => p.ProjectId == id && DemoDataSeeder.PermissionKeys.Contains(p.Key)).Select(p => p.Id)))
            .ShouldBe((DemoDataSeeder.PermissionKeys.Count, 0));
    }

    [Fact]
    public async Task DemoData_AlreadyPutIntoGlobalByAnOlderVersion_IsMovedToTheSampleProject()
    {
        await MigrateAndSeedAsync();
        await using (var scope = NewScope())
        {
            // what the older version did: the demo data created while the scope worked in _global
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            await scope.ServiceProvider.GetRequiredService<Auth2Seeder>().EnsureSampleProjectAndApplicationsAsync();
            scope.ServiceProvider.GetRequiredService<IProjectSelection>().ProjectKey = "_global";
            await scope.ServiceProvider.GetRequiredService<IProjectContext>().GetRequiredAsync();
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            (await access.CreateRoleAsync("OrdersViewer", "misplaced")).Succeeded.ShouldBeTrue();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<SystemX.Auth2.Server.Domain.Entities.AppUser>>();
            (await users.CreateAsync(new SystemX.Auth2.Server.Domain.Entities.AppUser { UserName = "alice", Email = "alice@example.com" }, DemoDataSeeder.Password)).Succeeded.ShouldBeTrue();
        }
        (await CountsAsync((db, id) => db.Roles.Where(r => r.ProjectId == id && r.Name == "OrdersViewer").Select(r => r.Id))).ShouldBe((0, 1));   // it is in _global

        await SeedAsync();

        (await CountsAsync((db, id) => db.Roles.Where(r => r.ProjectId == id && r.Name == "OrdersViewer").Select(r => r.Id))).ShouldBe((1, 0));
        (await CountsAsync((db, id) => db.UserProjects.Where(x => x.ProjectId == id && db.Users.First(u => u.Id == x.UserId).UserName == "alice").Select(x => x.UserId))).ShouldBe((1, 0));
        Names(await MeAsync("alice"), "roles").ShouldBe(["Admin"]);
    }

    [Fact]
    public async Task TheWorkerSampleApplication_GetsAMachineTokenThatTheMinimalApiSampleAccepts()
    {
        await MigrateAndSeedAsync();
        await SeedAsync();

        async Task<Microsoft.IdentityModel.JsonWebTokens.JsonWebToken> TokenAsync(string scope)
        {
            var response = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = "worker-sample", ["client_secret"] = "worker-sample-secret", ["scope"] = scope,
            });
            var body = await response.JsonAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
            return new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(body.Str("access_token"));
        }

        var token = await TokenAsync("reports.read jobs.run");
        token.Audiences.ShouldBe(["worker-sample", "reports-api", "minimal-api-sample"]);   // the MinimalApi sample accepts a token whose audience is its client id
        token.GetClaim("machine").Value.ShouldBe("true");

        // the worker may not ask for what it does not need
        var refused = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "worker-sample", ["client_secret"] = "worker-sample-secret", ["scope"] = "auth2.admin",
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunningItAgain_ChangesNothing()
    {
        await MigrateAndSeedAsync();
        await SeedAsync();
        (await SeedAsync()).ShouldBe(0);
        Names(await MeAsync("alice"), "roles").ShouldBe(["Admin"]);
    }
}
