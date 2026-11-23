using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public abstract class IdentityTestBase : ServerTestBase
{
    protected const string PassA = "PassA-12345!x";
    protected const string PassB = "PassB-98765!y";

    protected Auth2Application A = null!;
    protected Auth2Application B = null!;
    protected string ASecret = "";
    protected string BSecret = "";

    protected async Task SetUpAsync()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplicationService>();
        var a = await svc.CreateAsync("appa", "App A", null, null);
        var b = await svc.CreateAsync("appb", "App B", null, null);
        (A, ASecret) = (a.Application, a.ClientSecret);
        (B, BSecret) = (b.Application, b.ClientSecret);
    }

    protected async Task<AppUser> CreateUserInAsync(string clientId, string name = "sara", string password = PassA)
    {
        await using var scope = ScopeFor(clientId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = name, Email = $"{name}@example.com" };
        var result = await users.CreateAsync(user, password);
        result.Succeeded.ShouldBeTrue(string.Join(",", result.Errors.Select(e => e.Code)));
        return user;
    }

    /// <summary>Stand-in for the "join an application" service of phase 6: adds the membership row only.</summary>
    protected async Task JoinAsync(AppUser user, Auth2Application app)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        db.UserApplications.Add(new UserApplication { UserId = user.Id, ApplicationId = app.Id, JoinedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    protected async Task<T> InAsync<T>(string clientId, Func<UserManager<AppUser>, Task<T>> action)
    {
        await using var scope = ScopeFor(clientId);
        return await action(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
    }

    protected async Task UpdateSettingsAsync(Auth2Application app, Action<Domain.Settings.ApplicationSettings> change)
    {
        await using var scope = NewScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IApplicationSettingsProvider>().GetAsync(app.Id);
        var copy = Persistence.SettingsJson.Clone(settings);
        change(copy);
        await scope.ServiceProvider.GetRequiredService<IApplicationService>().UpdateSettingsAsync(app.Id, copy);
    }
}

public class PerApplicationCredentialTests : IdentityTestBase
{
    [Fact]
    public async Task Register_SameUserInTwoApplications_OneUserRow_TwoRows_TwoPasswords()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        (await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB))).Succeeded.ShouldBeTrue();

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.Users.CountAsync(u => u.UserName == "sara")).ShouldBe(1);
        (await db.UserApplications.CountAsync(x => x.UserId == user.Id)).ShouldBe(2);
        (await db.Users.SingleAsync(u => u.UserName == "sara")).PasswordHash.ShouldBeNull();

        (await InAsync("appa", async m => (await m.CheckPasswordAsync((await m.FindByNameAsync("sara"))!, PassA), await m.CheckPasswordAsync((await m.FindByNameAsync("sara"))!, PassB)))).ShouldBe((true, false));
        (await InAsync("appb", async m => (await m.CheckPasswordAsync((await m.FindByNameAsync("sara"))!, PassA), await m.CheckPasswordAsync((await m.FindByNameAsync("sara"))!, PassB)))).ShouldBe((false, true));
    }

    [Fact]
    public async Task CheckPassword_PasswordOfAppA_FailsInAppB_WhenUserNeverJoinedB()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        (await InAsync("appb", async m =>
        {
            var u = (await m.FindByNameAsync("sara"))!;
            return (await m.HasPasswordAsync(u), await m.CheckPasswordAsync(u, PassA));
        })).ShouldBe((false, false));
    }

    [Fact]
    public async Task AddPassword_ForExistingUserWithoutRow_IsRefused_UntilTheyJoin()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await Should.ThrowAsync<InvalidOperationException>(() =>
            InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB)));

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserApplications.CountAsync(x => x.ApplicationId == B.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Create_UserRowAndCredentialRow_AreSavedTogether_PasswordIsInCredentialRow()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var row = await db.UserApplications.SingleAsync(x => x.UserId == user.Id);
        row.ApplicationId.ShouldBe(A.Id);
        row.PasswordHash.ShouldNotBeNull();
        row.SecurityStamp.ShouldNotBeNull();
        row.LockoutEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_FailedValidation_LeavesNoRows()
    {
        await SetUpAsync();
        var result = await InAsync("appa", m => m.CreateAsync(new AppUser { UserName = "bob", Email = "bob@example.com" }, "short"));
        result.Succeeded.ShouldBeFalse();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.Users.CountAsync(u => u.UserName == "bob")).ShouldBe(0);
        (await db.UserApplications.CountAsync(x => x.ApplicationId == A.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Totp_EnabledInA_IsNotRequiredInB_AndKeyIsEncrypted()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);

        string key = "";
        await InAsync("appa", async m =>
        {
            var u = (await m.FindByNameAsync("sara"))!;
            await m.ResetAuthenticatorKeyAsync(u);
            key = (await m.GetAuthenticatorKeyAsync(u))!;
            return (await m.SetTwoFactorEnabledAsync(u, true)).Succeeded;
        });
        key.ShouldNotBeNullOrEmpty();

        (await InAsync("appa", async m => await m.GetTwoFactorEnabledAsync((await m.FindByNameAsync("sara"))!))).ShouldBeTrue();
        (await InAsync("appb", async m => await m.GetTwoFactorEnabledAsync((await m.FindByNameAsync("sara"))!))).ShouldBeFalse();
        (await InAsync("appb", async m => await m.GetAuthenticatorKeyAsync((await m.FindByNameAsync("sara"))!))).ShouldBeNull();

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var stored = (await db.UserApplications.SingleAsync(x => x.UserId == user.Id && x.ApplicationId == A.Id)).AuthenticatorKey;
        stored.ShouldNotBeNull();
        stored.ShouldNotContain(key);
    }

    [Fact]
    public async Task Lockout_InA_DoesNotLockB_AndUsesApplicationSettings()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));
        await UpdateSettingsAsync(A, s => s.Lockout.MaxFailedAttempts = 2);

        await InAsync("appa", async m =>
        {
            var u = (await m.FindByNameAsync("sara"))!;
            await m.AccessFailedAsync(u);
            (await m.IsLockedOutAsync(u)).ShouldBeFalse();
            await m.AccessFailedAsync(u);
            return true;
        });

        (await InAsync("appa", async m => await m.IsLockedOutAsync((await m.FindByNameAsync("sara"))!))).ShouldBeTrue();
        (await InAsync("appb", async m => await m.IsLockedOutAsync((await m.FindByNameAsync("sara"))!))).ShouldBeFalse();
        (await InAsync("appb", async m => await m.GetAccessFailedCountAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(0);
    }

    [Fact]
    public async Task Lockout_DisabledInSettings_NeverLocks()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Lockout.LockoutEnabled = false);
        var locked = await InAsync("appa", async m =>
        {
            var u = (await m.FindByNameAsync("sara"))!;
            for (var i = 0; i < 10; i++) await m.AccessFailedAsync(u);
            return await m.IsLockedOutAsync(u);
        });
        locked.ShouldBeFalse();
    }

    [Fact]
    public async Task RecoveryCodes_AreHashed_PerApplication_AndSingleUse()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);

        var codes = (await InAsync("appa", async m => await m.GenerateNewTwoFactorRecoveryCodesAsync((await m.FindByNameAsync("sara"))!, 3)))!.ToList();
        codes.Count.ShouldBe(3);
        (await InAsync("appa", async m => await m.CountRecoveryCodesAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(3);
        (await InAsync("appb", async m => await m.CountRecoveryCodesAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(0);
        (await InAsync("appb", async m => (await m.RedeemTwoFactorRecoveryCodeAsync((await m.FindByNameAsync("sara"))!, codes[0])).Succeeded)).ShouldBeFalse();

        (await InAsync("appa", async m => (await m.RedeemTwoFactorRecoveryCodeAsync((await m.FindByNameAsync("sara"))!, codes[0])).Succeeded)).ShouldBeTrue();
        (await InAsync("appa", async m => (await m.RedeemTwoFactorRecoveryCodeAsync((await m.FindByNameAsync("sara"))!, codes[0])).Succeeded)).ShouldBeFalse();
        (await InAsync("appa", async m => await m.CountRecoveryCodesAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(2);

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var hashes = await db.RecoveryCodes.Select(c => c.CodeHash).ToListAsync();
        hashes.Count.ShouldBe(3);
        hashes.ShouldAllBe(h => h.StartsWith("v1."));
        codes.ShouldAllBe(c => !hashes.Any(h => h.Contains(c)));
    }
}

public class RolesAndRulesTests : IdentityTestBase
{
    [Fact]
    public async Task Roles_SameNameInTwoApplications_AssignmentsDoNotMix()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));

        foreach (var (client, app) in new[] { ("appa", A), ("appb", B) })
        {
            await using var scope = ScopeFor(client);
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
            (await roles.CreateAsync(new AppRole("writer") { ApplicationId = app.Id })).Succeeded.ShouldBeTrue();
        }

        // Duplicate inside the same application is refused.
        await using (var scope = ScopeFor("appa"))
        {
            var again = await scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>().CreateAsync(new AppRole("Writer") { ApplicationId = A.Id });
            again.Errors.ShouldContain(e => e.Code == "DuplicateRoleName");
        }

        (await InAsync("appa", async m => (await m.AddToRoleAsync((await m.FindByNameAsync("sara"))!, "writer")).Succeeded)).ShouldBeTrue();

        (await InAsync("appa", async m => (await m.GetRolesAsync((await m.FindByNameAsync("sara"))!)).ToList())).ShouldBe(["writer"]);
        (await InAsync("appb", async m => (await m.GetRolesAsync((await m.FindByNameAsync("sara"))!)).ToList())).ShouldBeEmpty();
        (await InAsync("appb", async m => await m.IsInRoleAsync((await m.FindByNameAsync("sara"))!, "writer"))).ShouldBeFalse();
        (await InAsync("appa", async m => await m.IsInRoleAsync((await m.FindByNameAsync("sara"))!, "writer"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Roles_ApplicationRoleWinsOverGlobalRoleWithSameName()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            db.Roles.Add(new AppRole("Support") { NormalizedName = "SUPPORT", ApplicationId = null });
            await db.SaveChangesAsync();
        }
        await using (var scope = ScopeFor("appa"))
            (await scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>().CreateAsync(new AppRole("Support") { ApplicationId = A.Id })).Succeeded.ShouldBeTrue();

        await InAsync("appa", async m => await m.AddToRoleAsync((await m.FindByNameAsync("sara"))!, "Support"));

        await using var check = NewScope();
        var db2 = check.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var __ = check.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var assignedRole = await (from ur in db2.UserRoles join r in db2.Roles on ur.RoleId equals r.Id where r.Name == "Support" select r.ApplicationId).SingleAsync();
        assignedRole.ShouldBe(A.Id);
    }

    [Fact]
    public async Task GlobalRoles_AreNotInApplicationRoleList_ButAreInGlobalProject()
    {
        await SetUpAsync();
        var admin = await InAsync("_global", async m => (await m.FindByNameAsync("globaladmin"))!);
        (await InAsync("_global", async m => (await m.GetRolesAsync((await m.FindByNameAsync("globaladmin"))!)).ToList())).ShouldBe([Auth2Roles.GlobalAdmin]);

        await JoinAsync(admin, A);
        (await InAsync("appa", async m => (await m.GetRolesAsync((await m.FindByNameAsync("globaladmin"))!)).ToList())).ShouldBeEmpty();
    }

    [Fact]
    public async Task PasswordRules_ComeFromTheApplication_AndErrorsAreLocalized()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => { s.Password.MinLength = 14; s.Password.RequireNonAlphanumeric = true; });

        var strict = await InAsync("appa", m => m.CreateAsync(new AppUser { UserName = "bob", Email = "bob@example.com" }, "Abcdefgh1234"));
        strict.Errors.Select(e => e.Code).ShouldBe([Auth2ErrorCodes.PasswordTooShort, Auth2ErrorCodes.PasswordRequiresNonAlphanumeric], ignoreOrder: true);
        strict.Errors.First(e => e.Code == Auth2ErrorCodes.PasswordTooShort).Description.ShouldContain("14");

        (await InAsync("appb", m => m.CreateAsync(new AppUser { UserName = "bob", Email = "bob@example.com" }, "Abcdefgh1234"))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task UserRules_NameEmailDomainAndPhone_ComeFromTheApplication()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s =>
        {
            s.UserName.AllowedCharacters = "^[a-z]+$";
            s.Email.BlockedDomains.Add("spam.test");
        });

        var badName = await InAsync("appa", m => m.CreateAsync(new AppUser { UserName = "Bob_1", Email = "b@example.com" }, PassA));
        badName.Errors.ShouldContain(e => e.Code == Auth2ErrorCodes.InvalidUserName);

        var badDomain = await InAsync("appa", m => m.CreateAsync(new AppUser { UserName = "bob", Email = "b@SPAM.test" }, PassA));
        badDomain.Errors.ShouldContain(e => e.Code == Auth2ErrorCodes.EmailDomainNotAllowed);

        var badPhone = await InAsync("appa", m => m.CreateAsync(new AppUser { UserName = "bob", Email = "b@example.com", PhoneNumber = "12345" }, PassA));
        badPhone.Errors.ShouldContain(e => e.Code == Auth2ErrorCodes.InvalidPhoneNumber);

        (await InAsync("appa", m => m.CreateAsync(new AppUser { UserName = "bob", Email = "b@example.com", PhoneNumber = "+31612345678" }, PassA))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Email_IsUniqueAcrossApplications()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        var dup = await InAsync("appb", m => m.CreateAsync(new AppUser { UserName = "sara2", Email = "SARA@example.com" }, PassB));
        dup.Errors.ShouldContain(e => e.Code == "DuplicateEmail");
    }
}

public class SharedCredentialTests : IdentityTestBase
{
    protected override void Customize(IServiceCollection services) =>
        services.Configure<Auth2ServerOptions>(o => o.CredentialScope = SystemX.Auth2.Contracts.Enums.CredentialScope.Shared);

    [Fact]
    public async Task SharedScope_PasswordLivesInUserRow_AndWorksInEveryApplication()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");

        (await InAsync("appb", async m => await m.CheckPasswordAsync((await m.FindByNameAsync("sara"))!, PassA))).ShouldBeTrue();

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).PasswordHash.ShouldNotBeNull();
        (await db.UserApplications.CountAsync(x => x.UserId == user.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task SharedScope_RecoveryCodesAndKey_AreHashedOrEncrypted_InUserTokens()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        string key = "";
        var codes = (await InAsync("appa", async m =>
        {
            var u = (await m.FindByNameAsync("sara"))!;
            await m.ResetAuthenticatorKeyAsync(u);
            key = (await m.GetAuthenticatorKeyAsync(u))!;
            return await m.GenerateNewTwoFactorRecoveryCodesAsync(u, 2);
        }))!.ToList();

        (await InAsync("appb", async m => await m.GetAuthenticatorKeyAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(key);
        (await InAsync("appb", async m => (await m.RedeemTwoFactorRecoveryCodeAsync((await m.FindByNameAsync("sara"))!, codes[0])).Succeeded)).ShouldBeTrue();
        (await InAsync("appa", async m => (await m.RedeemTwoFactorRecoveryCodeAsync((await m.FindByNameAsync("sara"))!, codes[0])).Succeeded)).ShouldBeFalse();
        (await InAsync("appa", async m => await m.CountRecoveryCodesAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(1);

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var tokens = await db.UserTokens.Where(t => t.UserId == user.Id).ToListAsync();
        tokens.ShouldAllBe(t => t.Value != null && !t.Value.Contains(key) && !codes.Any(c => t.Value.Contains(c)));
    }
}

public class SingleModeIdentityTests : IdentityTestBase
{
    protected override bool SingleMode => true;

    [Fact]
    public async Task SingleMode_CredentialsLiveInUserRow_AndNoUserApplicationsRowIsWritten()
    {
        await MigrateAndSeedAsync();
        await using (var scope = NewScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.CreateAsync(new AppUser { UserName = "sara", Email = "sara@example.com" }, PassA)).Succeeded.ShouldBeTrue();
            var u = (await users.FindByNameAsync("sara"))!;
            for (var i = 0; i < 5; i++) await users.AccessFailedAsync(u);
            (await users.IsLockedOutAsync(u)).ShouldBeTrue();
        }
        await using var check = NewScope();
        var db = check.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = check.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.UserApplications.CountAsync()).ShouldBe(0);
        (await db.Users.SingleAsync(u => u.UserName == "sara")).PasswordHash.ShouldNotBeNull();
    }
}
