using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class ConsentFlowTests : Phase6Base
{
    private const string Scopes = "openid profile offline_access";

    private async Task PrepareAsync(ConsentMode mode, bool require = true)
    {
        await PrepareAppsAsync(selfUi: false, both: s => { s.Sessions.RequireConsent = require; s.Sessions.ConsentMode = mode; });
        await CreateUserInAsync("appa");
    }

    private static string Authorize(string challenge, string scopes = Scopes, string? extra = null) =>
        $"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope={Uri.EscapeDataString(scopes)}" +
        $"&state=page-b&code_challenge={challenge}&code_challenge_method=S256{extra}";

    private async Task SignInAsync(Browser b)
    {
        var r = await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static string ConsentPost(string location, string handler) => location.Replace("/Account/Consent?", $"/Account/Consent?handler={handler}&");

    private async Task<HttpResponseMessage> AcceptAsync(Browser b, string consentLocation)
    {
        (await b.GetAsync(consentLocation)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return await b.PostPageAsync(ConsentPost(consentLocation, "Accept"), new Dictionary<string, string>());
    }

    [Fact]
    public async Task Explicit_AsksOnce_ThenRemembersTheAgreement()
    {
        await PrepareAsync(ConsentMode.Explicit);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (verifier, challenge) = HttpExtensions.NewPkce();

        var first = await b.GetAsync(Authorize(challenge));
        first.Location()!.ShouldStartWith("/Account/Consent?ClientId=appa&ReturnUrl=");

        var accepted = await AcceptAsync(b, first.Location()!);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        accepted.Location()!.ShouldContain("/connect/authorize");
        accepted.Location()!.ShouldContain("&consent=");

        var code = await b.GetAsync(accepted.Location()!);
        code.Location()!.ShouldStartWith(Redirect + "?");
        HttpExtensions.QueryValue(code.Location()!, "state").ShouldBe("page-b");

        // the next authorize request for the same scopes needs no new consent
        var second = await b.GetAsync(Authorize(challenge));
        second.Location()!.ShouldStartWith(Redirect + "?");

        // more scopes than agreed: ask again
        var more = await b.GetAsync(Authorize(challenge, Scopes + " email"));
        more.Location()!.ShouldStartWith("/Account/Consent");

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM openiddict_authorizations WHERE type = 'permanent'").SingleAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Explicit_TokensBelongToTheSavedAuthorization_SoTheyShowAsASession()
    {
        await PrepareAsync(ConsentMode.Explicit);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (verifier, challenge) = HttpExtensions.NewPkce();
        var consent = await b.GetAsync(Authorize(challenge));
        var accepted = await AcceptAsync(b, consent.Location()!);
        var code = HttpExtensions.QueryValue((await b.GetAsync(accepted.Location()!)).Location()!, "code");
        var token = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect, ["code_verifier"] = verifier, ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        token.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = ScopeFor("appa");
        var user = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByNameAsync("sara"))!;
        var app = await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetProjectAsync();
        var sessions = await scope.ServiceProvider.GetRequiredService<ISessionService>().ListAsync(user, app);
        sessions.Count.ShouldBe(1);
        sessions[0].Scopes.ShouldBe(["openid", "profile", "offline_access"], ignoreOrder: true);
    }

    [Fact]
    public async Task Systematic_AsksEveryTime()
    {
        await PrepareAsync(ConsentMode.Systematic);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (_, challenge) = HttpExtensions.NewPkce();
        for (var i = 0; i < 2; i++)
        {
            var ask = await b.GetAsync(Authorize(challenge));
            ask.Location()!.ShouldStartWith("/Account/Consent");
            var accepted = await AcceptAsync(b, ask.Location()!);
            (await b.GetAsync(accepted.Location()!)).Location()!.ShouldStartWith(Redirect + "?");
        }
    }

    [Fact]
    public async Task Consent_Denied_SendsAccessDeniedToTheRegisteredRedirectUri_WithState()
    {
        await PrepareAsync(ConsentMode.Explicit);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (_, challenge) = HttpExtensions.NewPkce();
        var ask = await b.GetAsync(Authorize(challenge));
        var denied = await b.PostPageAsync(ConsentPost(ask.Location()!, "Deny"), new Dictionary<string, string>());
        denied.Location()!.ShouldStartWith(Redirect + "?error=access_denied");
        denied.Location()!.ShouldContain("state=page-b");
        // nothing was remembered
        (await b.GetAsync(Authorize(challenge))).Location()!.ShouldStartWith("/Account/Consent");
    }

    [Fact]
    public async Task PromptNone_WhenConsentIsNeeded_ReturnsConsentRequired()
    {
        await PrepareAsync(ConsentMode.Explicit);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (_, challenge) = HttpExtensions.NewPkce();
        var r = await b.GetAsync(Authorize(challenge, extra: "&prompt=none"));
        r.Location()!.ShouldStartWith(Redirect);
        r.Location()!.ShouldContain("error=consent_required");
    }

    [Fact]
    public async Task ConsentToken_IsBoundToTheUserTheApplicationAndTheScopes()
    {
        await PrepareAsync(ConsentMode.Systematic);
        var other = await CreateUserInAsync("appa", "other", PassA);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (_, challenge) = HttpExtensions.NewPkce();

        (await b.GetAsync(Authorize(challenge, extra: "&consent=garbage"))).Location()!.ShouldStartWith("/Account/Consent");

        // a token made for another user, or for other scopes, does not pass
        string TokenFor(AppUser user, string[] scopes)
        {
            using var scope = ScopeFor("appa");
            var app = scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetClientAsync().GetAwaiter().GetResult()!;
            return scope.ServiceProvider.GetRequiredService<IConsentService>().CreateGrantToken(user, app, scopes);
        }
        var sara = await UserAsync("sara@example.com");
        var otherToken = TokenFor(other, ["openid", "profile", "offline_access"]);
        (await b.GetAsync(Authorize(challenge, extra: "&consent=" + Uri.EscapeDataString(otherToken)))).Location()!.ShouldStartWith("/Account/Consent");
        var wrongScopes = TokenFor(sara, ["openid"]);
        (await b.GetAsync(Authorize(challenge, extra: "&consent=" + Uri.EscapeDataString(wrongScopes)))).Location()!.ShouldStartWith("/Account/Consent");
        var right = TokenFor(sara, ["openid", "profile", "offline_access"]);
        (await b.GetAsync(Authorize(challenge, extra: "&consent=" + Uri.EscapeDataString(right)))).Location()!.ShouldStartWith(Redirect + "?");
    }

    [Fact]
    public async Task ConsentPage_RefusesAReturnUrlThatIsNotAnAuthorizeRequest_AndNeedsTheSession()
    {
        await PrepareAsync(ConsentMode.Explicit);
        var b = new Browser(Factory);
        (await b.GetAsync("/Account/Consent?ClientId=appa&ReturnUrl=%2Fconnect%2Fauthorize%3Fscope%3Dopenid")).StatusCode.ShouldBe(HttpStatusCode.Redirect);   // no session: to the login page
        await SignInAsync(b);
        (await b.GetAsync("/Account/Consent?ClientId=appa&ReturnUrl=%2Fadmin")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.GetAsync("/Account/Consent?ClientId=appa&ReturnUrl=" + Uri.EscapeDataString("https://evil.com/connect/authorize?scope=openid"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.GetAsync("/Account/Consent?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequireConsentOff_MeansImplicit_WhateverTheModeSays()
    {
        await PrepareAsync(ConsentMode.Systematic, require: false);
        var b = new Browser(Factory);
        await SignInAsync(b);
        var (_, challenge) = HttpExtensions.NewPkce();
        (await b.GetAsync(Authorize(challenge))).Location()!.ShouldStartWith(Redirect + "?");
    }
}

public class ManagePagesTests : Phase6Base
{
    private async Task<Browser> SignedInAsync()
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return b;
    }

    [Theory]
    [InlineData("/Manage")]
    [InlineData("/Manage/ChangePassword")]
    [InlineData("/Manage/Sessions")]
    [InlineData("/Manage/TwoFactor/Enable")]
    [InlineData("/Manage/TwoFactor/RecoveryCodes")]
    public async Task ManagePages_NeedASession_AndGoToTheLoginPageOtherwise(string url)
    {
        await PrepareAppsAsync();
        var r = await new Browser(Factory).GetAsync(url + "?ClientId=appa");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldContain("/Account/Login?ReturnUrl=");   // the cookie middleware makes it absolute
        Uri.UnescapeDataString(r.Location()!).ShouldContain("ClientId=appa");   // so the login page knows the application
    }

    [Fact]
    public async Task ManagePages_Load_WithASession()
    {
        var b = await SignedInAsync();
        foreach (var url in new[] { "/Manage", "/Manage/ChangePassword", "/Manage/Sessions", "/Manage/TwoFactor/Enable", "/Manage/TwoFactor/RecoveryCodes" })
            (await b.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
    }

    [Fact]
    public async Task ChangePassword_Page_ChangesThePassword()
    {
        var b = await SignedInAsync();
        var wrong = await b.PostPageAsync("/Manage/ChangePassword", new Dictionary<string, string> { ["Input.CurrentPassword"] = "Wrong-Password-1", ["Input.NewPassword"] = "New-Pass-12345!" });
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var ok = await b.PostPageAsync("/Manage/ChangePassword", new Dictionary<string, string> { ["Input.CurrentPassword"] = PassA, ["Input.NewPassword"] = "New-Pass-12345!" });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UserTokensAsync("sara", "New-Pass-12345!")).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task TwoFactorEnable_Page_ShowsTheKeyThenTurnsItOn_AndCanTurnItOffWithThePassword()
    {
        var b = await SignedInAsync();
        (await b.GetAsync("/Manage/TwoFactor/Enable")).StatusCode.ShouldBe(HttpStatusCode.OK);   // makes the key

        string key;
        await using (var scope = ScopeFor("appa"))
        {
            var m = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            key = (await m.GetAuthenticatorKeyAsync((await m.FindByNameAsync("sara"))!))!;
        }
        (await b.PostPageAsync("/Manage/TwoFactor/Enable", new Dictionary<string, string> { ["Input.Code"] = "000000" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Manage/TwoFactor/Enable", new Dictionary<string, string> { ["Input.Code"] = Totp.Now(key) })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var scope = ScopeFor("appa"))
        {
            var m = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            (await m.GetTwoFactorEnabledAsync((await m.FindByNameAsync("sara"))!)).ShouldBeTrue();
        }

        (await b.PostPageAsync("/Manage/TwoFactor/RecoveryCodes", new Dictionary<string, string> { ["Input.Code"] = Totp.Now(key) })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.PostPageAsync("/Manage/TwoFactor/Enable?handler=Disable", new Dictionary<string, string> { ["Input.Password"] = "Wrong-Password-1" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Manage/TwoFactor/Enable?handler=Disable", new Dictionary<string, string> { ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Sessions_Page_ListsSignIns_AndRevokingOneEndsItsTokens_ButNotSomeoneElsesSession()
    {
        var b = await SignedInAsync();
        var mine = await UserTokensAsync("sara", PassA);
        var other = await CreateUserInAsync("appa", "other", PassA);
        var theirs = await UserTokensAsync("other", PassA);

        await using var scope = ScopeFor("appa");
        var app = await scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>().GetProjectAsync();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var sara = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByNameAsync("sara"))!;
        var list = await sessions.ListAsync(sara, app);
        list.Count.ShouldBe(1);

        var otherList = await sessions.ListAsync((await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByNameAsync("other"))!, app);
        // trying to revoke somebody else's session through the page does nothing and looks like "not found"
        var steal = await b.PostPageAsync("/Manage/Sessions?handler=Revoke", new Dictionary<string, string> { ["AuthorizationId"] = otherList[0].Id });
        steal.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        async Task<HttpStatusCode> Refresh(JsonElement t, string client = "appa") => (await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = t.Str("refresh_token"), ["client_id"] = client, ["client_secret"] = ASecret })).StatusCode;

        var revoke = await b.PostPageAsync("/Manage/Sessions?handler=Revoke", new Dictionary<string, string> { ["AuthorizationId"] = list[0].Id });
        revoke.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await Refresh(mine)).ShouldBe(HttpStatusCode.BadRequest);
        (await Refresh(theirs)).ShouldBe(HttpStatusCode.OK);
        await using var fresh = ScopeFor("appa");   // a new request, a new DbContext (this one still tracks the old values)
        (await fresh.ServiceProvider.GetRequiredService<ISessionService>().ListAsync(sara, app)).ShouldBeEmpty();
        (await AuditAsync("session.revoked")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ManagePages_RefuseASessionOfAnotherApplication()
    {
        var b = await SignedInAsync();   // signed in for appa
        // the cookie says appa; a request that pretends to be appb must not be served as appb
        var r = await b.GetAsync("/Manage?ClientId=appb");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);   // the application is taken from the cookie, not from the query
        (await b.PostPageAsync("/Manage/ChangePassword?ClientId=appb", new Dictionary<string, string> { ["Input.CurrentPassword"] = PassA, ["Input.NewPassword"] = "New-Pass-12345!" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UserTokensAsync("sara", "New-Pass-12345!", "appa")).Str("token_type").ShouldBe("Bearer");   // it changed the appa password
    }
}
