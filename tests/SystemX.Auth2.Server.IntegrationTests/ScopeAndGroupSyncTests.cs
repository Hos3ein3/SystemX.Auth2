using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class GroupSyncTests : Phase6Base
{
    private const string Scope = "auth2.groups.sync auth2.groups.read";

    private Task<(HttpStatusCode Status, JsonElement Body)> SyncAsync(string token, GroupSyncRequest request) =>
        CallAsync(new Browser(Factory), Auth2Routes.GroupsSync, request, token);

    private async Task<List<Group>> GroupsAsync(Project app)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.Groups.Where(g => g.ProjectId == app.Id).OrderBy(g => g.Name).ToListAsync();
    }

    [Fact]
    public async Task GroupSync_IsOffByDefault_Is403_AndNeedsItsScope()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var r = await SyncAsync(token, new GroupSyncRequest(["finance"]));
        r.Status.ShouldBe(HttpStatusCode.Forbidden);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.SyncNotAllowed);

        await UpdateSettingsAsync(A, s => s.Clients.GroupSyncAllowed = true);
        (await SyncAsync(await ClientTokenAsync("appa", "auth2.groups.read"), new GroupSyncRequest(["finance"]))).Body.Str("code").ShouldBe(Auth2ErrorCodes.InsufficientScope);
    }

    [Fact]
    public async Task Push_AddsMissingGroups_Normalizes_AndIsIdempotent()
    {
        await PrepareAppsAsync(both: s => s.Clients.GroupSyncAllowed = true);
        var token = await ClientTokenAsync("appa", Scope);
        var first = await SyncAsync(token, new GroupSyncRequest(null, [new GroupDefinition("Finance", "Finance team"), new GroupDefinition("hr"), new GroupDefinition(" HR ")]));
        first.Status.ShouldBe(HttpStatusCode.OK, first.Body.ToString());
        first.Body.GetProperty("added").GetInt32().ShouldBe(2);
        first.Body.Values("addedKeys").ShouldBe(["finance", "hr"]);

        var second = await SyncAsync(token, new GroupSyncRequest(["finance", "hr"]));   // the short form
        (second.Body.GetProperty("added").GetInt32(), second.Body.GetProperty("unchanged").GetInt32()).ShouldBe((0, 2));
        (await GroupsAsync(A)).Select(g => g.Name).ShouldBe(["finance", "hr"]);
        (await GroupsAsync(A)).Single(g => g.Name == "finance").DisplayName.ShouldBe("Finance team");
        (await AuditAsync("groups.synced")).Count.ShouldBe(1);   // the second call changed nothing
    }

    [Fact]
    public async Task Push_NeverChangesOrDeletes_WhatAnAdminDecided()
    {
        await PrepareAppsAsync(both: s => s.Clients.GroupSyncAllowed = true);
        await using (var scope = ScopeFor("appa"))
            await scope.ServiceProvider.GetRequiredService<IGroupService>().CreateAsync("finance", "Admin's name", "Admin text");
        var token = await ClientTokenAsync("appa", Scope);
        await SyncAsync(token, new GroupSyncRequest(null, [new GroupDefinition("finance", "Client's name")]));
        await SyncAsync(token, new GroupSyncRequest(null, [new GroupDefinition("other")]));   // "finance" is missing from this list
        var groups = await GroupsAsync(A);
        groups.Select(g => g.Name).ShouldBe(["finance", "other"]);
        groups.Single(g => g.Name == "finance").DisplayName.ShouldBe("Admin's name");
    }

    [Fact]
    public async Task Push_OneInvalidName_RejectsAll_TooManyItems_AndEachApplicationIsSeparate()
    {
        await PrepareAppsAsync(both: s => s.Clients.GroupSyncAllowed = true);
        var token = await ClientTokenAsync("appa", Scope);
        var bad = await SyncAsync(token, new GroupSyncRequest(["good", "bad name"]));
        bad.Status.ShouldBe(HttpStatusCode.BadRequest);
        bad.Body.Str("code").ShouldBe(Auth2ErrorCodes.ValidationFailed);
        (await GroupsAsync(A)).ShouldBeEmpty();

        (await SyncAsync(token, new GroupSyncRequest([.. Enumerable.Range(0, 2001).Select(i => $"g{i}")]))).Body.Str("code").ShouldBe(Auth2ErrorCodes.TooManyItems);

        await SyncAsync(token, new GroupSyncRequest(["finance"]));
        await SyncAsync(await ClientTokenAsync("appb", Scope), new GroupSyncRequest(["hr"]));
        (await GroupsAsync(A)).Select(g => g.Name).ShouldBe(["finance"]);
        (await GroupsAsync(B)).Select(g => g.Name).ShouldBe(["hr"]);
    }

    [Fact]
    public async Task ParallelPushes_OfTheSameGroups_EndWithOneRowEach()
    {
        await PrepareAppsAsync(both: s => s.Clients.GroupSyncAllowed = true);
        var token = await ClientTokenAsync("appa", Scope);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SyncAsync(token, new GroupSyncRequest(["a", "b", "c"]))));
        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        (await GroupsAsync(A)).Count.ShouldBe(3);
    }
}

