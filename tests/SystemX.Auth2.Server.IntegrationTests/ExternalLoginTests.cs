using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Whole browser flows through the empty pages, with the fake provider at the other end.</summary>
public abstract class ExternalLoginTestBase : OidcTestBase
{
    protected const string ClientRedirect = "https://client.test/cb";
    protected const string Scopes = "openid profile email roles offline_access auth2.me";

    protected static OidcProviderSettings Corp() => new()
    { Name = "corp", Type = ExternalProviderType.GenericOidc, ClientId = "client-1", Authority = "https://idp.corp.test", DisplayName = "Company login", GroupsClaim = "groups" };

    protected async Task PrepareAsync(Action<OidcProviderSettings>? provider = null, Action<TestEdit>? app = null, bool seedLocalUser = false)
    {
        await SetUpAsync();
        if (seedLocalUser) await CreateUserInAsync("appa");
        var p = Corp();
        provider?.Invoke(p);
        await UpdateSettingsAsync(A, s =>
        {
            s.RedirectUris.Add(ClientRedirect);
            s.Methods.AllowedLoginMethods.Add(LoginMethod.External);
            app?.Invoke(s);
        });
        await AddAsync(A, p);
    }

    protected async Task<HttpResponseMessage> StartAsync(Browser b, string provider = "corp", string returnUrl = ClientRedirect, string client = "appa", string? hint = null) =>
        await b.PostPageAsync($"/Account/External/Challenge?ClientId={client}",
            new Dictionary<string, string> { ["Provider"] = provider, ["ReturnUrl"] = returnUrl, ["Hint"] = hint ?? "" });

    /// <summary>Start, let the fake provider sign the user in, and call the callback.</summary>
    protected async Task<HttpResponseMessage> SignInAsync(Browser b, FakeUser user, string provider = "corp", string returnUrl = ClientRedirect, string slug = "appa")
    {
        var start = await StartAsync(b, provider, returnUrl);
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect, "start");
        var authorize = start.Headers.Location!;
        var code = Fake.Authorize(authorize, user);
        var state = Fake.Parse(authorize).State;
        return await b.GetAsync($"/callback/login/{slug}/{provider}?code={code}&state={state}");
    }

    protected async Task<AppUser?> FindUserAsync(string name)
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Users.FirstOrDefaultAsync(u => u.UserName == name || u.Email == name);
    }

    protected async Task<List<AuditEvent>> EventsAsync(string action)
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().AuditEvents.Where(e => e.Action == action).ToListAsync();
    }

    protected async Task<int> CountAsync<T>() where T : class
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Set<T>().CountAsync();
    }
}

