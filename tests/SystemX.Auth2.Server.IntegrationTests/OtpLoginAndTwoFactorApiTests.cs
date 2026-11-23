using System.Net;
using System.Text.Json;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.IntegrationTests;

public class OtpLoginTests : Phase6Base
{
    private const string Email = "sara@example.com";
    private const string Phone = "+31612345678";

    private async Task<string> StartAsync(Action<ApplicationSettings>? more = null)
    {
        await PrepareAppsAsync(both: s =>
        {
            s.Methods.AllowedLoginMethods.AddRange([LoginMethod.EmailOtp, LoginMethod.PhoneOtp]);
            s.Methods.AllowedRegisterMethods.Add(LoginMethod.PhonePassword);
            more?.Invoke(s);
        });
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("sara", Email, Phone, PassA), token);
        return token;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> OtpGrantAsync(string login, string code, string clientId = "appa") =>
        CallForm(new Dictionary<string, string> { ["grant_type"] = Auth2GrantTypes.Otp, ["username"] = login, ["code"] = code, ["scope"] = "openid offline_access", ["client_id"] = clientId, ["client_secret"] = clientId == "appa" ? ASecret : BSecret });

    private async Task<(HttpStatusCode, JsonElement)> CallForm(Dictionary<string, string> form)
    {
        var r = await new Browser(Factory).PostFormAsync("/connect/token", form);
        return (r.StatusCode, await r.JsonAsync());
    }

    [Fact]
    public async Task EmailOtp_RequestThenLogin_IssuesTokens_AndMarksTheEmailConfirmed()
    {
        var token = await StartAsync();
        (await UserAsync(Email)).EmailConfirmed.ShouldBeFalse();
        var request = await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Email), token);
        request.Status.ShouldBe(HttpStatusCode.Accepted);
        var code = Factory.Messages.LastEmailTo(Email)!["code"];
        code.Length.ShouldBe(6);

        var (status, body) = await OtpGrantAsync(Email, code);
        status.ShouldBe(HttpStatusCode.OK);
        HttpExtensions.JwtPayload(body.Str("access_token")).Values("amr").ShouldBe(["otp"]);
        body.TryGetProperty("refresh_token", out _).ShouldBeTrue();
        (await UserAsync(Email)).EmailConfirmed.ShouldBeTrue();   // the code went to this address

        (await OtpGrantAsync(Email, code)).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidOtp);   // one use only
    }

    [Fact]
    public async Task PhoneOtp_GoesBySms_AndWorksForLogin()
    {
        var token = await StartAsync();
        await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Phone), token);
        var code = Factory.Messages.LastTextTo(Phone)!["code"];
        (await OtpGrantAsync(Phone, code)).Status.ShouldBe(HttpStatusCode.OK);
        (await UserAsync(Email)).PhoneNumberConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Otp_Request_LooksTheSame_ForKnownAndUnknown_AndSendsOnlyForKnown()
    {
        var token = await StartAsync();
        var known = await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Email), token);
        var unknown = await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest("nobody@example.com"), token);
        unknown.Status.ShouldBe(known.Status);
        Factory.Messages.CountTo("nobody@example.com").ShouldBe(0);
        (await OtpGrantAsync("nobody@example.com", "123456")).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
        // asking again too soon also looks the same (and sends nothing new)
        var count = Factory.Messages.CountTo(Email);
        (await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Email), token)).Status.ShouldBe(known.Status);
        Factory.Messages.CountTo(Email).ShouldBe(count);
    }

    [Fact]
    public async Task Otp_WrongCodes_LeadToLockout_EvenForTheRightCode()
    {
        var token = await StartAsync(s => s.TwoFactor.OtpMaxAttempts = 100);
        await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Email), token);
        var code = Factory.Messages.LastEmailTo(Email)!["code"];
        var wrong = code == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++) (await OtpGrantAsync(Email, wrong)).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await OtpGrantAsync(Email, code)).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.LockedOut);
    }

    [Fact]
    public async Task Otp_MethodNotAllowed_IsReported_AndUserNamesCannotUseIt()
    {
        var token = await StartAsync(s => s.Methods.AllowedLoginMethods.Remove(LoginMethod.EmailOtp));
        var r = await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Email), token);
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorCode(r.Body).ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);
        (await OtpGrantAsync(Email, "123456")).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);
        (await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest("sara"), token)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Otp_CodeFromApplicationA_DoesNotLogIntoB()
    {
        var token = await StartAsync();
        var user = await UserAsync(Email);
        await JoinAsync(user, B);
        await CallAsync(new Browser(Factory), Auth2Routes.OtpRequest, new OtpRequest(Email), token);
        var code = Factory.Messages.LastEmailTo(Email)!["code"];
        (await OtpGrantAsync(Email, code, "appb")).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await OtpGrantAsync(Email, code, "appa")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Otp_Grant_IsOffWithoutSelfUiLogin()
    {
        var token = await StartAsync();
        await UpdateSettingsAsync(A, s => s.Clients.AllowSelfUiLogin = false);
        var (status, body) = await OtpGrantAsync(Email, "123456");
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error").ShouldBe("unauthorized_client");
    }

    [Fact]
    public async Task OtpPage_SendsThenSignsIn_ForTheHostedUi()
    {
        await StartAsync();
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/VerifyOtp?handler=Send&ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = Email })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var code = Factory.Messages.LastEmailTo(Email)!["code"];

        var bad = await b.PostPageAsync("/Account/VerifyOtp?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = Email, ["Input.Code"] = code == "000000" ? "000001" : "000000" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();

        var ok = await b.PostPageAsync("/Account/VerifyOtp?ClientId=appa&ReturnUrl=%2Fpage-b", new Dictionary<string, string> { ["Input.Login"] = Email, ["Input.Code"] = code });
        ok.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        ok.Location().ShouldBe("/page-b");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
    }
}

