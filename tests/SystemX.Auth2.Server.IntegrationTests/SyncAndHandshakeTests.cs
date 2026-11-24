using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class HandshakeTests : Phase6Base
{
    private const string Scope = "auth2.client.handshake";

    private async Task<(HttpStatusCode Status, JsonElement Body)> HandshakeAsync(string token, string? key, string name) =>
        await CallAsync(new Browser(Factory), Auth2Routes.Handshake, new ClientHandshakeRequest(key, name, "1.0"), token);

    [Fact]
    public async Task Multi_Ok_ReturnsModeIssuerVersionAndFlags()
    {
        await PrepareAppsAsync();
        await UpdateSettingsAsync(A, s => { s.Sessions.SingleActiveSession = true; s.Sessions.CookieLifetimeMinutes = 90; });
        var (status, body) = await HandshakeAsync(await ClientTokenAsync("appa", Scope), "appa", "App A");
        status.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.Str("projectKey").ShouldBe("appa");
        body.Str("serverMode").ShouldBe("MultiProject");
        body.Str("applicationName").ShouldBe("App A");
        body.GetProperty("singleActiveSession").GetBoolean().ShouldBeTrue();
        body.Str("cookieLifetime").ShouldBe("01:30:00");
        body.Str("issuer").ShouldStartWith("http");
        body.GetProperty("applicationId").GetGuid().ShouldBe(A.Id);
    }

    [Fact]
    public async Task Multi_ProjectKeyOfAnotherApplication_IsRejected_AndNeverSelectsTheApplication()
    {
        await PrepareAppsAsync();
        var (status, body) = await HandshakeAsync(await ClientTokenAsync("appa", Scope), "appb", "App B");   // token is App A's
        status.ShouldBe(HttpStatusCode.Conflict);
        body.Str("code").ShouldBe(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
    }

    [Fact]
    public async Task Multi_WrongName_IsRejected()
    {
        await PrepareAppsAsync();
        var (status, body) = await HandshakeAsync(await ClientTokenAsync("appa", Scope), "appa", "Other name");
        status.ShouldBe(HttpStatusCode.Conflict);
        body.Str("code").ShouldBe(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
    }

    [Fact]
    public async Task Multi_MissingProjectKey_IsAServerModeMismatch()
    {
        await PrepareAppsAsync();
        var (status, body) = await HandshakeAsync(await ClientTokenAsync("appa", Scope), null, "App A");
        status.ShouldBe(HttpStatusCode.Conflict);
        body.Str("code").ShouldBe(Auth2ErrorCodes.ServerModeMismatch);
    }

    [Fact]
    public async Task NeedsTheHandshakeScope_AndAMachineToken()
    {
        await PrepareAppsAsync();
        (await HandshakeAsync(await ClientTokenAsync("appa", "auth2.register"), "appa", "App A")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await HandshakeAsync("bad", "appa", "App A")).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DisabledApplication_IsRefused()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            await db.Projects.Where(a => a.Id == A.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsEnabled, false));
        }
        scopeCacheNote();
        var (status, body) = await HandshakeAsync(token, "appa", "App A");
        status.ShouldBe(HttpStatusCode.Forbidden);
        body.Str("code").ShouldBe(Auth2ErrorCodes.ProjectDisabled);

        static void scopeCacheNote() { }
    }
}

public class SingleModeHandshakeTests : IdentityTestBase
{
    protected override bool SingleMode => true;

