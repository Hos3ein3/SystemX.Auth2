using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Old passwords are remembered (hashed), may be refused as a new password, and a password can expire.</summary>
public abstract class PasswordHistoryBase : IdentityTestBase
{
    protected const string P2 = "Second-Pass-22!";
    protected const string P3 = "Third-Pass-333!";
    protected const string P4 = "Fourth-Pass-44!";
    protected const string P5 = "Fifth-Pass-555!";

    protected async Task<OperationResult> ChangeAsync(string clientId, string current, string next)
    {
        await using var scope = ScopeFor(clientId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByNameAsync("sara"))!;
        return await scope.ServiceProvider.GetRequiredService<IPasswordService>().ChangeAsync(user, current, next, null);
    }

    protected async Task<IdentityResult> ResetAsync(string clientId, string next)
    {
        await using var scope = ScopeFor(clientId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByNameAsync("sara"))!;
        return await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), next);
    }

    protected async Task<LoginResult> LoginAsync(string clientId, string password)
    {
        await using var scope = ScopeFor(clientId);
        return await scope.ServiceProvider.GetRequiredService<ILoginService>().PasswordLoginAsync(new PasswordLoginRequest("sara", password, "203.0.113.5", "test-agent"));
    }

    /// <summary>The remembered old passwords of the person, newest first.</summary>
    protected async Task<List<PasswordHistoryEntry>> HistoryAsync(Guid userId)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.PasswordHistory.AsNoTracking().Where(h => h.UserId == userId).OrderByDescending(h => h.SavedAtUtc).ToListAsync();
    }

    protected static bool Reused(OperationResult r) => r.Errors.Any(e => e.Code == Auth2ErrorCodes.PasswordReused);
}

