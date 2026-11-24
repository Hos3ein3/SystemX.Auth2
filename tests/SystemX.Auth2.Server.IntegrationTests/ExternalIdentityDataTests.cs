using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class ExternalLoginStoreTests : IdentityTestBase
{
    private static readonly UserLoginInfo Google = new("google", "109283746", "Google");

    [Fact]
    public async Task SameGoogleAccount_LinkedInTwoApplications_GivesTwoRows_AndOneUser()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        (await InAsync("appa", async m => await m.AddLoginAsync((await m.FindByNameAsync("sara"))!, Google))).Succeeded.ShouldBeTrue();
        (await InAsync("appb", async m => await m.AddLoginAsync((await m.FindByNameAsync("sara"))!, Google))).Succeeded.ShouldBeTrue();

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserLogins.CountAsync()).ShouldBe(2);
        (await db.UserLogins.Select(l => l.ProjectId).Distinct().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task FindByLogin_InApplicationA_NeverReturnsALinkOfApplicationB()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddLoginAsync((await m.FindByNameAsync("sara"))!, Google));

        (await InAsync("appa", m => m.FindByLoginAsync("google", "109283746"))).ShouldBeNull();
        (await InAsync("appb", m => m.FindByLoginAsync("google", "109283746")))!.UserName.ShouldBe("sara");
    }

    [Fact]
    public async Task GetLogins_ListsOnlyTheCurrentApplication_AndRemoveOnlyAffectsIt()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appa", async m => await m.AddLoginAsync((await m.FindByNameAsync("sara"))!, Google));
        await InAsync("appb", async m => await m.AddLoginAsync((await m.FindByNameAsync("sara"))!, Google));

        (await InAsync("appa", async m => await m.GetLoginsAsync((await m.FindByNameAsync("sara"))!))).Count.ShouldBe(1);
        (await InAsync("appa", async m => await m.RemoveLoginAsync((await m.FindByNameAsync("sara"))!, "google", "109283746"))).Succeeded.ShouldBeTrue();
        (await InAsync("appa", m => m.FindByLoginAsync("google", "109283746"))).ShouldBeNull();
        (await InAsync("appb", m => m.FindByLoginAsync("google", "109283746"))).ShouldNotBeNull();
    }

    [Fact]
    public async Task TheSameLink_ForTwoUsersInOneApplication_IsRefused()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa", "sara");
        await CreateUserInAsync("appa", "dan");
        (await InAsync("appa", async m => await m.AddLoginAsync((await m.FindByNameAsync("sara"))!, Google))).Succeeded.ShouldBeTrue();
        var second = await InAsync("appa", async m => await m.AddLoginAsync((await m.FindByNameAsync("dan"))!, Google));
        second.Succeeded.ShouldBeFalse();
        second.Errors.Single().Code.ShouldBe("LoginAlreadyAssociated");
    }
}

public class PasskeyStoreTests : IdentityTestBase
{
    private static UserPasskeyInfo Key(byte id) =>
        new([id, 1, 2, 3], [9, 9, 9], DateTimeOffset.UtcNow, 0, ["internal"], true, true, false, [1], [2]) { Name = "laptop" };

    [Fact]
    public async Task PasskeyOfApplicationA_IsNotFoundInApplicationB()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appa", async m =>
        {
            var u = (await m.FindByNameAsync("sara"))!;
            await ((IUserPasskeyStore<AppUser>)Store(m)).AddOrUpdatePasskeyAsync(u, Key(1), default);
            return 0;
        });

        (await InAsync("appa", m => ((IUserPasskeyStore<AppUser>)Store(m)).FindByPasskeyIdAsync([1, 1, 2, 3], default))).ShouldNotBeNull();
        (await InAsync("appb", m => ((IUserPasskeyStore<AppUser>)Store(m)).FindByPasskeyIdAsync([1, 1, 2, 3], default))).ShouldBeNull();
        (await InAsync("appb", async m => await ((IUserPasskeyStore<AppUser>)Store(m)).GetPasskeysAsync((await m.FindByNameAsync("sara"))!, default))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Passkey_CanBeAddedListedAndRemoved_InOneApplication()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await InAsync("appa", async m =>
        {
            var store = (IUserPasskeyStore<AppUser>)Store(m);
            var u = (await m.FindByNameAsync("sara"))!;
            await store.AddOrUpdatePasskeyAsync(u, Key(5), default);
            (await store.GetPasskeysAsync(u, default)).Single().Name.ShouldBe("laptop");
            await store.RemovePasskeyAsync(u, [5, 1, 2, 3], default);
            (await store.GetPasskeysAsync(u, default)).ShouldBeEmpty();
            return 0;
        });
    }

    private static IUserStore<AppUser> Store(UserManager<AppUser> m) =>
        (IUserStore<AppUser>)typeof(UserManager<AppUser>).GetProperty("Store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(m)!;
}

