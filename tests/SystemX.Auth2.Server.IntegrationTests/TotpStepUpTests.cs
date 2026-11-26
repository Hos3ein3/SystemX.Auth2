using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// <c>POST /api/auth2/account/2fa/verify</c>: one code of the authenticator app is checked for the signed-in person (what <c>WithTotp()</c> asks the Server).
/// A code works once, wrong codes lock the account like wrong passwords, and a person without two-factor or a program gets a refusal.
/// </summary>
public class TotpStepUpTests : Phase6Base
{
    private async Task<(string Token, string Key)> SignedInWithTwoFactorAsync()
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var token = (await UserTokensAsync("sara", PassA)).Str("access_token");   // the token first: a person with two-factor on needs two steps to get one

        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByNameAsync("sara"))!;
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
        return (token, key);
    }

    private Task<(HttpStatusCode Status, System.Text.Json.JsonElement Body)> VerifyAsync(string token, string code, bool optional = false) =>
        CallAsync(new Browser(Factory), Auth2Routes.TwoFactorVerify, new TwoFactorVerifyRequest(code, optional), token);

    [Fact]
    public async Task ARightCode_IsAccepted_AndAuditedButWorksOnlyOnce()
    {
        var (token, key) = await SignedInWithTwoFactorAsync();
        var code = Totp.Now(key);
        (await VerifyAsync(token, code)).Status.ShouldBe(HttpStatusCode.NoContent);   // nothing to return: it worked
        (await AuditAsync("2fa.step_up")).Count.ShouldBe(1);

        var again = await VerifyAsync(token, code);   // the same code a second time: a copied code is of no use
        again.Status.ShouldBe(HttpStatusCode.BadRequest);
        again.Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await AuditAsync("2fa.step_up_replayed")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AWrongCode_IsRefused_AndCountsTowardsTheLockout()
    {
        var (token, _) = await SignedInWithTwoFactorAsync();
        for (var i = 0; i < 4; i++)
            (await VerifyAsync(token, "000000")).Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await AuditAsync("2fa.step_up_failed")).Count.ShouldBe(4);

        // the default lockout is 5 failed attempts: the fifth wrong guess locks the account, and then even a right code is refused
        (await VerifyAsync(token, "000000")).Status.ShouldNotBe(HttpStatusCode.OK);
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.IsLockedOutAsync((await users.FindByNameAsync("sara"))!)).ShouldBeTrue();
    }

    [Fact]
    public async Task APersonWithoutTwoFactor_IsToldToSetItUp_WhenTheActionRequiresIt()
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var token = (await UserTokensAsync("sara", PassA)).Str("access_token");
        var r = await VerifyAsync(token, "123456");
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
        r.Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.TotpSetupRequired);
        r.Body.GetProperty("message").GetString()!.ShouldContain("two-factor");   // the text of Errors.resx
        (await VerifyAsync(token, "")).Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.TotpSetupRequired);   // with no code too: setting it up comes first
    }

    [Fact]
    public async Task APersonWithoutTwoFactor_Passes_WhenTheActionIsOptional()
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var token = (await UserTokensAsync("sara", PassA)).Str("access_token");
        (await VerifyAsync(token, "", optional: true)).Status.ShouldBe(HttpStatusCode.NoContent);        // nothing to check
        (await VerifyAsync(token, "999999", optional: true)).Status.ShouldBe(HttpStatusCode.NoContent);   // a code is not even looked at
    }

    [Fact]
    public async Task APersonWithTwoFactor_IsAskedForACode_EvenWhenTheActionIsOptional()
    {
        var (token, key) = await SignedInWithTwoFactorAsync();
        (await VerifyAsync(token, "", optional: true)).Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.TotpRequired);
        (await VerifyAsync(token, "000000", optional: true)).Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await VerifyAsync(token, Totp.Now(key), optional: true)).Status.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NoCode_IsTotpRequired_AndIsNotCountedAsAWrongGuess()
    {
        var (token, key) = await SignedInWithTwoFactorAsync();
        for (var i = 0; i < 6; i++)   // more than the 5 wrong attempts that lock an account (and under the rate limit of 10 a minute)
            (await VerifyAsync(token, "  ")).Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.TotpRequired);
        (await AuditAsync("2fa.step_up_failed")).ShouldBeEmpty();
        (await VerifyAsync(token, Totp.Now(key))).Status.ShouldBe(HttpStatusCode.NoContent);   // the account is not locked
    }

    [Fact]
    public async Task ABadRequest_NoToken_OrAProgramToken_IsRefused()
    {
        var (token, key) = await SignedInWithTwoFactorAsync();
        (await VerifyAsync("", Totp.Now(key))).Status.ShouldBe(HttpStatusCode.Unauthorized);                                  // nobody
        var machine = await ClientTokenAsync("appa", "auth2.client.handshake");
        (await VerifyAsync(machine, Totp.Now(key))).Status.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);   // a program has no authenticator app
        (await VerifyAsync(token, "")).Body.GetProperty("code").GetString().ShouldBe(Auth2ErrorCodes.TotpRequired);          // an empty code: asks for one
    }
}
