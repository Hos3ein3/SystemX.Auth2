using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.IntegrationTests.Spikes;

namespace SystemX.Auth2.Server.IntegrationTests;

public abstract class LdapTestBase(LdapFixture ldap) : ExternalLoginTestBase
{
    protected LdapFixture Ldap { get; } = ldap;

    protected LdapProviderSettings Directory(Action<LdapProviderSettings>? change = null)
    {
        var p = new LdapProviderSettings
        {
            Name = "ldap-hq", Type = ExternalProviderType.Ldap, Enabled = true, DisplayName = "Directory",
            Host = Ldap.Host, Port = Ldap.LdapsPort, Security = LdapSecurity.Ldaps, TlsServerName = Ldap.ServerName,
            CustomCaCertificate = Ldap.CaCertificate.ExportCertificatePem(),
            BindDn = LdapFixture.AdminDn,
            UserSearchBase = "ou=people," + LdapFixture.BaseDn, UserSearchFilter = "(&(objectClass=inetOrgPerson)(uid={0}))",
            UserNameAttribute = "uid", UniqueIdAttribute = "entryUUID",
            GroupSearchBase = "ou=groups," + LdapFixture.BaseDn, GroupFilter = "(member={0})",
            TrustProviderEmailVerification = true,
        };
        change?.Invoke(p);
        return p;
    }

    protected async Task PrepareLdapAsync(Action<LdapProviderSettings>? change = null, Action<ApplicationSettings>? app = null)
    {
        await SetUpAsync();
        var p = Directory(change);
        p.BindPasswordProtected ??= Secrets.Protect(LdapFixture.AdminPassword);
        await UpdateSettingsAsync(A, s =>
        {
            s.Redirects.AllowedRedirectUris.Add(ClientRedirect);
            s.Methods.AllowedLoginMethods.Add(LoginMethod.Ldap);
            s.ExternalProviders.Add(p);
            app?.Invoke(s);
        });
    }

    protected Task<HttpResponseMessage> LoginAsync(Browser b, string login, string password) =>
        b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = login, ["Input.Password"] = password });

    protected async Task<LoginResult> ServiceLoginAsync(string login, string password)
    {
        await using var scope = ScopeFor("appa");
        return await scope.ServiceProvider.GetRequiredService<ILoginService>().PasswordLoginAsync(new PasswordLoginRequest(login, password, "127.0.0.1"));
    }

    protected RegisteredProvider LdapProvider() => Factory.Services.GetRequiredService<IExternalProviderRegistry>().Get(A.Id, "ldap-hq")!;
}

public class LdapTextTests
{
    [Theory]
    [InlineData("sara", "sara")]
    [InlineData("*", "\\2a")]
    [InlineData("a)(uid=*", "a\\29\\28uid=\\2a")]
    [InlineData("a\\b", "a\\5cb")]
    [InlineData("a\0b", "a\\00b")]
    [InlineData("o(r)", "o\\28r\\29")]
    public void EscapeFilter_EscapesTheFiveSpecialCharacters(string input, string expected) => LdapText.EscapeFilter(input).ShouldBe(expected);

    [Fact]
    public void Build_PutsTheEscapedValueIntoTheFilter_AndNeverChangesItsShape() =>
        LdapText.Build("(&(objectClass=person)(uid={0}))", "x)(objectClass=*").ShouldBe("(&(objectClass=person)(uid=x\\29\\28objectClass=\\2a))");

    [Theory]
    [InlineData("cn=finance,ou=groups,dc=example,dc=org", "finance")]
    [InlineData("CN=Domain Admins,CN=Users,DC=corp,DC=com", "Domain Admins")]
    [InlineData("ou=groups,dc=example,dc=org", null)]
    public void CommonName_ReadsTheFirstCn(string dn, string? expected) => LdapText.CommonName(dn).ShouldBe(expected);
}

