using System.Net;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// The self-service pages (<c>/Manage</c>) look like the admin console and the account pages, are in the navigation of the admin console,
/// and work for the admin who comes from there.
/// </summary>
public class ManagePagesInAdminTests : Phase6Base
{
    private static readonly string[] ManagePages =
    [
        "/Manage", "/Manage/Profile", "/Manage/ChangeEmail", "/Manage/ChangePassword", "/Manage/TwoFactor/Enable",
        "/Manage/TwoFactor/RecoveryCodes", "/Manage/Passkeys", "/Manage/ExternalLogins", "/Manage/Sessions", "/Manage/DeleteAccount",
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
    public async Task TheAdminNavigation_HasEveryManagePage()
    {
        var b = await GlobalAdminAsync();
        var html = await (await b.GetAsync("/admin/p/appa")).Content.ReadAsStringAsync();
        html.ShouldContain("My account");
        foreach (var page in ManagePages.Where(p => p != "/Manage/TwoFactor/RecoveryCodes"))
            html.ShouldContain($"href=\"{page}?ClientId=", Case.Sensitive, page);   // the link carries the application of the cookie
    }

    [Fact]
    public async Task EveryManagePage_WorksForTheAdmin_WithAndWithoutTheClientIdOfTheLink()
    {
        var b = await GlobalAdminAsync();
        var home = await (await b.GetAsync("/admin/p/appa")).Content.ReadAsStringAsync();
        var link = System.Text.RegularExpressions.Regex.Match(home, "href=\"/Manage/Passkeys\\?ClientId=([^\"]+)\"").Groups[1].Value;
        link.ShouldNotBeNullOrEmpty();

        foreach (var page in ManagePages)
        {
            (await b.GetAsync($"{page}?ClientId={link}")).StatusCode.ShouldBe(HttpStatusCode.OK, page);
            (await b.GetAsync(page)).StatusCode.ShouldBe(HttpStatusCode.OK, page + " (no client id)");
        }
    }

    [Fact]
    public async Task ManagePages_FollowTheAdminDesign_DarkModeSidebarThemeToggleAndLanguage()
    {
        var b = await GlobalAdminAsync();
        foreach (var page in new[] { "/Manage", "/Manage/Profile", "/Manage/ChangePassword", "/Manage/Passkeys", "/Manage/Sessions", "/Manage/ExternalLogins", "/Manage/DeleteAccount" })
        {
            var html = await (await b.GetAsync(page + "?ClientId=_global")).Content.ReadAsStringAsync();
            html.ShouldContain("dark:bg-gray-900", Case.Sensitive, page);      // dark mode like the admin console
            html.ShouldContain("data-toggle-theme", Case.Sensitive, page);     // the theme button
            html.ShouldContain("data-culture-select", Case.Sensitive, page);   // the language selector
            html.ShouldContain("My account", Case.Sensitive, page);            // the sidebar of the account section
        }
    }

    [Fact]
    public async Task ThePasswordAndEmailForms_GetTheRulesOfTheProject()
    {
        var b = await GlobalAdminAsync();
        var password = await (await b.GetAsync("/Manage/ChangePassword?ClientId=_global")).Content.ReadAsStringAsync();
        password.ShouldContain("data-a2-rule=", Case.Sensitive);
        var email = await (await b.GetAsync("/Manage/ChangeEmail?ClientId=_global")).Content.ReadAsStringAsync();
        email.ShouldContain("data-a2-rule=", Case.Sensitive);
    }

    [Fact]
    public async Task ThePasskeysPage_SaysWhyItIsEmpty_AndOffersTheButtonOnceTheProjectAllowsPasskeys()
    {
        var b = await GlobalAdminAsync();
        // passkeys are a setting of each project: off in _global (the project of this session) until it is turned on there
        var off = await (await b.GetAsync("/Manage/Passkeys?ClientId=_global")).Content.ReadAsStringAsync();
        off.ShouldContain("Passkeys are not enabled for the project", Case.Sensitive);
        off.ShouldContain("/admin/p/_global/settings", Case.Sensitive);   // an admin gets the way to the setting
        off.ShouldNotContain("Register a Passkey", Case.Sensitive);

        await UpdateSettingsAsync((await FindProjectAsync("_global"))!, s => s.Passkeys.AllowPasskeys = true);
        var on = await (await b.GetAsync("/Manage/Passkeys?ClientId=_global")).Content.ReadAsStringAsync();
        on.ShouldNotContain("Passkeys are not enabled for the project", Case.Sensitive);
        on.ShouldContain("Register a Passkey", Case.Sensitive);
    }
}
