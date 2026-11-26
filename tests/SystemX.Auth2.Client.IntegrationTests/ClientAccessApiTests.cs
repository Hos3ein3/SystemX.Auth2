using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Client;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.IntegrationTests;

/// <summary><see cref="IAuth2AdminClientService"/>: roles, permissions, groups, scopes and their links, with the token of an admin, against the real Server.</summary>
public class ClientAccessApiTests : ClientAppTestBase
{
    private IAuth2ApiClientService Api = null!;
    private IAuth2AdminClientService Access = null!;       // the admin
    private IAuth2AdminClientService PlainAccess = null!;  // a person who is no admin
    private string AdminToken = "";     // token of a ProjectAdmin
    private string Plain = "";     // token of a person who is no admin
    private Guid SaraId;

    private async Task StartAsync()
    {
        await StartAppAsync(_ => { });
        await UpdateSettingsAsync(s => s.AllowedScopes.Add("auth2.admin"));
        await CreateUserAsync("root");
        SaraId = (await CreateUserAsync("sara")).Id;
        await AccessAsync("root", async (a, g, u) => { await a.CreateRoleAsync("ProjectAdmin"); await a.AssignRoleAsync(u, "ProjectAdmin"); });
        var api = ApiClient.GetRequiredService<IAuth2ApiClientService>();
        AdminToken = (await api.LoginAsync(new LoginRequest("root", Password, ["openid", "auth2.admin"]))).Value!.AccessToken!;
        Plain = (await api.LoginAsync(new LoginRequest("sara", Password, ["openid", "auth2.admin"]))).Value!.AccessToken!;
        Api = api;
        Access = api.WithTokens(AdminToken).Admin;
        PlainAccess = api.WithTokens(Plain).Admin;
    }

    [Fact]
    public async Task Roles_Permissions_Groups_CreateReadChangeDelete()
    {
        await StartAsync();

        var role = await Access.Roles.CreateAsync(new CreateRoleRequest("Editor", "Edits"));
        role.IsSuccess.ShouldBeTrue(role.Error?.Code);
        role.Value!.Id.ShouldNotBe(Guid.Empty);
        (await Access.Roles.CreateAsync(new CreateRoleRequest("Editor"))).IsSuccess.ShouldBeFalse();
        (await Access.Roles.CreateAsync(new CreateRoleRequest("GlobalAdmin"))).Error!.Code.ShouldBe(Auth2ErrorCodes.Forbidden);

        (await Access.Roles.UpdateAsync(role.Value.Id, new UpdateRoleRequest(Description: "Edits articles"))).IsSuccess.ShouldBeTrue();
        (await Access.Roles.GetAsync(role.Value.Id)).Value!.Description.ShouldBe("Edits articles");
        (await Access.Roles.ListAsync(new PageRequest(Search: "edit"))).Value!.Items.Select(r => r.Name).ShouldBe(["Editor"]);
        (await Access.Roles.GetAsync(Guid.NewGuid())).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);

        var permission = await Access.Permissions.CreateAsync(new PermissionDefinition("Articles.Write", "Write articles"));
        permission.IsSuccess.ShouldBeTrue(permission.Error?.Code);
        permission.Value!.Key.ShouldBe("articles.write");
        (await Access.Permissions.CreateAsync(new PermissionDefinition("bad key"))).IsSuccess.ShouldBeFalse();
        (await Access.Permissions.UpdateAsync(permission.Value.Id, new UpdatePermissionRequest(Description: "Can write", Group: "Articles"))).IsSuccess.ShouldBeTrue();
        var changed = (await Access.Permissions.GetAsync(permission.Value.Id)).Value!;
        (changed.DisplayName, changed.Description, changed.Group).ShouldBe(("Write articles", "Can write", "Articles"));
        (await Access.Permissions.SetObsoleteAsync(permission.Value.Id, true)).IsSuccess.ShouldBeTrue();
        (await Access.Permissions.GetAsync(permission.Value.Id)).Value!.IsObsolete.ShouldBeTrue();
        (await Access.Permissions.ListAsync()).Value!.Items.Select(p => p.Key).ShouldContain("articles.write");

        var group = await Access.Groups.CreateAsync(new CreateGroupRequest("Newsroom", "News"));
        group.IsSuccess.ShouldBeTrue(group.Error?.Code);
        group.Value!.Name.ShouldBe("newsroom");
        (await Access.Groups.CreateAsync(new CreateGroupRequest("newsroom"))).Error!.Code.ShouldBe(Auth2ErrorCodes.Conflict);
        (await Access.Groups.UpdateAsync(group.Value.Id, new UpdateGroupRequest(Description: "The news team"))).IsSuccess.ShouldBeTrue();
        (await Access.Groups.GetAsync(group.Value.Id)).Value!.Description.ShouldBe("The news team");