[Collection("ldap")]
public class LdapLoginTests(LdapFixture ldap) : LdapTestBase(ldap)
{
    [Fact]
    public async Task DirectoryUser_SignsIn_IsRegisteredWithoutAPassword_AndLinkedByTheUniqueId()
    {
        await PrepareLdapAsync();
        var b = new Browser(Factory);
        var r = await LoginAsync(b, "sara", "sara-pass-1");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
        var user = (await FindUserAsync("sara@example.org"))!;
        user.FirstName.ShouldBe("Sara");
        user.PhoneNumber.ShouldBe("+31612345678");
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.HasPasswordAsync(user)).ShouldBeFalse();
        var login = (await users.GetLoginsAsync(user)).Single();
        login.LoginProvider.ShouldBe("ldap-hq");
        login.ProviderKey.ShouldNotContain("uid=");                 // the unique id, never the DN
        Guid.TryParse(login.ProviderKey, out _).ShouldBeTrue();      // entryUUID
        (await EventsAsync("external.register")).Count.ShouldBe(1);

        (await LoginAsync(new Browser(Factory), "sara", "sara-pass-1")).StatusCode.ShouldBe(HttpStatusCode.Redirect);   // again: the link is found
        (await EventsAsync("external.register")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task WrongPassword_UnknownUser_EmptyPassword_AndAmbiguousUser_AllGiveInvalidCredentials()
    {
        await PrepareLdapAsync();
        foreach (var (login, password) in new[] { ("sara", "wrong"), ("nobody", "x"), ("sara", ""), ("twin", "twin-pass-1"), ("", "sara-pass-1") })
        {
            var result = await ServiceLoginAsync(login, password);
            result.Succeeded.ShouldBeFalse(login);
            result.ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials, $"{login}/{password}");
        }
        (await FindUserAsync("sara@example.org")).ShouldBeNull();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*)(uid=*")]
    [InlineData("sara)(|(uid=*")]
    [InlineData("sar*")]
    [InlineData("\\2a")]
    public async Task FilterInjection_FindsNobody(string login)
    {
        await PrepareLdapAsync();
        var result = await ServiceLoginAsync(login, "sara-pass-1");
        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task NoPasswordOrUserName_IsEverLogged_OrAudited()
    {
        await PrepareLdapAsync();
        await ServiceLoginAsync("sara", "wrong-password-xyz");
        await ServiceLoginAsync("sara", "sara-pass-1");
        var logs = string.Join("\n", Factory.Logs.Entries.Select(e => e.Message));
        logs.ShouldNotContain("wrong-password-xyz");
        logs.ShouldNotContain("sara-pass-1");
        logs.ShouldNotContain(LdapFixture.AdminPassword);
        foreach (var e in await EventsAsync("ldap.login.failed")) { e.DataJson!.ShouldNotContain("wrong-password-xyz"); e.DataJson!.ShouldNotContain("\"sara\""); }
    }

    [Fact]
    public async Task StartTls_Works_Too()
    {
        await PrepareLdapAsync(p => { p.Security = LdapSecurity.StartTls; p.Port = Ldap.LdapPort; });
        (await ServiceLoginAsync("dan", "dan-pass-1")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Certificate_MustBeTrusted_AndMatchTheName()
    {
        await PrepareLdapAsync(p => p.CustomCaCertificate = null);   // the test CA is not in the system store
        (await ServiceLoginAsync("dan", "dan-pass-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.LdapUnavailable);
        await UpdateSettingsAsync(A, s => { var p = (LdapProviderSettings)s.ExternalProviders[0]; p.CustomCaCertificate = Ldap.CaCertificate.ExportCertificatePem(); p.TlsServerName = "other.name"; });
        (await ServiceLoginAsync("dan", "dan-pass-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.LdapUnavailable);
        await UpdateSettingsAsync(A, s => { var p = (LdapProviderSettings)s.ExternalProviders[0]; p.TlsServerName = Ldap.ServerName; });
        (await ServiceLoginAsync("dan", "dan-pass-1")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateServerCertificateOff_AcceptsAnyCertificate()
    {
        await PrepareLdapAsync(p => { p.CustomCaCertificate = null; p.ValidateServerCertificate = false; });
        (await ServiceLoginAsync("dan", "dan-pass-1")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DirectoryDown_OrWrongServiceAccount_IsLdapUnavailable_NotInvalidCredentials()
    {
        await PrepareLdapAsync(p => p.Port = 1);
        var down = await ServiceLoginAsync("dan", "dan-pass-1");
        down.ErrorCode.ShouldBe(Auth2ErrorCodes.LdapUnavailable);
        await UpdateSettingsAsync(A, s => { var p = (LdapProviderSettings)s.ExternalProviders[0]; p.Port = Ldap.LdapsPort; p.BindPasswordProtected = Secrets.Protect("wrong-service-password"); });
        (await ServiceLoginAsync("dan", "dan-pass-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.LdapUnavailable);
    }

    [Fact]
    public async Task Groups_DirectAndNested_AreReadAndMapped()
    {
        await PrepareLdapAsync(p => { p.NestedGroups = true; p.GroupMappingMode = GroupMappingMode.Sync; });
        Guid role;
        await using (var scope = ScopeFor("appa"))
        {
            await scope.ServiceProvider.GetRequiredService<IAccessService>().CreateRoleAsync("staff-role");
            role = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                scope.ServiceProvider.GetRequiredService<Persistence.Auth2DbContext>().Roles.Where(r => r.Name == "staff-role").Select(r => r.Id));
        }
        await using (var scope = NewScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var db = scope.ServiceProvider.GetRequiredService<Persistence.Auth2DbContext>();
            db.ExternalGroupMappings.Add(new ExternalGroupMapping { ApplicationId = A.Id, ProviderName = "ldap-hq", SourceType = MappingSourceType.Group, MatchType = MappingMatchType.Exact, SourceValue = "staff", TargetType = MappingTargetType.Role, TargetId = role });
            await db.SaveChangesAsync();
        }
        // sara is in "finance" only; "finance" is a member of "staff" (nested)
        (await ServiceLoginAsync("sara", "sara-pass-1")).Succeeded.ShouldBeTrue();
        await using var check = ScopeFor("appa");
        var users = check.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.GetRolesAsync((await users.FindByEmailAsync("sara@example.org"))!)).ShouldContain("staff-role");
    }

    [Fact]
    public async Task Lookup_ShowsAttributesAndGroups_WithoutAPassword_AndNestingIsOptional()
    {
        await PrepareLdapAsync();
        var directory = Factory.Services.GetRequiredService<ILdapDirectory>();
        var flat = await directory.LookupAsync(LdapProvider(), "sara", default);
        flat.Dn.ShouldBe("uid=sara,ou=people,dc=example,dc=org");
        flat.Attributes["mail"].ShouldBe(["sara@example.org"]);
        flat.Groups.ShouldContain("finance");
        flat.Groups.ShouldNotContain("staff");   // nesting is off
        await UpdateSettingsAsync(A, s => ((LdapProviderSettings)s.ExternalProviders[0]).NestedGroups = true);
        (await directory.LookupAsync(LdapProvider(), "sara", default)).Groups.ShouldContain("staff");
        await directory.TestConnectionAsync(LdapProvider(), default);
        (await Should.ThrowAsync<LdapAuthException>(() => directory.LookupAsync(LdapProvider(), "nobody", default))).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task FailedPasswords_CountTowardTheLockoutOfTheLinkedUser()
    {
        await PrepareLdapAsync(null, s => { s.Lockout.MaxFailedAttempts = 3; s.Lockout.LockoutEnabled = true; });
        (await ServiceLoginAsync("dan", "dan-pass-1")).Succeeded.ShouldBeTrue();
        for (var i = 0; i < 3; i++) (await ServiceLoginAsync("dan", "wrong")).Succeeded.ShouldBeFalse();
        var locked = await ServiceLoginAsync("dan", "dan-pass-1");
        locked.Succeeded.ShouldBeFalse();
        locked.ErrorCode.ShouldBe(Auth2ErrorCodes.LockedOut);
    }

    [Fact]
    public async Task RateLimit_StopsTheCallsBeforeTheDirectoryIsAsked()
    {
        await PrepareLdapAsync();
        await using var scope = ScopeFor("appa");
        var limiter = scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Infrastructure.Operations.IRateLimiter>();
        _ = limiter;
        var options = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Auth2ServerOptions>>().Value;
        options.RateLimiting.Policies["ldap"] = new RateLimitRule { PermitLimit = 2, WindowSeconds = 60 };
        (await ServiceLoginAsync("dan", "x")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        (await ServiceLoginAsync("dan", "x")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        (await ServiceLoginAsync("dan", "dan-pass-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.RateLimited);
    }

    [Fact]
    public async Task LoginMethodLdap_MustBeAllowed()
    {
        await PrepareLdapAsync(null, s => s.Methods.AllowedLoginMethods.Remove(LoginMethod.Ldap));
        (await ServiceLoginAsync("dan", "dan-pass-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);
    }

    [Fact]
    public async Task Unlink_OfADirectoryLogin_IsRefused_InLdapOnlyMode()
    {
        await PrepareLdapAsync();
        await ServiceLoginAsync("dan", "dan-pass-1");
        var user = (await FindUserAsync("dan@example.org"))!;
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = (await users.FindByIdAsync(user.Id.ToString()))!;
        var key = (await users.GetLoginsAsync(u)).Single().ProviderKey;
        (await scope.ServiceProvider.GetRequiredService<IExternalLinkService>().UnlinkAsync(u, "ldap-hq", key)).ErrorCode.ShouldBe(Auth2ErrorCodes.CannotRemoveLastLoginMethod);
    }
}

[Collection("ldap")]
public class LdapModeTests(LdapFixture ldap) : LdapTestBase(ldap)
{
    private async Task<AppUser> LocalUserAsync(string name, string password)
    {
        var user = await CreateUserInAsync("appa", name, password);
        return user;
    }

    [Fact]
    public async Task LdapOnly_IgnoresTheLocalPassword()
    {
        await PrepareLdapAsync(p => p.Mode = LdapMode.LdapOnly);
        await LocalUserAsync("localonly", PassA);
        (await ServiceLoginAsync("localonly", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task LocalThenLdap_TriesThePasswordFirst_ThenTheDirectory()
    {
        await PrepareLdapAsync(p => p.Mode = LdapMode.LocalThenLdap);
        await LocalUserAsync("localuser", PassA);
        (await ServiceLoginAsync("localuser", PassA)).Succeeded.ShouldBeTrue();
        (await ServiceLoginAsync("dan", "dan-pass-1")).Succeeded.ShouldBeTrue();
        (await ServiceLoginAsync("localuser", "wrong")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task LdapThenLocal_TriesTheDirectoryFirst_ThenThePassword_AndSurvivesADirectoryOutage()
    {
        await PrepareLdapAsync(p => p.Mode = LdapMode.LdapThenLocal);
        await LocalUserAsync("localuser", PassA);
        (await ServiceLoginAsync("dan", "dan-pass-1")).Succeeded.ShouldBeTrue();
        (await ServiceLoginAsync("localuser", PassA)).Succeeded.ShouldBeTrue();
        await UpdateSettingsAsync(A, s => ((LdapProviderSettings)s.ExternalProviders[0]).Port = 1);
        (await ServiceLoginAsync("dan", "dan-pass-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.LdapUnavailable);
        // a directory outage is not "wrong password": the local password is not tried
    }

    [Fact]
    public async Task NoLdapProvider_LeavesThePasswordLoginUntouched()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa", "sara", PassA);
        (await ServiceLoginAsync("sara", PassA)).Succeeded.ShouldBeTrue();
    }
}
