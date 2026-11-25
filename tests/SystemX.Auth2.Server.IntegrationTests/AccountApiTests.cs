using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class RegisterApiTests : Phase6Base
{
    private const string Email = "sara@example.com";

    private Task<(HttpStatusCode Status, JsonElement Body)> RegisterAsync(string clientId, string token, string? email = Email, string password = PassA, string? phone = null, string? userName = null, string? returnUrl = null) =>
        CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest(userName, email, phone, password, "Sara", "K", returnUrl), token);

    [Fact]
    public async Task Register_NewUser_CreatesPersonAndMembership_AndSendsTheConfirmationEmail()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register");
        var (status, body) = await RegisterAsync("appa", token);
        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("userWasCreated").GetBoolean().ShouldBeTrue();
        body.GetProperty("joinedProject").GetBoolean().ShouldBeTrue();
        body.GetProperty("confirmationRequired").GetBoolean().ShouldBeFalse();

        (await CountsAsync(Email)).ShouldBe((1, 1));
        (await HasRowAsync(Email, A)).ShouldBeTrue();
        var mail = Factory.Messages.LastEmailTo(Email)!;
        mail["link"].ShouldContain("/Account/ConfirmEmail");
        mail["link"].ShouldContain("ClientId=appa");
        (await AuditAsync("register.created")).Count.ShouldBe(1);
        (await UserTokensAsync(Email, PassA)).Str("token_type").ShouldBe("Bearer");   // the password works
    }

    [Fact]
    public async Task Register_TheApplicationComesFromTheToken_NotFromTheBody()
    {
        await PrepareAsync2();
        var tokenB = await ClientTokenAsync("appb", "auth2.register");
        (await RegisterAsync("appb", tokenB)).Status.ShouldBe(HttpStatusCode.OK);
        (await HasRowAsync(Email, B)).ShouldBeTrue();
        (await HasRowAsync(Email, A)).ShouldBeFalse();
    }

    private Task PrepareAsync2() => PrepareAppsAsync();

    [Fact]
    public async Task Register_Twice_IsIdempotent_AndNeverChangesThePassword()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register");
        await RegisterAsync("appa", token);
        var (status, body) = await RegisterAsync("appa", token, password: "Another-Pass-999!");
        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("userWasCreated").GetBoolean().ShouldBeFalse();
        body.GetProperty("joinedProject").GetBoolean().ShouldBeTrue();

        (await CountsAsync(Email)).ShouldBe((1, 1));
        (await UserTokensAsync(Email, PassA)).Str("token_type").ShouldBe("Bearer");
        var wrong = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "password", ["username"] = "sara@example.com", ["password"] = "Another-Pass-999!", ["client_id"] = "appa", ["client_secret"] = ASecret });
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_ManyAtTheSameTime_CreatesOnePerson_AndNobodyGetsAServerError()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RegisterAsync("appa", token)));
        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        results.Count(r => r.Body.GetProperty("userWasCreated").GetBoolean()).ShouldBe(1);
        (await CountsAsync(Email)).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Register_InASecondApplication_NeedsProof_NoRowAndNoPasswordUntilThen()
    {
        await PrepareAppsAsync();
        var tokenA = await ClientTokenAsync("appa", "auth2.register");
        var tokenB = await ClientTokenAsync("appb", "auth2.register");
        await RegisterAsync("appa", tokenA);
        await ConfirmEmailAsync(Email);   // the first address was confirmed: this is what makes it a proof channel
        var mailsBefore = Factory.Messages.CountTo(Email);

        // someone else tries to "register" the same email in application B with their own password
        var (status, body) = await RegisterAsync("appb", tokenB, password: "Attacker-Pass-1!");
        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("userWasCreated").GetBoolean().ShouldBeFalse();
        body.GetProperty("joinedProject").GetBoolean().ShouldBeFalse();
        body.GetProperty("confirmationRequired").GetBoolean().ShouldBeTrue();

        (await CountsAsync(Email)).ShouldBe((1, 1));                 // no new row
        (await HasRowAsync(Email, B)).ShouldBeFalse();
        Factory.Messages.CountTo(Email).ShouldBe(mailsBefore + 1);    // the proof code went to the OWNER's mailbox
        var code = Factory.Messages.LastEmailTo(Email)!["code"];

        // the attacker's password does not work in B, and the proof code is needed
        var attackerLogin = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "password", ["username"] = Email, ["password"] = "Attacker-Pass-1!", ["client_id"] = "appb", ["client_secret"] = BSecret });
        attackerLogin.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var wrongCode = await CallAsync(new Browser(Factory), Auth2Routes.JoinComplete, new JoinCompleteRequest(Email, code == "000000" ? "000001" : "000000", PassB), tokenB);
        wrongCode.Status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorCode(wrongCode.Body).ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await HasRowAsync(Email, B)).ShouldBeFalse();

        var weak = await CallAsync(new Browser(Factory), Auth2Routes.JoinComplete, new JoinCompleteRequest(Email, code, "short"), tokenB);
        weak.Status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorCode(weak.Body).ShouldBe(Auth2ErrorCodes.PasswordTooShort);
        (await HasRowAsync(Email, B)).ShouldBeFalse();               // and the weak password did not use up the code

        var ok = await CallAsync(new Browser(Factory), Auth2Routes.JoinComplete, new JoinCompleteRequest(Email, code, PassB), tokenB);
        ok.Status.ShouldBe(HttpStatusCode.NoContent);
        (await CountsAsync(Email)).ShouldBe((1, 2));
        (await AuditAsync("application.joined")).Count.ShouldBe(1);

        // one person, two applications, two passwords that do not mix
        (await UserTokensAsync(Email, PassA, "appa")).Str("token_type").ShouldBe("Bearer");
        (await UserTokensAsync(Email, PassB, "appb")).Str("token_type").ShouldBe("Bearer");
        foreach (var (pass, client, secret) in new[] { (PassA, "appb", BSecret), (PassB, "appa", ASecret) })
        {
            var r = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
            { ["grant_type"] = "password", ["username"] = Email, ["password"] = pass, ["client_id"] = client, ["client_secret"] = secret });
            r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // the code works once
        (await CallAsync(new Browser(Factory), Auth2Routes.JoinComplete, new JoinCompleteRequest(Email, code, PassB), tokenB)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_ExistingPersonWithAnUnconfirmedEmail_GetsNoProofCode_SoCannotBeJoined()
    {
        await PrepareAppsAsync();
        var tokenA = await ClientTokenAsync("appa", "auth2.register");
        var tokenB = await ClientTokenAsync("appb", "auth2.register");
        await RegisterAsync("appa", tokenA);   // email NOT confirmed
        var before = Factory.Messages.CountTo(Email);

        var (status, body) = await RegisterAsync("appb", tokenB, password: "Attacker-Pass-1!");
        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("joinedProject").GetBoolean().ShouldBeFalse();
        Factory.Messages.CountTo(Email).ShouldBe(before);             // nothing was sent: an unconfirmed address proves nothing
        (await HasRowAsync(Email, B)).ShouldBeFalse();
        (await AuditAsync("join.no_proof_channel")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Join_NeedsJoinModeAutoJoin()
    {
        await PrepareAppsAsync();
        await UpdateSettingsAsync(B, s => s.Registration.JoinMode = JoinMode.Closed);
        var tokenA = await ClientTokenAsync("appa", "auth2.register");
        var tokenB = await ClientTokenAsync("appb", "auth2.register");
        await RegisterAsync("appa", tokenA);
        await ConfirmEmailAsync(Email);
        var before = Factory.Messages.CountTo(Email);
        await RegisterAsync("appb", tokenB);
        Factory.Messages.CountTo(Email).ShouldBe(before);
        (await CallAsync(new Browser(Factory), Auth2Routes.JoinComplete, new JoinCompleteRequest(Email, "123456", PassB), tokenB)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_Rules_WeakPassword_Disabled_MethodNotAllowed_BlockedDomain_NoIdentifier()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register");

        var weak = await RegisterAsync("appa", token, password: "short");
        weak.Status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorCode(weak.Body).ShouldBe(Auth2ErrorCodes.PasswordTooShort);
        weak.Body.GetProperty("details").GetProperty("errors").GetArrayLength().ShouldBeGreaterThan(0);
        (await CountsAsync(Email)).ShouldBe((0, 0));

        var noId = await RegisterAsync("appa", token, email: null);
        ErrorCode(noId.Body).ShouldBe(Auth2ErrorCodes.ValidationFailed);

        var phone = await RegisterAsync("appa", token, email: null, phone: "+31612345678", userName: "bob");   // phone is not an allowed register method
        ErrorCode(phone.Body).ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);

        await UpdateSettingsAsync(A, s => s.Email.BlockedDomains.Add("spam.test"));
        ErrorCode((await RegisterAsync("appa", token, email: "x@spam.test")).Body).ShouldBe(Auth2ErrorCodes.EmailDomainNotAllowed);

        await UpdateSettingsAsync(A, s => s.Registration.RegistrationEnabled = false);
        var closed = await RegisterAsync("appa", token);
        closed.Status.ShouldBe(HttpStatusCode.Forbidden);
        ErrorCode(closed.Body).ShouldBe(Auth2ErrorCodes.RegistrationDisabled);
    }

    [Fact]
    public async Task Register_WithPhone_SendsACode_AndConfirmingItWorks()
    {
        await PrepareAppsAsync(both: s => { s.Methods.AllowedRegisterMethods.Add(LoginMethod.PhonePassword); });
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        var (status, _) = await RegisterAsync("appa", token, email: "p@example.com", phone: "+31612345678");
        status.ShouldBe(HttpStatusCode.OK);
        var code = Factory.Messages.LastTextTo("+31612345678")!["code"];

        var wrong = await CallAsync(new Browser(Factory), Auth2Routes.PhoneConfirm, new ConfirmPhoneRequest("+31612345678", code == "000000" ? "000001" : "000000"), token);
        ErrorCode(wrong.Body).ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await UserAsync("p@example.com")).PhoneNumberConfirmed.ShouldBeFalse();

        (await CallAsync(new Browser(Factory), Auth2Routes.PhoneConfirm, new ConfirmPhoneRequest("+31612345678", code), token)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await UserAsync("p@example.com")).PhoneNumberConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_RequireConfirmedEmail_BlocksLoginUntilConfirmed()
    {
        await PrepareAppsAsync(both: s => s.Email.RequireConfirmedEmail = true);
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        var (_, body) = await RegisterAsync("appa", token);
        body.GetProperty("confirmationRequired").GetBoolean().ShouldBeTrue();

        var blocked = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "password", ["username"] = Email, ["password"] = PassA, ["client_id"] = "appa", ["client_secret"] = ASecret });
        (await blocked.JsonAsync()).Str("error_code").ShouldBe(Auth2ErrorCodes.EmailNotConfirmed);

        var mail = Factory.Messages.LastEmailTo(Email)!;
        var user = await UserAsync(Email);
        (await CallAsync(new Browser(Factory), Auth2Routes.EmailConfirm, new ConfirmEmailRequest(user.Id, "wrong-token"), token)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(new Browser(Factory), Auth2Routes.EmailConfirm, new ConfirmEmailRequest(user.Id, mail["token"]), token)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await UserTokensAsync(Email, PassA)).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task Resend_AlwaysLooksTheSame_AndSendsOnlyForAnUnconfirmedKnownEmail()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        await RegisterAsync("appa", token);
        var before = Factory.Messages.CountTo(Email);

        var known = await CallAsync(new Browser(Factory), Auth2Routes.EmailConfirmResend, new ResendEmailConfirmationRequest(Email), token);
        var unknown = await CallAsync(new Browser(Factory), Auth2Routes.EmailConfirmResend, new ResendEmailConfirmationRequest("nobody@example.com"), token);
        known.Status.ShouldBe(HttpStatusCode.Accepted);
        unknown.Status.ShouldBe(known.Status);
        Factory.Messages.CountTo(Email).ShouldBe(before + 1);
        Factory.Messages.CountTo("nobody@example.com").ShouldBe(0);

        await ConfirmEmailAsync(Email);
        await CallAsync(new Browser(Factory), Auth2Routes.EmailConfirmResend, new ResendEmailConfirmationRequest(Email), token);
        Factory.Messages.CountTo(Email).ShouldBe(before + 1);   // already confirmed: nothing new
    }

    [Fact]
    public async Task ReturnUrl_InEmails_IsKeptWhenAllowed_AndDroppedWhenNot()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register");
        await RegisterAsync("appa", token, email: "ok@example.com", returnUrl: Redirect);
        Factory.Messages.LastEmailTo("ok@example.com")!["link"].ShouldContain("ReturnUrl=" + Uri.EscapeDataString(Redirect));

        await RegisterAsync("appa", token, email: "evil@example.com", returnUrl: "https://evil.com/steal");
        var link = Factory.Messages.LastEmailTo("evil@example.com")!["link"];
        link.ShouldNotContain("evil.com");
        link.ShouldNotContain("ReturnUrl");
    }

    [Fact]
    public async Task Api_Protection_NoToken_WrongKindOfToken_MissingScope_DisabledApplication()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        var none = await CallAsync(b, Auth2Routes.Register, new RegisterRequest(null, Email, null, PassA), null);
        none.Status.ShouldBe(HttpStatusCode.Unauthorized);
        ErrorCode(none.Body).ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await CallAsync(b, Auth2Routes.Register, new RegisterRequest(null, Email, null, PassA), "not.a.token")).Status.ShouldBe(HttpStatusCode.Unauthorized);

        var wrongScope = await CallAsync(b, Auth2Routes.Register, new RegisterRequest(null, Email, null, PassA), await ClientTokenAsync("appa", "auth2.login"));
        wrongScope.Status.ShouldBe(HttpStatusCode.Forbidden);
        ErrorCode(wrongScope.Body).ShouldBe(Auth2ErrorCodes.InsufficientScope);

        await RegisterAsync("appa", await ClientTokenAsync("appa", "auth2.register"));
        var userToken = (await UserTokensAsync(Email, PassA)).Str("access_token");
        var machineOnly = await CallAsync(b, Auth2Routes.Register, new RegisterRequest(null, "x@example.com", null, PassA), userToken);
        machineOnly.Status.ShouldBe(HttpStatusCode.Forbidden);          // a user token cannot call a machine endpoint
        ErrorCode(machineOnly.Body).ShouldBe(Auth2ErrorCodes.Forbidden);
        var userOnly = await CallAsync(b, Auth2Routes.PasswordChange, new ChangePasswordRequest(PassA, "New-Pass-12345!"), await ClientTokenAsync("appa", "auth2.me"));
        userOnly.Status.ShouldBe(HttpStatusCode.Forbidden);              // and a machine token cannot call a user endpoint

        var tokenA = await ClientTokenAsync("appa", "auth2.register");
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            (await db.Projects.SingleAsync(a => a.ProjectKey == "appa")).IsEnabled = false;
            await db.SaveChangesAsync();
        }
        var disabled = await CallAsync(b, Auth2Routes.Register, new RegisterRequest(null, "y@example.com", null, PassA), tokenA);
        disabled.Status.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);   // the token of a disabled project is refused at its next validation
        if (disabled.Status == HttpStatusCode.Forbidden) ErrorCode(disabled.Body).ShouldBe(Auth2ErrorCodes.AccessDenied);
    }
}