    [Fact]
    public async Task Single_NoProjectKey_Ok_WithProjectKey_IsAModeMismatch()
    {
        await MigrateAndSeedAsync();
        string secret, name;
        await using (var scope = NewScope())
        {
            secret = await scope.ServiceProvider.GetRequiredService<IApplicationService>().RotateSecretAsync("default");
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            name = (await db.Projects.SingleAsync(a => a.ProjectKey == "default")).Name;
        }
        var b = new Browser(Factory);
        var t = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "default", ["client_secret"] = secret, ["scope"] = "auth2.client.handshake",
        });
        var token = (await t.JsonAsync()).Str("access_token");

        var ok = await b.PostJsonAsync(Auth2Routes.Handshake, new ClientHandshakeRequest(null, name, "1.0"), token);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement;
        body.Str("serverMode").ShouldBe("SingleProject");
        body.TryGetProperty("projectKey", out var key).ShouldBeTrue();
        key.ValueKind.ShouldBe(JsonValueKind.Null);

        var bad = await b.PostJsonAsync(Auth2Routes.Handshake, new ClientHandshakeRequest("default", name, "1.0"), token);
        bad.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement.Str("code").ShouldBe(Auth2ErrorCodes.ServerModeMismatch);

        var wrongName = await b.PostJsonAsync(Auth2Routes.Handshake, new ClientHandshakeRequest(null, "nope", "1.0"), token);
        JsonDocument.Parse(await wrongName.Content.ReadAsStringAsync()).RootElement.Str("code").ShouldBe(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
    }
}

public class PermissionSyncTests : Phase6Base
{
    private const string Scope = "auth2.permissions.sync auth2.roles.sync auth2.groups.read";

    private async Task<(HttpStatusCode Status, JsonElement Body)> SyncPermsAsync(string token, params PermissionDefinition[] items) =>
        await CallAsync(new Browser(Factory), Auth2Routes.PermissionsSync, new PermissionSyncRequest(items), token);

