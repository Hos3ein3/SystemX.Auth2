using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public abstract class AdminBase : Phase6Base
{
    protected const string Admin = Auth2Routes.Admin;
    protected string GlobalToken = "";     // GlobalAdmin, token of the _global project
    protected string ProjectToken = "";    // ProjectAdmin of appa
    protected string PlainToken = "";      // a normal user of appa (no admin role)
    protected AppUser Pam = null!;

    /// <summary>Two applications, the global admin, a project admin of appa and a plain user of appa. Tokens carry the admin scopes.</summary>
    protected async Task StartAsync()
    {
        await PrepareAppsAsync(both: s => s.Clients.AllowedScopes.AddRange(["auth2.admin", "auth2.admin.global"]));
        Auth2Application global; string globalSecret;
        await using (var scope = NewScope())
        {
            var apps = scope.ServiceProvider.GetRequiredService<IApplicationService>();
            global = (await apps.FindByProjectKeyAsync("_global"))!;
            globalSecret = await apps.RotateClientSecretAsync("_global");
        }
        await UpdateSettingsAsync(global, s =>
        {
            s.Clients.AllowSelfUiLogin = true;
            s.Clients.AllowedScopes.AddRange(["auth2.admin", "auth2.admin.global"]);
        });
        GlobalToken = (await TokenAsync("_global", globalSecret, "globaladmin", "Admin@12345!ChangeMe")).Str("access_token");

        Pam = await CreateUserInAsync("appa", "pam");
        await CreateUserInAsync("appa", "plain");
        await using (var scope = ScopeFor("appa"))
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
            (await roles.CreateAsync(new AppRole("ProjectAdmin") { ApplicationId = A.Id, IsSystem = true })).Succeeded.ShouldBeTrue();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.AddToRoleAsync((await users.FindByNameAsync("pam"))!, "ProjectAdmin")).Succeeded.ShouldBeTrue();
        }
        ProjectToken = (await UserTokensAsync("pam", PassA, "appa", "openid auth2.admin")).Str("access_token");
        PlainToken = (await UserTokensAsync("plain", PassA, "appa", "openid auth2.admin")).Str("access_token");
    }

    private async Task<JsonElement> TokenAsync(string clientId, string secret, string user, string password)
    {
        var r = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = user, ["password"] = password, ["scope"] = "openid auth2.admin auth2.admin.global",
            ["client_id"] = clientId, ["client_secret"] = secret,
        });
        var body = await r.JsonAsync();
        r.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        return body;
    }

    protected async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> SendAsync(HttpMethod method, string path, string? token, object? body = null, params (string, string)[] headers)
    {
        var r = await new Browser(Factory).SendJsonAsync(method, Admin + path, body, token, headers);
        var text = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), r);
    }

    protected Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> GetA(string path, string? token, params (string, string)[] h) => SendAsync(HttpMethod.Get, path, token, null, h);
}

public class AdminAccessControlTests : AdminBase
{
    [Fact]
    public async Task NoToken_BadToken_MachineToken_PlainUser_AndMissingScope_AreRefused()
    {
        await StartAsync();
        (await GetA("/users", null)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetA("/users", "bad")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetA("/users", await ClientTokenAsync("appa", "auth2.admin"))).Status.ShouldBe(HttpStatusCode.Forbidden);   // machine token
        var plain = await GetA("/users", PlainToken);
        plain.Status.ShouldBe(HttpStatusCode.Forbidden);
        plain.Body.Str("code").ShouldBe(Auth2ErrorCodes.Forbidden);
        var noScope = (await UserTokensAsync("pam", PassA, "appa", "openid")).Str("access_token");
        (await GetA("/users", noScope)).Body.Str("code").ShouldBe(Auth2ErrorCodes.InsufficientScope);
    }