public class PasswordApiTests : Phase6Base
{
    private const string Email = "sara@example.com";

    private async Task<string> RegisteredAsync()
    {
        await PrepareAppsAsync();
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        (await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("sara", Email, null, PassA), token)).Status.ShouldBe(HttpStatusCode.OK);
        return token;
    }

    [Fact]
    public async Task Forgot_LooksTheSameForKnownAndUnknown_AndMailsOnlyTheKnown()
    {
        var token = await RegisteredAsync();
        var before = Factory.Messages.CountTo(Email);
        var known = await CallAsync(new Browser(Factory), Auth2Routes.PasswordForgot, new ForgotPasswordRequest(Email), token);
        var unknown = await CallAsync(new Browser(Factory), Auth2Routes.PasswordForgot, new ForgotPasswordRequest("nobody@example.com"), token);
        known.Status.ShouldBe(HttpStatusCode.Accepted);
        unknown.Status.ShouldBe(known.Status);
        known.Body.ToString().ShouldBe(unknown.Body.ToString());
        Factory.Messages.CountTo(Email).ShouldBe(before + 1);
        Factory.Messages.CountTo("nobody@example.com").ShouldBe(0);
        Factory.Messages.LastEmailTo(Email)!["link"].ShouldContain("/Account/ResetPassword");
    }

    [Fact]
    public async Task Reset_WithTheToken_ChangesThePassword_EndsSessions_ClearsLockout_AndWorksOnce()
    {
        var token = await RegisteredAsync();
        var session = await UserTokensAsync("sara", PassA);
        for (var i = 0; i < 5; i++)
            await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
            { ["grant_type"] = "password", ["username"] = "sara", ["password"] = "Wrong-Password-1", ["client_id"] = "appa", ["client_secret"] = ASecret });

        await CallAsync(new Browser(Factory), Auth2Routes.PasswordForgot, new ForgotPasswordRequest(Email), token);
        var reset = Factory.Messages.LastEmailTo(Email)!["token"];

        var weak = await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest(Email, reset, "weak"), token);
        ErrorCode(weak.Body).ShouldBe(Auth2ErrorCodes.PasswordTooShort);   // the token is still good after a weak password

        (await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest(Email, reset, "New-Pass-12345!"), token)).Status.ShouldBe(HttpStatusCode.NoContent);

        (await UserTokensAsync("sara", "New-Pass-12345!")).Str("token_type").ShouldBe("Bearer");   // works, and the lockout is gone
        var old = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "password", ["username"] = "sara", ["password"] = PassA, ["client_id"] = "appa", ["client_secret"] = ASecret });
        old.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var refresh = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = session.Str("refresh_token"), ["client_id"] = "appa", ["client_secret"] = ASecret });
        refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the old session ended

        var again = await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest(Email, reset, "Third-Pass-12345!"), token);
        ErrorCode(again.Body).ShouldBe(Auth2ErrorCodes.InvalidToken);   // one use only
        (await AuditAsync("password.reset")).Count.ShouldBe(1);
        (await AuditAsync("password.reset_requested")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reset_BadTokenOrUnknownUser_GiveTheSameError()
    {
        var token = await RegisteredAsync();
        var bad = await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest(Email, "garbage", "New-Pass-12345!"), token);
        var unknown = await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest("nobody@example.com", "garbage", "New-Pass-12345!"), token);
        ErrorCode(bad.Body).ShouldBe(Auth2ErrorCodes.InvalidToken);
        bad.Body.ToString().ShouldBe(unknown.Body.ToString());
    }

    [Fact]
    public async Task Reset_TokenFromApplicationA_DoesNotWorkInApplicationB_ForNonMembersAndMembers()
    {
        var tokenA = await RegisteredAsync();
        var tokenB = await ClientTokenAsync("appb", "auth2.login");
        await CallAsync(new Browser(Factory), Auth2Routes.PasswordForgot, new ForgotPasswordRequest(Email), tokenA);
        var reset = Factory.Messages.LastEmailTo(Email)!["token"];

        // not a member of B: no reset mail in B, and the token is refused
        var before = Factory.Messages.CountTo(Email);
        await CallAsync(new Browser(Factory), Auth2Routes.PasswordForgot, new ForgotPasswordRequest(Email), tokenB);
        Factory.Messages.CountTo(Email).ShouldBe(before);
        ErrorCode((await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest(Email, reset, "New-Pass-12345!"), tokenB)).Body).ShouldBe(Auth2ErrorCodes.InvalidToken);

        // member of B with its own credentials: A's token is still no good in B (the security stamp is per application)
        var user = await UserAsync(Email);
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByIdAsync(user.Id.ToString()))!, PassB));
        ErrorCode((await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest(Email, reset, "New-Pass-12345!"), tokenB)).Body).ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await UserTokensAsync("sara", PassB, "appb")).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task Reset_ByPhone_UsesASmsCode()
    {
        await PrepareAppsAsync(both: s => { s.Methods.AllowedRegisterMethods.Add(LoginMethod.PhonePassword); s.Methods.AllowedLoginMethods.Add(LoginMethod.PhonePassword); });
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("bob", null, "+31612345678", PassA), token);
        var confirm = Factory.Messages.LastTextTo("+31612345678")!["code"];
        await CallAsync(new Browser(Factory), Auth2Routes.PhoneConfirm, new ConfirmPhoneRequest("+31612345678", confirm), token);

        await CallAsync(new Browser(Factory), Auth2Routes.PasswordForgot, new ForgotPasswordRequest("+31612345678"), token);
        var code = Factory.Messages.LastTextTo("+31612345678")!["code"];
        code.ShouldNotBe(confirm);
        (await CallAsync(new Browser(Factory), Auth2Routes.PasswordReset, new ResetPasswordRequest("+31612345678", code, "New-Pass-12345!"), token)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await UserTokensAsync("bob", "New-Pass-12345!")).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task Change_WrongCurrentPassword_Refused_RightOne_ChangesIt_AndEndsOtherSessionsButNotThisOne()
    {
        await RegisteredAsync();
        var thisSession = await UserTokensAsync("sara", PassA);
        var otherSession = await UserTokensAsync("sara", PassA);
        var bearer = thisSession.Str("access_token");

        var wrong = await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest("Wrong-Password-1", "New-Pass-12345!"), bearer);
        wrong.Status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorCode(wrong.Body).ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        var weak = await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest(PassA, "weak"), bearer);
        ErrorCode(weak.Body).ShouldBe(Auth2ErrorCodes.PasswordTooShort);

        (await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest(PassA, "New-Pass-12345!"), bearer)).Status.ShouldBe(HttpStatusCode.NoContent);

        async Task<HttpStatusCode> Refresh(JsonElement s) => (await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = s.Str("refresh_token"), ["client_id"] = "appa", ["client_secret"] = ASecret })).StatusCode;
        (await Refresh(otherSession)).ShouldBe(HttpStatusCode.BadRequest);   // ended
        (await Refresh(thisSession)).ShouldBe(HttpStatusCode.OK);            // the caller stays signed in
        (await UserTokensAsync("sara", "New-Pass-12345!")).Str("token_type").ShouldBe("Bearer");
        (await AuditAsync("password.changed")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Change_WithSignOutAfterChangePassword_EndsTheCallersSessionToo()
    {
        await RegisteredAsync();
        await UpdateSettingsAsync(A, s => s.Project.Password.SignOutAfterChangePassword = true);
        var thisSession = await UserTokensAsync("sara", PassA);
        var otherSession = await UserTokensAsync("sara", PassA);

        (await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest(PassA, "New-Pass-12345!"), thisSession.Str("access_token"))).Status.ShouldBe(HttpStatusCode.NoContent);

        async Task<HttpStatusCode> Refresh(JsonElement s) => (await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = s.Str("refresh_token"), ["client_id"] = "appa", ["client_secret"] = ASecret })).StatusCode;
        (await Refresh(otherSession)).ShouldBe(HttpStatusCode.BadRequest);
        (await Refresh(thisSession)).ShouldBe(HttpStatusCode.BadRequest);   // the caller does not stay
        (await UserTokensAsync("sara", "New-Pass-12345!")).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task Change_NeedsAUserToken_WithTheMeScope()
    {
        await RegisteredAsync();
        var noScope = (await UserTokensAsync("sara", PassA, scope: "openid")).Str("access_token");
        (await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest(PassA, "New-Pass-12345!"), noScope)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest(PassA, "New-Pass-12345!"), null)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Errors_AreLocalized_ButTheCodeStaysStable()
    {
        await RegisteredAsync();
        var bearer = (await UserTokensAsync("sara", PassA)).Str("access_token");
        var en = await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest("Wrong-Password-1", "New-Pass-12345!"), bearer, "en");
        var fa = await CallAsync(new Browser(Factory), Auth2Routes.PasswordChange, new ChangePasswordRequest("Wrong-Password-1", "New-Pass-12345!"), bearer, "fa");
        ErrorCode(en.Body).ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        ErrorCode(fa.Body).ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        fa.Body.Str("message").ShouldNotBe(en.Body.Str("message"));
    }
}
