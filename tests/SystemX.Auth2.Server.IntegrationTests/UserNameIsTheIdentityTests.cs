using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// The user name is the identity of a person: always unique, and it can be an email address, a phone number or plain text. Sign-in tries the exact user name first;
/// an email or a phone number of a person can never be the user name of somebody else.
/// </summary>
public class UserNameIsTheIdentityTests : LoginServiceTests
{
    private async Task<IdentityResult> CreateAsync(string userName, string? email = null, string? phone = null, string password = PassA)
    {
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = userName, Email = email, PhoneNumber = phone };
        return await users.CreateAsync(user, password);   // a user created inside an application is a member of it
    }

    private static string[] Codes(IdentityResult r) => [.. r.Errors.Select(e => e.Code)];

    [Fact]
    public async Task AUserNameThatLooksLikeAnEmail_SignsIn_WhenUserNamesAreAllowed()
    {
        await SetUpAsync();
        (await CreateAsync("jo@example.com")).Succeeded.ShouldBeTrue();   // the user name is an address, the Email column is empty
        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.UserNamePassword]);

        (await LoginAsync("appa", "jo@example.com", PassA)).Succeeded.ShouldBeTrue();   // before: looked up only as an email, so nobody was found
        (await LoginAsync("appa", "JO@EXAMPLE.COM", PassA)).Succeeded.ShouldBeTrue();   // the user name does not depend on the letter case
    }

    [Fact]
    public async Task AUserNameThatLooksLikeAPhoneNumber_SignsIn_WhenUserNamesAreAllowed()
    {
        await SetUpAsync();
        (await CreateAsync("+14155552671")).Succeeded.ShouldBeTrue();
        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.UserNamePassword]);
        (await LoginAsync("appa", "+14155552671", PassA)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task TheMethodThatFindsThePerson_MustBeAllowed()
    {
        await SetUpAsync();
        (await CreateAsync("sara", email: "sara@example.com")).Succeeded.ShouldBeTrue();

        // only user names: the email of a person (not their user name) is no way in, and the answer is the same as before (nobody has that text as a user name)
        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.UserNamePassword]);
        (await LoginAsync("appa", "sara", PassA)).Succeeded.ShouldBeTrue();
        (await LoginAsync("appa", "sara@example.com", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);

        // only emails: a user name is refused as before
        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.EmailPassword]);
        (await LoginAsync("appa", "sara", PassA)).ErrorCode.ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);
        (await LoginAsync("appa", "sara@example.com", PassA)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ThePersonWhoseUserNameItIs_IsFoundFirst_AndTheMatchSaysHow()
    {
        await SetUpAsync();
        (await CreateAsync("sara", email: "sara@example.com", phone: "+14155552671")).Succeeded.ShouldBeTrue();
        await using var scope = ScopeFor("appa");
        var lookup = scope.ServiceProvider.GetRequiredService<UserLookup>();
        var settings = await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetSettingsAsync();

        (await lookup.FindMatchAsync("sara", settings, default)).Matched.ShouldBe(IdentifierKind.UserName);
        (await lookup.FindMatchAsync("sara@example.com", settings, default)).Matched.ShouldBe(IdentifierKind.Email);
        (await lookup.FindMatchAsync("+14155552671", settings, default)).Matched.ShouldBe(IdentifierKind.Phone);
        (await lookup.FindMatchAsync("nobody@example.com", settings, default)).User.ShouldBeNull();
    }

    // ---------------------------------------------------------------- one text, one person

    [Fact]
    public async Task AnEmail_CannotBeTheUserNameOfAnotherPerson()
    {
        await SetUpAsync();
        (await CreateAsync("bob@example.com")).Succeeded.ShouldBeTrue();
        var result = await CreateAsync("robert", email: "bob@example.com");   // would match two people at sign-in
        result.Succeeded.ShouldBeFalse();
        Codes(result).ShouldContain("DuplicateEmail");
    }

    [Fact]
    public async Task AUserName_CannotBeTheEmailOfAnotherPerson()
    {
        await SetUpAsync();
        (await CreateAsync("robert", email: "bob@example.com")).Succeeded.ShouldBeTrue();
        var result = await CreateAsync("bob@example.com");
        result.Succeeded.ShouldBeFalse();
        Codes(result).ShouldContain("DuplicateUserName");
    }

    [Fact]
    public async Task APhoneNumber_AndAUserName_CannotCollide_InEitherDirection()
    {
        await SetUpAsync();
        (await CreateAsync("+14155552671")).Succeeded.ShouldBeTrue();
        Codes(await CreateAsync("someone", phone: "+14155552671")).ShouldContain(Auth2ErrorCodes.PhoneNumberTaken);

        (await CreateAsync("other", phone: "+14155550000")).Succeeded.ShouldBeTrue();
        Codes(await CreateAsync("+14155550000")).ShouldContain("DuplicateUserName");
    }

    [Fact]
    public async Task APerson_MayUseTheirOwnEmailOrPhoneNumberAsTheUserName()
    {
        await SetUpAsync();
        (await CreateAsync("sara@example.com", email: "sara@example.com")).Succeeded.ShouldBeTrue();      // what a registration by email does
        (await CreateAsync("+14155552671", phone: "+14155552671")).Succeeded.ShouldBeTrue();              // and by phone
    }

    [Fact]
    public async Task ChangingTheUserName_OrTheEmail_ToTheTextOfAnotherPerson_IsRefused()
    {
        await SetUpAsync();
        (await CreateAsync("ann", email: "ann@example.com")).Succeeded.ShouldBeTrue();
        (await CreateAsync("ben", email: "ben@example.com")).Succeeded.ShouldBeTrue();

        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var ben = (await users.FindByNameAsync("ben"))!;
        ben.UserName = "ann@example.com";   // the email of ann
        Codes(await users.UpdateAsync(ben)).ShouldContain("DuplicateUserName");

        var ann = (await users.FindByNameAsync("ann"))!;
        ann.Email = "ann2@example.com";
        ann.UserName = "ann2@example.com";   // her own new address: fine
        (await users.UpdateAsync(ann)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task TheDatabase_KeepsTheUserNameUnique_WithoutLetterCase_AndTheEmailToo()
    {
        await SetUpAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Persistence.Auth2DbContext>();
        var indexes = await db.Database.SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'users' AND indexname IN ('user_name_index', 'email_index')").ToListAsync();
        indexes.Count.ShouldBe(2);
        indexes.ShouldAllBe(d => d.StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal));
        indexes.ShouldContain(d => d.Contains("normalized_user_name", StringComparison.Ordinal));   // the user name (normalized: the letter case does not matter)
        indexes.ShouldContain(d => d.Contains("normalized_email", StringComparison.Ordinal));

        (await CreateAsync("Sara")).Succeeded.ShouldBeTrue();
        Codes(await CreateAsync("SARA")).ShouldContain("DuplicateUserName");   // the validator says it first, the index is the last word
    }

    [Fact]
    public async Task TheUserManagerOfTheServer_IsTheAuth2UserManager_SoItsProjectRulesApplyEverywhere()
    {
        await SetUpAsync();
        await using var scope = ScopeFor("appa");
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        manager.ShouldBeOfType<SystemX.Auth2.Server.Persistence.Identity.Auth2UserManager>();                                                   // asking for the Identity type gives ours
        scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Persistence.Identity.Auth2UserManager>().ShouldBeSameAs(manager);          // the same object in one scope
    }
}