        (await Access.Roles.DeleteAsync(role.Value.Id)).IsSuccess.ShouldBeTrue();
        (await Access.Permissions.DeleteAsync(permission.Value.Id)).IsSuccess.ShouldBeTrue();
        (await Access.Groups.DeleteAsync(group.Value.Id)).IsSuccess.ShouldBeTrue();
        (await Access.Roles.GetAsync(role.Value.Id)).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);
        (await Access.Groups.DeleteAsync(group.Value.Id)).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);
    }

    [Fact]
    public async Task TheLinks_RolePermissions_GroupRolesAndPermissions_GroupMembers_UserRolesPermissionsAndGroups()
    {
        await StartAsync();
        var role = (await Access.Roles.CreateAsync(new CreateRoleRequest("Editor"))).Value!;
        var read = (await Access.Permissions.CreateAsync(new PermissionDefinition("articles.read"))).Value!;
        var write = (await Access.Permissions.CreateAsync(new PermissionDefinition("articles.write"))).Value!;
        var group = (await Access.Groups.CreateAsync(new CreateGroupRequest("newsroom"))).Value!;

        // role_permissions
        (await Access.Roles.AddPermissionAsync(role.Id, "articles.read")).IsSuccess.ShouldBeTrue();
        (await Access.Roles.AddPermissionAsync(role.Id, "articles.read")).IsSuccess.ShouldBeTrue();   // again: fine
        (await Access.Roles.AddPermissionAsync(role.Id, "no.such.key")).IsSuccess.ShouldBeFalse();
        (await Access.Roles.ListPermissionsAsync(role.Id)).Value!.ShouldBe(["articles.read"]);

        // group_roles and group_permissions and user_groups
        (await Access.Groups.AddRoleAsync(group.Id, role.Id)).IsSuccess.ShouldBeTrue();
        (await Access.Groups.AddPermissionAsync(group.Id, "articles.write")).IsSuccess.ShouldBeTrue();
        (await Access.Groups.AddMemberAsync(group.Id, SaraId)).IsSuccess.ShouldBeTrue();
        var inGroup = (await Access.Groups.GetAsync(group.Id)).Value!;
        inGroup.Roles.ShouldBe(["Editor"]);
        inGroup.Permissions.ShouldBe(["articles.write"]);
        inGroup.MemberCount.ShouldBe(1);
        (await Access.Groups.ListMembersAsync(group.Id)).Value!.ShouldBe([SaraId]);

        var access = (await Access.Users.GetAccessAsync(SaraId)).Value!;
        access.Roles.ShouldContain("Editor");
        access.Groups.ShouldContain("newsroom");
        access.Permissions.ShouldContain("articles.read");    // through the role of the group
        access.Permissions.ShouldContain("articles.write");   // through the group

        (await Access.Groups.RemoveRoleAsync(group.Id, role.Id)).IsSuccess.ShouldBeTrue();
        (await Access.Groups.RemovePermissionAsync(group.Id, "articles.write")).IsSuccess.ShouldBeTrue();
        (await Access.Groups.RemoveMemberAsync(group.Id, SaraId)).IsSuccess.ShouldBeTrue();
        (await Access.Groups.RemoveMemberAsync(group.Id, SaraId)).IsSuccess.ShouldBeTrue();   // not a member any more: fine
        (await Access.Groups.ListMembersAsync(group.Id)).Value!.ShouldBeEmpty();
        (await Access.Users.GetAccessAsync(SaraId)).Value!.Permissions.ShouldNotContain("articles.write");

        // user_roles and user_permissions
        (await Access.Users.AssignRoleAsync(SaraId, role.Id)).IsSuccess.ShouldBeTrue();
        (await Access.Users.GrantPermissionAsync(SaraId, "articles.write")).IsSuccess.ShouldBeTrue();
        access = (await Access.Users.GetAccessAsync(SaraId)).Value!;
        access.Roles.ShouldContain("Editor");
        access.Permissions.ShouldContain("articles.write");
        (await Access.Users.RevokePermissionAsync(SaraId, "articles.write")).IsSuccess.ShouldBeTrue();
        (await Access.Users.RemoveRoleAsync(SaraId, role.Id)).IsSuccess.ShouldBeTrue();
        access = (await Access.Users.GetAccessAsync(SaraId)).Value!;
        access.Roles.ShouldNotContain("Editor");
        access.Permissions.ShouldNotContain("articles.write");

        (await Access.Roles.RemovePermissionAsync(role.Id, "articles.read")).IsSuccess.ShouldBeTrue();
        (await Access.Roles.ListPermissionsAsync(role.Id)).Value!.ShouldBeEmpty();
        (await Access.Users.AssignRoleAsync(Guid.NewGuid(), role.Id)).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);

        (await Access.Users.MakeProjectAdminAsync(SaraId)).IsSuccess.ShouldBeTrue();
        (await Access.Users.GetAccessAsync(SaraId)).Value!.Roles.ShouldContain("ProjectAdmin");
    }

    [Fact]
    public async Task Users_CreateReadChangeLockSessionsAndDelete()
    {
        await StartAsync();
        var users = Access.Users;

        var created = await users.CreateAsync(new AdminCreateUserRequest("nora", "nora@example.com", null, "Pass-12345!x", "Nora", "N"));
        created.IsSuccess.ShouldBeTrue(created.Error?.Code);
        created.Value!.UserWasCreated.ShouldBeTrue();
        var id = created.Value.UserId;
        var again = await users.CreateAsync(new AdminCreateUserRequest("nora", "other@example.com", null, "Pass-12345!x"));   // the person exists: only joined (here already a member)
        again.Value!.UserWasCreated.ShouldBeFalse();
        again.Value.UserId.ShouldBe(id);
        (await users.CreateAsync(new AdminCreateUserRequest("weak", "weak@example.com", null, "1"))).IsSuccess.ShouldBeFalse();                 // the password rules apply

        var one = (await users.GetAsync(id)).Value!;
        (one.UserName, one.Email, one.FirstName, one.IsActive).ShouldBe(("nora", "nora@example.com", "Nora", true));
        (await users.GetAsync(Guid.NewGuid())).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);
        (await users.ListAsync(new PageRequest(Search: "nora"))).Value!.Items.Select(u => u.UserName).ShouldContain("nora");

        var changed = await users.UpdateAsync(id, new AdminUpdateUserRequest("Noor", null, "Noor N", null, null));
        changed.IsSuccess.ShouldBeTrue(changed.Error?.Code);
        (changed.Value!.FirstName, changed.Value.LastName).ShouldBe(("Noor", "N"));   // null kept the last name

        (await users.LockAsync(id)).IsSuccess.ShouldBeTrue();
        (await users.GetAsync(id)).Value!.IsLockedOut.ShouldBeTrue();
        (await users.ListAsync(lockedOut: true)).Value!.Items.Select(u => u.Id).ShouldContain(id);
        (await users.ListAsync(lockedOut: false)).Value!.Items.Select(u => u.Id).ShouldNotContain(id);
        (await users.UnlockAsync(id)).IsSuccess.ShouldBeTrue();
        (await users.GetAsync(id)).Value!.IsLockedOut.ShouldBeFalse();

        (await users.ListSessionsAsync(id)).IsSuccess.ShouldBeTrue();
        (await users.RevokeAllSessionsAsync(id)).IsSuccess.ShouldBeTrue();
        (await users.ResetTwoFactorAsync(id)).IsSuccess.ShouldBeTrue();

        // the links of a user
        var role = (await Access.Roles.CreateAsync(new CreateRoleRequest("Editor"))).Value!;
        var group = (await Access.Groups.CreateAsync(new CreateGroupRequest("newsroom"))).Value!;
        (await users.AssignRoleAsync(id, role.Id)).IsSuccess.ShouldBeTrue();
        (await users.AddToGroupAsync(id, group.Id)).IsSuccess.ShouldBeTrue();
        var access = (await users.GetAccessAsync(id)).Value!;
        access.Roles.ShouldContain("Editor");
        access.Groups.ShouldContain("newsroom");
        (await users.RemoveFromGroupAsync(id, group.Id)).IsSuccess.ShouldBeTrue();
        (await users.RemoveRoleAsync(id, role.Id)).IsSuccess.ShouldBeTrue();

        (await users.DeleteAsync(id)).IsSuccess.ShouldBeTrue();
        (await users.GetAsync(id)).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);
        (await PlainAccess.Users.ListAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.Forbidden);   // not an admin
    }

    [Fact]
    public async Task Scopes_AnAdminReads_AProjectAdminCannotChange()
    {
        await StartAsync();
        (await Access.Scopes.ListAsync()).IsSuccess.ShouldBeTrue();
        (await Access.Scopes.CreateAsync(new CreateScopeRequest("reports.read"))).Error!.Code.ShouldBe(Auth2ErrorCodes.Forbidden);
        var id = Guid.NewGuid();
        (await Access.Scopes.UpdateAsync(id, new UpdateScopeRequest(DisplayName: "x"))).IsSuccess.ShouldBeFalse();
        (await Access.Scopes.DeleteAsync(id)).IsSuccess.ShouldBeFalse();
        (await Access.Scopes.GetAsync(id)).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);
    }

    [Fact]
    public async Task APersonWhoIsNoAdmin_AnEmptyToken_AndAnotherProject_AreRefusedWithCodes()
    {
        await StartAsync();
        (await PlainAccess.Roles.ListAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.Forbidden);
        (await PlainAccess.Roles.CreateAsync(new CreateRoleRequest("X"))).Error!.Code.ShouldBe(Auth2ErrorCodes.Forbidden);
        (await Api.WithTokens(null).Admin.Roles.ListAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);   // no request, no token
        (await Api.WithTokens("not-a-token").Admin.Roles.ListAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);

        // a ProjectAdmin never works on another project
        (await Access.ForProject(Guid.NewGuid()).Roles.ListAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.NotFound);
        // the machine token of the application is not an admin
        var machine = await MachineTokenAsync(Auth2Scopes.RolesSync);
        (await Api.WithTokens(machine).Admin.Roles.ListAsync()).IsSuccess.ShouldBeFalse();
    }
}