    [Fact]
    public async Task GlobalRoutes_NeedGlobalAdmin_AndTheGlobalScope()
    {
        await StartAsync();
        (await GetA("/applications", ProjectToken)).Status.ShouldBe(HttpStatusCode.OK);   // a project admin sees only the own application
        var post = await SendAsync(HttpMethod.Post, "/applications", ProjectToken, new CreateApplicationRequest("newapp", "New"));
        post.Status.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/applications/{B.Id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProjectAdmin_CannotChooseAnotherApplication_GlobalAdminCan()
    {
        await StartAsync();
        var denied = await GetA("/users", ProjectToken, ("X-Auth2-Application", "appb"));
        denied.Status.ShouldBe(HttpStatusCode.Forbidden);
        (await GetA("/users", ProjectToken, ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.OK);
        (await GetA("/users", GlobalToken, ("X-Auth2-Application", "appb"))).Status.ShouldBe(HttpStatusCode.OK);
        (await GetA("/users", GlobalToken, ("X-Auth2-Application", "nope"))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProjectAdmin_SeesOnlyOwnApplications_AndHiddenOnesAre404()
    {
        await StartAsync();
        var list = await GetA("/applications", ProjectToken);
        list.Body.GetProperty("items").EnumerateArray().Select(a => a.Str("projectKey")).ShouldBe(["appa"]);
        (await GetA($"/applications/{A.Id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.OK);
        (await GetA($"/applications/{B.Id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await GetA($"/applications/{B.Id}/settings", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await GetA("/applications", GlobalToken)).Body.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(3);   // _global, appa, appb
    }

    [Fact]
    public async Task ProjectAdmin_CannotReadAnotherApplicationsUsersRolesOrAudit()
    {
        await StartAsync();
        await using (var scope = ScopeFor("appb"))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.CreateAsync(new AppUser { UserName = "bob", Email = "bob@example.com" }, PassB);
            await scope.ServiceProvider.GetRequiredService<IAccessService>().CreateRoleAsync("BOnly");
        }
        var users2 = (await GetA("/users", ProjectToken)).Body.GetProperty("items").EnumerateArray().Select(u => u.Str("userName")).ToList();
        users2.ShouldNotContain("bob");
        (await GetA("/roles", ProjectToken)).Body.GetProperty("items").EnumerateArray().Select(r => r.Str("name")).ShouldNotContain("BOnly");
        var bobId = (await GetA("/users", GlobalToken, ("X-Auth2-Application", "appb"))).Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await GetA($"/users/{bobId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await GetA("/audit-events?all=true", ProjectToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        var ownAudit = (await GetA("/audit-events", ProjectToken)).Body.GetProperty("items").EnumerateArray().ToList();
        ownAudit.ShouldAllBe(e => e.GetProperty("applicationId").GetGuid() == A.Id);
    }

    [Fact]
    public async Task ProjectAdmin_CannotGrantGlobalAdmin()
    {
        await StartAsync();
        var plain = (await GetA("/users?q=plain", ProjectToken)).Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        // The global role is not even visible in a normal project.
        var roles = (await GetA("/roles", ProjectToken)).Body.GetProperty("items").EnumerateArray().Select(r => r.Str("name")).ToList();
        roles.ShouldNotContain("GlobalAdmin");

        // In the _global project a project admin still cannot hand it out.
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var globalRoleId = (await db.Roles.SingleAsync(r => r.Name == "GlobalAdmin")).Id;
        (await SendAsync(HttpMethod.Put, $"/users/{plain}/roles/{globalRoleId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }
}

public class AdminApplicationTests : AdminBase
{
    [Fact]
    public async Task GlobalAdmin_CreatesGetsUpdatesAndDeletesAnApplication_AndTheSecretIsShownOnce()
    {
        await StartAsync();
        var created = await SendAsync(HttpMethod.Post, "/applications", GlobalToken, new CreateApplicationRequest("shop", "Shop", "A shop"));
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body.ToString());
        var id = created.Body.GetProperty("application").GetProperty("id").GetGuid();
        created.Body.Str("clientSecret").ShouldNotBeNullOrEmpty();
        (await GetA($"/applications/{id}", GlobalToken)).Body.ToString().ShouldNotContain("clientSecret");

        (await SendAsync(HttpMethod.Post, "/applications", GlobalToken, new CreateApplicationRequest("shop", "Again"))).Status.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, "/applications", GlobalToken, new CreateApplicationRequest("_global", "x"))).Status.ShouldBe(HttpStatusCode.BadRequest);

        var updated = await SendAsync(HttpMethod.Put, $"/applications/{id}", GlobalToken, new UpdateApplicationRequest("Shop 2", null, false));
        updated.Body.Str("name").ShouldBe("Shop 2");
        updated.Body.GetProperty("isEnabled").GetBoolean().ShouldBeFalse();

        (await SendAsync(HttpMethod.Delete, $"/applications/{id}", GlobalToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/applications/{id}", GlobalToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await AuditAsync("application.deleted")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheGlobalApplication_CannotBeDeletedOrDisabled()
    {
        await StartAsync();
        var list = await GetA("/applications?q=global", GlobalToken);
        var g = list.Body.GetProperty("items")[0];
        var id = g.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Delete, $"/applications/{id}", GlobalToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/applications/{id}", GlobalToken, new UpdateApplicationRequest("Global", null, false))).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Settings_ETagAndIfMatch_AreEnforced()
    {
        await StartAsync();
        var get = await GetA($"/applications/{B.Id}/settings", GlobalToken);
        var etag = get.Response.Headers.ETag!.Tag;
        var json = get.Body.GetRawText();

        var changed = json.Replace("\"minLength\":" + get.Body.GetProperty("password").GetProperty("minLength").GetInt32(), "\"minLength\":12");
        var ok = await SendAsync(HttpMethod.Put, $"/applications/{B.Id}/settings", GlobalToken, changed, ("If-Match", etag));
        ok.Status.ShouldBe(HttpStatusCode.NoContent, ok.Body.ToString());
        var stale = await SendAsync(HttpMethod.Put, $"/applications/{B.Id}/settings", GlobalToken, changed, ("If-Match", etag));
        stale.Status.ShouldBe(HttpStatusCode.Conflict);
        (await GetA($"/applications/{B.Id}/settings", GlobalToken)).Body.GetProperty("password").GetProperty("minLength").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task Settings_InvalidValues_AreRejectedWithDetails()
    {
        await StartAsync();
        var get = await GetA($"/applications/{B.Id}/settings", GlobalToken);
        var bad = get.Body.GetRawText().Replace("\"minLength\":" + get.Body.GetProperty("password").GetProperty("minLength").GetInt32(), "\"minLength\":1");
        var r = await SendAsync(HttpMethod.Put, $"/applications/{B.Id}/settings", GlobalToken, bad);
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.ValidationFailed);
        (await SendAsync(HttpMethod.Put, $"/applications/{B.Id}/settings", GlobalToken, "{not json")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ProjectAdmin_MayEditOnlyProjectAdminEditableSettings()
    {
        await StartAsync();
        var get = await GetA($"/applications/{A.Id}/settings", ProjectToken);
        var node = System.Text.Json.Nodes.JsonNode.Parse(get.Body.GetRawText())!;
        node["localization"]!["brandName"] = "Pam's Blog";
        var ok = await SendAsync(HttpMethod.Put, $"/applications/{A.Id}/settings", ProjectToken, node.ToJsonString());
        ok.Status.ShouldBe(HttpStatusCode.NoContent, ok.Body.ToString());

        node["clients"]!["allowSelfUiLogin"] = false;   // GlobalOnly
        var denied = await SendAsync(HttpMethod.Put, $"/applications/{A.Id}/settings", ProjectToken, node.ToJsonString());
        denied.Status.ShouldBe(HttpStatusCode.Forbidden);
        denied.Body.GetProperty("details").GetProperty("errors").ToString().ShouldContain("Clients.AllowSelfUiLogin");

        var uris = await SendAsync(HttpMethod.Put, $"/applications/{A.Id}/redirect-uris", ProjectToken, new RedirectUrisDto(["https://evil.test/cb"], []));
        uris.Status.ShouldBe(HttpStatusCode.Forbidden);   // redirect URIs are GlobalOnly
        var globalUris = await SendAsync(HttpMethod.Put, $"/applications/{A.Id}/redirect-uris", GlobalToken, new RedirectUrisDto(["https://ok.test/cb"], []));
        globalUris.Status.ShouldBe(HttpStatusCode.NoContent, globalUris.Body.ToString());
        (await GetA($"/applications/{A.Id}/redirect-uris", ProjectToken)).Body.Values("redirectUris").ShouldBe(["https://ok.test/cb"]);
    }

    [Fact]
    public async Task SecretRotation_NeedsGlobalAdmin_OrTheSetting_AndTheOldSecretStopsWorking()
    {
        await StartAsync();
        (await SendAsync(HttpMethod.Post, $"/applications/{A.Id}/secret/rotate", ProjectToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/applications/{B.Id}/secret/rotate", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);

        var rotated = await SendAsync(HttpMethod.Post, $"/applications/{A.Id}/secret/rotate", GlobalToken);
        var secret = rotated.Body.Str("clientSecret");
        secret.ShouldNotBe(ASecret);
        var old = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = "appa", ["client_secret"] = ASecret });
        old.StatusCode.ShouldBe(HttpStatusCode.BadRequest.Equals(old.StatusCode) ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized);
        var fresh = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = "appa", ["client_secret"] = secret });
        fresh.StatusCode.ShouldBe(HttpStatusCode.OK);

        await UpdateSettingsAsync(A, s => s.Clients.AllowProjectAdminRotateSecret = true);
        (await SendAsync(HttpMethod.Post, $"/applications/{A.Id}/secret/rotate", ProjectToken)).Status.ShouldBe(HttpStatusCode.OK);
    }
}

public class AdminUserTests : AdminBase
{
    private async Task<Guid> UserIdAsync(string name, string? token = null) =>
        (await GetA($"/users?q={name}", token ?? ProjectToken)).Body.GetProperty("items")[0].GetProperty("id").GetGuid();

    [Fact]
    public async Task List_IsPagedFilteredAndSorted_AndPageSizeIsCapped()
    {
        await StartAsync();
        for (var i = 0; i < 5; i++) await SendAsync(HttpMethod.Post, "/users", ProjectToken, new AdminCreateUserRequest($"user{i}", $"user{i}@example.com", null, PassA));
        var page = await GetA("/users?pageSize=3&page=2&sort=username", ProjectToken);
        page.Body.GetProperty("totalCount").GetInt32().ShouldBe(7);   // pam, plain + 5 (the global admin is not in appa)
        page.Body.GetProperty("items").GetArrayLength().ShouldBe(3);
        page.Body.GetProperty("pageSize").GetInt32().ShouldBe(3);
        (await GetA("/users?pageSize=100000", ProjectToken)).Body.GetProperty("pageSize").GetInt32().ShouldBe(200);
        var search = await GetA("/users?q=USER3", ProjectToken);
        search.Body.GetProperty("items").EnumerateArray().Select(u => u.Str("userName")).ShouldBe(["user3"]);
        var desc = await GetA("/users?sort=username&desc=true&pageSize=2", ProjectToken);
        desc.Body.GetProperty("items")[0].Str("userName").ShouldBe("user4");
    }

    [Fact]
    public async Task Create_NewUser_CanSignIn_AndAnExistingPersonOnlyJoins_WithoutAPassword()
    {
        await StartAsync();
        var created = await SendAsync(HttpMethod.Post, "/users", ProjectToken, new AdminCreateUserRequest("nina", "nina@example.com", null, PassA, "Nina", "K"));
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body.ToString());
        created.Body.GetProperty("userWasCreated").GetBoolean().ShouldBeTrue();
        (await UserTokensAsync("nina", PassA)).Str("access_token").ShouldNotBeNullOrEmpty();

        // The same person is added to appb by its admin: no password is set, the person cannot log in there.
        var joined = await SendAsync(HttpMethod.Post, "/users", GlobalToken, new AdminCreateUserRequest("nina", "nina@example.com", null, "Hacker-Pass-1!"), ("X-Auth2-Application", "appb"));
        joined.Status.ShouldBe(HttpStatusCode.Created);
        joined.Body.GetProperty("userWasCreated").GetBoolean().ShouldBeFalse();
        joined.Body.GetProperty("joinedApplication").GetBoolean().ShouldBeTrue();
        var attempt = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "nina", ["password"] = "Hacker-Pass-1!", ["scope"] = "openid", ["client_id"] = "appb", ["client_secret"] = BSecret,
        });
        attempt.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await UserTokensAsync("nina", PassA)).Str("access_token").ShouldNotBeNullOrEmpty();   // appa password untouched
    }

    [Fact]
    public async Task Create_WithNothing_OrAWeakPassword_IsRejected()
    {
        await StartAsync();
        (await SendAsync(HttpMethod.Post, "/users", ProjectToken, new AdminCreateUserRequest(null, null, null, PassA))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, "/users", ProjectToken, new AdminCreateUserRequest("weak", "weak@example.com", null, "123"))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_UsesETag_AndDeactivationEndsSessions()
    {
        await StartAsync();
        var id = await UserIdAsync("plain");
        var get = await GetA($"/users/{id}", ProjectToken);
        var etag = get.Response.Headers.ETag!.Tag;
        var ok = await SendAsync(HttpMethod.Put, $"/users/{id}", ProjectToken, new AdminUpdateUserRequest("Pl", "Ain", null, null, null), ("If-Match", etag));
        ok.Status.ShouldBe(HttpStatusCode.OK);
        ok.Body.Str("firstName").ShouldBe("Pl");
        (await SendAsync(HttpMethod.Put, $"/users/{id}", ProjectToken, new AdminUpdateUserRequest("X", null, null, null, null), ("If-Match", etag))).Status.ShouldBe(HttpStatusCode.Conflict);

        (await SendAsync(HttpMethod.Put, $"/users/{id}", ProjectToken, new AdminUpdateUserRequest(null, null, null, null, false))).Status.ShouldBe(HttpStatusCode.OK);
        (await GetA("/users", PlainToken)).Status.ShouldBe(HttpStatusCode.Unauthorized);   // the token of a deactivated user stops working
    }

    [Fact]
    public async Task LockUnlock_BlockLogin_EndSessions_AndAreAudited()
    {
        await StartAsync();
        var id = await UserIdAsync("plain");
        var token = (await UserTokensAsync("plain", PassA)).Str("access_token");
        (await SendAsync(HttpMethod.Post, $"/users/{id}/lock", ProjectToken, new LockUserRequest())).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/users/{id}", ProjectToken)).Body.GetProperty("isLockedOut").GetBoolean().ShouldBeTrue();
        (await new Browser(Factory).GetAsync(Auth2Routes.Me, token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // the old token is dead
        var login = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "plain", ["password"] = PassA, ["scope"] = "openid", ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        login.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await GetA("/users?lockedOut=true", ProjectToken)).Body.GetProperty("items").GetArrayLength().ShouldBe(1);

        (await SendAsync(HttpMethod.Post, $"/users/{id}/unlock", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await UserTokensAsync("plain", PassA)).Str("access_token").ShouldNotBeNullOrEmpty();
        (await AuditAsync("user.locked")).Single().ActorUserId.ShouldBe(Pam.Id);   // the admin is recorded
        (await AuditAsync("user.unlocked")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnAdmin_CannotLockOrRemoveThemselves()
    {
        await StartAsync();
        (await SendAsync(HttpMethod.Post, $"/users/{Pam.Id}/lock", ProjectToken)).Status.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Delete, $"/users/{Pam.Id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ResetTwoFactor_TurnsItOff()
    {
        await StartAsync();
        var id = await UserIdAsync("plain");
        await using (var scope = ScopeFor("appa"))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = (await users.FindByNameAsync("plain"))!;
            await users.ResetAuthenticatorKeyAsync(u);
            await users.SetTwoFactorEnabledAsync(u, true);
        }
        (await GetA($"/users/{id}", ProjectToken)).Body.GetProperty("twoFactorEnabled").GetBoolean().ShouldBeTrue();
        (await SendAsync(HttpMethod.Post, $"/users/{id}/reset-2fa", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/users/{id}", ProjectToken)).Body.GetProperty("twoFactorEnabled").GetBoolean().ShouldBeFalse();
        (await AuditAsync("user.2fa_reset")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sessions_ListAndRevoke()
    {
        await StartAsync();
        var id = await UserIdAsync("plain");
        var token = (await UserTokensAsync("plain", PassA, "appa", "openid offline_access auth2.me")).Str("access_token");
        var sessions = await GetA($"/users/{id}/sessions", ProjectToken);
        sessions.Body.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        (await SendAsync(HttpMethod.Delete, $"/users/{id}/sessions/does-not-exist", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/users/{id}/sessions", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await new Browser(Factory).GetAsync(Auth2Routes.Me, token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetA($"/users/{id}/sessions", ProjectToken)).Body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Remove_TakesThePersonOutOfThisApplicationOnly()
    {
        await StartAsync();
        var plain = await UserAsync("plain@example.com");
        await JoinAsync(plain, B);
        var id = plain.Id;
        await using (var scope = ScopeFor("appa"))
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            await access.CreatePermissionAsync(new PermissionDefinition("a.one"));
            await access.GrantPermissionAsync((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id.ToString()))!, "a.one");
        }
        (await SendAsync(HttpMethod.Delete, $"/users/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/users/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await HasRowAsync("plain@example.com", A)).ShouldBeFalse();
        (await HasRowAsync("plain@example.com", B)).ShouldBeTrue();
        (await GetA($"/users/{id}", GlobalToken, ("X-Auth2-Application", "appb"))).Status.ShouldBe(HttpStatusCode.OK);
        await using var check = NewScope();
        var db = check.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = check.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserPermissions.CountAsync(p => p.UserId == id)).ShouldBe(0);
    }
}

public class AdminAccessApiTests : AdminBase
{
    private async Task<Guid> IdOf(string path, string name, string field = "name") =>
        (await GetA(path, ProjectToken)).Body.GetProperty("items").EnumerateArray().Single(x => x.Str(field) == name).GetProperty("id").GetGuid();

    [Fact]
    public async Task PermissionsRolesGroups_FullFlow_ChangesEffectiveAccess_AndIsAudited()
    {
        await StartAsync();
        var plain = (await GetA("/users?q=plain", ProjectToken)).Body.GetProperty("items")[0].GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, "/permissions", ProjectToken, new PermissionDefinition("orders.read", "Read"))).Status.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, "/permissions", ProjectToken, new PermissionDefinition("bad key"))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, "/roles", ProjectToken, new CreateRoleRequest("Editor"))).Status.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, "/groups", ProjectToken, new CreateGroupRequest("finance"))).Status.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, "/groups", ProjectToken, new CreateGroupRequest("finance"))).Status.ShouldBe(HttpStatusCode.Conflict);

        var roleId = await IdOf("/roles", "Editor");
        var groupId = await IdOf("/groups", "finance");
        (await SendAsync(HttpMethod.Put, $"/roles/{roleId}/permissions/orders.read", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Put, $"/groups/{groupId}/roles/{roleId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Put, $"/groups/{groupId}/members/{plain}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);

        var access = await GetA($"/users/{plain}/access", ProjectToken);
        access.Body.Values("permissions").ShouldBe(["orders.read"]);
        access.Body.Values("roles").ShouldBe(["Editor"]);
        access.Body.Values("groups").ShouldBe(["finance"]);

        (await GetA($"/groups/{groupId}/members", ProjectToken)).Body.EnumerateArray().Select(x => x.GetGuid()).ShouldBe([plain]);
        (await SendAsync(HttpMethod.Delete, $"/groups/{groupId}/members/{plain}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/users/{plain}/access", ProjectToken)).Body.Values("permissions").ShouldBeEmpty();

        (await SendAsync(HttpMethod.Put, $"/users/{plain}/roles/{roleId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Put, $"/users/{plain}/permissions/orders.read", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/users/{plain}/access", ProjectToken)).Body.Values("roles").ShouldBe(["Editor"]);

        var permId = await IdOf("/permissions", "orders.read", "key");
        (await SendAsync(HttpMethod.Put, $"/permissions/{permId}/obsolete", ProjectToken, new SetObsoleteRequest(true))).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA("/permissions", ProjectToken)).Body.GetProperty("items")[0].GetProperty("isObsolete").GetBoolean().ShouldBeTrue();
        (await SendAsync(HttpMethod.Delete, $"/permissions/{permId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/users/{plain}/access", ProjectToken)).Body.Values("permissions").ShouldBeEmpty();

        // every write is in the audit log with the admin who made it
        var events = (await GetA("/audit-events?pageSize=200", ProjectToken)).Body.GetProperty("items").EnumerateArray().ToList();
        events.Select(e => e.Str("action")).ShouldContain("permission.created");
        events.Select(e => e.Str("action")).ShouldContain("group.user_added");
        events.Where(e => e.Str("action").StartsWith("permission.") || e.Str("action").StartsWith("group.") || e.Str("action").StartsWith("role."))
            .ShouldAllBe(e => e.GetProperty("actorUserId").GetGuid() == Pam.Id);
    }

    [Fact]
    public async Task UnknownIds_AreNotFound_AndRoleOfAnotherApplicationIsInvisible()
    {
        await StartAsync();
        await using (var scope = ScopeFor("appb")) await scope.ServiceProvider.GetRequiredService<IAccessService>().CreateRoleAsync("BRole");
        var bRole = (await GetA("/roles", GlobalToken, ("X-Auth2-Application", "appb"))).Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Delete, $"/roles/{bRole}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/roles/{Guid.NewGuid()}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/groups/{Guid.NewGuid()}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProjectAdmin_CanMakeAnotherUserProjectAdmin_ButNotThemselvesNone()
    {
        await StartAsync();
        var plain = (await GetA("/users?q=plain", ProjectToken)).Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Put, $"/users/{plain}/project-admin", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        var fresh = (await UserTokensAsync("plain", PassA, "appa", "openid auth2.admin")).Str("access_token");
        (await GetA("/users", fresh)).Status.ShouldBe(HttpStatusCode.OK);

        var roleId = await IdOf("/roles", "ProjectAdmin");
        (await SendAsync(HttpMethod.Delete, $"/users/{Pam.Id}/roles/{roleId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Delete, $"/users/{plain}/roles/{roleId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA("/users", fresh)).Status.ShouldBe(HttpStatusCode.Forbidden);   // the role is looked up on every call
    }

    [Fact]
    public async Task Audit_FilterByActionAndPaging_AndGlobalAdminCanSeeAll()
    {
        await StartAsync();
        await SendAsync(HttpMethod.Post, "/permissions", ProjectToken, new PermissionDefinition("x.one"));
        await SendAsync(HttpMethod.Post, "/permissions", GlobalToken, new PermissionDefinition("y.one"), ("X-Auth2-Application", "appb"));
        var own = await GetA("/audit-events?action=permission.created", ProjectToken);
        own.Body.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var all = await GetA("/audit-events?action=permission.created&all=true", GlobalToken);
        all.Body.GetProperty("totalCount").GetInt32().ShouldBe(2);
        (await GetA("/audit-events?pageSize=1", ProjectToken)).Body.GetProperty("items").GetArrayLength().ShouldBe(1);
        all.Body.GetProperty("items")[0].GetProperty("ipAddress").ValueKind.ShouldNotBe(JsonValueKind.Undefined);
    }
}

public class AdminSingleModeTests : IdentityTestBase
{
    protected override bool SingleMode => true;

    [Fact]
    public async Task SingleMode_GlobalAdminManagesUsers_AndApplicationCreateDeleteAreRefused()
    {
        await MigrateAndSeedAsync();
        string secret;
        await using (var scope = NewScope())
        {
            var apps = scope.ServiceProvider.GetRequiredService<IApplicationService>();
            secret = await apps.RotateClientSecretAsync("default");
            var app = (await apps.FindByProjectKeyAsync("default"))!;
            var settings = Persistence.SettingsJson.Clone(app.Settings);
            settings.Clients.AllowSelfUiLogin = true;
            settings.Clients.AllowedScopes.AddRange(["auth2.admin", "auth2.admin.global"]);
            await apps.UpdateSettingsAsync(app.Id, settings);
        }
        var b = new Browser(Factory);
        var t = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "globaladmin", ["password"] = "Admin@12345!ChangeMe", ["scope"] = "openid auth2.admin auth2.admin.global",
            ["client_id"] = "default", ["client_secret"] = secret,
        });
        var token = (await t.JsonAsync()).Str("access_token");

        var create = await b.SendJsonAsync(HttpMethod.Post, Auth2Routes.Admin + "/users", new AdminCreateUserRequest("sara", "sara@example.com", null, PassA), token);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var list = await (await b.SendJsonAsync(HttpMethod.Get, Auth2Routes.Admin + "/users", null, token)).JsonAsync();
        list.GetProperty("items").EnumerateArray().Select(u => u.Str("userName")).ShouldBe(["globaladmin", "sara"], ignoreOrder: true);
        (await b.SendJsonAsync(HttpMethod.Post, Auth2Routes.Admin + "/applications", new CreateApplicationRequest("another", "Another"), token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var apps2 = await (await b.SendJsonAsync(HttpMethod.Get, Auth2Routes.Admin + "/applications", null, token)).JsonAsync();
        apps2.GetProperty("items").EnumerateArray().ShouldAllBe(a => a.GetProperty("projectKey").ValueKind == JsonValueKind.Null);
    }
}