public class TwoFactorApiTests : Phase6Base
{
    private async Task<(string Bearer, Browser B)> StartAsync(Action<ApplicationSettings>? more = null)
    {
        await PrepareAppsAsync(both: more);
        await CreateUserInAsync("appa");
        return ((await UserTokensAsync("sara", PassA)).Str("access_token"), new Browser(Factory));
    }

    private static string KeyFrom(JsonElement setup) => setup.Str("sharedKey").Replace(" ", "");

    [Fact]
    public async Task Setup_Enable_GivesRecoveryCodes_AndLoginNeedsTheSecondStep()
    {
        var (bearer, b) = await StartAsync();
        var setup = await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer);
        setup.Status.ShouldBe(HttpStatusCode.OK);
        setup.Body.Str("authenticatorUri").ShouldStartWith("otpauth://totp/");
        setup.Body.Str("authenticatorUri").ShouldContain("secret=");
        var key = KeyFrom(setup.Body);

        (await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest("000000"), bearer)).Body.Str("code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
        var enable = await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(key)), bearer);
        enable.Status.ShouldBe(HttpStatusCode.OK);
        enable.Body.GetProperty("recoveryCodes").GetArrayLength().ShouldBe(10);

        (await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Status.ShouldBe(HttpStatusCode.Conflict);   // already on

        var login = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "password", ["username"] = "sara", ["password"] = PassA, ["client_id"] = "appa", ["client_secret"] = ASecret });
        (await login.JsonAsync()).Str("error").ShouldBe("two_factor_required");
        (await AuditAsync("2fa.enabled")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Setup_AgainBeforeEnabling_MakesANewKey()
    {
        var (bearer, b) = await StartAsync();
        var first = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        var second = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        second.ShouldNotBe(first);
        (await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(first)), bearer)).Status.ShouldBe(HttpStatusCode.BadRequest);   // the old key is dead
        (await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(second)), bearer)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enable_WithoutSetup_IsAConflict()
    {
        var (bearer, b) = await StartAsync();
        (await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest("123456"), bearer)).Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Disable_NeedsThePassword_AndTurnsEverythingOff()
    {
        var (bearer, b) = await StartAsync();
        var key = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(key)), bearer);

        var wrong = await CallAsync(b, Auth2Routes.TwoFactorDisable, new TwoFactorDisableRequest("Wrong-Password-1"), bearer);
        wrong.Status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorCode(wrong.Body).ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        (await CallAsync(b, Auth2Routes.TwoFactorDisable, new TwoFactorDisableRequest(PassA), bearer)).Status.ShouldBe(HttpStatusCode.NoContent);

        (await UserTokensAsync("sara", PassA)).Str("token_type").ShouldBe("Bearer");   // no second step now
        ErrorCode((await CallAsync(b, Auth2Routes.TwoFactorDisable, new TwoFactorDisableRequest(PassA), bearer)).Body).ShouldBe(Auth2ErrorCodes.TwoFactorNotEnabled);
        // a new setup starts clean
        (await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disable_IsRefused_WhenTheApplicationRequiresTwoFactor()
    {
        var (bearer, b) = await StartAsync();
        var key = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(key)), bearer);
        await UpdateSettingsAsync(A, s => s.TwoFactor.Mode = TwoFactorMode.Required);
        var r = await CallAsync(b, Auth2Routes.TwoFactorDisable, new TwoFactorDisableRequest(PassA), bearer);
        r.Status.ShouldBe(HttpStatusCode.Forbidden);
        ErrorCode(r.Body).ShouldBe(Auth2ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task RecoveryCodes_CanBeRegenerated_WithACode_AndOldOnesStopWorking()
    {
        var (bearer, b) = await StartAsync();
        var key = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        var oldCodes = (await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(key)), bearer)).Body
            .GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();

        (await CallAsync(b, Auth2Routes.TwoFactorRecoveryCodes, new RecoveryCodesRequest("000000"), bearer)).Body.Str("code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
        var fresh = await CallAsync(b, Auth2Routes.TwoFactorRecoveryCodes, new RecoveryCodesRequest(Totp.Now(key)), bearer);
        fresh.Status.ShouldBe(HttpStatusCode.OK);
        var newCodes = fresh.Body.GetProperty("codes").EnumerateArray().Select(x => x.GetString()!).ToList();
        newCodes.Count.ShouldBe(10);
        newCodes.Intersect(oldCodes).ShouldBeEmpty();

        async Task<string> Second(string recovery)
        {
            var first = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
            { ["grant_type"] = "password", ["username"] = "sara", ["password"] = PassA, ["client_id"] = "appa", ["client_secret"] = ASecret });
            var t = (await first.JsonAsync()).Str("two_factor_token");
            var r = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
            { ["grant_type"] = Auth2GrantTypes.TwoFactor, ["two_factor_token"] = t, ["recovery_code"] = recovery, ["client_id"] = "appa", ["client_secret"] = ASecret });
            return r.StatusCode == HttpStatusCode.OK ? "ok" : (await r.JsonAsync()).Str("error_code");
        }
        (await Second(oldCodes[0])).ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await Second(newCodes[0])).ShouldBe("ok");
    }

    [Fact]
    public async Task NotAllowed_WhenTheApplicationTurnsAuthenticatorAppsOff()
    {
        var (bearer, b) = await StartAsync(s => s.TwoFactor.AllowTotp = false);
        var r = await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer);
        r.Status.ShouldBe(HttpStatusCode.Forbidden);
        ErrorCode(r.Body).ShouldBe(Auth2ErrorCodes.TwoFactorNotAllowed);
    }

    [Fact]
    public async Task EnableInAppA_IsNotAskedInAppB_ForTheSamePerson()
    {
        var (bearer, b) = await StartAsync();
        var user = await UserAsync("sara@example.com");
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));
        var key = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(key)), bearer);
        (await UserTokensAsync("sara", PassB, "appb")).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task NoRecoveryCodes_WhenTheApplicationTurnsThemOff()
    {
        var (bearer, b) = await StartAsync(s => s.TwoFactor.AllowRecoveryCodes = false);
        var key = KeyFrom((await CallAsync(b, Auth2Routes.TwoFactorSetup, null, bearer)).Body);
        var enable = await CallAsync(b, Auth2Routes.TwoFactorEnable, new TwoFactorEnableRequest(Totp.Now(key)), bearer);
        enable.Body.GetProperty("recoveryCodes").GetArrayLength().ShouldBe(0);
    }
}