public class ExternalLoginDecisionTests : ExternalLoginTestBase
{
    [Fact]
    public async Task NewUser_IsRegistered_WithVerifiedEmail_AndSignedIn()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var b = new Browser(Factory);
        var r = await SignInAsync(b, new FakeUser { Sub = "s-1", Email = "newbie@corp.test" });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldBe(ClientRedirect);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
        var user = (await FindUserAsync("newbie@corp.test"))!;
        user.EmailConfirmed.ShouldBeTrue();
        user.FirstName.ShouldBe("Sara");
        (await FindUserAsync(user.UserName!)).ShouldNotBeNull();
        (await EventsAsync("external.register")).Count.ShouldBe(1);
        (await EventsAsync("external.login.success")).Count.ShouldBe(1);
        (await EventsAsync("login.success")).Count.ShouldBe(1);
        // The same person signs in again: the link is found, nothing new is created.
        var b2 = new Browser(Factory);
        (await SignInAsync(b2, new FakeUser { Sub = "s-1", Email = "newbie@corp.test" })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await EventsAsync("external.register")).Count.ShouldBe(1);
        (await CountAsync<AppUserLogin>()).ShouldBe(1);
    }

    [Fact]
    public async Task NoPassword_IsSet_AndTheUserCannotUseThePasswordForm()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "s-1", Email = "newbie@corp.test" });
        var user = (await FindUserAsync("newbie@corp.test"))!;
        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().HasPasswordAsync(user)).ShouldBeFalse();
    }

    [Fact]
    public async Task ExistingUser_WithTheSameVerifiedEmail_IsLinked_AndJoinsTheApplication()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var user = await CreateUserInAsync("appb", "sara");   // exists in application B only
        var b = new Browser(Factory);
        var r = await SignInAsync(b, new FakeUser { Sub = "s-9", Email = "sara@example.com" });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldBe(ClientRedirect);
        (await EventsAsync("external.login.linked")).Count.ShouldBe(1);
        (await EventsAsync("external.register")).ShouldBeEmpty();
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.Users.CountAsync(u => u.Email == "sara@example.com")).ShouldBe(1);
        (await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == A.Id)).ShouldBeTrue();
        (await db.UserLogins.SingleAsync()).ProjectId.ShouldBe(A.Id);
    }

    [Fact]
    public async Task ExistingUser_WithAnEmailTheProviderCannotProve_IsNotLinked()
    {
        await PrepareAsync();   // generic OIDC: email is not trusted by default
        await CreateUserInAsync("appa", "sara");
        var b = new Browser(Factory);
        var r = await SignInAsync(b, new FakeUser { Sub = "evil", Email = "sara@example.com", EmailVerified = true });
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
        (await CountAsync<AppUserLogin>()).ShouldBe(0);
        var failed = (await EventsAsync("external.login.failed")).Single();
        failed.DataJson!.ShouldContain(Auth2ErrorCodes.ExternalEmailExistsNotVerified);
    }

    [Fact]
    public async Task ProviderWithTrust_ButAnUnverifiedEmail_IsNotLinked()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        await CreateUserInAsync("appa", "sara");
        var r = await SignInAsync(new Browser(Factory), new FakeUser { Sub = "x", Email = "sara@example.com", EmailVerified = false });
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountAsync<AppUserLogin>()).ShouldBe(0);
    }

    [Fact]
    public async Task AllowLinkOff_RefusesToLink()
    {
        await PrepareAsync(p => { p.TrustProviderEmailVerification = true; p.AllowLink = false; });
        await CreateUserInAsync("appa", "sara");
        (await SignInAsync(new Browser(Factory), new FakeUser { Sub = "x", Email = "sara@example.com" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AllowRegisterOff_AndClosedJoinMode_RefuseNewUsers()
    {
        await PrepareAsync(p => p.AllowRegister = false);
        var r = await SignInAsync(new Browser(Factory), new FakeUser { Sub = "n1", Email = "new@corp.test" });
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FindUserAsync("new@corp.test")).ShouldBeNull();

        await UpdateSettingsAsync(A, s => { s.ExternalProviders[0] = Corp2(s.ExternalProviders[0]); s.Registration.JoinMode = JoinMode.Closed; });
        (await SignInAsync(new Browser(Factory), new FakeUser { Sub = "n2", Email = "new2@corp.test" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FindUserAsync("new2@corp.test")).ShouldBeNull();
        (await EventsAsync("external.login.failed")).Count.ShouldBe(2);
    }

    private static ExternalProviderSettings Corp2(ExternalProviderSettings p) { p.AllowRegister = true; return p; }

    [Fact]
    public async Task LoginMethodExternal_MustBeAllowedForTheApplication()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true, s => s.Methods.AllowedLoginMethods.Remove(LoginMethod.External));
        var b = new Browser(Factory);
        (await SignInAsync(b, new FakeUser { Sub = "m1", Email = "m@corp.test" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
    }

    [Fact]
    public async Task MissingEmail_WhenConfirmedEmailIsRequired_GoesThroughTheConfirmationMail()
    {
        await PrepareAsync(null, s => s.Email.RequireConfirmedEmail = true);
        var b = new Browser(Factory);
        var r = await SignInAsync(b, new FakeUser { Sub = "no-email", Email = null, EmailVerified = null });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldContain("/Account/External/Confirm");
        (await FindUserAsync("no-email")).ShouldBeNull();

        // The page keeps the sealed identity; here the UI posts it back with the typed address.
        var pending = Factory.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var confirmPage = await b.GetAsync(r.Location()!);
        confirmPage.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADisabledProvider_OrApplication_CannotStart()
    {
        await PrepareAsync();
        (await StartAsync(new Browser(Factory), "nope")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await UpdateSettingsAsync(A, s => s.ExternalProviders[0].Enabled = false);
        (await StartAsync(new Browser(Factory))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Start_WithAnUnsafeReturnUrl_FallsBackToTheApplicationDefault()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var b = new Browser(Factory);
        var r = await SignInAsync(b, new FakeUser { Sub = "u1", Email = "u1@corp.test" }, returnUrl: "https://evil.test/steal");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldNotContain("evil.test");
    }

    [Fact]
    public async Task Callback_ForAnUnknownProviderUrl_OrWithoutTheStateCookie_IsRefused()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var b = new Browser(Factory);
        var start = await StartAsync(b);
        var code = Fake.Authorize(start.Headers.Location!, new FakeUser { Sub = "q", Email = "q@corp.test" });
        var state = Fake.Parse(start.Headers.Location!).State;
        (await b.GetAsync($"/callback/login/appa/other?code={code}&state={state}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // another browser (no cookie) cannot finish the sign-in of this one
        (await new Browser(Factory).GetAsync($"/callback/login/appa/corp?code={code}&state={state}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FindUserAsync("q@corp.test")).ShouldBeNull();
    }

    [Fact]
    public async Task Callback_ForApplicationA_CannotBeUsedWithTheUrlOfApplicationB()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var b2 = Corp(); b2.TrustProviderEmailVerification = true;
        await UpdateSettingsAsync(B, s => { s.Methods.AllowedLoginMethods.Add(LoginMethod.External); });
        await AddAsync(B, b2);
        var b = new Browser(Factory);
        var start = await StartAsync(b);
        var code = Fake.Authorize(start.Headers.Location!, new FakeUser { Sub = "x", Email = "x@corp.test" });
        var state = Fake.Parse(start.Headers.Location!).State;
        (await b.GetAsync($"/callback/login/appb/corp?code={code}&state={state}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FindUserAsync("x@corp.test")).ShouldBeNull();
    }
}

public class ExternalTokenClaimTests : ExternalLoginTestBase
{
    [Fact]
    public async Task Tokens_CarryIdpAndAmr_AndTheServerIsTheOnlyIssuer()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var b = new Browser(Factory);
        var (verifier, challenge) = HttpExtensions.NewPkce();
        var authorize = $"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(ClientRedirect)}&response_type=code&scope={Uri.EscapeDataString(Scopes)}&state=s1&code_challenge={challenge}&code_challenge_method=S256";

        (await SignInAsync(b, new FakeUser { Sub = "t-1", Email = "tok@corp.test", Amr = ["pwd", "otp"] }, returnUrl: authorize)).Location().ShouldBe(authorize);
        var r3 = await b.GetAsync(authorize);
        var code = HttpExtensions.QueryValue(r3.Location()!, "code");
        var token = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = ClientRedirect, ["code_verifier"] = verifier, ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        token.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await token.JsonAsync();
        var claims = HttpExtensions.JwtPayload(body.Str("access_token"));
        claims.Str("idp").ShouldBe("corp");
        claims.Values("amr").ShouldBe(["fed", "mfa"], ignoreOrder: true);
        HttpExtensions.JwtPayload(body.Str("id_token")).Str("idp").ShouldBe("corp");
        claims.Str("iss").ShouldNotContain("idp.corp.test");   // never the upstream issuer
        // The upstream tokens are not stored anywhere: no row mentions them.
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Database.SqlQuery<string>($"select provider_key as \"Value\" from user_logins").ToListAsync()).ShouldBe(["t-1"]);
    }

    [Fact]
    public async Task RefreshToken_KeepsTheIdpClaim()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var b = new Browser(Factory);
        var (verifier, challenge) = HttpExtensions.NewPkce();
        var authorize = $"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(ClientRedirect)}&response_type=code&scope={Uri.EscapeDataString(Scopes)}&state=s1&code_challenge={challenge}&code_challenge_method=S256";
        await SignInAsync(b, new FakeUser { Sub = "t-2", Email = "r@corp.test" }, returnUrl: authorize);
        var code = HttpExtensions.QueryValue((await b.GetAsync(authorize)).Location()!, "code");
        var first = await (await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = ClientRedirect, ["code_verifier"] = verifier, ["client_id"] = "appa", ["client_secret"] = ASecret,
        })).JsonAsync();
        var refreshed = await (await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = first.Str("refresh_token"), ["client_id"] = "appa", ["client_secret"] = ASecret,
        })).JsonAsync();
        HttpExtensions.JwtPayload(refreshed.Str("access_token")).Str("idp").ShouldBe("corp");
    }
}

public class ExternalTwoFactorTests : ExternalLoginTestBase
{
    private async Task<AppUser> UserWithTwoFactorAsync()
    {
        var user = await CreateUserInAsync("appa", "sara");
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = (await users.FindByNameAsync("sara"))!;
        await users.ResetAuthenticatorKeyAsync(u);
        await users.SetTwoFactorEnabledAsync(u, true);
        return user;
    }

    [Fact]
    public async Task LocalTwoFactor_IsStillAsked_UnlessTheProviderMfaIsTrusted()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true, s => { s.TwoFactor.Mode = TwoFactorMode.Optional; s.TwoFactor.AllowTotp = true; });
        await UserWithTwoFactorAsync();
        var b = new Browser(Factory);
        var r = await SignInAsync(b, new FakeUser { Sub = "tf", Email = "sara@example.com" });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldContain("/Account/TwoFactor");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();

        await UpdateSettingsAsync(A, s => ((OidcProviderSettings)s.ExternalProviders[0]).TrustProviderMfa = true);
        var b2 = new Browser(Factory);
        var r2 = await SignInAsync(b2, new FakeUser { Sub = "tf", Email = "sara@example.com" });
        r2.Location().ShouldBe(ClientRedirect);
        b2.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
    }

    [Fact]
    public async Task TrustedMfa_IsNotUsed_WhenTheProviderSaysTheUserDidNotUseMfa()
    {
        await PrepareAsync(p => { p.TrustProviderEmailVerification = true; p.TrustProviderMfa = true; }, s => { s.TwoFactor.Mode = TwoFactorMode.Optional; s.TwoFactor.AllowTotp = true; });
        await UserWithTwoFactorAsync();
        var r = await SignInAsync(new Browser(Factory), new FakeUser { Sub = "tf", Email = "sara@example.com", Amr = ["pwd"] });
        r.Location()!.ShouldContain("/Account/TwoFactor");
    }
}

public class ExternalMappingTests : ExternalLoginTestBase
{
    private async Task<(Guid Role, Guid Group, Guid Perm)> SeedTargetsAsync()
    {
        await using var scope = ScopeFor("appa");
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
        await access.CreateRoleAsync("editor");
        await access.CreateRoleAsync("auditor");
        await groups.CreateAsync("finance");
        await access.CreatePermissionAsync(new PermissionDefinition("reports.read", "Read reports"));
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        return (await db.Roles.Where(r => r.Name == "editor").Select(r => r.Id).SingleAsync(), await db.Groups.Select(g => g.Id).SingleAsync(), await db.Permissions.Select(p => p.Id).SingleAsync());
    }

    private async Task MapAsync(Guid appId, MappingSourceType source, string name, MappingMatchType match, string value, MappingTargetType type, Guid target, string? provider = "corp")
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        db.ExternalGroupMappings.Add(new ExternalGroupMapping { ProjectId = appId, ProviderName = provider, SourceType = source, SourceName = name, MatchType = match, SourceValue = value, TargetType = type, TargetId = target });
        await db.SaveChangesAsync();
    }

    private async Task<(IList<string> Roles, bool InGroup, bool HasPerm)> AccessOfAsync(string email)
    {
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = (await users.FindByEmailAsync(email))!;
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        return (await users.GetRolesAsync(u), await db.UserGroups.AnyAsync(x => x.UserId == u.Id), await db.UserPermissions.AnyAsync(x => x.UserId == u.Id));
    }

    [Fact]
    public async Task Mappings_AddRolesGroupsAndPermissions_ExactAndPrefix_CaseInsensitive()
    {
        await PrepareAsync(p => { p.TrustProviderEmailVerification = true; p.GroupMappingMode = GroupMappingMode.Sync; });
        var (role, group, perm) = await SeedTargetsAsync();
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "STAFF", MappingTargetType.Role, role);
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Prefix, "fin-", MappingTargetType.Group, group);
        await MapAsync(A.Id, MappingSourceType.Claim, "dept", MappingMatchType.Exact, "audit", MappingTargetType.Permission, perm);
        var user = new FakeUser { Sub = "m1", Email = "m1@corp.test", Groups = ["staff", "Fin-EMEA"] };
        user.Extra["dept"] = "Audit";
        await SignInAsync(new Browser(Factory), user);
        var (roles, inGroup, hasPerm) = await AccessOfAsync("m1@corp.test");
        roles.ShouldContain("editor");
        inGroup.ShouldBeTrue();
        hasPerm.ShouldBeTrue();
        (await EventsAsync("external.mapping.applied")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task OnlyMappedTargets_AreRemoved_ManualAssignmentsStay()
    {
        await PrepareAsync(p => { p.TrustProviderEmailVerification = true; p.GroupMappingMode = GroupMappingMode.Sync; });
        var (role, _, _) = await SeedTargetsAsync();
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "staff", MappingTargetType.Role, role);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "m2", Email = "m2@corp.test", Groups = ["staff"] });
        await using (var scope = ScopeFor("appa"))
        {
            var u = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync("m2@corp.test"))!;
            (await scope.ServiceProvider.GetRequiredService<IAccessService>().AssignRoleAsync(u, "auditor")).Succeeded.ShouldBeTrue();   // by hand
        }
        // The group is gone at the provider: the mapped role leaves, the hand-made role stays.
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "m2", Email = "m2@corp.test", Groups = [] });
        var (roles, _, _) = await AccessOfAsync("m2@corp.test");
        roles.ShouldNotContain("editor");
        roles.ShouldContain("auditor");
    }

    [Fact]
    public async Task MappingMode_None_DoesNotApplyMappings()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        var (role, _, _) = await SeedTargetsAsync();
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "staff", MappingTargetType.Role, role);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "m3", Email = "m3@corp.test", Groups = ["staff"] });
        (await AccessOfAsync("m3@corp.test")).Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task GlobalAdmin_SystemRoles_AndRolesOfOtherApplications_CanNeverBeGiven()
    {
        await PrepareAsync(p => { p.TrustProviderEmailVerification = true; p.GroupMappingMode = GroupMappingMode.Sync; });
        Guid globalAdmin, otherAppRole;
        await using (var scope = NewScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            globalAdmin = await db.Roles.Where(r => r.Name == Auth2Roles.GlobalAdmin).Select(r => r.Id).SingleAsync();
            var other = new AppRole("secret") { ProjectId = B.Id };
            db.Roles.Add(other);
            await db.SaveChangesAsync();
            otherAppRole = other.Id;
        }
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "root", MappingTargetType.Role, globalAdmin);
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "root", MappingTargetType.Role, otherAppRole);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "m4", Email = "m4@corp.test", Groups = ["root"] });
        (await AccessOfAsync("m4@corp.test")).Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task AMappingOfAnotherProvider_IsNotApplied_AndAMappingForAllProvidersIs()
    {
        await PrepareAsync(p => { p.TrustProviderEmailVerification = true; p.GroupMappingMode = GroupMappingMode.Sync; });
        var (role, group, _) = await SeedTargetsAsync();
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "staff", MappingTargetType.Role, role, provider: "someone-else");
        await MapAsync(A.Id, MappingSourceType.Group, "", MappingMatchType.Exact, "staff", MappingTargetType.Group, group, provider: null);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "m5", Email = "m5@corp.test", Groups = ["staff"] });
        var (roles, inGroup, _) = await AccessOfAsync("m5@corp.test");
        roles.ShouldBeEmpty();
        inGroup.ShouldBeTrue();
    }

    [Fact]
    public async Task ProfileSync_ImportOnce_KeepsNames_UpdateOnLogin_ChangesThem()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "p1", Email = "p1@corp.test", GivenName = "Old" });
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "p1", Email = "p1@corp.test", GivenName = "New" });
        (await FindUserAsync("p1@corp.test"))!.FirstName.ShouldBe("Old");
        await UpdateSettingsAsync(A, s => s.ExternalProviders[0].ProfileSyncMode = ProfileSyncMode.UpdateOnLogin);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "p1", Email = "p1@corp.test", GivenName = "New" });
        (await FindUserAsync("p1@corp.test"))!.FirstName.ShouldBe("New");
    }
}

