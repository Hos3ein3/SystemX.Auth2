using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The resources of a scope ("Associated Resources") become audiences of the tokens that have the scope.</summary>
public class ScopeResourcesTests : Phase6Base
{
    private const string Scope = "orders.api";

    private async Task PrepareAsync(params string[] resources)
    {
        await PrepareAppsAsync(both: s => s.AllowedScopes.Add(Scope));
        await CreateUserInAsync("appa");
        await using var scope = NewScope();
        var (result, _) = await scope.ServiceProvider.GetRequiredService<IAdminScopeService>().CreateAsync(new CreateScopeRequest(Scope, "Orders API", null, resources), isGlobalAdmin: true);
        result.Succeeded.ShouldBeTrue();
    }

    private static string[] Audiences(string jwt) => [.. new JsonWebToken(jwt).Audiences];

    private async Task<string> AccessTokenAsync(string scope) => (await UserTokensAsync("sara", PassA, scope: "openid offline_access auth2.me " + scope)).Str("access_token");

    [Fact]
    public async Task AUserToken_HasTheClientIdFirst_ThenTheResourcesOfItsScopes()
    {
        await PrepareAsync("orders-api", "reports-api");
        Audiences(await AccessTokenAsync(Scope)).ShouldBe(["appa", "orders-api", "reports-api"]);
        Audiences(await AccessTokenAsync("")).ShouldBe(["appa"]);   // without the scope the token is for the application only
    }

    [Fact]
    public async Task AMachineToken_GetsTheResourcesToo()
    {
        await PrepareAsync("orders-api");
        Audiences(await ClientTokenAsync("appa", "auth2.me " + Scope)).ShouldBe(["appa", "orders-api"]);
        Audiences(await ClientTokenAsync("appa", "auth2.me")).ShouldBe(["appa"]);
    }

    [Fact]
    public async Task ADuplicateOrTheClientIdItself_IsNotRepeated()
    {
        await PrepareAsync("appa", "orders-api", "orders-api");
        Audiences(await AccessTokenAsync(Scope)).ShouldBe(["appa", "orders-api"]);
    }

    [Fact]
    public async Task TwoScopes_AddTheResourcesOfBoth()
    {
        await PrepareAppsAsync(both: s => { s.AllowedScopes.Add(Scope); s.AllowedScopes.Add("reports.api"); });
        await CreateUserInAsync("appa");
        await using (var scope = NewScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminScopeService>();
            (await admin.CreateAsync(new CreateScopeRequest(Scope, null, null, ["orders-api"]), true)).Result.Succeeded.ShouldBeTrue();
            (await admin.CreateAsync(new CreateScopeRequest("reports.api", null, null, ["reports-api", "orders-api"]), true)).Result.Succeeded.ShouldBeTrue();
        }
        Audiences(await AccessTokenAsync(Scope + " reports.api")).ShouldBe(["appa", "orders-api", "reports-api"]);
    }

    [Fact]
    public async Task TheServerStillFindsTheApplication_FromTheClientId_WhateverTheResourcesAre()
    {
        // a resource that is the client id of ANOTHER application must never make the Server think the token belongs to that application
        await PrepareAsync("appb", "orders-api");
        var token = await AccessTokenAsync(Scope);
        Audiences(token).ShouldBe(["appa", "appb", "orders-api"]);

        var me = await new Browser(Factory).GetAsync(Auth2Routes.Me, token);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.JsonAsync()).Str("userName").ShouldBe("sara");   // sara exists in appa only

        // a machine token with the resources still reaches the machine-only API of its own application
        var machine = await ClientTokenAsync("appa", "auth2.permissions.sync " + Scope);
        (await new Browser(Factory).GetAsync(Auth2Routes.Permissions, machine)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AChangeOfTheResources_ReachesTheTokenAtTheNextRefresh()
    {
        await PrepareAsync("orders-api");
        var first = await UserTokensAsync("sara", PassA, scope: "openid offline_access auth2.me " + Scope);
        Audiences(first.Str("access_token")).ShouldBe(["appa", "orders-api"]);

        await using (var scope = NewScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminScopeService>();
            var id = (await admin.ListAsync(new PageQuery { Search = Scope })).Items.Single().Id;
            (await admin.UpdateAsync(id, new UpdateScopeRequest(null, null, ["orders-api", "billing-api"]), true)).Result.Succeeded.ShouldBeTrue();
        }

        var refreshed = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = first.Str("refresh_token"), ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        Audiences((await refreshed.JsonAsync()).Str("access_token")).ShouldBe(["appa", "orders-api", "billing-api"]);
    }

    [Fact]
    public async Task EmptyingTheResources_GivesTokensOfTheApplicationOnly_AndNullLeavesThemAlone()
    {
        await PrepareAsync("orders-api");
        await using var scope = NewScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminScopeService>();
        var id = (await admin.ListAsync(new PageQuery { Search = Scope })).Items.Single().Id;

        (await admin.UpdateAsync(id, new UpdateScopeRequest("New name", null, null), true)).Scope!.Resources.ShouldBe(["orders-api"]);   // null: untouched
        (await admin.UpdateAsync(id, new UpdateScopeRequest(null, null, []), true)).Scope!.Resources.ShouldBeEmpty();                  // empty: removed
        Audiences(await AccessTokenAsync(Scope)).ShouldBe(["appa"]);
    }
}
