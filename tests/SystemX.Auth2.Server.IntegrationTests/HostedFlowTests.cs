using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class HostedFlowTests : IdentityTestBase
{
    private const string Redirect = "https://client.test/cb";
    private const string Scopes = "openid profile email roles offline_access auth2.me";

    private async Task PrepareAsync(Action<TestEdit>? more = null)
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s =>
        {
            s.RedirectUris.Add(Redirect);
            s.PostLogoutRedirectUris.Add("https://client.test/bye");
            more?.Invoke(s);
        });
    }

    private static string AuthorizeUrl(string clientId, string challenge, string state = "page-b", string? extra = null) =>
        $"/connect/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope={Uri.EscapeDataString(Scopes)}" +
        $"&state={state}&code_challenge={challenge}&code_challenge_method=S256{extra}";

    private async Task<HttpResponseMessage> LoginPageAsync(Browser b, string loginUrl, string login = "sara", string password = PassA) =>
        await b.PostPageAsync(loginUrl, new Dictionary<string, string> { ["Input.Login"] = login, ["Input.Password"] = password });

    private async Task<JsonElementResult> ExchangeAsync(Browser b, string code, string verifier, string clientId = "appa", string? secret = null)
    {
        var r = await b.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect, ["code_verifier"] = verifier,
            ["client_id"] = clientId, ["client_secret"] = secret ?? ASecret,
        });
        return new JsonElementResult(r.StatusCode, await r.JsonAsync());
    }

    public sealed record JsonElementResult(HttpStatusCode Status, System.Text.Json.JsonElement Body);

    [Fact]
    public async Task CodeFlow_WithPkce_EndToEnd_ThroughTheEmptyPages()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var (verifier, challenge) = HttpExtensions.NewPkce();
        var authorize = AuthorizeUrl("appa", challenge);

        // 1. not signed in: the authorize endpoint sends the browser to the login page, with the authorize URL as ReturnUrl
        var r1 = await b.GetAsync(authorize);
        r1.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r1.Location()!.ShouldContain("/Account/Login?ReturnUrl=");
        var loginUrl = new Uri(new Uri("http://localhost"), r1.Location()).PathAndQuery;

        // 2. the login page handler signs in and goes back to the authorize URL (page B is kept inside it)
        var r2 = await LoginPageAsync(b, loginUrl);
        r2.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r2.Location().ShouldBe(authorize);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();

        // 3. the authorize endpoint now issues a code and goes back to the client
        var r3 = await b.GetAsync(r2.Location()!);
        r3.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r3.Location()!.ShouldStartWith(Redirect + "?");
        HttpExtensions.QueryValue(r3.Location()!, "state").ShouldBe("page-b");
        var code = HttpExtensions.QueryValue(r3.Location()!, "code");

        // 4. the client exchanges the code (with the verifier and its secret)
        var token = await ExchangeAsync(b, code, verifier);
        token.Status.ShouldBe(HttpStatusCode.OK);
        token.Body.Str("token_type").ShouldBe("Bearer");
        token.Body.GetProperty("expires_in").GetInt32().ShouldBeInRange(15 * 60 - 5, 15 * 60);
        token.Body.TryGetProperty("refresh_token", out _).ShouldBeTrue();
        token.Body.TryGetProperty("id_token", out _).ShouldBeTrue();

        var claims = HttpExtensions.JwtPayload(token.Body.Str("access_token"));
        claims.Str("preferred_username").ShouldBe("sara");
        claims.Str("project_id").ShouldBe(A.Id.ToString("D"));
        claims.Str("client_id").ShouldBe("appa");
        claims.TryGetProperty("application_id", out _).ShouldBeTrue();
        claims.Str("projectkey").ShouldBe("appa");
        claims.Values("amr").ShouldBe(["pwd"]);
        claims.Values("aud").ShouldContain("appa");
        claims.TryGetProperty("sid", out _).ShouldBeTrue();
        claims.TryGetProperty("pv", out _).ShouldBeTrue();
        claims.TryGetProperty("permissions", out _).ShouldBeFalse();   // the permission list is never in the token
        claims.TryGetProperty("machine", out _).ShouldBeFalse();

        // 5. userinfo
        var info = await (await b.GetAsync("/connect/userinfo", token.Body.Str("access_token"))).JsonAsync();
        info.Str("sub").ShouldBe(claims.Str("sub"));
        info.Str("email").ShouldBe("sara@example.com");
    }

    [Fact]
    public async Task CodeFlow_CodeIsOneTimeUse_AndNeedsTheRightVerifierAndSecret()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var (verifier, challenge) = HttpExtensions.NewPkce();
        var code = await GetCodeAsync(b, challenge);

        (await ExchangeAsync(b, code, "wrong-verifier-wrong-verifier-wrong-verifier")).Status.ShouldBe(HttpStatusCode.BadRequest);
        // the failed attempt may already have used the code, so get a fresh one for the next checks
        var code2 = await GetCodeAsync(b, challenge);
        (await ExchangeAsync(b, code2, verifier, secret: "not-the-secret")).Status.ShouldBe(HttpStatusCode.Unauthorized);

        var code3 = await GetCodeAsync(b, challenge);
        (await ExchangeAsync(b, code3, verifier)).Status.ShouldBe(HttpStatusCode.OK);
        (await ExchangeAsync(b, code3, verifier)).Status.ShouldBe(HttpStatusCode.BadRequest);   // second use
    }

    [Fact]
    public async Task Authorize_WithoutPkce_IsRefused()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await b.GetAsync($"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope=openid");
        // OpenIddict answers with an error redirect to the client (or a 400); either way, no login page and no code
        (r.Location() ?? "").ShouldNotContain("/Account/Login");
        (r.Location() ?? "").ShouldNotContain("code=");
    }

    [Fact]
    public async Task Authorize_PlainCodeChallengeMethod_IsRefused()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await b.GetAsync($"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope=openid&code_challenge=abcdefghijklmnopqrstuvwxyzabcdefghijklmnopq&code_challenge_method=plain");
        (r.Location() ?? "").ShouldNotContain("/Account/Login");
        (r.Location() ?? "").ShouldNotContain("code=");
    }

    [Fact]
    public async Task Authorize_UnregisteredRedirectUri_IsRefused_WithoutRedirectingToIt()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var (_, challenge) = HttpExtensions.NewPkce();
        var r = await b.GetAsync($"/connect/authorize?client_id=appa&redirect_uri={Uri.EscapeDataString("https://evil.test/cb")}&response_type=code&scope=openid&code_challenge={challenge}&code_challenge_method=S256");
        r.StatusCode.ShouldNotBe(HttpStatusCode.Redirect);
        (r.Location() ?? "").ShouldNotContain("evil.test");
    }

    [Fact]
    public async Task Authorize_PromptLogin_ForcesLogin_AndDoesNotLoop()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var (_, challenge) = HttpExtensions.NewPkce();
        await SignInAsync(b, challenge);

        var again = await b.GetAsync(AuthorizeUrl("appa", challenge, extra: "&prompt=login"));
        again.Location()!.ShouldContain("/Account/Login?ReturnUrl=");
        Uri.UnescapeDataString(again.Location()!).ShouldNotContain("prompt=login");
    }

    [Fact]
    public async Task Authorize_PromptNone_WithoutSession_ReturnsLoginRequiredToTheClient()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var (_, challenge) = HttpExtensions.NewPkce();
        var r = await b.GetAsync(AuthorizeUrl("appa", challenge, extra: "&prompt=none"));
        r.Location().ShouldNotBeNull().ShouldStartWith(Redirect);
        r.Location()!.ShouldContain("error=login_required");
    }

    [Fact]
    public async Task Authorize_SessionOfOneApplication_IsNotUsedForAnother()
    {
        await PrepareAsync();
        var user = await CreateUserInAsync("appa", "other", PassA);
        await UpdateSettingsAsync(B, s => s.RedirectUris.Add(Redirect));
        await JoinAsync(user, B);

        var b = new Browser(Factory);
        var (_, challenge) = HttpExtensions.NewPkce();
        await SignInAsync(b, challenge);   // signed in for appa

        var forB = await b.GetAsync(AuthorizeUrl("appb", challenge));
        forB.Location()!.ShouldContain("/Account/Login?ReturnUrl=");   // must sign in again for appb
    }

    [Fact]
    public async Task Login_WrongPassword_And_UnknownUser_GiveTheSameStatus_AndNoSessionCookie()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var url = "/Account/Login?ClientId=appa";
        var wrong = await LoginPageAsync(b, url, "sara", "Wrong-Password-1");
        var unknown = await LoginPageAsync(b, url, "nobody", "Wrong-Password-1");
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(wrong.StatusCode);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
    }

    [Fact]
    public async Task Login_PostWithoutAntiforgeryToken_IsRefused()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await b.PostFormAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA });
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
    }

    [Fact]
    public async Task Login_AfterTooManyFailures_GoesToTheLockoutPage()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        for (var i = 0; i < 5; i++) await LoginPageAsync(b, "/Account/Login?ClientId=appa", "sara", "Wrong-Password-1");
        var locked = await LoginPageAsync(b, "/Account/Login?ClientId=appa", "sara", PassA);
        locked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        locked.Location()!.ShouldStartWith("/Account/Lockout");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
        (await b.GetAsync("/Account/Lockout?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_UnknownApplication_IsNotFound_AndMultiModeNeedsAClientId()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        (await b.GetAsync("/Account/Login?ClientId=nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.GetAsync("/Account/Login")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.GetAsync("/Account/Login?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TwoFactorPage_CompletesTheLogin_AndSignsIn()
    {
        await PrepareAsync();
        string key;
        await using (var scope = ScopeFor("appa"))
        {
            var m = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var u = (await m.FindByNameAsync("sara"))!;
            await m.ResetAuthenticatorKeyAsync(u);
            key = (await m.GetAuthenticatorKeyAsync(u))!;
            await m.SetTwoFactorEnabledAsync(u, true);
        }

        var b = new Browser(Factory);
        // the login page sends the browser to the TwoFactor page (the token travels in TempData)
        var first = await LoginPageAsync(b, "/Account/Login?ClientId=appa&ReturnUrl=%2Fpage-b");
        first.Location()!.ShouldStartWith("/Account/TwoFactor");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();

        // the page needs the token in the form; a UI would read it from the page model, the test asks the service
        string token;
        await using (var scope = ScopeFor("appa"))
            token = (await scope.ServiceProvider.GetRequiredService<ILoginService>().PasswordLoginAsync(new PasswordLoginRequest("sara", PassA))).TwoFactorToken!;

        var bad = await b.PostPageAsync("/Account/TwoFactor?ClientId=appa", new Dictionary<string, string> { ["TwoFactorToken"] = token, ["Input.Code"] = "000000" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var good = await b.PostPageAsync("/Account/TwoFactor?ClientId=appa&ReturnUrl=%2Fpage-b", new Dictionary<string, string> { ["TwoFactorToken"] = token, ["Input.Code"] = Totp.Now(key) });
        good.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        good.Location().ShouldBe("/page-b");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
    }

    // ---------------------------------------------------------------- return URL rules

    [Fact]
    public async Task ReturnUrl_DirectLogin_UsesRedirectAfterLogin()
    {
        await PrepareAsync(s => s.Redirects.RedirectAfterLogin = "/dashboard");
        var b = new Browser(Factory);
        var r = await LoginPageAsync(b, "/Account/Login?ClientId=appa");
        r.Location().ShouldBe("/dashboard");
    }

    [Fact]
    public async Task ReturnUrl_ValidLocalUrl_WinsOverRedirectAfterLogin()
    {
        await PrepareAsync(s => s.Redirects.RedirectAfterLogin = "/dashboard");
        var b = new Browser(Factory);
        var r = await LoginPageAsync(b, "/Account/Login?ClientId=appa&ReturnUrl=" + Uri.EscapeDataString("/orders/42?tab=1"));
        r.Location().ShouldBe("/orders/42?tab=1");
    }

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/%2Fevil.com")]
    [InlineData("/%5Cevil.com")]
    [InlineData("https://client.test/not-registered")]
    public async Task ReturnUrl_EvilOrUnregistered_IsIgnored_AndFallbackIsUsed(string evil)
    {
        await PrepareAsync(s => s.Redirects.RedirectAfterLogin = "/dashboard");
        var b = new Browser(Factory);
        var r = await LoginPageAsync(b, "/Account/Login?ClientId=appa&ReturnUrl=" + Uri.EscapeDataString(evil));
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldBe("/dashboard");
    }

    [Fact]
    public async Task ReturnUrl_EvilWithNoFallback_GoesHome()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await LoginPageAsync(b, "/Account/Login?ClientId=appa&ReturnUrl=" + Uri.EscapeDataString("//evil.com"));
        r.Location().ShouldBe("/");
    }

    [Fact]
    public async Task ReturnUrl_RegisteredAbsoluteRedirectUri_IsAccepted()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await LoginPageAsync(b, "/Account/Login?ClientId=appa&ReturnUrl=" + Uri.EscapeDataString(Redirect));
        r.Location().ShouldBe(Redirect);
    }

    // ---------------------------------------------------------------- logout

    [Fact]
    public async Task Logout_Page_SignsOut_AndUsesRedirectAfterLogout()
    {
        await PrepareAsync(s => s.Redirects.RedirectAfterLogout = "/signed-out");
        var b = new Browser(Factory);
        await LoginPageAsync(b, "/Account/Login?ClientId=appa");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
        var r = await b.PostPageAsync("/Account/Logout?ClientId=appa", new Dictionary<string, string>());
        r.Location().ShouldBe("/signed-out");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
    }

    [Fact]
    public async Task Logout_Page_IgnoresEvilReturnUrl()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await b.PostPageAsync("/Account/Logout?ClientId=appa&ReturnUrl=" + Uri.EscapeDataString("https://evil.com"), new Dictionary<string, string>());
        r.Location().ShouldBe("/");
    }

    [Fact]
    public async Task EndSession_RegisteredPostLogoutUri_IsUsed_AndSessionEnds()
    {
        await PrepareAsync(s => s.Redirects.RedirectAfterLogout = "/signed-out");
        var b = new Browser(Factory);
        await LoginPageAsync(b, "/Account/Login?ClientId=appa");

        var r = await b.GetAsync("/connect/endsession?client_id=appa&post_logout_redirect_uri=" + Uri.EscapeDataString("https://client.test/bye") + "&state=s1");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldStartWith("https://client.test/bye");
        HttpExtensions.QueryValue(r.Location()!, "state").ShouldBe("s1");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
    }

    [Fact]
    public async Task EndSession_WithoutPostLogoutUri_UsesRedirectAfterLogout()
    {
        await PrepareAsync(s => s.Redirects.RedirectAfterLogout = "/signed-out");
        var b = new Browser(Factory);
        var r = await b.GetAsync("/connect/endsession?client_id=appa");
        r.Location().ShouldBe("/signed-out");
    }

    [Fact]
    public async Task EndSession_UnregisteredPostLogoutUri_IsNotFollowed()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var r = await b.GetAsync("/connect/endsession?client_id=appa&post_logout_redirect_uri=" + Uri.EscapeDataString("https://evil.test/bye"));
        (r.Location() ?? "").ShouldNotContain("evil.test");
    }

    // ---------------------------------------------------------------- helpers

    private async Task SignInAsync(Browser b, string challenge)
    {
        var r1 = await b.GetAsync(AuthorizeUrl("appa", challenge));
        var loginUrl = new Uri(new Uri("http://localhost"), r1.Location()).PathAndQuery;
        (await LoginPageAsync(b, loginUrl)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private async Task<string> GetCodeAsync(Browser b, string challenge)
    {
        var r = await b.GetAsync(AuthorizeUrl("appa", challenge));
        if (r.Location()!.Contains("/Account/Login"))
        {
            var loginUrl = new Uri(new Uri("http://localhost"), r.Location()).PathAndQuery;
            await LoginPageAsync(b, loginUrl);
            r = await b.GetAsync(AuthorizeUrl("appa", challenge));
        }
        return HttpExtensions.QueryValue(r.Location()!, "code");
    }
}
