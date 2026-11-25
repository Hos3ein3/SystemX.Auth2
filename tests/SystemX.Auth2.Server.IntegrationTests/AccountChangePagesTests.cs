using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The pages where a signed-in person changes their own password, user name, email address and phone number.</summary>
public class AccountChangePagesTests : Phase6Base
{
    private const string NewPass = "New-Pass-12345!";
    private static readonly string[] Pages = ["/Account/ChangePassword", "/Account/ChangeUserName", "/Account/ChangeEmailAddress", "/Account/ChangePhoneNumber"];

    private async Task<Browser> SignedInAsync(Action<TestEdit>? both = null)
    {
        await PrepareAppsAsync(selfUi: true, both);
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return b;
    }

    private async Task<AppUser> SaraAsync()
    {
        await using var scope = ScopeFor("appa");
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("sara"))!;
    }

    private static Dictionary<string, string> Form(params (string Key, string Value)[] fields) => fields.ToDictionary(f => f.Key, f => f.Value);

    [Theory]
    [InlineData("/Account/ChangePassword")]
    [InlineData("/Account/ChangeUserName")]
    [InlineData("/Account/ChangeEmailAddress")]
    [InlineData("/Account/ChangePhoneNumber")]
    public async Task EveryPage_NeedsASession_AndGoesToTheLoginPageOtherwise(string url)
    {
        await PrepareAppsAsync();
        var r = await new Browser(Factory).GetAsync(url + "?ClientId=appa");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldContain("/Account/Login?ReturnUrl=");
    }

    [Fact]
    public async Task EveryPage_Loads_WithASession()
    {
        var b = await SignedInAsync();
        foreach (var url in Pages) (await b.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
    }

    // ---- password ----

    [Fact]
    public async Task ChangePassword_NeedsTheCurrentPassword_AndKeepsTheSession()
    {
        var b = await SignedInAsync();
        (await b.PostPageAsync("/Account/ChangePassword", Form(("Input.CurrentPassword", "Wrong-Password-1"), ("Input.NewPassword", NewPass)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangePassword", Form(("Input.CurrentPassword", PassA), ("Input.NewPassword", "short")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await b.PostPageAsync("/Account/ChangePassword", Form(("Input.CurrentPassword", PassA), ("Input.NewPassword", NewPass)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UserTokensAsync("sara", NewPass)).Str("token_type").ShouldBe("Bearer");
        (await b.GetAsync("/Account/ChangePassword")).StatusCode.ShouldBe(HttpStatusCode.OK);   // the cookie session stays by default
    }

    [Fact]
    public async Task ChangePassword_WithSignOutAfterChangePassword_RemovesTheSession_AndGoesToTheLoginPage()
    {
        var b = await SignedInAsync(s => s.Project.Password.SignOutAfterChangePassword = true);
        var r = await b.PostPageAsync("/Account/ChangePassword", Form(("Input.CurrentPassword", PassA), ("Input.NewPassword", NewPass)));
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldContain("/Account/Login");
        r.Location()!.ShouldContain("ClientId=appa");

        (await b.GetAsync("/Account/ChangePassword?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.Redirect);   // signed out: back to the login page
        (await UserTokensAsync("sara", NewPass)).Str("token_type").ShouldBe("Bearer");   // the new password works
    }

    [Fact]
    public async Task AWrongPassword_OnTheChangePassword_CountsAsAFailedAttempt()
    {
        var b = await SignedInAsync();
        await b.PostPageAsync("/Account/ChangePassword", Form(("Input.CurrentPassword", "Wrong-Password-1"), ("Input.NewPassword", NewPass)));
        (await InAsync("appa", async m => await m.GetAccessFailedCountAsync((await m.FindByNameAsync("sara"))!))).ShouldBe(1);
    }

    // ---- user name ----

    [Fact]
    public async Task ChangeUserName_ChecksThePassword_TheRules_AndTheOtherNames_ThenChangesIt()
    {
        var b = await SignedInAsync();
        await CreateUserInAsync("appa", "taken");

        (await b.PostPageAsync("/Account/ChangeUserName", Form(("Input.NewUserName", "sara2"), ("Input.CurrentPassword", "Wrong-Password-1")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangeUserName", Form(("Input.NewUserName", "no spaces allowed!"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangeUserName", Form(("Input.NewUserName", "taken"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangeUserName", Form(("Input.NewUserName", "sara"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the same name
        (await SaraAsync()).UserName.ShouldBe("sara");

        (await b.PostPageAsync("/Account/ChangeUserName", Form(("Input.NewUserName", "sara2"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InAsync("appa", m => m.FindByNameAsync("sara2"))).ShouldNotBeNull();
        (await UserTokensAsync("sara2", PassA)).Str("token_type").ShouldBe("Bearer");
        (await b.GetAsync("/Account/ChangeUserName")).StatusCode.ShouldBe(HttpStatusCode.OK);   // the session continues with the new name
    }

    // ---- email ----

    private static string PathOf(IReadOnlyDictionary<string, string>? data) => new Uri(data!["link"]).PathAndQuery;

    [Fact]
    public async Task ChangeEmailAddress_SendsTheLinkToTheNewAddress_AndChangesOnlyWhenItIsOpened()
    {
        var b = await SignedInAsync();
        var form = Form(("Input.NewEmail", "sara.new@example.com"), ("Input.CurrentPassword", PassA));

        (await b.PostPageAsync("/Account/ChangeEmailAddress", Form(("Input.NewEmail", "sara.new@example.com"), ("Input.CurrentPassword", "Wrong-Password-1")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Factory.Messages.CountTo("sara.new@example.com").ShouldBe(0);

        (await b.PostPageAsync("/Account/ChangeEmailAddress", form)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = Factory.Messages.LastEmailTo("sara.new@example.com");
        data.ShouldNotBeNull();   // the mail goes to the NEW address
        Factory.Messages.CountTo("sara@example.com").ShouldBe(0);
        (await SaraAsync()).Email.ShouldBe("sara@example.com");   // nothing changed yet

        var path = PathOf(data);
        path.ShouldStartWith("/Account/ChangeEmailAddress?");
        (await b.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var sara = await SaraAsync();
        sara.Email.ShouldBe("sara.new@example.com");
        sara.EmailConfirmed.ShouldBeTrue();

        // The link works once.
        (await b.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeEmailAddress_RefusesATakenAddress_ABadOne_AndALinkForAnotherAddress()
    {
        var b = await SignedInAsync();
        await CreateUserInAsync("appa", "other");   // other@example.com

        (await b.PostPageAsync("/Account/ChangeEmailAddress", Form(("Input.NewEmail", "other@example.com"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangeEmailAddress", Form(("Input.NewEmail", "not-an-email"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangeEmailAddress", Form(("Input.NewEmail", "sara@example.com"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the same
        Factory.Messages.CountTo("other@example.com").ShouldBe(0);

        await b.PostPageAsync("/Account/ChangeEmailAddress", Form(("Input.NewEmail", "sara.new@example.com"), ("Input.CurrentPassword", PassA)));
        var path = PathOf(Factory.Messages.LastEmailTo("sara.new@example.com"));
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://x" + path).Query).ToDictionary(q => q.Key, q => (string?)q.Value.ToString());
        query["email"] = "evil@example.com";
        var tampered = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/Account/ChangeEmailAddress", query);
        (await b.GetAsync(tampered)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the token is for one address only
        (await SaraAsync()).Email.ShouldBe("sara@example.com");
    }

    // ---- phone ----

    [Fact]
    public async Task ChangePhoneNumber_SendsTheCodeToTheNewNumber_AndChangesOnlyWithTheRightCode()
    {
        var b = await SignedInAsync();
        const string number = "+15551234567";

        (await b.PostPageAsync("/Account/ChangePhoneNumber?handler=SendCode", Form(("Input.NewPhoneNumber", "abc"), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ChangePhoneNumber?handler=SendCode", Form(("Input.NewPhoneNumber", number), ("Input.CurrentPassword", "Wrong-Password-1")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Factory.Messages.CountTo(number).ShouldBe(0);

        (await b.PostPageAsync("/Account/ChangePhoneNumber?handler=SendCode", Form(("Input.NewPhoneNumber", number), ("Input.CurrentPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var code = Factory.Messages.LastTextTo(number)!["code"];   // the SMS goes to the NEW number
        (await SaraAsync()).PhoneNumber.ShouldBeNull();

        (await b.PostPageAsync("/Account/ChangePhoneNumber", Form(("Input.NewPhoneNumber", number), ("Input.Code", code == "000000" ? "111111" : "000000")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SaraAsync()).PhoneNumber.ShouldBeNull();

        // The code belongs to that number: it does not confirm another one.
        (await b.PostPageAsync("/Account/ChangePhoneNumber", Form(("Input.NewPhoneNumber", "+15559876543"), ("Input.Code", code)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await b.PostPageAsync("/Account/ChangePhoneNumber", Form(("Input.NewPhoneNumber", number), ("Input.Code", code)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var sara = await SaraAsync();
        sara.PhoneNumber.ShouldBe(number);
        sara.PhoneNumberConfirmed.ShouldBeTrue();

        // The code works once.
        (await b.PostPageAsync("/Account/ChangePhoneNumber", Form(("Input.NewPhoneNumber", number), ("Input.Code", code)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePhoneNumber_AskingForTheCodeAgainTooSoon_IsRefused()
    {
        var b = await SignedInAsync();
        var form = Form(("Input.NewPhoneNumber", "+15551234567"), ("Input.CurrentPassword", PassA));
        (await b.PostPageAsync("/Account/ChangePhoneNumber?handler=SendCode", form)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.PostPageAsync("/Account/ChangePhoneNumber?handler=SendCode", form)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        Factory.Messages.CountTo("+15551234567").ShouldBe(1);
    }
}
