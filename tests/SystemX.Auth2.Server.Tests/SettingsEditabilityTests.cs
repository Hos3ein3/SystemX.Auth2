using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Admin;

namespace SystemX.Auth2.Server.Tests;

public class SettingsEditabilityTests
{
    [Fact]
    public void NoChange_NothingIsReported() =>
        SettingsEditability.ChangedGlobalOnly(new ApplicationSettings(), new ApplicationSettings()).ShouldBeEmpty();

    [Fact]
    public void ProjectAdminEditableChange_IsNotReported()
    {
        var after = new ApplicationSettings();
        after.Localization.BrandName = "Blog";
        after.Password.UniqueChars = after.Password.UniqueChars;   // same value
        SettingsEditability.ChangedGlobalOnly(new ApplicationSettings(), after).ShouldBeEmpty();
    }

    [Fact]
    public void GlobalOnlyChanges_AreReportedWithTheirPath()
    {
        var after = new ApplicationSettings();
        after.Clients.AllowSelfUiLogin = true;
        after.Redirects.AllowedRedirectUris.Add("https://a.test/cb");   // a list
        after.Sessions.SingleActiveSession = true;
        SettingsEditability.ChangedGlobalOnly(new ApplicationSettings(), after)
            .ShouldBe(["Redirects.AllowedRedirectUris", "Sessions.SingleActiveSession", "Clients.AllowSelfUiLogin"], ignoreOrder: true);
    }
}
