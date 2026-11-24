using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.IntegrationTests;

public abstract class PasskeyTestBase : ExternalLoginTestBase
{
    protected async Task PreparePasskeysAsync(Action<ApplicationSettings>? app = null, bool both = false)
    {
        await SetUpAsync();
        foreach (var a in both ? new[] { A, B } : [A])
            await UpdateSettingsAsync(a, s =>
            {
                s.Redirects.AllowedRedirectUris.Add(ClientRedirect);
                s.Methods.AllowedLoginMethods.Add(LoginMethod.Passkey);
                s.Passkeys.AllowPasskeys = true;
                app?.Invoke(s);
            });
    }

    protected async Task<Browser> LocalSessionAsync(string name = "sara", string client = "appa", string password = PassA)
    {
        var b = new Browser(Factory);
        (await b.PostPageAsync($"/Account/Login?ClientId={client}", new Dictionary<string, string> { ["Input.Login"] = name, ["Input.Password"] = password })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return b;
    }

    protected static async Task<string> BodyAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    protected async Task<HttpResponseMessage> RegisterAsync(Browser b, SoftwareAuthenticator key, string? name = "Laptop", string client = "appa")
    {
        var options = await b.SendJsonWithTokenAsync(HttpMethod.Post, $"/account/passkey/creation-options?client_id={client}", new { });
        if (options.StatusCode != HttpStatusCode.OK) return options;
        var credential = JsonDocument.Parse(key.Create(await BodyAsync(options))).RootElement;
        return await b.SendJsonWithTokenAsync(HttpMethod.Post, $"/account/passkey/register?client_id={client}", new { credential, name });
    }

    protected async Task<HttpResponseMessage> PasskeySignInAsync(Browser b, SoftwareAuthenticator key, string? login = "sara", string client = "appa", bool userHandle = true, string? returnUrl = null)
    {
        var options = await b.PostJsonAsync($"/account/passkey/request-options?client_id={client}", new { login });
        if (options.StatusCode != HttpStatusCode.OK) return options;
        var credential = JsonDocument.Parse(key.Get(await BodyAsync(options), userHandle)).RootElement;
        return await b.PostJsonAsync($"/account/passkey/signin?client_id={client}", new { credential, returnUrl });
    }

    protected static async Task<string> CodeOf(HttpResponseMessage r) => (await r.JsonAsync()).Str("code");
}

public class PasskeyFlowTests : PasskeyTestBase
{
    [Fact]
    public async Task Register_ThenSignIn_WithAndWithoutAUserName_AndTheCounterGoesUp()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa");
        var key = new SoftwareAuthenticator();
        var session = await LocalSessionAsync();
        var registered = await RegisterAsync(session, key, "MacBook");
        registered.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(registered));
        var dto = await registered.JsonAsync();
        dto.Str("name").ShouldBe("MacBook");
        dto.GetProperty("isUserVerified").GetBoolean().ShouldBeTrue();
        (await EventsAsync("passkey.registered")).Count.ShouldBe(1);