    private async Task<List<Permission>> PermsAsync(Project app)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.Permissions.Where(p => p.ProjectId == app.Id).OrderBy(p => p.Key).ToListAsync();
    }

    [Fact]
    public async Task Sync_AddsNormalizesAndIsIdempotent()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var first = await SyncPermsAsync(token, new PermissionDefinition("Orders.Read", "Read orders", null, "Orders"), new PermissionDefinition("orders.write"), new PermissionDefinition(" orders.read "));   // duplicate after normalizing
        first.Status.ShouldBe(HttpStatusCode.OK, first.Body.ToString());
        first.Body.GetProperty("added").GetInt32().ShouldBe(2);
        first.Body.Values("addedKeys").ShouldBe(["orders.read", "orders.write"]);

        var second = await SyncPermsAsync(token, new PermissionDefinition("orders.read", "Read orders", null, "Orders"), new PermissionDefinition("orders.write"));
        second.Body.GetProperty("added").GetInt32().ShouldBe(0);
        second.Body.GetProperty("updated").GetInt32().ShouldBe(0);
        second.Body.GetProperty("unchanged").GetInt32().ShouldBe(2);
        (await PermsAsync(A)).Select(p => (p.Key, p.Source)).ShouldBe([("orders.read", PermissionSource.Sync), ("orders.write", PermissionSource.Sync)]);
        (await AuditAsync("permissions.synced")).Count.ShouldBe(1);   // the second call changed nothing
    }

    [Fact]
    public async Task Sync_NeverDeletes_NeverOverwritesAdminText_AndRestoresObsolete()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await SyncPermsAsync(token, new PermissionDefinition("a.one", "One"), new PermissionDefinition("a.two"));
        await using (var scope = ScopeFor("appa"))
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            await access.CreatePermissionAsync(new PermissionDefinition("admin.only", "Admin text"));
            await access.SetPermissionObsoleteAsync("a.two", true);
        }
        var r = await SyncPermsAsync(token, new PermissionDefinition("a.one", "One changed"), new PermissionDefinition("a.two"), new PermissionDefinition("admin.only", "Client text"));
        r.Body.GetProperty("updated").GetInt32().ShouldBe(2);   // a.one text, a.two restored

        var perms = await PermsAsync(A);
        perms.Single(p => p.Key == "a.one").DisplayName.ShouldBe("One changed");
        perms.Single(p => p.Key == "a.two").IsObsolete.ShouldBeFalse();
        perms.Single(p => p.Key == "admin.only").DisplayName.ShouldBe("Admin text");   // admin wins

        await SyncPermsAsync(token, new PermissionDefinition("a.one"));   // others missing: nothing is removed
        (await PermsAsync(A)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Sync_WithTheOptInSetting_MarksMissingSyncedPermissionsObsolete_ButNotAdminOnes()
    {
        await PrepareAppsAsync(both: s => s.Clients.AllowRemovePermissionsFromSync = true);
        var token = await ClientTokenAsync("appa", Scope);
        await SyncPermsAsync(token, new PermissionDefinition("a.one"), new PermissionDefinition("a.two"));
        await using (var scope = ScopeFor("appa"))
            await scope.ServiceProvider.GetRequiredService<IAccessService>().CreatePermissionAsync(new PermissionDefinition("admin.only"));
        await SyncPermsAsync(token, new PermissionDefinition("a.one"));
        var perms = await PermsAsync(A);
        perms.Single(p => p.Key == "a.two").IsObsolete.ShouldBeTrue();
        perms.Single(p => p.Key == "a.one").IsObsolete.ShouldBeFalse();
        perms.Single(p => p.Key == "admin.only").IsObsolete.ShouldBeFalse();
        perms.Count.ShouldBe(3);   // still nothing deleted
    }

    [Fact]
    public async Task Sync_OneInvalidKey_RejectsTheWholeRequest()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var r = await SyncPermsAsync(token, new PermissionDefinition("good.key"), new PermissionDefinition("bad key"));
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.InvalidPermissionKey);
        (await PermsAsync(A)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sync_TooManyItems_IsRefused()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var many = Enumerable.Range(0, 2001).Select(i => new PermissionDefinition($"p.{i}")).ToArray();
        (await SyncPermsAsync(token, many)).Body.Str("code").ShouldBe(Auth2ErrorCodes.TooManyItems);
        (await SyncPermsAsync(token, many.Take(2000).ToArray())).Status.ShouldBe(HttpStatusCode.OK);
        (await PermsAsync(A)).Count.ShouldBe(2000);
    }

    [Fact]
    public async Task Sync_NotAllowed_Is403_AndNeedsTheScope()
    {
        await PrepareAppsAsync(both: s => s.Clients.PermissionSyncAllowed = false);
        var token = await ClientTokenAsync("appa", Scope);
        var r = await SyncPermsAsync(token, new PermissionDefinition("a.one"));
        r.Status.ShouldBe(HttpStatusCode.Forbidden);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.SyncNotAllowed);

        var noScope = await ClientTokenAsync("appb", "auth2.groups.read");
        (await SyncPermsAsync(noScope, new PermissionDefinition("a.one"))).Body.Str("code").ShouldBe(Auth2ErrorCodes.InsufficientScope);
    }

    [Fact]
    public async Task Sync_GoesToTheTokensApplication_Only()
    {
        await PrepareAppsAsync();
        await SyncPermsAsync(await ClientTokenAsync("appa", Scope), new PermissionDefinition("a.one"));
        await SyncPermsAsync(await ClientTokenAsync("appb", Scope), new PermissionDefinition("a.one"), new PermissionDefinition("b.only"));
        (await PermsAsync(A)).Select(p => p.Key).ShouldBe(["a.one"]);
        (await PermsAsync(B)).Select(p => p.Key).ShouldBe(["a.one", "b.only"]);
    }

    [Fact]
    public async Task ParallelSyncs_OfTheSameKeys_EndWithOneRowEach()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SyncPermsAsync(token, new PermissionDefinition("x.one"), new PermissionDefinition("x.two"), new PermissionDefinition("x.three"))));
        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        (await PermsAsync(A)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Pull_ReturnsPermissionsRolesAndGroups_OfTheTokensApplication()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await SyncPermsAsync(token, new PermissionDefinition("a.one", "One"));
        await using (var scope = ScopeFor("appa"))
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
            await access.CreateRoleAsync("Editor");
            await access.AddRolePermissionAsync("Editor", "a.one");
            await groups.CreateAsync("finance");
            await groups.AddRoleAsync("finance", "Editor");
        }
        await SyncPermsAsync(await ClientTokenAsync("appb", Scope), new PermissionDefinition("b.only"));

        var b = new Browser(Factory);
        var perms = JsonDocument.Parse(await (await b.GetAsync(Auth2Routes.Permissions, token)).Content.ReadAsStringAsync()).RootElement;
        perms.EnumerateArray().Select(p => p.Str("key")).ShouldBe(["a.one"]);
        var roles = JsonDocument.Parse(await (await b.GetAsync(Auth2Routes.Roles, token)).Content.ReadAsStringAsync()).RootElement;
        roles.EnumerateArray().Single().Values("permissions").ShouldBe(["a.one"]);
        var groups2 = JsonDocument.Parse(await (await b.GetAsync(Auth2Routes.Groups, token)).Content.ReadAsStringAsync()).RootElement;
        groups2.EnumerateArray().Single().Values("roles").ShouldBe(["Editor"]);

        var none = await ClientTokenAsync("appa", "auth2.client.handshake");
        (await b.GetAsync(Auth2Routes.Permissions, none)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

public class RoleSyncTests : Phase6Base
{
    private const string Scope = "auth2.permissions.sync auth2.roles.sync";

    private async Task<(HttpStatusCode Status, JsonElement Body)> SyncRolesAsync(string token, params RoleDefinition[] items) =>
        await CallAsync(new Browser(Factory), Auth2Routes.RolesSync, new RoleSyncRequest(items), token);

    [Fact]
    public async Task Sync_CreatesRoles_AddsPermissionLinks_CreatesUnknownPermissions_AndIsIdempotent()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var first = await SyncRolesAsync(token, new RoleDefinition("Editor", null, "Edits", ["Orders.Read", "orders.write"]), new RoleDefinition("Viewer"));
        first.Status.ShouldBe(HttpStatusCode.OK, first.Body.ToString());
        first.Body.GetProperty("added").GetInt32().ShouldBe(2);
        first.Body.Values("addedKeys").ShouldBe(["Editor", "Viewer"]);

        var second = await SyncRolesAsync(token, new RoleDefinition("editor", null, "Edits", ["orders.read"]), new RoleDefinition("Viewer"));
        second.Body.GetProperty("added").GetInt32().ShouldBe(0);
        second.Body.GetProperty("unchanged").GetInt32().ShouldBe(2);

        await using var scope = ScopeFor("appa");
        var roles = await scope.ServiceProvider.GetRequiredService<IAccessService>().ListRolesAsync();
        roles.Single(r => r.Name == "Editor").Permissions.ShouldBe(["orders.read", "orders.write"]);   // never removed
        roles.Single(r => r.Name == "Editor").Description.ShouldBe("Edits");
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().ListPermissionsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sync_AddingPermissionsToARole_RaisesTheApplicationAccessVersion()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await SyncRolesAsync(token, new RoleDefinition("Editor"));
        async Task<int> Version() { await using var s = NewScope(); var db = s.ServiceProvider.GetRequiredService<Auth2DbContext>(); using var _ = s.ServiceProvider.GetRequiredService<IGlobalScope>().Begin(); return (await db.Projects.AsNoTracking().SingleAsync(a => a.Id == A.Id)).AccessVersion; }
        var before = await Version();
        await SyncRolesAsync(token, new RoleDefinition("Editor", null, null, ["a.one"]));
        (await Version()).ShouldBeGreaterThan(before);
        var same = await Version();
        await SyncRolesAsync(token, new RoleDefinition("Editor", null, null, ["a.one"]));
        (await Version()).ShouldBe(same);
    }

    [Fact]
    public async Task Sync_WithAnEmptyPermissionList_RemovesNothing()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await SyncRolesAsync(token, new RoleDefinition("Editor", null, null, ["a.one"]));
        await SyncRolesAsync(token, new RoleDefinition("Editor", null, null, []));
        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().ListRolesAsync()).Single().Permissions.ShouldBe(["a.one"]);
    }

    [Fact]
    public async Task Sync_RoleOfAnotherApplicationWithTheSameName_IsSeparate()
    {
        await PrepareAppsAsync();
        await SyncRolesAsync(await ClientTokenAsync("appa", Scope), new RoleDefinition("Editor", null, null, ["a.one"]));
        await SyncRolesAsync(await ClientTokenAsync("appb", Scope), new RoleDefinition("Editor", null, null, ["b.one"]));
        await using var a = ScopeFor("appa");
        await using var b = ScopeFor("appb");
        (await a.ServiceProvider.GetRequiredService<IAccessService>().ListRolesAsync()).Single().Permissions.ShouldBe(["a.one"]);
        (await b.ServiceProvider.GetRequiredService<IAccessService>().ListRolesAsync()).Single().Permissions.ShouldBe(["b.one"]);
    }

    [Theory]
    [InlineData("GlobalAdmin")]
    [InlineData("projectadmin")]
    [InlineData("bad/name")]
    [InlineData("")]
    public async Task Sync_ReservedOrInvalidNames_AreRejected(string name)
    {
        await PrepareAppsAsync();
        var r = await SyncRolesAsync(await ClientTokenAsync("appa", Scope), new RoleDefinition("Fine"), new RoleDefinition(name));
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.ValidationFailed);
        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<IAccessService>().ListRolesAsync()).ShouldBeEmpty();   // all or nothing
    }

    [Fact]
    public async Task Sync_InvalidPermissionKeyInARole_IsRejected()
    {
        await PrepareAppsAsync();
        var r = await SyncRolesAsync(await ClientTokenAsync("appa", Scope), new RoleDefinition("Editor", null, null, ["bad key"]));
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.InvalidPermissionKey);
    }

    [Fact]
    public async Task Sync_NotAllowed_Is403()
    {
        await PrepareAppsAsync(both: s => s.Clients.RoleSyncAllowed = false);
        var r = await SyncRolesAsync(await ClientTokenAsync("appa", Scope), new RoleDefinition("Editor"));
        r.Status.ShouldBe(HttpStatusCode.Forbidden);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.SyncNotAllowed);
    }

    [Fact]
    public async Task Sync_UnknownPermissionsInRoles_NeedPermissionSyncToBeAllowed()
    {
        await PrepareAppsAsync(both: s => s.Clients.PermissionSyncAllowed = false);
        var r = await SyncRolesAsync(await ClientTokenAsync("appa", Scope), new RoleDefinition("Editor", null, null, ["a.one"]));
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.SyncNotAllowed);
    }

    [Fact]
    public async Task ParallelSyncs_OfTheSameRoles_EndWithOneRole()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SyncRolesAsync(token, new RoleDefinition("Editor", null, null, ["a.one", "a.two"]))));
        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        await using var scope = ScopeFor("appa");
        var roles = await scope.ServiceProvider.GetRequiredService<IAccessService>().ListRolesAsync();
        roles.Count.ShouldBe(1);
        roles[0].Permissions.ShouldBe(["a.one", "a.two"]);
    }
}

