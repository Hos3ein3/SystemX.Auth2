using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class LoginServiceTests : IdentityTestBase
{
    protected async Task<LoginResult> LoginAsync(string clientId, string login, string password)
    {
        await using var scope = ScopeFor(clientId);
        return await scope.ServiceProvider.GetRequiredService<ILoginService>().PasswordLoginAsync(new PasswordLoginRequest(login, password, "203.0.113.5", "test-agent"));
    }

    private async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.AuditEvents.Where(a => a.Action == action).OrderBy(a => a.OccurredAtUtc).ToListAsync();
    }

    [Fact]
    public async Task Login_Success_ByUserNameAndByEmail_ReturnsUserAndAmr()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        var byName = await LoginAsync("appa", "sara", PassA);
        byName.Succeeded.ShouldBeTrue();
        byName.Amr.ShouldBe(["pwd"]);
        byName.SessionId.ShouldNotBeNullOrEmpty();
        (await LoginAsync("appa", "SARA@example.com", PassA)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_ByPhone_NeedsTheMethodToBeAllowed()
    {
        await SetUpAsync();
        await using (var scope = ScopeFor("appa"))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.CreateAsync(new AppUser { UserName = "sara", Email = "s@example.com", PhoneNumber = "+31612345678" }, PassA)).Succeeded.ShouldBeTrue();
        }
        (await LoginAsync("appa", "+31612345678", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);

        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods.Add(LoginMethod.PhonePassword));
        (await LoginAsync("appa", "+31612345678", PassA)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_MethodNotAllowed_IsReported()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.EmailPassword]);
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);
        (await LoginAsync("appa", "sara@example.com", PassA)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_UnknownUserAndWrongPassword_GiveTheSameError()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        var unknown = await LoginAsync("appa", "nobody", PassA);
        var wrong = await LoginAsync("appa", "sara", "Wrong-Password-1");
        unknown.ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        wrong.ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        unknown.TwoFactorToken.ShouldBeNull();
    }

    [Fact]
    public async Task Login_UserNotInApplication_LooksLikeWrongPassword_ButAuditKnowsTheReason()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        var result = await LoginAsync("appb", "sara", PassA);
        result.ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        (await AuditAsync("login.failed")).ShouldContain(e => e.ProjectId == B.Id && (e.DataJson ?? "").Contains(Auth2ErrorCodes.NotRegisteredInProject));
    }

    [Fact]
    public async Task Login_AppAPasswordFailsInAppB_EvenWhenUserJoinedB()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));
        (await LoginAsync("appb", "sara", PassA)).Succeeded.ShouldBeFalse();
        (await LoginAsync("appb", "sara", PassB)).Succeeded.ShouldBeTrue();
        (await LoginAsync("appa", "sara", PassB)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Login_DisabledApplication_IsRefused()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await using (var scope = NewScope())
        {
            // the real way to disable a project: it also drops the cached copy, so the next request sees it
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            var project = await db.Projects.AsNoTracking().SingleAsync(a => a.ProjectKey == "appa");
            await scope.ServiceProvider.GetRequiredService<IProjectService>().SetEnabledAsync(project.Id, false, null, null);
        }
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);   // the public answer is generic; the audit log has the reason
        (await AuditAsync("login.failed")).Last().DataJson!.ShouldContain("project_disabled");
    }

    [Fact]
    public async Task Login_InactiveUser_LooksLikeWrongPassword()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            (await db.Users.SingleAsync(u => u.UserName == "sara")).IsActive = false;
            await db.SaveChangesAsync();
        }
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_Lockout_AfterMaxFailedAttempts_EvenTheRightPasswordIsRefused_AndOtherApplicationIsFree()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));

        for (var i = 0; i < 5; i++) (await LoginAsync("appa", "sara", "Wrong-Password-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.LockedOut);
        (await LoginAsync("appb", "sara", PassB)).Succeeded.ShouldBeTrue();
        (await AuditAsync("login.locked_out")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Login_EmailNotConfirmed_IsRefused_WhenRequired()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Email.RequireConfirmedEmail = true);
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.EmailNotConfirmed);
        (await LoginAsync("appa", "sara", "Wrong-Password-1")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials); // no hint before the password is right
    }

    [Fact]
    public async Task Login_TwoFactor_RequiredStep_ThenCompleteWithTotpOrRecoveryCode()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        var (key, codes) = await EnableTotpAsync("appa");

        var first = await LoginAsync("appa", "sara", PassA);
        first.Succeeded.ShouldBeFalse();
        first.ErrorCode.ShouldBe(Auth2ErrorCodes.TwoFactorRequired);
        first.TwoFactorToken.ShouldNotBeNullOrEmpty();

        await using (var scope = ScopeFor("appa"))
        {
            var login = scope.ServiceProvider.GetRequiredService<ILoginService>();
            (await login.CompleteTwoFactorAsync(first.TwoFactorToken!, "000000", null)).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidOtp);
            var ok = await login.CompleteTwoFactorAsync(first.TwoFactorToken!, Totp.Now(key), null);
            ok.Succeeded.ShouldBeTrue();
            ok.Amr.ShouldBe(["pwd", "otp"]);
        }

        var second = await LoginAsync("appa", "sara", PassA);
        await using var scope2 = ScopeFor("appa");
        var login2 = scope2.ServiceProvider.GetRequiredService<ILoginService>();
        (await login2.CompleteTwoFactorAsync(second.TwoFactorToken!, null, codes[0])).Succeeded.ShouldBeTrue();
        (await login2.CompleteTwoFactorAsync(second.TwoFactorToken!, null, codes[0])).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidOtp); // single use
    }

    [Fact]
    public async Task Login_TwoFactorToken_IsOnlyValidForItsApplication_AndGarbageIsRefused()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));
        var (key, _) = await EnableTotpAsync("appa");
        var first = await LoginAsync("appa", "sara", PassA);

        await using var scope = ScopeFor("appb");
        var login = scope.ServiceProvider.GetRequiredService<ILoginService>();
        (await login.CompleteTwoFactorAsync(first.TwoFactorToken!, Totp.Now(key), null)).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await login.CompleteTwoFactorAsync("garbage", "123456", null)).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task Login_TwoFactorEnabledInA_IsNotAskedInB()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));
        await EnableTotpAsync("appa");
        (await LoginAsync("appb", "sara", PassB)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_TwoFactorRequiredBySettings_ButNotSetUp_ReportsSetupRequired()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.TwoFactor.Mode = TwoFactorMode.Required);
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.TwoFactorSetupRequired);
    }

    [Fact]
    public async Task Login_TwoFactorOff_IgnoresEnabledTotp()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await EnableTotpAsync("appa");
        await UpdateSettingsAsync(A, s => s.TwoFactor.Mode = TwoFactorMode.Off);
        (await LoginAsync("appa", "sara", PassA)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_Audit_WritesSuccessAndFailure_WithoutSecrets()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await LoginAsync("appa", "sara", "Wrong-Password-1");
        await LoginAsync("appa", "sara", PassA);

        var ok = await AuditAsync("login.success");
        var failed = await AuditAsync("login.failed");
        ok.ShouldHaveSingleItem().IpAddress.ShouldBe("203.0.113.5");
        failed.ShouldHaveSingleItem().DataJson!.ShouldContain("wrong_password");
        foreach (var e in ok.Concat(failed))
        {
            var text = e.DataJson ?? "";
            text.ShouldNotContain(PassA);
            text.ShouldNotContain("Wrong-Password-1");
            text.ShouldNotContain("sara@example.com");
        }
        Factory.Logs.Entries.ShouldNotContain(e => e.Message.Contains(PassA) || e.Message.Contains("Wrong-Password-1"));
    }

    [Fact]
    public async Task Login_UpdatesLastLogin_InMultiMode()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await LoginAsync("appa", "sara", PassA);
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserProjects.SingleAsync(x => x.UserId == user.Id)).LastLoginUtc.ShouldNotBeNull();
    }

    protected async Task<(string Key, List<string> Codes)> EnableTotpAsync(string clientId)
    {
        await using var scope = ScopeFor(clientId);
        var m = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = (await m.FindByNameAsync("sara"))!;
        await m.ResetAuthenticatorKeyAsync(u);
        var key = (await m.GetAuthenticatorKeyAsync(u))!;
        await m.SetTwoFactorEnabledAsync(u, true);
        var codes = (await m.GenerateNewTwoFactorRecoveryCodesAsync(u, 3))!.ToList();
        return (key, codes);
    }
}