/// <summary><see cref="IAuth2AdminClientService"/> from <c>IAuth2WebClientService.Admin</c>: the same calls with the token of the cookie.</summary>
public class ClientWebAccessTests : ClientAppTestBase
{
    private static void Map(IEndpointRouteBuilder e)
    {
        e.MapPost("/login", async (HttpContext http, IAuth2WebClientService web) =>
        {
            var form = await http.Request.ReadFormAsync();
            var scopes = form["scopes"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = await web.LoginAsync(new LoginRequest(form["login"].ToString(), form["password"].ToString(), scopes));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(new { result.Error!.Code });
        }).WithAnonymous();
        e.MapGet("/roles", async (HttpContext http, IAuth2WebClientService web) =>
        {
            var r = await web.Admin.Roles.ListAsync();
            return r.IsSuccess ? Results.Json(r.Value!.Items.Select(x => x.Name)) : Results.Json(new { r.Error!.Code }, statusCode: 400);
        }).WithAnonymous();
        e.MapPost("/roles", async (HttpContext http, IAuth2WebClientService web, string name) =>
        {
            var r = await web.Admin.Roles.CreateAsync(new CreateRoleRequest(name));
            return r.IsSuccess ? Results.Json(new { r.Value!.Id }) : Results.Json(new { r.Error!.Code }, statusCode: 400);
        }).WithAnonymous();
        e.MapDelete("/roles/{id:guid}", async (HttpContext http, IAuth2WebClientService web, Guid id) =>
        {
            var r = await web.Admin.Roles.DeleteAsync(id);
            return r.IsSuccess ? Results.NoContent() : Results.Json(new { r.Error!.Code }, statusCode: 400);
        }).WithAnonymous();
    }

    private async Task<Jar> StartAsync(string scopes)
    {
        await StartAppAsync(Map, b => b.UseSelfUi().UseCookieAuthentication(o => o.SecurePolicy = CookieSecurePolicy.SameAsRequest), handshake: true);
        await UpdateSettingsAsync(s => s.AllowedScopes.Add("auth2.admin"));
        await CreateUserAsync("root");
        await AccessAsync("root", async (a, g, u) => { await a.CreateRoleAsync("ProjectAdmin"); await a.AssignRoleAsync(u, "ProjectAdmin"); });
        var jar = new Jar(App.GetTestClient());
        var login = await jar.SendAsync(HttpMethod.Post, "/login", "application/json", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["login"] = "root", ["password"] = Password, ["scopes"] = scopes }));
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return jar;
    }

    [Fact]
    public async Task AnAdminWithTheAdminScope_ManagesRolesWithTheCookie()
    {
        var jar = await StartAsync("openid offline_access auth2.admin");
        var created = await jar.SendAsync(HttpMethod.Post, "/roles?name=Editor", "application/json");
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await (await jar.GetAsync("/roles", "application/json")).Content.ReadFromJsonAsync<string[]>())!.ShouldContain("Editor");
        (await jar.SendAsync(HttpMethod.Delete, $"/roles/{id}", "application/json")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await jar.GetAsync("/roles", "application/json")).Content.ReadFromJsonAsync<string[]>())!.ShouldNotContain("Editor");
    }

    [Fact]
    public async Task ASessionWithoutTheAdminScope_GetsAnError_NotAnException()
    {
        var jar = await StartAsync("openid offline_access auth2.me");
        var response = await jar.GetAsync("/roles", "application/json");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.InsufficientScope);
    }

    [Fact]
    public async Task WithoutACookie_TheAnswerIsInvalidToken()
    {
        await StartAppAsync(Map, b => b.UseSelfUi().UseCookieAuthentication(o => o.SecurePolicy = CookieSecurePolicy.SameAsRequest), handshake: true);
        var response = await new Jar(App.GetTestClient()).GetAsync("/roles", "application/json");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.InvalidToken);
    }
}
