using System.Net;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The admin console pages: who gets in, and that every list page renders for a global admin.</summary>
public class AdminPagesTests : Phase6Base
{
    private static readonly string[] ListPages =
    [
        "/admin", "/admin/projects", "/admin/jobs", "/admin/keys", "/admin/settings",
        "/admin/p/{k}", "/admin/p/{k}/users", "/admin/p/{k}/roles", "/admin/p/{k}/permissions", "/admin/p/{k}/groups", "/admin/p/{k}/applications",
        "/admin/p/{k}/scopes", "/admin/p/{k}/authorizations", "/admin/p/{k}/providers", "/admin/p/{k}/sessions", "/admin/p/{k}/audit", "/admin/p/{k}/reports",
        "/admin/p/{k}/settings", "/admin/p/{k}/ui", "/admin/p/{k}/branding", "/admin/p/{k}/email-templates",
        "/admin/p/{k}/users/create", "/admin/p/{k}/roles/create", "/admin/p/{k}/permissions/create", "/admin/p/{k}/groups/create",
        "/admin/p/{k}/applications/create", "/admin/p/{k}/scopes/create", "/admin/p/{k}/providers/create", "/admin/projects/create",
    ];

    private async Task<Browser> GlobalAdminAsync()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        var login = await b.PostPageAsync("/Account/Login?ClientId=_global", new Dictionary<string, string> { ["Input.Login"] = "globaladmin", ["Input.Password"] = "Admin@12345!ChangeMe" });
        login.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.OK);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
        return b;
    }

    [Fact]
    public async Task Anonymous_GetsNothing_OnAnyAdminPage()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        foreach (var page in ListPages)
        {
            var r = await b.GetAsync(page.Replace("{k}", "appa"));
            r.StatusCode.ShouldBeOneOf([HttpStatusCode.NotFound, HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], page);
        }
    }

    [Fact]
    public async Task GlobalAdmin_SeesEveryListPage_OfAnyProject()
    {
        var b = await GlobalAdminAsync();
        var failures = new List<string>();
        foreach (var key in new[] { "_global", "appa" })
            foreach (var page in ListPages.Where(p => p.Contains("{k}")))
            {
                var url = page.Replace("{k}", key) + (page.Contains("?") ? "" : "?ClientId=_global");
                var r = await b.GetAsync(url);
                if (r.StatusCode != HttpStatusCode.OK) failures.Add($"{url} -> {(int)r.StatusCode}");
            }
        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task GlobalAdmin_SeesTheGlobalPages_AndAnUnknownProjectIs404()
    {
        var b = await GlobalAdminAsync();
        foreach (var page in new[] { "/admin", "/admin/projects", "/admin/jobs", "/admin/keys", "/admin/settings" })
            (await b.GetAsync(page + "?ClientId=_global")).StatusCode.ShouldBe(HttpStatusCode.OK, page);
        (await b.GetAsync("/admin/p/nope/users?ClientId=_global")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task APlainUser_GetsNotFound_OnTheAdminPages_EvenOfTheOwnProject()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        foreach (var page in new[] { "/admin/p/appa/users", "/admin/p/appa", "/admin/p/appb/users", "/admin/projects", "/admin/keys" })
            (await b.GetAsync(page + "?ClientId=appa")).StatusCode.ShouldBeOneOf([HttpStatusCode.NotFound, HttpStatusCode.Redirect, HttpStatusCode.Forbidden], page);
    }

    [Fact]
    public async Task TheHomePage_SendsAPlainUserToTheirOwnProject_InsteadOfFailing()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var home = await b.GetAsync("/");   // no client id and no project in the request: only the session cookie says who this is
        home.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        home.Headers.Location!.ToString().ShouldBe("/admin/p/appa");
    }

    [Fact]
    public async Task RolesPage_ListsARoleThatAClientSynced()
    {
        var admin = await GlobalAdminAsync();
        var token = await ClientTokenAsync("appa", "auth2.permissions.sync auth2.roles.sync");
        (await CallAsync(new Browser(Factory), Auth2Routes.RolesSync, new RoleSyncRequest([new RoleDefinition("Oskol")]), token)).Status.ShouldBe(HttpStatusCode.OK);

        var r = await admin.GetAsync("/admin/p/appa/roles?ClientId=_global");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).ShouldContain("Oskol");
    }
}
