using System.Net;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The public Privacy and Terms pages.</summary>
public class LegalPagesTests : Phase6Base
{
    [Theory]
    [InlineData("/Privacy", "Privacy Policy")]
    [InlineData("/Terms", "Terms of Use")]
    public async Task ThePages_AreOpenToEverybody_AlsoWithoutAnApplication(string url, string title)
    {
        await PrepareAppsAsync();
        var anonymous = await new Browser(Factory).GetAsync(url);               // no ClientId, not signed in
        anonymous.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.Content.ReadAsStringAsync()).ShouldContain(title);

        var known = await new Browser(Factory).GetAsync(url + "?ClientId=appa");   // with an application
        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await known.Content.ReadAsStringAsync();
        html.ShouldContain(title);
        html.ShouldContain("App A");                                            // the brand of the project
        html.ShouldContain("ClientId=appa");                                    // the links keep the application

        (await new Browser(Factory).GetAsync(url + "?ClientId=does-not-exist")).StatusCode.ShouldBe(HttpStatusCode.OK);   // an unknown application is not an error here
    }

    [Fact]
    public async Task TheSupportAddress_OfTheProject_IsShown()
    {
        await PrepareAppsAsync(both: s => s.Project.Localization.SupportEmail = "help@example.org");
        foreach (var url in new[] { "/Privacy", "/Terms" })
            (await (await new Browser(Factory).GetAsync(url + "?ClientId=appa")).Content.ReadAsStringAsync()).ShouldContain("mailto:help@example.org");
        (await (await new Browser(Factory).GetAsync("/Privacy")).Content.ReadAsStringAsync()).ShouldNotContain("mailto:");   // no application: no address
    }

    [Fact]
    public async Task TheFooterOfTheSignInPage_LinksToBothPages()
    {
        await PrepareAppsAsync();
        var html = await (await new Browser(Factory).GetAsync("/Account/Login?ClientId=appa")).Content.ReadAsStringAsync();
        html.ShouldContain("href=\"/privacy?ClientId=appa\"");
        html.ShouldContain("href=\"/terms?ClientId=appa\"");
    }
}
