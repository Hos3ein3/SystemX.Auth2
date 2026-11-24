using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Client.IntegrationTests;

public class ClientExternalProvidersTests : ClientTestBase
{
    [Fact]
    public async Task GetExternalProviders_ListsTheEnabledProviders_WithoutSecrets()
    {
        await UpdateSettingsAsync(s =>
        {
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "corp", Type = ExternalProviderType.GenericOidc, Enabled = true, DisplayName = "Company login", ClientId = "client-1", ClientSecretProtected = "cipher", Authority = "https://idp.corp.test" });
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "off", Type = ExternalProviderType.Google, Enabled = false, ClientId = "x", ClientSecretProtected = "cipher" });
            s.ExternalProviders.Add(new LdapProviderSettings { Name = "ldap-hq", Type = ExternalProviderType.Ldap, Enabled = true, Host = "ldap.corp.test", BindDn = "cn=x", BindPasswordProtected = "cipher", UserSearchBase = "dc=corp", UserSearchFilter = "(uid={0})" });
        });
        await using var client = BuildClient("blog");
        var result = await client.GetRequiredService<IAuth2ApiClientService>().GetExternalProvidersAsync();

        result.IsSuccess.ShouldBeTrue();
        var providers = result.Value!;
        providers.Select(p => p.Name).ShouldBe(["corp", "ldap-hq"], ignoreOrder: true);
        var corp = providers.First(p => p.Name == "corp");
        (corp.DisplayName, corp.Kind, corp.LoginFormRequired).ShouldBe(("Company login", ExternalProviderType.GenericOidc, false));
        corp.ChallengeUrl.ShouldNotBeNull();
        var ldap = providers.First(p => p.Name == "ldap-hq");
        (ldap.Kind, ldap.LoginFormRequired, ldap.ChallengeUrl).ShouldBe((ExternalProviderType.Ldap, true, null));
    }

    [Fact]
    public async Task GetExternalProviders_IsEmpty_WhenNothingIsConfigured()
    {
        await using var client = BuildClient("blog");
        (await client.GetRequiredService<IAuth2ApiClientService>().GetExternalProvidersAsync()).Value!.ShouldBeEmpty();
    }
}