        var anonymous = new Browser(Factory);
        var r1 = await PasskeySignInAsync(anonymous, key, "sara", returnUrl: ClientRedirect);
        r1.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(r1));
        var body = await r1.JsonAsync();
        body.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        body.Str("redirectTo").ShouldBe(ClientRedirect);
        anonymous.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();

        // usernameless: no login name, the browser offers the passkey and sends the user handle
        var again = new Browser(Factory);
        (await PasskeySignInAsync(again, key, login: null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await (await session.GetAsync("/account/passkeys?client_id=appa")).JsonAsync();
        list[0].GetProperty("signCount").GetUInt32().ShouldBe(2u);
    }

    [Fact]
    public async Task AnAssertion_CannotBeReplayed()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa");
        var key = new SoftwareAuthenticator();
        await RegisterAsync(await LocalSessionAsync(), key);

        var b = new Browser(Factory);
        var options = await BodyAsync(await b.PostJsonAsync("/account/passkey/request-options?client_id=appa", new { login = "sara" }));
        var credential = JsonDocument.Parse(key.Get(options)).RootElement;
        (await b.PostJsonAsync("/account/passkey/signin?client_id=appa", new { credential })).StatusCode.ShouldBe(HttpStatusCode.OK);
        // the same answer again, even with the cookies kept
        var replay = await b.PostJsonAsync("/account/passkey/signin?client_id=appa", new { credential });
        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await EventsAsync("passkey.login.failed")).Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task APasskey_OfApplicationA_IsNotAcceptedByApplicationB()
    {
        await PreparePasskeysAsync(both: true);
        await CreateUserInAsync("appa");
        await JoinAsync((await FindUserAsync("sara"))!, B);
        var key = new SoftwareAuthenticator();
        await RegisterAsync(await LocalSessionAsync(), key);
        (await PasskeySignInAsync(new Browser(Factory), key, "sara", "appa")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var denied = await PasskeySignInAsync(new Browser(Factory), key, "sara", "appb", userHandle: true);
        denied.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await denied.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Disabled_OrNotAnAllowedMethod_IsRefused()
    {
        await PreparePasskeysAsync(s => s.Passkeys.AllowPasskeys = false);
        await CreateUserInAsync("appa");
        var session = await LocalSessionAsync();
        var r = await RegisterAsync(session, new SoftwareAuthenticator());
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await r.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.PasskeyNotAllowed);
        (await new Browser(Factory).PostJsonAsync("/account/passkey/request-options?client_id=appa", new { login = "sara" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await UpdateSettingsAsync(A, s => { s.Passkeys.AllowPasskeys = true; s.Methods.AllowedLoginMethods.Remove(LoginMethod.Passkey); });
        var key = new SoftwareAuthenticator();
        (await RegisterAsync(session, key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var signIn = await PasskeySignInAsync(new Browser(Factory), key);
        (await signIn.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.LoginMethodNotAllowed);
    }

    [Fact]
    public async Task Limits_MaxPasskeys_NameLength_AndUserVerification()
    {
        await PreparePasskeysAsync(s => { s.Passkeys.MaxPasskeysPerUser = 2; s.Passkeys.PasskeyNameMaxLength = 8; });
        await CreateUserInAsync("appa");
        var session = await LocalSessionAsync();
        (await RegisterAsync(session, new SoftwareAuthenticator(), "ok")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RegisterAsync(session, new SoftwareAuthenticator(), "a-name-that-is-too-long")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RegisterAsync(session, new SoftwareAuthenticator { UserVerified = false }, "noverify")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // UV is required by default
        (await RegisterAsync(session, new SoftwareAuthenticator(), "second")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var third = await RegisterAsync(session, new SoftwareAuthenticator(), "third");
        third.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await third.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.PasskeyLimitReached);
    }

    [Fact]
    public async Task ACredentialThatExistsAlready_CannotBeRegisteredAgain()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa", "sara");
        await CreateUserInAsync("appa", "dan");
        var key = new SoftwareAuthenticator();
        (await RegisterAsync(await LocalSessionAsync("sara"), key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var other = new SoftwareAuthenticator { CredentialId = key.CredentialId };
        var r = await RegisterAsync(await LocalSessionAsync("dan"), other);
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BlockedAuthenticators_AndTheAllowList_AreEnforced()
    {
        var blocked = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa");
        var session = await LocalSessionAsync();
        var options = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Auth2ServerOptions>>().Value;
        options.Passkeys.BlockedAaguids.Add(blocked);
        var denied = await RegisterAsync(session, new SoftwareAuthenticator { Aaguid = blocked });
        (await denied.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.PasskeyBlockedAuthenticator);
        (await RegisterAsync(session, new SoftwareAuthenticator())).StatusCode.ShouldBe(HttpStatusCode.OK);

        options.Passkeys.AttestationMode = "AaguidAllowList";
        var other = new SoftwareAuthenticator { Aaguid = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002") };
        (await RegisterAsync(session, other)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        options.Passkeys.AllowedAaguids.Add(other.Aaguid);
        (await RegisterAsync(session, other)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EventsAsync("passkey.blocked")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task ManagementRoutes_NeedTheSessionAndTheAntiforgeryHeader()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa");
        var anonymous = new Browser(Factory);
        (await anonymous.PostJsonAsync("/account/passkey/creation-options?client_id=appa", new { })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/account/passkeys?client_id=appa")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var session = await LocalSessionAsync();
        (await session.PostJsonAsync("/account/passkey/creation-options?client_id=appa", new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // no token
        (await session.SendJsonWithTokenAsync(HttpMethod.Post, "/account/passkey/creation-options?client_id=appa", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rename_AndRemove_WorkForOwnPasskeysOnly_AndTheLastMethodStays()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa", "sara");
        await CreateUserInAsync("appa", "dan");
        var key = new SoftwareAuthenticator();
        var session = await LocalSessionAsync("sara");
        var id = (await (await RegisterAsync(session, key, "old")).JsonAsync()).Str("id");

        (await session.SendJsonWithTokenAsync(HttpMethod.Patch, $"/account/passkeys/{id}?client_id=appa", new { name = "renamed" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await session.GetAsync("/account/passkeys?client_id=appa")).JsonAsync())[0].Str("name").ShouldBe("renamed");

        var dan = await LocalSessionAsync("dan");
        (await dan.SendJsonWithTokenAsync(HttpMethod.Delete, $"/account/passkeys/{id}?client_id=appa", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);   // not dan's

        // sara has a password too, so she may remove it; without a password the last method stays
        await using (var scope = ScopeFor("appa"))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.RemovePasswordAsync((await users.FindByNameAsync("sara"))!);
        }
        var refused = await session.SendJsonWithTokenAsync(HttpMethod.Delete, $"/account/passkeys/{id}?client_id=appa", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.CannotRemoveLastLoginMethod);
        (await session.SendJsonWithTokenAsync(HttpMethod.Delete, "/account/passkeys/not-base64!?client_id=appa", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tokens_CarryAmr_WithMfaForAUserVerifiedPasskey()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa");
        var key = new SoftwareAuthenticator();
        await RegisterAsync(await LocalSessionAsync(), key);
        var b = new Browser(Factory);
        var (verifier, challenge) = HttpExtensions.NewPkce();
        var authorize = $"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(ClientRedirect)}&response_type=code&scope={Uri.EscapeDataString(Scopes)}&state=s1&code_challenge={challenge}&code_challenge_method=S256";
        (await PasskeySignInAsync(b, key, returnUrl: authorize)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var code = HttpExtensions.QueryValue((await b.GetAsync(authorize)).Location()!, "code");
        var token = await (await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = ClientRedirect, ["code_verifier"] = verifier, ["client_id"] = "appa", ["client_secret"] = ASecret,
        })).JsonAsync();
        var claims = HttpExtensions.JwtPayload(token.Str("access_token"));
        claims.Values("amr").ShouldBe(["swk", "mfa"], ignoreOrder: true);
        claims.TryGetProperty("idp", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task LocalTwoFactor_IsSkippedForAUserVerifiedPasskey_ButNotForOneWithoutVerification()
    {
        await PreparePasskeysAsync(s => { s.TwoFactor.Mode = TwoFactorMode.Optional; s.TwoFactor.AllowTotp = true; s.Passkeys.PasskeyUserVerification = "preferred"; });
        await CreateUserInAsync("appa");
        var verified = new SoftwareAuthenticator();
        var unverified = new SoftwareAuthenticator { UserVerified = false };
        var session = await LocalSessionAsync();
        await RegisterAsync(session, verified, "uv");
        await RegisterAsync(session, unverified, "no-uv");
        await using (var scope = ScopeFor("appa"))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = (await users.FindByNameAsync("sara"))!;
            await users.ResetAuthenticatorKeyAsync(u);
            await users.SetTwoFactorEnabledAsync(u, true);
        }
        (await (await PasskeySignInAsync(new Browser(Factory), verified)).JsonAsync()).GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        var second = await (await PasskeySignInAsync(new Browser(Factory), unverified)).JsonAsync();
        second.GetProperty("succeeded").GetBoolean().ShouldBeFalse();
        second.Str("errorCode").ShouldBe(Auth2ErrorCodes.TwoFactorRequired);
    }

    [Fact]
    public async Task OptionsJson_CarriesTheRulesOfTheApplication()
    {
        await PreparePasskeysAsync(s => { s.Passkeys.PasskeyResidentKey = "required"; });
        await CreateUserInAsync("appa");
        var session = await LocalSessionAsync();
        var creation = JsonDocument.Parse(await BodyAsync(await session.SendJsonWithTokenAsync(HttpMethod.Post, "/account/passkey/creation-options?client_id=appa", new { }))).RootElement;
        var selection = creation.GetProperty("authenticatorSelection");
        selection.Str("userVerification").ShouldBe("required");
        selection.Str("residentKey").ShouldBe("required");
        var request = JsonDocument.Parse(await BodyAsync(await new Browser(Factory).PostJsonAsync("/account/passkey/request-options?client_id=appa", new { login = "nobody" }))).RootElement;
        request.Str("userVerification").ShouldBe("required");   // an unknown user gets options like any other
        request.Str("challenge").Length.ShouldBeGreaterThan(20);
    }
}

public class PasskeyBackupRuleTests : PasskeyTestBase
{
    [Fact]
    public async Task APersonWithOneLoginAndOnePasskey_MustHaveASecondPasskeyBeforeUnlinking()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true, s => { s.Methods.AllowedLoginMethods.Add(LoginMethod.Passkey); s.Passkeys.AllowPasskeys = true; });
        var browser = new Browser(Factory);
        (await SignInAsync(browser, new FakeUser { Sub = "bk", Email = "bk@corp.test" })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var first = new SoftwareAuthenticator();
        (await RegisterAsync(browser, first, "one")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = (await FindUserAsync("bk@corp.test"))!;
        await using var scope = ScopeFor("appa");
        var links = scope.ServiceProvider.GetRequiredService<IExternalLinkService>();
        var u = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id.ToString()))!;
        (await links.UnlinkAsync(u, "corp", "bk")).ErrorCode.ShouldBe(Auth2ErrorCodes.CannotRemoveLastLoginMethod);

        (await RegisterAsync(browser, new SoftwareAuthenticator(), "two")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var fresh = ScopeFor("appa");
        var u2 = (await fresh.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id.ToString()))!;
        var result = await fresh.ServiceProvider.GetRequiredService<IExternalLinkService>().UnlinkAsync(u2, "corp", "bk");
        result.Succeeded.ShouldBeTrue(result.ErrorCode + " " + string.Join(",", result.Errors.Select(e => e.Code)));
    }

    [Fact]
    public async Task WithTheRuleOff_OnePasskeyIsEnough()
    {
        await PrepareAsync(p => p.TrustProviderEmailVerification = true, s => { s.Methods.AllowedLoginMethods.Add(LoginMethod.Passkey); s.Passkeys.AllowPasskeys = true; s.Passkeys.RequirePasskeyBackupMethod = false; });
        var browser = new Browser(Factory);
        await SignInAsync(browser, new FakeUser { Sub = "bk", Email = "bk@corp.test" });
        await RegisterAsync(browser, new SoftwareAuthenticator());
        var user = (await FindUserAsync("bk@corp.test"))!;
        await using var scope = ScopeFor("appa");
        var u = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id.ToString()))!;
        (await scope.ServiceProvider.GetRequiredService<IExternalLinkService>().UnlinkAsync(u, "corp", "bk")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ManagePage_ListsPasskeys_AndSuggestsABackupWhenNoneIsBackedUp()
    {
        await PreparePasskeysAsync();
        await CreateUserInAsync("appa");
        var session = await LocalSessionAsync();
        await RegisterAsync(session, new SoftwareAuthenticator { BackedUp = false });
        (await session.GetAsync("/Manage/Passkeys")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await (await session.GetAsync("/account/passkeys?client_id=appa")).JsonAsync())[0].Str("id");
        (await session.PostPageAsync("/Manage/Passkeys?handler=Rename", new Dictionary<string, string> { ["Id"] = id, ["Name"] = "Phone" })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await (await session.GetAsync("/account/passkeys?client_id=appa")).JsonAsync())[0].Str("name").ShouldBe("Phone");
        (await session.PostPageAsync("/Manage/Passkeys?handler=Delete", new Dictionary<string, string> { ["Id"] = id })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await (await session.GetAsync("/account/passkeys?client_id=appa")).JsonAsync()).GetArrayLength().ShouldBe(0);
    }
}
