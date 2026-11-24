using System.Net;
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

/// <summary>A new member gets the roles, groups and permissions of <c>Registration.AssignedRoles</c>, <c>AssignedGroups</c> and <c>AssignedPermissions</c>.</summary>
public class RegistrationDefaultsTests : Phase6Base
{
    private async Task CreateCatalogAsync()
    {
        await using var scope = ScopeFor("appa");
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        (await access.CreatePermissionAsync(new PermissionDefinition("orders.read"))).Succeeded.ShouldBeTrue();
        (await access.CreateRoleAsync("Editor")).Succeeded.ShouldBeTrue();
        (await scope.ServiceProvider.GetRequiredService<IGroupService>().CreateAsync("finance")).Succeeded.ShouldBeTrue();
    }

    private async Task RegisterAsync(string clientId, string userName, string email)
    {
        var token = await ClientTokenAsync(clientId, "auth2.register");
        (await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest(userName, email, null, "Very-Secret-Pass-1!"), token)).Status.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<(IList<string> Roles, List<string> Groups, List<string> Permissions)> AccessOfAsync(string userName)
    {
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByNameAsync(userName))!;
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var groups = await (from ug in db.UserGroups join g in db.Groups on ug.GroupId equals g.Id where ug.UserId == user.Id select g.Name).ToListAsync();
        var permissions = await (from up in db.UserPermissions join p in db.Permissions on up.PermissionId equals p.Id where up.UserId == user.Id select p.Key).ToListAsync();
        return (await users.GetRolesAsync(user), groups, permissions);
    }

    [Fact]
    public async Task ARegistration_GivesTheAssignedRolesGroupsAndPermissions_OnlyInThatProject()
    {
        await PrepareAppsAsync();
        await CreateCatalogAsync();
        await UpdateSettingsAsync(A, s =>
        {
            s.Project.Registration.AssignedRoles = ["Editor"];
            s.Project.Registration.AssignedGroups = ["finance"];
            s.Project.Registration.AssignedPermissions = ["orders.read"];
        });

        await RegisterAsync("appa", "sara", "sara@example.com");
        var (roles, groups, permissions) = await AccessOfAsync("sara");
        roles.ShouldBe(["Editor"]);
        groups.ShouldBe(["finance"]);
        permissions.ShouldBe(["orders.read"]);

        await RegisterAsync("appb", "tom", "tom@example.com");   // another project: nothing is assigned there
        await using var scope = ScopeFor("appb");
        var tom = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("tom"))!;
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetRolesAsync(tom)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AMissingName_IsSkippedAndAudited_AndTheRegistrationStillWorks()
    {
        await PrepareAppsAsync();
        await CreateCatalogAsync();
        await UpdateSettingsAsync(A, s =>
        {
            s.Project.Registration.AssignedRoles = ["Editor", "Ghost"];
            s.Project.Registration.AssignedGroups = ["nobody"];
        });

        await RegisterAsync("appa", "sara", "sara@example.com");
        var (roles, groups, _) = await AccessOfAsync("sara");
        roles.ShouldBe(["Editor"]);
        groups.ShouldBeEmpty();

        await using var scope = ScopeFor("appa");
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var audit = await db.AuditEvents.AsNoTracking().Where(e => e.Action == "register.defaults_applied").SingleAsync();
        var data = audit.DataJson ?? "";
        data.ShouldContain("role:Ghost");
        data.ShouldContain("group:nobody");
        data.ShouldContain("role:Editor");
    }

    [Fact]
    public async Task WithNothingAssigned_NothingIsAssigned_AndNoDefaultsEventIsWritten()
    {
        await PrepareAppsAsync();
        await RegisterAsync("appa", "sara", "sara@example.com");
        var (roles, groups, permissions) = await AccessOfAsync("sara");
        (roles.Count + groups.Count + permissions.Count).ShouldBe(0);
    }
}
