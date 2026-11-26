using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// "Sudo mode" like GitHub: a person with two-factor on is asked for a code of the authenticator app again before a restricted page
/// (delete the account, turn two-factor off), unless they gave one recently in this session (the second step of the sign-in counts).
/// </summary>
public class StepUpPagesTests : Phase6Base
{
    private const string Delete = "/Manage/DeleteAccount";
    private const string Verify = "/Account/VerifyTotp?ClientId=appa";

    private static Dictionary<string, string> Form(string code) => new() { ["Input.Code"] = code };

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

    /// <summary>A person signed in with a password only (no code in this session), who then turned two-factor on.</summary>
    private async Task<(Browser Browser, string Key)> SignedInWithTwoFactorAsync(bool turnOn = true)
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return (b, turnOn ? await TurnOnTwoFactorAsync() : "");
    }

    private async Task<bool> UserExistsAsync()
    {
        await using var scope = ScopeFor("appa");
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("sara") is not null;
    }

    [Fact]
    public async Task ARestrictedPage_AsksForACodeFirst_AndComesBackAfterTheRightOne()
    {
        var (b, key) = await SignedInWithTwoFactorAsync();

        var ask = await b.GetAsync($"{Delete}?ClientId=appa");
        ask.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = ask.Location()!;
        location.ShouldStartWith("/Account/VerifyTotp");
        Uri.UnescapeDataString(location).ShouldContain("ReturnUrl=/Manage/DeleteAccount");   // it comes back to this very page

        var back = await b.PostPageAsync(location, Form(Totp.Now(key)));
        back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        back.Location().ShouldStartWith("/Manage/DeleteAccount");

        (await b.GetAsync($"{Delete}?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);   // no second question inside the window
    }

    [Fact]
    public async Task ThePost_OfARestrictedPage_IsNotExecutedWithoutAFreshCode()
    {
        var (b, _) = await SignedInWithTwoFactorAsync();
        var r = await b.PostPageAsync($"{Delete}?ClientId=appa", new Dictionary<string, string> { ["Input.Password"] = PassA });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldStartWith("/Account/VerifyTotp");
        (await UserExistsAsync()).ShouldBeTrue();   // the account was not deleted
    }

    [Fact]
    public async Task TurningTwoFactorOff_IsRestrictedToo()
    {
        var (b, _) = await SignedInWithTwoFactorAsync();
        var r = await b.PostPageAsync("/Manage/TwoFactor/Enable?ClientId=appa&handler=Disable", new Dictionary<string, string> { ["Input.Password"] = PassA });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldStartWith("/Account/VerifyTotp");
        await using var scope = ScopeFor("appa");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.GetTwoFactorEnabledAsync((await users.FindByNameAsync("sara"))!)).ShouldBeTrue();   // still on
    }

    [Fact]
    public async Task APersonWithoutTwoFactor_IsNotAsked()
    {
        var (b, _) = await SignedInWithTwoFactorAsync(turnOn: false);
        (await b.GetAsync($"{Delete}?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);   // there is no authenticator app to ask
    }

    [Fact]
    public async Task WithTheWindowAtZero_NoOneIsAsked()
    {
        var (b, _) = await SignedInWithTwoFactorAsync();
        await UpdateSettingsAsync(A, s => s.TwoFactor.StepUpMinutes = 0);
        (await b.GetAsync($"{Delete}?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheSecondStepOfTheSignIn_CountsAsACode()
    {
        await PrepareAppsAsync(selfUi: true);
        await CreateUserInAsync("appa");
        var key = await TurnOnTwoFactorAsync();

        var b = new Browser(Factory);
        var login = await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA });
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Location().ShouldStartWith("/Account/TwoFactor");   // the sign-in itself asks for the code (this existed before)

        var page = await (await b.GetAsync(login.Location()!)).Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"TwoFactorToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        token.ShouldNotBeNullOrEmpty();
        page.ShouldContain("data-a2-digits", Case.Sensitive);   // the code box takes digits only, at most 10 of them
        page.ShouldContain("pattern=\"[0-9 ]*\"", Case.Sensitive);
        page.ShouldContain("maxlength=\"10\"", Case.Sensitive);
        var second = await b.PostPageAsync(login.Location()!, new Dictionary<string, string> { ["TwoFactorToken"] = token, ["Input.Code"] = Totp.Now(key) });
        second.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await b.GetAsync($"{Delete}?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);   // just gave a code: not asked again at once
    }

    [Fact]
    public async Task ChangingADetail_KeepsTheTimeOfTheLastCode()
    {
        var (b, key) = await SignedInWithTwoFactorAsync();
        var done = await b.PostPageAsync(Verify, Form(Totp.Now(key)));
        done.StatusCode.ShouldBe(HttpStatusCode.OK);

        // a page that signs the session in again (the user name changes) must not lose the code
        var change = await b.PostPageAsync("/Account/ChangeUserName?ClientId=appa", new Dictionary<string, string> { ["Input.NewUserName"] = "sara2", ["Input.CurrentPassword"] = PassA });
        change.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.GetAsync($"{Delete}?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
