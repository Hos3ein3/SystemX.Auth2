using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// The hosted-UI step-up: an application that needs a fresh code of the authenticator app (<c>WithTotp()</c> / <c>[Auth2Totp]</c>) sends the browser to <c>/connect/authorize</c> with
/// <c>acr_values</c>; the Server shows <c>/Account/VerifyTotp</c>, then goes on with the sign-in, and the access token carries <c>step_up_at</c>.
/// </summary>
public class StepUpAuthorizeTests : Phase6Base
{
    private const string Scopes = "openid profile offline_access";

    private static string Authorize(string challenge, string? extra = null) =>
        $"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope={Uri.EscapeDataString(Scopes)}" +
        $"&state=s1&code_challenge={challenge}&code_challenge_method=S256{extra}";

    private static string StepUp => "&acr_values=" + Uri.EscapeDataString(Auth2AcrValues.Totp);

    private async Task<Browser> SignedInAsync()
    {
        await PrepareAppsAsync(selfUi: false);
        await CreateUserInAsync("appa");
        return await NewSessionAsync();
    }

    private async Task<Browser> NewSessionAsync()
    {
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return b;
    }

    /// <summary>
    /// Two sessions of the same person, both signed in <b>before</b> two-factor existed: the first turns it on (its first code counts), the second never gave a code.
    /// (Signing in after two-factor is on would ask for a code at the sign-in.)
    /// </summary>
    private async Task<(Browser WithCode, Browser WithoutCode, string Key)> TwoSessionsAsync()
    {
        var withCode = await SignedInAsync();
        var withoutCode = await NewSessionAsync();
        var key = await EnableTwoFactorAsync(withCode);
        return (withCode, withoutCode, key);
    }

    /// <summary>Turns two-factor on (the first code counts as a code in that session) and returns the key of the authenticator app.</summary>
    private async Task<string> EnableTwoFactorAsync(Browser b)
    {
        await b.GetAsync("/Manage/TwoFactor/Enable");
        string key;
        await using (var scope = ScopeFor("appa"))
        {
            var m = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            key = (await m.GetAuthenticatorKeyAsync((await m.FindByNameAsync("sara"))!))!;
        }
        (await b.PostPageAsync("/Manage/TwoFactor/Enable", new Dictionary<string, string> { ["Input.Code"] = Totp.Now(key) })).StatusCode.ShouldBe(HttpStatusCode.OK);
        return key;
    }

    private async Task<JsonWebToken> AccessTokenAsync(Browser b, string callback, string verifier)
    {
        var code = HttpExtensions.QueryValue(callback, "code");
        var token = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect, ["code_verifier"] = verifier, ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        token.StatusCode.ShouldBe(HttpStatusCode.OK);
        return new JsonWebToken((await token.JsonAsync()).Str("access_token"));
    }

