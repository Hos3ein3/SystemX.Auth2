using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary><c>/Account/VerifyTotp</c>: a signed-in person types the code of their authenticator app; a code works once.</summary>
public class VerifyTotpPageTests : Phase6Base
{
    private const string Page = "/Account/VerifyTotp?ClientId=appa";

    private static Dictionary<string, string> Form(string code) => new() { ["Input.Code"] = code };

    private async Task<Browser> SignedInAsync()
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return b;
    }

    /// <summary>Turns two-factor on for sara (as the person does in /Manage/TwoFactor/Enable) and returns the key of the app.</summary>
    private async Task<string> TurnOnTwoFactorAsync()
    {
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByNameAsync("sara"))!;
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
        return key;
    }

    [Fact]
    public async Task WithoutASession_ThePersonGoesToTheLoginPage()
    {
        await PrepareAppsAsync();
        var r = await new Browser(Factory).GetAsync(Page);
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldContain("/Account/Login?ReturnUrl=");
    }

    [Fact]
    public async Task WithoutTwoFactor_ThePageSaysThereIsNoCode_AndLinksToTurnItOn()
    {
        var b = await SignedInAsync();
        var html = await (await b.GetAsync(Page)).Content.ReadAsStringAsync();
        html.ShouldContain("Two-factor authentication is not turned on", Case.Sensitive);
        html.ShouldContain("/Manage/TwoFactor/Enable", Case.Sensitive);
        html.ShouldNotContain("Input.Code", Case.Sensitive);   // no form: there is nothing to type

        (await b.PostPageAsync(Page, Form("123456"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the POST is refused too
    }

    [Fact]
    public async Task TheFormHasTheCodeField_AndTheRuleForTheBrowser()
    {
        var b = await SignedInAsync();
        await TurnOnTwoFactorAsync();
        var html = await (await b.GetAsync(Page)).Content.ReadAsStringAsync();
        html.ShouldContain("name=\"Input.Code\"", Case.Sensitive);
        html.ShouldContain("autocomplete=\"one-time-code\"", Case.Sensitive);
        html.ShouldContain("data-a2-rule=", Case.Sensitive);   // the rule is checked while typing
        html.ShouldContain("data-a2-digits", Case.Sensitive);  // a2.js keeps letters out of the box
        html.ShouldContain("pattern=\"[0-9 ]*\"", Case.Sensitive);
        html.ShouldContain("inputmode=\"numeric\"", Case.Sensitive);
    }

    [Fact]
    public async Task AWrongCode_ShowsItsErrorOnce()
    {
        var b = await SignedInAsync();
        await TurnOnTwoFactorAsync();
        var wrong = await b.PostPageAsync(Page, Form("000000"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var html = await wrong.Content.ReadAsStringAsync();
        System.Text.RegularExpressions.Regex.Matches(html, "The code is wrong or has expired").Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheFormMayPostBackToTheApplication_BecauseTheRedirectAfterTheCodeEndsThere()
    {
        // The page is answered with a redirect that ends at the registered redirect address of the application. A browser checks that chain against form-action,
        // so the origin is named in the policy of this page (and only this origin).
        var b = await SignedInAsync();
        await TurnOnTwoFactorAsync();
        var r = await b.GetAsync(Page);
        var csp = r.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("form-action 'self' https://client.test", Case.Sensitive);
        csp.ShouldNotContain("*");
    }

    [Fact]
    public async Task ARightCode_IsAccepted_Once_AndAWrongOrUsedCodeIsRefused()
    {
        var b = await SignedInAsync();
        var key = await TurnOnTwoFactorAsync();

        (await b.PostPageAsync(Page, Form("000000"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // wrong
        (await b.PostPageAsync(Page, Form(""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);         // empty

        var code = Totp.Now(key);
        var ok = await b.PostPageAsync(Page, Form(code));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).ShouldContain("Your code was accepted", Case.Sensitive);
        (await AuditAsync("2fa.step_up")).Count.ShouldBe(1);

        (await b.PostPageAsync(Page, Form(code))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the same code again: used
        (await AuditAsync("2fa.step_up_replayed")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task WithAnAllowedReturnUrl_ThePersonGoesThere()
    {
        var b = await SignedInAsync();
        var key = await TurnOnTwoFactorAsync();
        var r = await b.PostPageAsync($"{Page}&ReturnUrl={Uri.EscapeDataString(Redirect)}", Form(Totp.Now(key)));
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldBe(Redirect);   // a redirect URI of the application
    }

    [Fact]
    public async Task WithAnUnknownReturnUrl_ThePersonStaysOnThePage()
    {
        var b = await SignedInAsync();
        var key = await TurnOnTwoFactorAsync();

        var stay = await b.PostPageAsync($"{Page}&ReturnUrl={Uri.EscapeDataString("https://evil.example/steal")}", Form(Totp.Now(key)));
        stay.StatusCode.ShouldBe(HttpStatusCode.OK);   // not a redirect URI of the application: no redirect (the code is used up, so the next one must be another)
        (await stay.Content.ReadAsStringAsync()).ShouldContain("Your code was accepted", Case.Sensitive);
    }
}