public class PasswordHistoryTests : PasswordHistoryBase
{
    [Fact]
    public async Task ByDefault_NothingIsRemembered_AndThePasswordMayBeChangedToItself()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");

        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();
        (await ChangeAsync("appa", P2, PassA)).Succeeded.ShouldBeTrue();   // an old password again: fine while the history is off
        (await ChangeAsync("appa", PassA, PassA)).Succeeded.ShouldBeTrue();
        (await HistoryAsync(user.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheOldPassword_IsSavedAsAHash_NeverAsText()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);

        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();

        var history = await HistoryAsync(user.Id);
        history.Count.ShouldBe(1);
        history[0].PasswordHash.ShouldNotContain(PassA);
        history[0].PasswordHash.ShouldNotBe(PassA);
        history[0].CredentialProjectId.ShouldBe(A.Id);   // per-project credentials: the history belongs to the project
        history[0].SavedAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-2));

        // It is a real password hash: the old password still matches it, another text does not.
        var hasher = new PasswordHasher<AppUser>();
        hasher.VerifyHashedPassword(user, history[0].PasswordHash, PassA).ShouldNotBe(PasswordVerificationResult.Failed);
        hasher.VerifyHashedPassword(user, history[0].PasswordHash, P2).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task WithoutReuse_TheCurrentAndEveryRememberedPassword_AreRefused()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => { s.Password.SaveOldPasswords = true; s.Password.AllowOldPasswordReuse = false; });
        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();
        (await ChangeAsync("appa", P2, P3)).Succeeded.ShouldBeTrue();   // current: P3, before it: P2, older: PassA

        Reused(await ChangeAsync("appa", P3, P3)).ShouldBeTrue();    // the current one
        Reused(await ChangeAsync("appa", P3, P2)).ShouldBeTrue();    // exactly one before
        Reused(await ChangeAsync("appa", P3, PassA)).ShouldBeTrue(); // older
        (await ChangeAsync("appa", P3, P4)).Succeeded.ShouldBeTrue();   // a new one is fine
        (await LoginAsync("appa", P4)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task WithReuse_OnlyTheCurrentAndTheOneBefore_AreRefused()
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => { s.Password.SaveOldPasswords = true; s.Password.AllowOldPasswordReuse = true; });
        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();
        (await ChangeAsync("appa", P2, P3)).Succeeded.ShouldBeTrue();   // current: P3, before it: P2, older: PassA

        Reused(await ChangeAsync("appa", P3, P3)).ShouldBeTrue();    // the current one
        Reused(await ChangeAsync("appa", P3, P2)).ShouldBeTrue();    // exactly one before
        (await ChangeAsync("appa", P3, PassA)).Succeeded.ShouldBeTrue();   // older: allowed
        // now: current PassA, before it P3, older P2
        Reused(await ChangeAsync("appa", PassA, P3)).ShouldBeTrue();
        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task OnlyTheLastNOldPasswords_AreKept()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => { s.Password.SaveOldPasswords = true; s.Password.OldPasswordsToKeep = 2; });
        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();   // saved: PassA
        (await ChangeAsync("appa", P2, P3)).Succeeded.ShouldBeTrue();      // saved: P2
        (await ChangeAsync("appa", P3, P4)).Succeeded.ShouldBeTrue();      // saved: P3, PassA is forgotten
        (await ChangeAsync("appa", P4, P5)).Succeeded.ShouldBeTrue();      // saved: P4, P2 is forgotten (now P4, P3)

        (await HistoryAsync(user.Id)).Count.ShouldBe(2);
        Reused(await ChangeAsync("appa", P5, P4)).ShouldBeTrue();
        Reused(await ChangeAsync("appa", P5, P3)).ShouldBeTrue();
        (await ChangeAsync("appa", P5, PassA)).Succeeded.ShouldBeTrue();   // forgotten long ago
    }

    [Fact]
    public async Task APasswordReset_IsRemembered_AndFollowsTheSameRules()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);

        (await ResetAsync("appa", PassA)).Succeeded.ShouldBeFalse();   // the current password again
        var fresh = await ResetAsync("appa", P2);
        fresh.Succeeded.ShouldBeTrue(string.Join(",", fresh.Errors.Select(e => e.Code)));
        (await HistoryAsync(user.Id)).Count.ShouldBe(1);
        (await ResetAsync("appa", PassA)).Errors.Any(e => e.Code == Auth2ErrorCodes.PasswordReused).ShouldBeTrue();   // the one before
        (await LoginAsync("appa", P2)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task TurningTheHistoryOn_StartsEmpty_AndTurningItOffStopsTheChecks()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();   // history off: PassA is not saved
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);
        (await ChangeAsync("appa", P2, PassA)).Succeeded.ShouldBeTrue();   // nobody remembered PassA

        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = false);
        (await ChangeAsync("appa", PassA, PassA)).Succeeded.ShouldBeTrue();   // checks are off again
        (await HistoryAsync(user.Id)).Count.ShouldBe(1);   // the saved row stays (it is not deleted)
    }

    [Fact]
    public async Task ARehashWhileLoggingIn_IsNotAPasswordChange()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);

        // The same password, stored with a weaker hash (fewer iterations): the next login makes Identity store it again with the normal strength.
        var weak = new PasswordHasher<AppUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 1000 })).HashPassword(user, PassA);
        var longAgo = DateTimeOffset.UtcNow.AddDays(-100);
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var row = await db.UserProjects.SingleAsync(x => x.UserId == user.Id && x.ProjectId == A.Id);
            (row.PasswordHash, row.PasswordChangedAtUtc) = (weak, longAgo);
            await db.SaveChangesAsync();
        }

        (await LoginAsync("appa", PassA)).Succeeded.ShouldBeTrue();

        await using var check = NewScope();
        var db2 = check.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var __ = check.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var after = await db2.UserProjects.AsNoTracking().SingleAsync(x => x.UserId == user.Id && x.ProjectId == A.Id);
        after.PasswordHash.ShouldNotBe(weak);                          // it was rehashed ...
        after.PasswordChangedAtUtc.ShouldBe(longAgo);                  // ... and that did not renew the password date
        (await db2.PasswordHistory.CountAsync(h => h.UserId == user.Id)).ShouldBe(0);   // ... and did not become "an old password"
    }

    [Fact]
    public async Task APasswordChangeInOneProject_DoesNotShowInTheHistoryOfAnother()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);
        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();

        (await HistoryAsync(user.Id)).ShouldAllBe(h => h.CredentialProjectId == A.Id);
        await using var scope = ScopeFor("appb");   // another project: its query filter shows none of it
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.PasswordHistory.CountAsync()).ShouldBe(0);
    }
}