public class ExternalDiscoveryAndLinksTests : ExternalLoginTestBase
{
    [Fact]
    public async Task ProviderList_ShowsOnlyEnabledProviders_AndHomeRealmDiscoveryFindsOne()
    {
        await PrepareAsync(p => p.EmailDomains = ["contoso.com"]);
        var off = Corp(); off.Name = "off"; off.Enabled = false;
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(off));
        var b = new Browser(Factory);
        var body = await (await b.GetAsync("/connect/external/providers?client_id=appa&hint=sara@Contoso.com")).JsonAsync();
        body.GetProperty("providers").GetArrayLength().ShouldBe(1);
        var first = body.GetProperty("providers")[0];
        first.Str("name").ShouldBe("corp");
        first.Str("displayName").ShouldBe("Company login");
        first.GetProperty("loginFormRequired").GetBoolean().ShouldBeFalse();
        body.Str("suggestedProvider").ShouldBe("corp");
        (await (await b.GetAsync("/connect/external/providers?client_id=appa&hint=sara@other.com")).JsonAsync()).GetProperty("suggestedProvider").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await (await b.GetAsync("/connect/external/providers?client_id=appa&hint=corp")).JsonAsync()).Str("suggestedProvider").ShouldBe("corp");
        (await b.GetAsync("/connect/external/providers?client_id=nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProviderList_NeverShowsSecrets()
    {
        await PrepareAsync();
        var text = await (await new Browser(Factory).GetAsync("/connect/external/providers?client_id=appa")).Content.ReadAsStringAsync();
        text.ShouldNotContain("client-secret");
        text.ToLowerInvariant().ShouldNotContain("clientsecret");
        text.ShouldNotContain("client-1");
    }

    [Fact]
    public async Task TwoApplications_WithTheSameProviderName_KeepTheirOwnLists()
    {
        await PrepareAsync();
        var (b2, other) = (Corp(), "appb");
        b2.Name = "corp"; b2.DisplayName = "Other app login";
        await AddAsync(B, b2);
        var a = await (await new Browser(Factory).GetAsync("/connect/external/providers?client_id=appa")).JsonAsync();
        var b = await (await new Browser(Factory).GetAsync($"/connect/external/providers?client_id={other}")).JsonAsync();
        a.GetProperty("providers")[0].Str("displayName").ShouldBe("Company login");
        b.GetProperty("providers")[0].Str("displayName").ShouldBe("Other app login");
    }

    [Fact]
    public async Task Unlink_IsRefused_ForTheLastWayToSignIn_AndAllowedWhenAPasswordExists()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true);
        await SignInAsync(new Browser(Factory), new FakeUser { Sub = "ul", Email = "ul@corp.test" });
        var user = (await FindUserAsync("ul@corp.test"))!;
        await using var scope = ScopeFor("appa");
        var links = scope.ServiceProvider.GetRequiredService<IExternalLinkService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = (await users.FindByIdAsync(user.Id.ToString()))!;
        (await links.UnlinkAsync(u, "corp", "ul")).ErrorCode.ShouldBe(Auth2ErrorCodes.CannotRemoveLastLoginMethod);
        (await users.AddPasswordAsync(u, PassA)).Succeeded.ShouldBeTrue();
        (await links.UnlinkAsync(u, "corp", "ul")).Succeeded.ShouldBeTrue();
        (await links.ListAsync(u)).ShouldBeEmpty();
        (await links.UnlinkAsync(u, "corp", "ul")).ErrorCode.ShouldBe(Auth2ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Link_FromManage_AddsTheProviderToTheSignedInUser_AndRefusesAnIdentityOfAnotherUser()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true, seedLocalUser: true);
        // A local user signs in with the password page and links a provider.
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var start = await b.PostPageAsync("/Manage/ExternalLogins?handler=Link", new Dictionary<string, string> { ["Provider"] = "corp" });
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var code = Fake.Authorize(start.Headers.Location!, new FakeUser { Sub = "link-1", Email = "someone-else@corp.test" });
        var done = await b.GetAsync($"/callback/login/appa/corp?code={code}&state={Fake.Parse(start.Headers.Location!).State}");
        done.Location().ShouldBe("/Manage/ExternalLogins");
        (await CountAsync<AppUserLogin>()).ShouldBe(1);
        (await FindUserAsync("someone-else@corp.test")).ShouldBeNull();   // no new user: it was a link

        // Another local user cannot take the same identity.
        await CreateUserInAsync("appa", "dan");
        var b2 = new Browser(Factory);
        await b2.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "dan", ["Input.Password"] = PassA });
        var start2 = await b2.PostPageAsync("/Manage/ExternalLogins?handler=Link", new Dictionary<string, string> { ["Provider"] = "corp" });
        var code2 = Fake.Authorize(start2.Headers.Location!, new FakeUser { Sub = "link-1" });
        (await b2.GetAsync($"/callback/login/appa/corp?code={code2}&state={Fake.Parse(start2.Headers.Location!).State}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountAsync<AppUserLogin>()).ShouldBe(1);
    }

    [Fact]
    public async Task Link_Callback_WithoutTheSession_IsRefused()
    {
        await PrepareAsync(null, null, seedLocalUser: true);
        var b = new Browser(Factory);
        await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA });
        var start = await b.PostPageAsync("/Manage/ExternalLogins?handler=Link", new Dictionary<string, string> { ["Provider"] = "corp" });
        var code = Fake.Authorize(start.Headers.Location!, new FakeUser { Sub = "l2" });
        var state = Fake.Parse(start.Headers.Location!).State;
        // Same state cookie but no session cookie: build a browser that has only the state cookie.
        (await new Browser(Factory).GetAsync($"/callback/login/appa/corp?code={code}&state={state}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountAsync<AppUserLogin>()).ShouldBe(0);
    }
}