    [Fact]
    public async Task ASessionWithoutACode_IsSentToTheVerifyPage_AndComesBack_WithTheClaimInTheToken()
    {
        var (_, b, key) = await TwoSessionsAsync();   // b: nobody gave a code in this session
        var (verifier, challenge) = HttpExtensions.NewPkce();

        var asked = await b.GetAsync(Authorize(challenge, StepUp));
        asked.Location()!.ShouldStartWith("/Account/VerifyTotp?ClientId=appa&ReturnUrl=");
        var back = Uri.UnescapeDataString(HttpExtensions.QueryValue(asked.Location()!, "ReturnUrl"));
        back.ShouldStartWith("/connect/authorize?");
        back.ShouldContain("acr_values=");                       // the same request, so the sign-in goes on after the code

        (await b.GetAsync(asked.Location()!)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.PostPageAsync(asked.Location()!, new Dictionary<string, string> { ["Input.Code"] = "000000" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var accepted = await b.PostPageAsync(asked.Location()!, new Dictionary<string, string> { ["Input.Code"] = Totp.Now(key) });
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        accepted.Location()!.ShouldContain("/connect/authorize");

        var done = await b.GetAsync(accepted.Location()!);
        done.Location()!.ShouldStartWith(Redirect + "?");
        HttpExtensions.QueryValue(done.Location()!, "state").ShouldBe("s1");
        var access = await AccessTokenAsync(b, done.Location()!, verifier);
        var at = long.Parse(access.Claims.Single(c => c.Type == Auth2Claims.StepUpAt).Value);
        Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at).ShouldBeLessThan(60);   // "just now"

        // a second request in the same minutes goes straight through
        var (_, challenge2) = HttpExtensions.NewPkce();
        (await b.GetAsync(Authorize(challenge2, StepUp))).Location()!.ShouldStartWith(Redirect + "?");
    }

    [Fact]
    public async Task APersonWithoutTwoFactor_IsToldHowToTurnItOn_AndTheWayBackDoesNotAskAgain()
    {
        var b = await SignedInAsync();   // no two-factor
        var (_, challenge) = HttpExtensions.NewPkce();
        var asked = await b.GetAsync(Authorize(challenge, StepUp));
        asked.Location()!.ShouldStartWith("/Account/VerifyTotp?");

        var page = await b.GetAsync(asked.Location()!);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        html.ShouldContain("/Manage/TwoFactor/Enable");             // the way to turn it on
        html.ShouldNotContain("acr_values");                         // "back" is the sign-in without the step-up: no loop
        html.ShouldContain("/connect/authorize");
    }

    [Fact]
    public async Task WithoutTheValue_NothingChanges_AndAPlainRequestAfterACodeCarriesTheClaimToo()
    {
        var (b, other, key) = await TwoSessionsAsync();               // b gave the first code in its session
        var (v1, c1) = HttpExtensions.NewPkce();
        var plain = await b.GetAsync(Authorize(c1));
        plain.Location()!.ShouldStartWith(Redirect + "?");            // no step-up asked
        (await AccessTokenAsync(b, plain.Location()!, v1)).Claims.ShouldContain(c => c.Type == Auth2Claims.StepUpAt);

        var (v2, c2) = HttpExtensions.NewPkce();
        var noCode = await other.GetAsync(Authorize(c2));
        (await AccessTokenAsync(other, noCode.Location()!, v2)).Claims.ShouldNotContain(c => c.Type == Auth2Claims.StepUpAt);
        key.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task TheSettingIsTheOnlyWindow_TheTokenSaysUntilWhenTheCodeCounts()
    {
        var (b, _, _) = await TwoSessionsAsync();                      // b gave its code a moment ago
        var (v1, c1) = HttpExtensions.NewPkce();
        var first = await b.GetAsync(Authorize(c1, StepUp));
        var access = await AccessTokenAsync(b, first.Location()!, v1);
        var at = long.Parse(access.Claims.Single(c => c.Type == Auth2Claims.StepUpAt).Value);
        var until = long.Parse(access.Claims.Single(c => c.Type == Auth2Claims.StepUpUntil).Value);
        (until - at).ShouldBe(15 * 60);                                // TwoFactor.StepUpMinutes, 15 by default

        await UpdateSettingsAsync(A, s => s.TwoFactor.StepUpMinutes = 1);
        var (v2, c2) = HttpExtensions.NewPkce();
        var second = await b.GetAsync(Authorize(c2, StepUp));
        var again = await AccessTokenAsync(b, second.Location()!, v2);
        (long.Parse(again.Claims.Single(c => c.Type == Auth2Claims.StepUpUntil).Value) - at).ShouldBe(60);   // the changed setting, at once

        await UpdateSettingsAsync(A, s => s.TwoFactor.StepUpMinutes = 0);
        var (v3, c3) = HttpExtensions.NewPkce();
        var zero = await b.GetAsync(Authorize(c3, StepUp));
        var floor = await AccessTokenAsync(b, zero.Location()!, v3);
        (long.Parse(floor.Claims.Single(c => c.Type == Auth2Claims.StepUpUntil).Value) - at).ShouldBe(60);   // 0 switches the question off for the Server's own pages; an application still gets one minute
    }

    [Fact]
    public async Task AProofOlderThanTheSettingAllows_AsksAgain()
    {
        var (b, _, key) = await TwoSessionsAsync();
        await UpdateSettingsAsync(A, s => s.TwoFactor.StepUpMinutes = 1);
        await Task.Delay(TimeSpan.FromSeconds(61));                    // the minute (the least an application gets) is over
        var (_, c) = HttpExtensions.NewPkce();
        var asked = await b.GetAsync(Authorize(c, StepUp));
        asked.Location()!.ShouldStartWith("/Account/VerifyTotp?");
        (await b.PostPageAsync(asked.Location()!, new Dictionary<string, string> { ["Input.Code"] = Totp.Now(key) })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task PromptNone_CannotShowThePage_SoTheApplicationGetsInteractionRequired()
    {
        var (_, b, _) = await TwoSessionsAsync();
        var (_, challenge) = HttpExtensions.NewPkce();
        var r = await b.GetAsync(Authorize(challenge, StepUp + "&prompt=none"));
        r.Location()!.ShouldStartWith(Redirect + "?");
        HttpExtensions.QueryValue(r.Location()!, "error").ShouldBe("interaction_required");
    }
}