public class UserAccessEndpointTests : Phase6Base
{
    [Fact]
    public async Task MachineWithAdminScope_ReadsOneUsersAccess_OnlyInItsApplication()
    {
        await PrepareAppsAsync();
        foreach (var app in new[] { A, B }) await UpdateSettingsAsync(app, s => s.AllowedScopes.Add("auth2.admin"));
        var reg = await ClientTokenAsync("appa", "auth2.register");
        var user = (await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("sara", "sara@example.com", null, PassA), reg)).Body;
        var id = user.GetProperty("userId").GetGuid();
        await using (var scope = ScopeFor("appa"))
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
            var u = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id.ToString()))!;
            await access.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await access.GrantPermissionAsync(u, "orders.read");
        }

        var b = new Browser(Factory);
        var ok = await b.GetAsync(Auth2Routes.UserAccessFor(id), await ClientTokenAsync("appa", "auth2.admin"));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.Values("permissions").ShouldBe(["orders.read"]);

        (await b.GetAsync(Auth2Routes.UserAccessFor(id), await ClientTokenAsync("appb", "auth2.admin"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);   // not a member of B
        (await b.GetAsync(Auth2Routes.UserAccessFor(Guid.NewGuid()), await ClientTokenAsync("appa", "auth2.admin"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.GetAsync(Auth2Routes.UserAccessFor(id), await ClientTokenAsync("appa", "auth2.register"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
