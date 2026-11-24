using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Tests;

/// <summary>The rules of the project/application model that need no database.</summary>
public class ProjectModelTests
{
    private static ClientApplicationInfo Client(ClientApplicationSettings? settings = null, bool enabled = true, string[]? redirects = null, string? consent = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "pk.api", "api", enabled, redirects ?? [], [], ["openid"], consent, settings ?? new ClientApplicationSettings(), 1, DateTimeOffset.UnixEpoch);

    // ------------------------------------------------------------------ the one gate

    [Fact]
    public void Gate_ProjectDisabled_BlocksEveryApplication()
    {
        var project = new Project { IsEnabled = false };
        AccessGate.Check(project, Client()).ShouldBe(Auth2ErrorCodes.ProjectDisabled);
        AccessGate.Check(project, null).ShouldBe(Auth2ErrorCodes.ProjectDisabled);
        AccessGate.Check(project, Client(enabled: false)).ShouldBe(Auth2ErrorCodes.ProjectDisabled);   // the project is checked first
    }

    [Fact]
    public void Gate_ApplicationDisabled_BlocksOnlyThatApplication()
    {
        var project = new Project { IsEnabled = true };
        AccessGate.Check(project, Client(enabled: false)).ShouldBe(Auth2ErrorCodes.ApplicationDisabled);
        AccessGate.Check(project, Client()).ShouldBeNull();
        AccessGate.Check(project, null).ShouldBeNull();   // a request without a client id has no application to block
    }

    // ------------------------------------------------------------------ settings precedence: project default, overridden by the application

    [Fact]
    public void Effective_UsesTheProjectValue_WhenTheApplicationSetsNothing()
    {
        var project = new ProjectSettings();
        project.Sessions.AccessTokenMinutes = 20;
        project.Sessions.RefreshTokenDays = 7;
        project.Sessions.IdentityTokenMinutes = 5;
        project.Sessions.CookieLifetimeMinutes = 120;
        project.Clients.DefaultUiMode = UiMode.Self;
        var e = new EffectiveSettings(project, Client());
        (e.AccessTokenMinutes, e.RefreshTokenDays, e.IdentityTokenMinutes, e.CookieLifetimeMinutes, e.UiMode).ShouldBe((20, 7, 5, 120, UiMode.Self));
    }

    [Fact]
    public void Effective_TheApplicationValueWins_WhereItIsSet_AndOnlyThere()
    {
        var project = new ProjectSettings();
        project.Sessions.AccessTokenMinutes = 20;
        project.Sessions.RefreshTokenDays = 7;
        project.Sessions.CookieLifetimeMinutes = 120;
        project.Clients.DefaultUiMode = UiMode.Hosted;
        var app = new ClientApplicationSettings { AccessTokenMinutes = 3, CookieLifetimeMinutes = 15, UiMode = UiMode.None };
        var e = new EffectiveSettings(project, Client(app));
        e.AccessTokenMinutes.ShouldBe(3);            // overridden
        e.CookieLifetimeMinutes.ShouldBe(15);         // overridden
        e.UiMode.ShouldBe(UiMode.None);               // overridden
        e.RefreshTokenDays.ShouldBe(7);               // not set on the application: the project value
        e.IdentityTokenMinutes.ShouldBe(project.Sessions.IdentityTokenMinutes);
    }

    [Fact]
    public void Effective_WithoutAnApplication_IsTheProjectValues()
    {
        var project = new ProjectSettings();
        project.Sessions.AccessTokenMinutes = 9;
        var e = new EffectiveSettings(project, null);
        e.AccessTokenMinutes.ShouldBe(9);
        e.AllowSelfUiLogin.ShouldBeFalse();
        e.AllowedRedirectUris.ShouldBeEmpty();
    }

    [Fact]
    public void Effective_Consent_ApplicationOverrideThenProject()
    {
        var project = new ProjectSettings();
        new EffectiveSettings(project, Client()).ConsentMode.ShouldBe(ConsentMode.Implicit);
        project.Sessions.RequireConsent = true;
        new EffectiveSettings(project, Client()).ConsentMode.ShouldBe(ConsentMode.Explicit);
        new EffectiveSettings(project, Client(new ClientApplicationSettings { ConsentMode = ConsentMode.Systematic })).ConsentMode.ShouldBe(ConsentMode.Systematic);
    }

    [Fact]
    public void Effective_RedirectUrisAndSelfUi_AreApplicationValues()
    {
        var e = new EffectiveSettings(new ProjectSettings(), Client(new ClientApplicationSettings { AllowSelfUiLogin = true }, redirects: ["https://a.test/cb"]));
        e.AllowSelfUiLogin.ShouldBeTrue();
        e.AllowedRedirectUris.ShouldBe(["https://a.test/cb"]);
    }

    [Fact]
    public void Effective_ExternalProviders_ApplicationShowsASubset_NeverMore()
    {
        var providers = new[] { "google", "github", "corp-saml" };
        new EffectiveSettings(new ProjectSettings(), Client()).FilterProviders(providers, p => p).ShouldBe(providers);   // no list: all
        var some = new EffectiveSettings(new ProjectSettings(), Client(new ClientApplicationSettings { ExternalProviders = ["github", "unknown"] }));
        some.FilterProviders(providers, p => p).ShouldBe(["github"]);   // "unknown" does not add anything
        new EffectiveSettings(new ProjectSettings(), Client(new ClientApplicationSettings { ExternalProviders = [] })).FilterProviders(providers, p => p).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ names and claims

    [Theory]
    [InlineData("papertrade", true)]
    [InlineData("paper-trade_2", true)]
    [InlineData("_global", false)]
    [InlineData("Paper", false)]
    [InlineData("ab", false)]
    public void ProjectKey_Rules(string key, bool valid) => ProjectKeyRules.IsValidForNewProject(key).ShouldBe(valid);

    [Fact]
    public void Claims_AreProjectIdProjectKeyAndClientId_AndTheOldAppClaimIsGone()
    {
        Auth2Claims.ProjectId.ShouldBe("project_id");
        Auth2Claims.ProjectKey.ShouldBe("projectkey");
        Auth2Claims.ClientId.ShouldBe("client_id");
        Auth2Claims.ApplicationId.ShouldBe("application_id");
        new[] { Auth2Claims.ProjectId, Auth2Claims.ProjectKey, Auth2Claims.ClientId, Auth2Claims.ApplicationId }.ShouldNotContain("app");
    }

    [Fact]
    public void ApplicationSettings_Validator_ChecksLifetimesAndOrigins()
    {
        var errors = new List<SettingsError>();
        ClientApplicationSettingsValidator.Validate(new ClientApplicationSettings { AccessTokenMinutes = 0, RefreshTokenDays = 1000, AllowedCorsOrigins = ["https://*.x.test", "not a url"] }, (p, m) => errors.Add(new SettingsError(p, m)));
        errors.Select(e => e.Path).ShouldBe(["AccessTokenMinutes", "RefreshTokenDays", "AllowedCorsOrigins", "AllowedCorsOrigins"]);
        var ok = new List<SettingsError>();
        ClientApplicationSettingsValidator.Validate(new ClientApplicationSettings { AccessTokenMinutes = 5, AllowedCorsOrigins = ["https://app.test"] }, (p, m) => ok.Add(new SettingsError(p, m)));
        ok.ShouldBeEmpty();
    }
}
