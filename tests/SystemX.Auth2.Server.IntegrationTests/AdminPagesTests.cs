using System.Net;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Application;

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
        "/admin/p/{k}/assignments", "/admin/p/{k}/role-permissions", "/admin/p/{k}/user-permissions", "/admin/p/{k}/user-groups", "/admin/p/{k}/group-roles", "/admin/p/{k}/group-permissions",
        "/admin/p/{k}/role_permissions", "/admin/p/{k}/user_permissions", "/admin/p/{k}/user_groups", "/admin/p/{k}/group_roles", "/admin/p/{k}/group_permissions",
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
    public async Task DetailAndAssignmentPages_RenderWithRealData_InsteadOfFailingToTranslateTheQuery()
    {
        var admin = await GlobalAdminAsync();
        await CreateUserInAsync("appa");   // sara
        await using (var scope = ScopeFor("appa"))
        {
            var sp = scope.ServiceProvider;
            var access = sp.GetRequiredService<IAccessService>();
            var groups = sp.GetRequiredService<IGroupService>();
            var sara = (await sp.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("sara"))!;
            (await access.CreatePermissionAsync(new SystemX.Auth2.Contracts.Access.PermissionDefinition("orders.read"))).Succeeded.ShouldBeTrue();
            (await access.CreateRoleAsync("Editor")).Succeeded.ShouldBeTrue();
            (await access.AddRolePermissionAsync("Editor", "orders.read")).Succeeded.ShouldBeTrue();
            (await access.GrantPermissionAsync(sara, "orders.read")).Succeeded.ShouldBeTrue();
            (await groups.CreateAsync("finance")).Succeeded.ShouldBeTrue();
            (await groups.AddUserAsync("finance", sara)).Succeeded.ShouldBeTrue();
            (await groups.AddRoleAsync("finance", "Editor")).Succeeded.ShouldBeTrue();
        }

        var seen = new Dictionary<string, string>
        {
            ["/admin/p/appa/permissions/detail?key=orders.read&ClientId=_global"] = "sara",   // users granted the permission
            ["/admin/p/appa/groups/detail?name=finance&ClientId=_global"] = "sara",           // members of the group
            ["/admin/p/appa/assignments?ClientId=_global"] = "sara",                          // user permissions, user groups, group roles, role permissions
            ["/admin/p/appa/role-permissions?ClientId=_global"] = "orders.read",
            ["/admin/p/appa/user-permissions?ClientId=_global"] = "sara",
            ["/admin/p/appa/user-groups?ClientId=_global"] = "finance",
            ["/admin/p/appa/group-roles?ClientId=_global"] = "Editor",
        };
        var failures = new List<string>();
        foreach (var (url, text) in seen)
        {
            var r = await admin.GetAsync(url);
            if (r.StatusCode != HttpStatusCode.OK) { failures.Add($"{url} -> {(int)r.StatusCode}"); continue; }
            if (!(await r.Content.ReadAsStringAsync()).Contains(text)) failures.Add($"{url} does not show '{text}'");
        }
        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task RoleDetailPage_ListsTheUsersWhoHoldTheRole()
    {
        var admin = await GlobalAdminAsync();
        var r = await admin.GetAsync("/admin/p/_global/roles/detail?name=GlobalAdmin&ClientId=_global");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);   // used to fail: a Guid.ToString() in the query could not be translated to SQL
        (await r.Content.ReadAsStringAsync()).ShouldContain("globaladmin");
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

    [Fact]
    public async Task AuditPage_RendersFilterForm_AndTable_Correctly()
    {
        var b = await GlobalAdminAsync();
        var r = await b.GetAsync("/admin/p/_global/audit?ClientId=_global");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await r.Content.ReadAsStringAsync();
        html.ShouldContain("Audit Events");
        html.ShouldContain("name=\"q\"");
        html.ShouldContain("name=\"actionFilter\"");
        html.ShouldContain("name=\"targetType\"");
        html.ShouldContain("name=\"pageSize\"");
        html.ShouldContain("<table");
    }

    [Fact]
    public async Task SettingsPage_ShowsDefaultAssignedRights_ForRegistration()
    {
        var b = await GlobalAdminAsync();
        var r = await b.GetAsync("/admin/p/_global/settings?ClientId=_global");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await r.Content.ReadAsStringAsync();
        html.ShouldContain("Default Rights for New Members");
        html.ShouldContain("Assigned Roles");
        html.ShouldContain("Assigned Groups");
        html.ShouldContain("Assigned Permissions");
        html.ShouldContain("data-multiselect-search");
    }

    [Fact]
    public async Task ApplicationDetail_PostUpdate_Works()
    {
        var b = await GlobalAdminAsync();
        // Get applications for appa
        var listRes = await b.GetAsync("/admin/p/appa/applications?ClientId=_global");
        listRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listHtml = await listRes.Content.ReadAsStringAsync();
        
        // Find application detail link
        var match = System.Text.RegularExpressions.Regex.Match(listHtml, @"/admin/p/appa/applications/detail\?id=([a-f0-9\-]+)");
        match.Success.ShouldBeTrue();
        var appId = match.Groups[1].Value;

        // GET detail
        var getRes = await b.GetAsync($"/admin/p/appa/applications/detail?id={appId}");
        getRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getHtml = await getRes.Content.ReadAsStringAsync();
        getHtml.ShouldContain("Save Changes");

        // POST update
        var postRes = await b.PostPageAsync($"/admin/p/appa/applications/detail?handler=Update&id={appId}", new Dictionary<string, string>
        {
            ["Id"] = appId,
            ["Input.Name"] = "Updated App A",
            ["Input.IsEnabled"] = "true",
            ["Input.RedirectUris"] = "http://localhost:5000/callback",
            ["Input.PostLogoutUris"] = "http://localhost:5000/logout-callback",
            ["Input.AllowedScopes"] = "openid profile",
            ["Input.UiMode"] = "Hosted"
        });

        // What is postRes.StatusCode?
        Assert.True(postRes.StatusCode == HttpStatusCode.Redirect || postRes.StatusCode == HttpStatusCode.OK, $"Status was {postRes.StatusCode}");
    }

    [Fact]
    public async Task ProjectDetail_PostUpdate_Works()
    {
        var b = await GlobalAdminAsync();
        // Get projects list
        var listRes = await b.GetAsync("/admin/projects?ClientId=_global");
        listRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listHtml = await listRes.Content.ReadAsStringAsync();

        var match = System.Text.RegularExpressions.Regex.Match(listHtml, @"/admin/projects/detail\?id=([a-f0-9\-]+)");
        match.Success.ShouldBeTrue();
        var projId = match.Groups[1].Value;

        // GET detail
        var getRes = await b.GetAsync($"/admin/projects/detail?id={projId}&ClientId=_global");
        getRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getHtml = await getRes.Content.ReadAsStringAsync();
        getHtml.ShouldContain("Save Changes");
        getHtml.ShouldContain("name=\"__RequestVerificationToken\"");

        // POST update
        var postRes = await b.PostPageAsync($"/admin/projects/detail?handler=Update&id={projId}", new Dictionary<string, string>
        {
            ["Id"] = projId,
            ["Input.Name"] = "Updated Project Name",
            ["Input.Description"] = "Updated Description",
            ["Input.IsEnabled"] = "true"
        });

        Assert.True(postRes.StatusCode == HttpStatusCode.Redirect || postRes.StatusCode == HttpStatusCode.OK, $"Status was {postRes.StatusCode}");
    }
}
