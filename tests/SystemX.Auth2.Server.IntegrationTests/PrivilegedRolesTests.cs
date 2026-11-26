using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// The roles GlobalAdmin and Developer: never deleted, never created by anybody else, and given or taken away only by a Developer or a GlobalAdmin.
/// </summary>
public class PrivilegedRolesTests : Phase6Base
{
    /// <summary>Makes the code run "as" this person: a web request with a signed-in user (or an anonymous one, or none at all).</summary>
    private sealed class Acting : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        public Acting(AsyncServiceScope scope, Guid? userId, bool signedIn = true)
        {
            _accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            _accessor.HttpContext = userId is null && !signedIn
                ? new DefaultHttpContext { RequestServices = scope.ServiceProvider }
                : new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity(userId is null ? [] : [new Claim("sub", userId.Value.ToString())], "test")) };
        }
        public void Dispose() => _accessor.HttpContext = null;
    }

    private async Task<(AppUser Boss, AppUser Plain, AppUser Target)> PeopleAsync()
    {
        await PrepareAppsAsync();
        // GlobalAdmin and Developer are global roles: they can be managed only in the _global project (or in single-project mode)
        var boss = await CreateUserInAsync("_global", "boss");
        var plain = await CreateUserInAsync("_global", "plain");
        var target = await CreateUserInAsync("_global", "target");
        await using var scope = ScopeFor("_global");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.AddToRoleAsync((await users.FindByIdAsync(boss.Id.ToString("D")))!, Auth2Roles.Developer)).Succeeded.ShouldBeTrue();   // the seed way: straight, not through the service
        return (boss, plain, target);
    }

    private static async Task<AppUser> Fresh(AsyncServiceScope scope, AppUser user) =>
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id.ToString("D")))!;

    // ---------------------------------------------------------------- delete

    [Theory]
    [InlineData("GlobalAdmin")]
    [InlineData("Developer")]
    public async Task TheBuiltInRoles_CanNeverBeDeleted_NotEvenByADeveloper(string role)
    {
        var (boss, _, _) = await PeopleAsync();
        await using var scope = ScopeFor("_global");
        using var _ = new Acting(scope, boss.Id);
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().DeleteRoleAsync(role)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);

        // and not by any other code that uses the role manager
        var manager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var entity = (await manager.FindByNameAsync(role))!;
        (await manager.DeleteAsync(entity)).Succeeded.ShouldBeFalse();
        (await manager.FindByNameAsync(role)).ShouldNotBeNull();
    }

    // ---------------------------------------------------------------- create

    [Theory]
    [InlineData("Developer")]
    [InlineData("developer")]
    [InlineData(" GlobalAdmin ")]
    [InlineData("globaladmin")]
    public async Task NoOneCreatesARoleWithTheNameOfABuiltInRole(string name)
    {
        var (boss, _, _) = await PeopleAsync();
        await using var scope = ScopeFor("_global");
        using var _ = new Acting(scope, boss.Id);
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().CreateRoleAsync(name)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);

        // not through the role manager either: the Server reads these roles by name
        var manager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        (await manager.CreateAsync(new AppRole(name.Trim()) { ProjectId = A.Id })).Succeeded.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- give and take away

    [Theory]
    [InlineData("GlobalAdmin")]
    [InlineData("Developer")]
    public async Task OnlyADeveloperOrAGlobalAdmin_GivesTheBuiltInRoles(string role)
    {
        var (boss, plain, target) = await PeopleAsync();

        await using (var scope = ScopeFor("_global"))
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            using (new Acting(scope, plain.Id))
                (await access.AssignRoleAsync(await Fresh(scope, target), role)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);   // a person without the role
            using (new Acting(scope, null, signedIn: false))
                (await access.AssignRoleAsync(await Fresh(scope, target), role)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);   // nobody signed in
            using (new Acting(scope, Guid.NewGuid()))
                (await access.AssignRoleAsync(await Fresh(scope, target), role)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);   // a signed-in id that is nobody
            (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().IsInRoleAsync(await Fresh(scope, target), role)).ShouldBeFalse();
        }

        await using (var scope = ScopeFor("_global"))
        {
            using var _ = new Acting(scope, boss.Id);   // a Developer
            (await scope.ServiceProvider.GetRequiredService<IAccessService>().AssignRoleAsync(await Fresh(scope, target), role)).Succeeded.ShouldBeTrue();
            (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().IsInRoleAsync(await Fresh(scope, target), role)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task OnlyADeveloperOrAGlobalAdmin_TakesAwayTheBuiltInRoles()
    {
        var (boss, plain, target) = await PeopleAsync();
        await using var scope = ScopeFor("_global");
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        using (new Acting(scope, boss.Id)) (await access.AssignRoleAsync(await Fresh(scope, target), Auth2Roles.Developer)).Succeeded.ShouldBeTrue();

        using (new Acting(scope, plain.Id))
            (await access.RemoveRoleAsync(await Fresh(scope, target), Auth2Roles.Developer)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().IsInRoleAsync(await Fresh(scope, target), Auth2Roles.Developer)).ShouldBeTrue();

        using (new Acting(scope, boss.Id))
            (await access.RemoveRoleAsync(await Fresh(scope, target), Auth2Roles.Developer)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task InAnOrdinaryProject_TheBuiltInRoles_CannotBeSeenNorGiven()
    {
        await PrepareAppsAsync();
        var target = await CreateUserInAsync("appa", "target");
        var boss = await CreateUserInAsync("appa", "boss");
        await using var scope = ScopeFor("appa");
        using var _ = new Acting(scope, boss.Id);
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        foreach (var role in new[] { Auth2Roles.GlobalAdmin, Auth2Roles.Developer })
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(target.Id.ToString("D")))!;
            (await access.AssignRoleAsync(user, role)).Succeeded.ShouldBeFalse();
            (await access.DeleteRoleAsync(role)).Succeeded.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task OrdinaryRoles_AreStillGivenByAnyAdmin()
    {
        var (_, plain, target) = await PeopleAsync();
        await using var scope = ScopeFor("_global");
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        (await access.CreateRoleAsync("Editor")).Succeeded.ShouldBeTrue();
        using var _ = new Acting(scope, plain.Id);   // not privileged: that is fine for an ordinary role
        (await access.AssignRoleAsync(await Fresh(scope, target), "Editor")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CodeThatIsNotAPerson_TheSeedAndJobs_IsTrusted()
    {
        var (_, _, target) = await PeopleAsync();
        await using var scope = ScopeFor("_global");   // no web request at all
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().AssignRoleAsync(await Fresh(scope, target), Auth2Roles.Developer)).Succeeded.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- groups

    [Fact]
    public async Task TheBuiltInRoles_CannotBeGivenThroughAGroup_ByAnOrdinaryAdmin()
    {
        var (boss, plain, target) = await PeopleAsync();
        await using var scope = ScopeFor("_global");
        var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
        (await groups.CreateAsync("ops")).Succeeded.ShouldBeTrue();

        using (new Acting(scope, plain.Id))
            (await groups.AddRoleAsync("ops", Auth2Roles.Developer)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);
        using (new Acting(scope, boss.Id))
            (await groups.AddRoleAsync("ops", Auth2Roles.Developer)).Succeeded.ShouldBeTrue();

        // a group that has the role gives it to its members, so joining it follows the same rule
        using (new Acting(scope, plain.Id))
        {
            (await groups.AddUserAsync("ops", await Fresh(scope, target))).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);
            (await groups.RemoveRoleAsync("ops", Auth2Roles.Developer)).ErrorCode.ShouldBe(Auth2ErrorCodes.Forbidden);
        }
        using (new Acting(scope, boss.Id))
            (await groups.AddUserAsync("ops", await Fresh(scope, target))).Succeeded.ShouldBeTrue();
    }
}