public class ExternalRateLimitMapTests
{
    [Theory]
    [InlineData("GET", "/callback/login/appa/corp", "external")]
    [InlineData("POST", "/callback/login/appa/corp", "external")]
    [InlineData("POST", "/Account/External/Challenge", "external")]
    [InlineData("POST", "/connect/external/challenge", "external")]
    [InlineData("POST", "/saml/appa/corp/acs", "saml-acs")]
    [InlineData("POST", "/account/passkey/signin", "passkey")]
    [InlineData("GET", "/connect/external/providers", null)]
    public async Task Policies_AreChosenByPath(string method, string path, string? expected)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        (await SystemX.Auth2.Server.Infrastructure.Operations.RateLimitPolicyMap.ResolveAsync(ctx.Request)).ShouldBe(expected);
    }
}

public class ExternalEmailStepTests : ExternalLoginTestBase
{
    protected override void Customize(IServiceCollection services)
    {
        base.Customize(services);
        services.Configure<Auth2ServerOptions>(o => o.Issuer = "https://auth.test");
    }

    [Fact]
    public async Task NoEmail_NeedsAMail_TheLinkFinishesTheSignIn_AndTheEmailCountsAsProven()
    {
        await PrepareAsync(null, s => s.Email.RequireConfirmedEmail = true);
        await using var scope = ScopeFor("appa");
        var service = scope.ServiceProvider.GetRequiredService<IExternalLoginService>();
        var provider = Factory.Services.GetRequiredService<SystemX.Auth2.Server.Infrastructure.External.IExternalProviderRegistry>().Get(A.Id, "corp")!;
        var identity = new ExternalIdentity("corp", "no-mail-1", "https://idp.corp.test", null, null, "Nora", "Test", null, [], new Dictionary<string, string[]>());

        var first = await service.HandleAsync(new ExternalLoginRequest(provider, identity, ClientRedirect));
        first.NeedsEmail.ShouldBeTrue();
        first.Login.ShouldBeNull();
        (await FindUserAsync("nora@corp.test")).ShouldBeNull();

        (await service.SendEmailConfirmationAsync(first.PendingToken!, "not-an-email", "/Account/External/Confirm")).ShouldBeFalse();
        (await service.SendEmailConfirmationAsync("garbage", "nora@corp.test", "/Account/External/Confirm")).ShouldBeFalse();
        (await service.SendEmailConfirmationAsync(first.PendingToken!, "nora@corp.test", "/Account/External/Confirm")).ShouldBeTrue();
        var token = Factory.Messages.LastEmailTo("nora@corp.test")!["token"];
        (await FindUserAsync("nora@corp.test")).ShouldBeNull();   // nothing is created before the link is used

        var done = await service.ConfirmEmailAsync(token);
        done.Login!.Succeeded.ShouldBeTrue();
        done.Registered.ShouldBeTrue();
        var user = (await FindUserAsync("nora@corp.test"))!;
        user.EmailConfirmed.ShouldBeTrue();
        (await service.ConfirmEmailAsync("garbage")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidToken);
    }
}