public class ScopeSyncTests : Phase6Base
{
    private const string Scope = "auth2.scopes.sync";

    private Task<(HttpStatusCode Status, JsonElement Body)> SyncAsync(string token, params ScopeDefinition[] scopes) =>
        CallAsync(new Browser(Factory), Auth2Routes.ScopesSync, new ScopeSyncRequest(scopes), token);

    private async Task<List<string>> AllowedAsync(Project app)
    {
        await using var scope = NewScope();
        return [.. (await scope.ServiceProvider.GetRequiredService<IApplicationService>().ListAsync(app.Id)).First().AllowedScopes];
    }

    private async Task<HttpStatusCode> ClientCredentialsAsync(string clientId, string scope) =>
        (await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientId == "appa" ? ASecret : BSecret, ["scope"] = scope,
        })).StatusCode;

    [Fact]
    public async Task WithAutoAssignOff_TheScopeIsOnlyRegistered_AnAdminAllowsItLater()
    {
        await PrepareAppsAsync(both: s => s.Clients.AutoAssignScopeToApplication = false);
        var token = await ClientTokenAsync("appa", Scope);

        var r = await SyncAsync(token, new ScopeDefinition("Reports.Read", "Read reports", "Lets you read reports"));
        r.Status.ShouldBe(HttpStatusCode.OK, r.Body.ToString());
        r.Body.Values("addedKeys").ShouldBe(["reports.read"]);                 // registered by this call
        (await AllowedAsync(A)).ShouldNotContain("reports.read");              // but not given to the application
        (await ClientCredentialsAsync("appa", "reports.read")).ShouldBe(HttpStatusCode.BadRequest);   // so the client cannot use it

        // the scope exists in the database: a second sync registers nothing and still gives nothing
        var again = await SyncAsync(token, new ScopeDefinition("reports.read"));
        again.Body.GetProperty("added").GetInt32().ShouldBe(0);
        (await AllowedAsync(A)).ShouldNotContain("reports.read");

        // an admin allows it for the application: now the client can use it
        await using (var scope = NewScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IApplicationService>();
            var current = (await applications.ListAsync(A.Id)).First();
            await applications.UpdateAsync(current.Id, new ApplicationSpec("", AllowedScopes: [.. current.AllowedScopes, "reports.read"]));
        }
        (await ClientCredentialsAsync("appa", "reports.read")).ShouldBe(HttpStatusCode.OK);
        (await ClientCredentialsAsync("appb", "reports.read")).ShouldBe(HttpStatusCode.BadRequest);   // another application was never given it
    }

    [Fact]
    public async Task WithAutoAssignOff_ASyncCannotAllowAScopeAnAdminMadeForAnotherApplication()
    {
        await PrepareAppsAsync(both: s => s.Clients.AutoAssignScopeToApplication = false);
        var tokenB = await ClientTokenAsync("appb", Scope);
        (await SyncAsync(tokenB, new ScopeDefinition("orders.write"))).Status.ShouldBe(HttpStatusCode.OK);

        var tokenA = await ClientTokenAsync("appa", Scope);
        (await SyncAsync(tokenA, new ScopeDefinition("orders.write"))).Status.ShouldBe(HttpStatusCode.OK);   // the same name, pushed by another application
        (await AllowedAsync(A)).ShouldNotContain("orders.write");
    }

    [Fact]
    public async Task Sync_RegistersTheScope_AllowsItForTheApplication_AndTheClientCanUseItAtOnce()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        (await ClientCredentialsAsync("appa", "reports.read")).ShouldBe(HttpStatusCode.BadRequest);   // unknown scope

        var r = await SyncAsync(token, new ScopeDefinition("Reports.Read", "Read reports", "Lets you read reports"));
        r.Status.ShouldBe(HttpStatusCode.OK, r.Body.ToString());
        r.Body.GetProperty("added").GetInt32().ShouldBe(1);
        r.Body.Values("addedKeys").ShouldBe(["reports.read"]);
        (await AllowedAsync(A)).ShouldContain("reports.read");

        (await ClientCredentialsAsync("appa", "reports.read")).ShouldBe(HttpStatusCode.OK);   // the client has the permission now
        (await ClientCredentialsAsync("appb", "reports.read")).ShouldBe(HttpStatusCode.BadRequest);   // another application did not ask for it

        await using var scopeManager = NewScope();
        var registered = await scopeManager.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>().FindByNameAsync("reports.read");
        registered.ShouldNotBeNull();
        (await scopeManager.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>().GetDisplayNameAsync(registered!)).ShouldBe("Read reports");
        (await AuditAsync("scopes.synced")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sync_IsIdempotent_NeverRemoves_AndTheRegistryTextOfTheFirstCallerStays()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await SyncAsync(token, new ScopeDefinition("reports.read", "First"), new ScopeDefinition("reports.write"));
        var again = await SyncAsync(token, new ScopeDefinition("reports.read", "Changed"));
        (again.Body.GetProperty("added").GetInt32(), again.Body.GetProperty("unchanged").GetInt32()).ShouldBe((0, 1));
        (await AllowedAsync(A)).ShouldContain("reports.write");    // not in the second list, still there
        (await AuditAsync("scopes.synced")).Count.ShouldBe(1);

        await using var scope = NewScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        (await manager.GetDisplayNameAsync((await manager.FindByNameAsync("reports.read"))!)).ShouldBe("First");

        // a second application asks for the same name: the registry has it, it is only allowed for that application
        (await SyncAsync(await ClientTokenAsync("appb", Scope), new ScopeDefinition("reports.read"))).Body.GetProperty("added").GetInt32().ShouldBe(1);
        (await ClientCredentialsAsync("appb", "reports.read")).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("auth2.admin.global")]
    [InlineData("auth2.me")]
    [InlineData("openid")]
    [InlineData("offline_access")]
    [InlineData("bad scope")]
    [InlineData("")]
    public async Task ReservedOrInvalidNames_AreRefused_AndNothingIsSaved(string name)
    {
        await PrepareAppsAsync();
        var before = await AllowedAsync(A);
        var r = await SyncAsync(await ClientTokenAsync("appa", Scope), new ScopeDefinition("fine.scope"), new ScopeDefinition(name));
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
        r.Body.Str("code").ShouldBe(Auth2ErrorCodes.InvalidScopeName);
        (await AllowedAsync(A)).ShouldBe(before);   // all or nothing; auth2.admin.global was never reachable
    }

    [Fact]
    public async Task NotAllowed_TooMany_AndScopeRules()
    {
        await PrepareAppsAsync(both: s => s.Clients.ScopeSyncAllowed = false);
        var token = await ClientTokenAsync("appa", Scope);
        var r = await SyncAsync(token, new ScopeDefinition("a.b"));
        (r.Status, r.Body.Str("code")).ShouldBe((HttpStatusCode.Forbidden, Auth2ErrorCodes.SyncNotAllowed));

        await UpdateSettingsAsync(A, s => s.Clients.ScopeSyncAllowed = true);
        var many = Enumerable.Range(0, 501).Select(i => new ScopeDefinition($"s.{i}")).ToArray();
        (await SyncAsync(token, many)).Body.Str("code").ShouldBe(Auth2ErrorCodes.TooManyItems);
        (await SyncAsync(await ClientTokenAsync("appa", "auth2.groups.read"), new ScopeDefinition("a.b"))).Body.Str("code").ShouldBe(Auth2ErrorCodes.InsufficientScope);
    }

    [Fact]
    public async Task Pull_ListsOnlyTheCustomScopesOfTheApplication_WithTheirTexts()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        await SyncAsync(token, new ScopeDefinition("reports.read", "Read reports", "Lets you read reports"), new ScopeDefinition("exports.run"));
        var list = JsonDocument.Parse(await (await new Browser(Factory).GetAsync(Auth2Routes.Scopes, token)).Content.ReadAsStringAsync()).RootElement;
        list.EnumerateArray().Select(s => s.Str("name")).ShouldBe(["exports.run", "reports.read"]);   // no auth2.* and no openid here
        list.EnumerateArray().Single(s => s.Str("name") == "reports.read").Str("displayName").ShouldBe("Read reports");

        var other = JsonDocument.Parse(await (await new Browser(Factory).GetAsync(Auth2Routes.Scopes, await ClientTokenAsync("appb", Scope))).Content.ReadAsStringAsync()).RootElement;
        other.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ParallelSyncs_OfTheSameAndOfDifferentScopes_LoseNothing_AndFailNever()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", Scope);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => SyncAsync(token, new ScopeDefinition("shared.scope"), new ScopeDefinition($"own.{i}"))));
        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        var allowed = await AllowedAsync(A);
        allowed.ShouldContain("shared.scope");
        for (var i = 0; i < 6; i++) allowed.ShouldContain($"own.{i}");
        allowed.Count(s => s == "shared.scope").ShouldBe(1);
    }
}
