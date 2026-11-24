using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Admin;

namespace SystemX.Auth2.Server.Tests;

public class SettingsEditabilityTests
{
    [Fact]
    public void NoChange_NothingIsReported() =>
        SettingsEditability.ChangedGlobalOnly(new ProjectSettings(), new ProjectSettings()).ShouldBeEmpty();

    [Fact]
    public void ProjectAdminEditableChange_IsNotReported()
    {
        var after = new ProjectSettings();
        after.Localization.BrandName = "Blog";
        after.Password.UniqueChars = after.Password.UniqueChars;   // same value
        SettingsEditability.ChangedGlobalOnly(new ProjectSettings(), after).ShouldBeEmpty();
    }

    [Fact]
    public void GlobalOnlyChanges_AreReportedWithTheirPath()
    {
        var after = new ProjectSettings();
        after.Clients.ScopeSyncAllowed = false;
        after.Password.BannedPasswords.Add("secret123");   // a list
        after.Sessions.SingleActiveSession = true;
        SettingsEditability.ChangedGlobalOnly(new ProjectSettings(), after)
            .ShouldBe(["Password.BannedPasswords", "Sessions.SingleActiveSession", "Clients.ScopeSyncAllowed"], ignoreOrder: true);
    }
}