public class PasswordExpiryTests : PasswordHistoryBase
{
    private async Task SetPasswordDateAsync(Guid userId, DateTimeOffset? changedAt, DateTimeOffset? joinedAt = null)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var row = await db.UserProjects.SingleAsync(x => x.UserId == userId && x.ProjectId == A.Id);
        row.PasswordChangedAtUtc = changedAt;
        if (joinedAt is not null) row.JoinedAtUtc = joinedAt.Value;
        await db.SaveChangesAsync();
    }

    private async Task<DateTimeOffset?> PasswordDateAsync(Guid userId)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return (await db.UserProjects.AsNoTracking().SingleAsync(x => x.UserId == userId && x.ProjectId == A.Id)).PasswordChangedAtUtc;
    }

    [Fact]
    public async Task APasswordThatIsTooOld_BlocksThePasswordLogin_UntilItIsChanged()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.PasswordExpiryDays = 30);
        (await LoginAsync("appa", PassA)).Succeeded.ShouldBeTrue();   // just set: fine

        await SetPasswordDateAsync(user.Id, DateTimeOffset.UtcNow.AddDays(-31));
        var expired = await LoginAsync("appa", PassA);
        expired.Succeeded.ShouldBeFalse();
        expired.ErrorCode.ShouldBe(Auth2ErrorCodes.PasswordExpired);

        // A wrong password does not reveal anything about the age.
        (await LoginAsync("appa", "Wrong-Pass-999!")).ErrorCode.ShouldBe(Auth2ErrorCodes.InvalidCredentials);

        (await ResetAsync("appa", P2)).Succeeded.ShouldBeTrue();   // the way out: a reset (forgot-password flow)
        (await LoginAsync("appa", P2)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ThePasswordDate_IsSet_WhenThePasswordChanges()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        (await PasswordDateAsync(user.Id)).ShouldNotBeNull();   // a new person's first password gets a date too
        await SetPasswordDateAsync(user.Id, DateTimeOffset.UtcNow.AddDays(-400));

        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();
        (await PasswordDateAsync(user.Id))!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-2));
    }

    [Fact]
    public async Task WithExpiryOff_APasswordNeverExpires()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await SetPasswordDateAsync(user.Id, DateTimeOffset.UtcNow.AddDays(-5000));
        (await LoginAsync("appa", PassA)).Succeeded.ShouldBeTrue();   // PasswordExpiryDays = 0 (default)
    }

    [Fact]
    public async Task APasswordWithoutADate_IsAsOldAsTheMembership()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.PasswordExpiryDays = 30);
        await SetPasswordDateAsync(user.Id, null, joinedAt: DateTimeOffset.UtcNow.AddDays(-10));
        (await LoginAsync("appa", PassA)).Succeeded.ShouldBeTrue();

        await SetPasswordDateAsync(user.Id, null, joinedAt: DateTimeOffset.UtcNow.AddDays(-40));
        (await LoginAsync("appa", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.PasswordExpired);
    }

    [Fact]
    public async Task TheHostedLoginPage_ExplainsAnExpiredPassword()
    {
        await SetUpAsync();
        // (the page text exists in both languages; the service answers are covered above)
        await using var scope = NewScope();
        var text = scope.ServiceProvider.GetRequiredService<IAuth2Localizer>();
        text.Error(Auth2ErrorCodes.PasswordExpired).ShouldNotBe(Auth2ErrorCodes.PasswordExpired);
        text.Error(Auth2ErrorCodes.PasswordReused).ShouldNotBe(Auth2ErrorCodes.PasswordReused);
    }
}

public class SharedPasswordHistoryTests : PasswordHistoryBase
{
    protected override void Customize(IServiceCollection services) =>
        services.Configure<Auth2ServerOptions>(o => o.CredentialScope = SystemX.Auth2.Contracts.Enums.CredentialScope.Shared);

    [Fact]
    public async Task WithSharedCredentials_TheHistoryBelongsToThePerson_AndWorksInEveryProject()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);
        await UpdateSettingsAsync(B, s => s.Password.SaveOldPasswords = true);

        (await ChangeAsync("appa", PassA, P2)).Succeeded.ShouldBeTrue();

        var history = await HistoryAsync(user.Id);
        history.Count.ShouldBe(1);
        history[0].CredentialProjectId.ShouldBeNull();   // shared credentials: the history is the person's, not a project's
        Reused(await ChangeAsync("appb", P2, PassA)).ShouldBeTrue();   // refused in the other project too
        (await ChangeAsync("appb", P2, P3)).Succeeded.ShouldBeTrue();
    }
}
