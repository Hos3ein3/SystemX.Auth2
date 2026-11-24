using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.IntegrationTests;

public class ExternalRegistryTests : IdentityTestBase
{
    private IExternalProviderRegistry Registry(Auth2ServerFactory? f = null) => (f ?? Factory).Services.GetRequiredService<IExternalProviderRegistry>();

    private static OidcProviderSettings Google(string name = "google", bool enabled = true, params string[] domains) =>
        new() { Name = name, Type = ExternalProviderType.Google, Enabled = enabled, ClientId = "id", ClientSecretProtected = "x", EmailDomains = [.. domains] };

    [Fact]
    public async Task TwoApplications_WithTheSameProviderType_AreKeptApart()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(Google()));
        await UpdateSettingsAsync(B, s => s.ExternalProviders.Add(Google()));

        var a = Registry().Get(A.Id, "google")!;
        var b = Registry().Get(B.Id, "google")!;
        a.ProjectId.ShouldBe(A.Id);
        b.ProjectId.ShouldBe(B.Id);
        a.RegistrationName.ShouldNotBe(b.RegistrationName);
        a.RegistrationName.ShouldBe($"ext-{A.Id:N}-google");
        Registry().FindByUrl("appa", "google")!.ProjectId.ShouldBe(A.Id);
        Registry().FindByUrl("appb", "google")!.ProjectId.ShouldBe(B.Id);
        Registry().FindByUrl("appa", "other").ShouldBeNull();
    }

    [Fact]
    public async Task DisabledProviders_AndDisabledApplications_AreNotListed()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => { s.ExternalProviders.Add(Google("on")); s.ExternalProviders.Add(Google("off", enabled: false)); });
        Registry().ListForProject(A.Id).Select(p => p.Name).ShouldBe(["on"]);
        Registry().Get(A.Id, "off").ShouldBeNull();
    }

    [Fact]
    public async Task EmailDomain_FindsTheProvider_ForThatApplicationOnly()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(Google("corp", true, "contoso.com")));
        Registry().FindByEmailDomain(A.Id, "CONTOSO.com")!.Name.ShouldBe("corp");
        Registry().FindByEmailDomain(A.Id, "other.com").ShouldBeNull();
        Registry().FindByEmailDomain(B.Id, "contoso.com").ShouldBeNull();
    }

    [Fact]
    public async Task Changing_AProvider_IsVisibleAtOnce_OnThisInstance()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(Google()));
        Registry().Get(A.Id, "google")!.Settings.DisplayName.ShouldBeNull();
        await UpdateSettingsAsync(A, s => s.ExternalProviders[0].DisplayName = "Sign in with Google");
        Registry().Get(A.Id, "google")!.Settings.DisplayName.ShouldBe("Sign in with Google");
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Clear());
        Registry().ListForProject(A.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task Urls_AreUniquePerProviderAndApplication()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s =>
        {
            s.ExternalProviders.Add(Google());
            s.ExternalProviders.Add(new SamlProviderSettings { Name = "corp", Type = ExternalProviderType.Saml, Enabled = true, IdpMetadataUrl = "https://idp.test/m" });
        });
        var urls = ExternalProviderUrls.For(Registry().Get(A.Id, "google")!, "https://auth.test/");
        urls["callback"].ShouldBe("https://auth.test/callback/login/appa/google");
        var saml = ExternalProviderUrls.For(Registry().Get(A.Id, "corp")!, "https://auth.test");
        saml["acs"].ShouldBe("https://auth.test/saml/appa/corp/acs");
        saml["slo"].ShouldBe("https://auth.test/saml/appa/corp/slo");
        saml["metadata"].ShouldBe("https://auth.test/saml/appa/corp/metadata");
    }

    [Fact]
    public async Task ASecondInstance_SeesChanges_ByPolling()
    {
        await SetUpAsync();
        await using var second = new Auth2ServerFactory();
        second.ShareDatabaseWith(Factory);
        var other = Registry(second);
        await other.RefreshChangedAsync();
        other.ListForProject(A.Id).ShouldBeEmpty();

        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(Google()));
        other.ListForProject(A.Id).ShouldBeEmpty();           // not told yet
        (await other.RefreshChangedAsync()).ShouldBe(1);          // what the 30 second poll does
        other.Get(A.Id, "google").ShouldNotBeNull();
        (await other.RefreshChangedAsync()).ShouldBe(0);          // nothing changed since
    }
}

public class ExternalRegistryRedisTests : IdentityTestBase
{
    protected override bool UseRedis => true;

    [Fact]
    public async Task ASecondInstance_SeesChanges_ThroughRedis_WithoutPolling()
    {
        await SetUpAsync();
        await using var second = new Auth2ServerFactory();
        second.ShareDatabaseWith(Factory);
        var other = second.Services.GetRequiredService<IExternalProviderRegistry>();   // starts the second host and its subscription
        await Task.Delay(500);

        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(
            new OidcProviderSettings { Name = "google", Type = ExternalProviderType.Google, Enabled = true, ClientId = "id", ClientSecretProtected = "x" }));

        for (var i = 0; i < 50 && other.Get(A.Id, "google") is null; i++) await Task.Delay(100);
        other.Get(A.Id, "google").ShouldNotBeNull();
    }
}
