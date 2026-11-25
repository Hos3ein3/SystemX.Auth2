using System.Net;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The PostgreSQL and Redis badges of the project dashboard.</summary>
public abstract class InfrastructureHealthBase : Phase6Base
{
    protected async Task<string> DashboardAsync(string loginClient = "_global", string user = "globaladmin", string password = "Admin@12345!ChangeMe", string project = "appa")
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        (await b.PostPageAsync($"/Account/Login?ClientId={loginClient}", new Dictionary<string, string> { ["Input.Login"] = user, ["Input.Password"] = password })).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.OK);
        var page = await b.GetAsync($"/admin/p/{project}?ClientId={loginClient}");
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await page.Content.ReadAsStringAsync();
    }
}

public class InfrastructureHealthTests : InfrastructureHealthBase
{
    [Fact]
    public async Task TheDashboard_ShowsPostgreSqlHealthy_AndRedisOff_WhenRedisIsNotConfigured()
    {
        var html = await DashboardAsync();
        html.ShouldContain("PostgreSQL: Healthy");
        html.ShouldContain("Redis: Off");
        html.ShouldContain("Redis is not configured");   // the tooltip
    }
}

public class InfrastructureHealthWithRedisTests : InfrastructureHealthBase
{
    protected override bool UseRedis => true;

    [Fact]
    public async Task TheDashboard_ShowsBoth_WhenRedisIsOn()
    {
        var html = await DashboardAsync();
        html.ShouldContain("PostgreSQL: Healthy");
        html.ShouldContain("Redis: Healthy");
    }
}