public class SingleModeLoginTests : IdentityTestBase
{
    protected override bool SingleMode => true;

    [Fact]
    public async Task Login_SingleMode_WorksWithoutUserProjects_AndNeverWritesThem()
    {
        await MigrateAndSeedAsync();
        await using (var scope = NewScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.CreateAsync(new AppUser { UserName = "sara", Email = "sara@example.com" }, PassA)).Succeeded.ShouldBeTrue();
        }
        await using (var scope = NewScope())
        {
            var login = scope.ServiceProvider.GetRequiredService<ILoginService>();
            (await login.PasswordLoginAsync(new PasswordLoginRequest("sara", PassA))).Succeeded.ShouldBeTrue();
            (await login.PasswordLoginAsync(new PasswordLoginRequest("sara", "Wrong-Password-1"))).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        }
        await using var check = NewScope();
        var db = check.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = check.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserProjects.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Login_SingleMode_SeededGlobalAdmin_CanSignInWithTheDefaultPassword()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        (await scope.ServiceProvider.GetRequiredService<ILoginService>().PasswordLoginAsync(new PasswordLoginRequest("globaladmin", "Admin@12345!ChangeMe"))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SingleMode_Http_TokensHaveNoProjectKey_AndTheClientNeedsNoProjectKey()
    {
        await MigrateAndSeedAsync();
        await using (var scope = NewScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.CreateAsync(new AppUser { UserName = "sara", Email = "sara@example.com" }, PassA)).Succeeded.ShouldBeTrue();
            // the seeded client has no known secret: make one, like an operator would with the rotate-client-secret command
            await scope.ServiceProvider.GetRequiredService<IApplicationService>().RotateSecretAsync("default");
        }
        string secret;
        await using (var scope = NewScope())
            secret = await scope.ServiceProvider.GetRequiredService<IApplicationService>().RotateSecretAsync("default");

        var b = new Browser(Factory);
        var r = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "default", ["client_secret"] = secret, ["scope"] = "auth2.client.handshake",
        });
        r.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var claims = HttpExtensions.JwtPayload((await r.JsonAsync()).Str("access_token"));
        claims.Str("projectkey").ShouldBe("default");
        claims.TryGetProperty("project_id", out _).ShouldBeTrue();
        claims.Str("client_id").ShouldBe("default");
        claims.TryGetProperty("application_id", out _).ShouldBeTrue();
        claims.TryGetProperty("app", out _).ShouldBeFalse();   // the old claim is gone
        claims.Str("machine").ShouldBe("true");
    }
}