public class ExternalProviderSettingsPersistenceTests : IdentityTestBase
{
    [Fact]
    public async Task AllThreeProviderKinds_SurviveTheDatabase_AndSecretsStayEncrypted()
    {
        await SetUpAsync();
        string cipher;
        await using (var scope = NewScope())
        {
            var protector = scope.ServiceProvider.GetRequiredService<IExternalSecretProtector>();
            cipher = protector.Protect("very-secret-value")!;
        }
        await UpdateSettingsAsync(A, s =>
        {
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "google", Type = ExternalProviderType.Google, ClientId = "id", ClientSecretProtected = cipher, HostedDomain = "corp.com" });
            s.ExternalProviders.Add(new SamlProviderSettings { Name = "corp-saml", Type = ExternalProviderType.Saml, IdpMetadataUrl = "https://idp.test/m" });
            s.ExternalProviders.Add(new LdapProviderSettings { Name = "ldap-hq", Type = ExternalProviderType.Ldap, Host = "ldap.test" });
        });

        await using var scope2 = NewScope();
        var settings = await scope2.ServiceProvider.GetRequiredService<IProjectSettingsProvider>().GetAsync(A.Id);
        settings.ExternalProviders.Select(p => p.GetType()).ShouldBe([typeof(OidcProviderSettings), typeof(SamlProviderSettings), typeof(LdapProviderSettings)]);
        var oidc = (OidcProviderSettings)settings.ExternalProviders[0];
        oidc.ClientSecretProtected!.ShouldNotContain("very-secret-value");
        scope2.ServiceProvider.GetRequiredService<IExternalSecretProtector>().Unprotect(oidc.ClientSecretProtected).ShouldBe("very-secret-value");

        var db = scope2.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope2.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var raw = await db.Database.SqlQuery<string>($"select settings_json::text as \"Value\" from applications where id = {A.Id}").SingleAsync();
        raw.ShouldNotContain("very-secret-value");
    }

    [Fact]
    public async Task InvalidProviderSettings_AreRejectedWhenSaved()
    {
        await SetUpAsync();
        await Should.ThrowAsync<SettingsValidationException>(() => UpdateSettingsAsync(A, s =>
            s.ExternalProviders.Add(new LdapProviderSettings { Name = "Bad Name", Type = ExternalProviderType.Ldap })));
    }
}

public class ExternalMappingTableTests : IdentityTestBase
{
    [Fact]
    public async Task Mappings_AreFilteredByApplication()
    {
        await SetUpAsync();
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            db.ExternalGroupMappings.Add(new ExternalGroupMapping { ProjectId = A.Id, SourceValue = "staff", TargetId = Guid.NewGuid() });
            db.ExternalGroupMappings.Add(new ExternalGroupMapping { ProjectId = B.Id, SourceValue = "other", TargetId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await using var inA = ScopeFor("appa");
        var dbA = inA.ServiceProvider.GetRequiredService<Auth2DbContext>();
        await inA.ServiceProvider.GetRequiredService<IProjectContext>().GetRequiredAsync();
        (await dbA.ExternalGroupMappings.Select(m => m.SourceValue).ToListAsync()).ShouldBe(["staff"]);
    }
}

public class ExternalDescriptorTests : ServerTestBase
{
    [Fact]
    public async Task EveryProviderType_HasADescriptor_WithTheRightSettingsClass()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var all = scope.ServiceProvider.GetRequiredService<IExternalProviderDescriptors>();
        foreach (var type in Enum.GetValues<ExternalProviderType>())
        {
            var d = all.For(type);
            d.Type.ShouldBe(type);
            d.Fields.ShouldNotBeEmpty();
            typeof(ExternalProviderSettings).IsAssignableFrom(d.SettingsType).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task RedirectInfo_ShowsTheUrlsTheAdminMustEnter()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var all = scope.ServiceProvider.GetRequiredService<IExternalProviderDescriptors>();
        all.For(ExternalProviderType.Google).BuildRedirectInfo(new OidcProviderSettings { Name = "google" }, "https://auth.test/", "shop")
            .ShouldContain("https://auth.test/callback/login/shop/google");
        var saml = all.For(ExternalProviderType.Saml).BuildRedirectInfo(new SamlProviderSettings { Name = "corp" }, "https://auth.test", "shop");
        saml.ShouldContain("https://auth.test/saml/shop/corp/acs");
        saml.ShouldContain("https://auth.test/saml/shop/corp/metadata");
    }

    [Fact]
    public async Task Redact_RemovesEverySecretField()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var p = scope.ServiceProvider.GetRequiredService<IExternalSecretProtector>();
        var o = (OidcProviderSettings)p.Redact(new OidcProviderSettings { Name = "a", ClientSecretProtected = "x", PrivateKeyProtected = "y" });
        (o.ClientSecretProtected, o.PrivateKeyProtected).ShouldBe((null, null));
        ((SamlProviderSettings)p.Redact(new SamlProviderSettings { Name = "s", SpSigningCertificateProtected = "x" })).SpSigningCertificateProtected.ShouldBeNull();
        ((LdapProviderSettings)p.Redact(new LdapProviderSettings { Name = "l", BindPasswordProtected = "x" })).BindPasswordProtected.ShouldBeNull();
    }

    [Fact]
    public async Task Protect_EmptyGivesNull_AndTamperedValueFails()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var p = scope.ServiceProvider.GetRequiredService<IExternalSecretProtector>();
        p.Protect("").ShouldBeNull();
        p.Unprotect(null).ShouldBeNull();
        Should.Throw<System.Security.Cryptography.CryptographicException>(() => p.Unprotect("not-a-cipher"));
    }
}
