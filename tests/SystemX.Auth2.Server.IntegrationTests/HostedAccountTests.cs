using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.IntegrationTests;

public class HostedAccountTests : Phase6Base
{
    private const string Email = "sara@example.com";

    private static Dictionary<string, string> Form(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private static string PathAndQuery(string link) => new Uri(link).PathAndQuery;

    // ---------------------------------------------------------------- register

    [Fact]
    public async Task Register_Page_CreatesTheUser_SendsTheMail_AndGoesToLoginKeepingTheReturnUrl()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        var r = await b.PostPageAsync($"/Account/Register?ClientId=appa&ReturnUrl={Uri.EscapeDataString(Redirect)}",
            Form(("Input.Email", Email), ("Input.UserName", "sara"), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldStartWith("/Account/Login");
        Uri.UnescapeDataString(r.Location()!).ShouldContain($"ReturnUrl={Redirect}");
        r.Location()!.ShouldContain("ClientId=appa");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();   // LoginRequiredAfterRegister is on by default

        (await CountsAsync(Email)).ShouldBe((1, 1));
        var link = Factory.Messages.LastEmailTo(Email)!["link"];
        link.ShouldContain("ClientId=appa");
        Uri.UnescapeDataString(link).ShouldContain($"ReturnUrl={Redirect}");   // the return URL travels in the email link too
    }

    [Fact]
    public async Task Register_Page_NewAndExistingPerson_GetTheSameAnswer()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        var first = await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        var again = await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", "Other-Pass-12345!"), ("Input.ConfirmPassword", "Other-Pass-12345!")));
        again.StatusCode.ShouldBe(first.StatusCode);
        again.Location().ShouldBe(first.Location());
        (await CountsAsync(Email)).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Register_Page_WeakPassword_IsRefused_AndRegistrationDisabledIsForbidden()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", "short"), ("Input.ConfirmPassword", "short")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await UpdateSettingsAsync(A, s => s.Registration.RegistrationEnabled = false);
        (await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CountsAsync(Email)).ShouldBe((0, 0));
    }

    [Fact]
    public async Task Register_Page_WhenLoginIsNotRequired_SignsInAndUsesRedirectAfterRegister()
    {
        await PrepareAppsAsync(both: s => { s.Registration.LoginRequiredAfterRegister = false; s.Redirects.RedirectAfterRegister = "/welcome"; });
        var b = new Browser(Factory);
        var r = await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        r.Location().ShouldBe("/welcome");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();

        var b2 = new Browser(Factory);   // a valid return URL wins over RedirectAfterRegister
        var r2 = await b2.PostPageAsync("/Account/Register?ClientId=appa&ReturnUrl=%2Fpage-b", Form(("Input.Email", "two@example.com"), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        r2.Location().ShouldBe("/page-b");
    }

    [Fact]
    public async Task Register_Page_WhenConfirmationIsRequired_DoesNotSignIn()
    {
        await PrepareAppsAsync(both: s => { s.Registration.LoginRequiredAfterRegister = false; s.Email.RequireConfirmedEmail = true; });
        var b = new Browser(Factory);
        var r = await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        r.Location()!.ShouldStartWith("/Account/Login");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
    }

    // ---------------------------------------------------------------- confirm email

    [Fact]
    public async Task ConfirmEmail_Page_ConfirmsWithTheLinkFromTheMail_AndTheReturnUrlIsKept()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        await b.PostPageAsync($"/Account/Register?ClientId=appa&ReturnUrl={Uri.EscapeDataString(Redirect)}", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        (await UserAsync(Email)).EmailConfirmed.ShouldBeFalse();

        var link = PathAndQuery(Factory.Messages.LastEmailTo(Email)!["link"]);
        (await b.GetAsync(link)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UserAsync(Email)).EmailConfirmed.ShouldBeTrue();
        (await AuditAsync("email.confirmed")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConfirmEmail_Page_BadToken_IsRefused()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        var user = await UserAsync(Email);
        (await b.GetAsync($"/Account/ConfirmEmail?ClientId=appa&userId={user.Id}&token=garbage")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.GetAsync($"/Account/ConfirmEmail?ClientId=appa&userId={Guid.NewGuid()}&token=garbage")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.GetAsync("/Account/ConfirmEmail?ClientId=appa")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await UserAsync(Email)).EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmEmail_Page_ResendIsTheSameForEveryone()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        await b.PostPageAsync("/Account/Register?ClientId=appa", Form(("Input.Email", Email), ("Input.Password", PassA), ("Input.ConfirmPassword", PassA)));
        var before = Factory.Messages.CountTo(Email);
        (await b.PostPageAsync("/Account/ConfirmEmail?handler=Resend&ClientId=appa", Form(("Input.Login", Email)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.PostPageAsync("/Account/ConfirmEmail?handler=Resend&ClientId=appa", Form(("Input.Login", "nobody@example.com")))).StatusCode.ShouldBe(HttpStatusCode.OK);
        Factory.Messages.CountTo(Email).ShouldBe(before + 1);
        Factory.Messages.CountTo("nobody@example.com").ShouldBe(0);
    }

    // ---------------------------------------------------------------- forgot / reset

    [Fact]
    public async Task ForgotAndReset_Pages_WorkEndToEnd_AndKeepTheReturnUrl()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa");
        var b = new Browser(Factory);
        var known = await b.PostPageAsync($"/Account/ForgotPassword?ClientId=appa&ReturnUrl={Uri.EscapeDataString(Redirect)}", Form(("Input.Login", "sara@example.com")));
        var unknown = await b.PostPageAsync($"/Account/ForgotPassword?ClientId=appa&ReturnUrl={Uri.EscapeDataString(Redirect)}", Form(("Input.Login", "nobody@example.com")));
        unknown.StatusCode.ShouldBe(known.StatusCode);
        known.StatusCode.ShouldBe(HttpStatusCode.OK);

        var link = Factory.Messages.LastEmailTo("sara@example.com")!["link"];
        link.ShouldContain("ClientId=appa");
        Uri.UnescapeDataString(link).ShouldContain($"ReturnUrl={Redirect}");

        var uri = new Uri(link);
        var done = await b.PostPageAsync(uri.PathAndQuery, Form(("Input.NewPassword", "New-Pass-12345!")));
        done.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        done.Location()!.ShouldStartWith("/Account/Login");
        Uri.UnescapeDataString(done.Location()!).ShouldContain($"ReturnUrl={Redirect}");   // and back to the app after the next login
        (await UserTokensAsync("sara", "New-Pass-12345!")).Str("token_type").ShouldBe("Bearer");

        var again = await b.PostPageAsync(uri.PathAndQuery, Form(("Input.NewPassword", "Third-Pass-12345!")));
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the token works once
    }

    // ---------------------------------------------------------------- join

    [Fact]
    public async Task JoinProject_Page_NeedsTheProofCode_ThenAllowsLoginInTheSecondApplication()
    {
        await PrepareAppsAsync();
        var tokenA = await ClientTokenAsync("appa", "auth2.register");
        await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("sara", Email, null, PassA), tokenA);
        await ConfirmEmailAsync(Email);

        var b = new Browser(Factory);
        var send = await b.PostPageAsync("/Account/JoinProject?handler=Send&ClientId=appb", Form(("Input.Login", Email)));
        send.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sameForUnknown = await b.PostPageAsync("/Account/JoinProject?handler=Send&ClientId=appb", Form(("Input.Login", "nobody@example.com")));
        sameForUnknown.StatusCode.ShouldBe(send.StatusCode);
        Factory.Messages.CountTo("nobody@example.com").ShouldBe(0);
        var code = Factory.Messages.LastEmailTo(Email)!["code"];

        (await b.PostPageAsync("/Account/JoinProject?ClientId=appb", Form(("Input.Login", Email), ("Input.Code", code == "000000" ? "000001" : "000000"), ("Input.NewPassword", PassB))))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await HasRowAsync(Email, B)).ShouldBeFalse();

        var done = await b.PostPageAsync("/Account/JoinProject?ClientId=appb&ReturnUrl=%2Fpage-b", Form(("Input.Login", Email), ("Input.Code", code), ("Input.NewPassword", PassB)));
        done.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        done.Location()!.ShouldContain("/Account/Login");
        (await HasRowAsync(Email, B)).ShouldBeTrue();
        (await UserTokensAsync("sara", PassB, "appb")).Str("token_type").ShouldBe("Bearer");
    }

    [Fact]
    public async Task JoinProject_Page_IsOffInSingleModeAndWhenJoinModeIsNotAutoJoin()
    {
        await PrepareAppsAsync();
        await UpdateSettingsAsync(B, s => s.Registration.JoinMode = JoinMode.InviteOnly);
        var b = new Browser(Factory);
        (await b.GetAsync("/Account/JoinProject?ClientId=appb")).StatusCode.ShouldBe(HttpStatusCode.OK);   // the page loads; JoinEnabled is false
        var tokenA = await ClientTokenAsync("appa", "auth2.register");
        await CallAsync(b, Auth2Routes.Register, new RegisterRequest("sara", Email, null, PassA), tokenA);
        await ConfirmEmailAsync(Email);
        var before = Factory.Messages.CountTo(Email);
        await b.PostPageAsync("/Account/JoinProject?handler=Send&ClientId=appb", Form(("Input.Login", Email)));
        Factory.Messages.CountTo(Email).ShouldBe(before);
    }

    // ---------------------------------------------------------------- phone page

    [Fact]
    public async Task ConfirmPhone_Page_SendsAndConfirms()
    {
        await PrepareAppsAsync(both: s => s.Methods.AllowedRegisterMethods.Add(Contracts.Enums.LoginMethod.PhonePassword));
        var token = await ClientTokenAsync("appa", "auth2.register");
        await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest("bob", null, "+31612345678", PassA), token);
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/ConfirmPhone?handler=Send&ClientId=appa", Form(("Input.Login", "+31612345678")))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var code = Factory.Messages.LastTextTo("+31612345678")!["code"];
        (await b.PostPageAsync("/Account/ConfirmPhone?ClientId=appa", Form(("Input.Login", "+31612345678"), ("Input.Code", "999999")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync("/Account/ConfirmPhone?ClientId=appa", Form(("Input.Login", "+31612345678"), ("Input.Code", code)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UserAsync(Email: null, phone: "+31612345678")).PhoneNumberConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_WithClientId_PreservesClientIdInPasskeyAndRegisterLinks()
    {
        await PrepareAppsAsync(both: s =>
        {
            s.Methods.AllowedLoginMethods.Add(Contracts.Enums.LoginMethod.Passkey);
            s.Passkeys.AllowPasskeys = true;
        });

        var b = new Browser(Factory);
        var r = await b.GetAsync("/Account/Login?client_id=appa");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await r.Content.ReadAsStringAsync();

        html.ShouldContain("/Account/LoginPasskey?client_id=appa");
        html.ShouldNotContain("/Account/LoginPasskey?ReturnUrl=&ClientId=");
        html.ShouldContain("/Account/Register?client_id=appa");
        html.ShouldContain("/Account/ForgotPassword?client_id=appa");
    }

    [Fact]
    public async Task Pages_WithoutClientId_RenderDirectAuthenticationNotSupported()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);

        // GET /Account/Login without client_id
        var rLogin = await b.GetAsync("/Account/Login");
        rLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var htmlLogin = await rLogin.Content.ReadAsStringAsync();
        htmlLogin.ShouldContain("Direct Authentication Not Supported");
        htmlLogin.ShouldContain("This authentication service requires an application context");

        // GET /Account/LoginPasskey?ReturnUrl=&ClientId=
        var rPasskey = await b.GetAsync("/Account/LoginPasskey?ReturnUrl=&ClientId=");
        rPasskey.StatusCode.ShouldBe(HttpStatusCode.OK);
        var htmlPasskey = await rPasskey.Content.ReadAsStringAsync();
        htmlPasskey.ShouldContain("Direct Authentication Not Supported");
        htmlPasskey.ShouldNotContain("Continue with Passkey");

        // GET /Account/Register without client_id
        var rReg = await b.GetAsync("/Account/Register");
        rReg.StatusCode.ShouldBe(HttpStatusCode.OK);
        var htmlReg = await rReg.Content.ReadAsStringAsync();
        htmlReg.ShouldContain("Direct Authentication Not Supported");
    }

    private async Task<AppUser> UserAsync(string? Email, string phone)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return db.Users.AsNoTracking().Single(u => u.PhoneNumber == phone);
    }
}

public class NoSecretsInLogsTests : Phase6Base
{
    protected override void Customize(IServiceCollection services)
    {
        // The real development senders, with the option that would log bodies switched ON. In "Testing" it must still log nothing secret.
        services.Configure<Auth2ServerOptions>(o => o.Development.LogMessageBodies = true);
        services.AddScoped<IEmailSender, Infrastructure.Auth.LoggingEmailSender>();
        services.AddScoped<ISmsSender, Infrastructure.Auth.LoggingSmsSender>();
    }

    [Fact]
    public async Task RegisterResetAndOtp_NeverPutTokensCodesOrPasswordsInTheLog_OrTheAuditTrail()
    {
        await PrepareAppsAsync(both: s => { s.Methods.AllowedLoginMethods.Add(Contracts.Enums.LoginMethod.EmailOtp); });
        var token = await ClientTokenAsync("appa", "auth2.register auth2.login");
        var b = new Browser(Factory);
        await CallAsync(b, Auth2Routes.Register, new RegisterRequest("sara", "sara@example.com", null, "Very-Secret-Pass-1!"), token);
        await CallAsync(b, Auth2Routes.PasswordForgot, new ForgotPasswordRequest("sara@example.com"), token);
        await CallAsync(b, Auth2Routes.OtpRequest, new OtpRequest("sara@example.com"), token);
        await CallAsync(b, Auth2Routes.PasswordReset, new ResetPasswordRequest("sara@example.com", "some-token-value-12345", "Another-Secret-Pass-1!"), token);

        var logs = string.Join("\n", Factory.Logs.Entries.Select(e => e.Message));
        logs.ShouldNotContain("Very-Secret-Pass-1!");
        logs.ShouldNotContain("Another-Secret-Pass-1!");
        logs.ShouldNotContain("/Account/ConfirmEmail?userId");   // the link with the token
        logs.ShouldNotContain("/Account/ResetPassword?Login");
        logs.ShouldNotContain("some-token-value-12345");
        logs.ShouldContain("s***@example.com");                  // only the masked recipient

        var audit = string.Join("\n", (await AuditAsync("register.created")).Concat(await AuditAsync("password.reset_requested")).Select(a => a.DataJson ?? ""));
        audit.ShouldNotContain("Very-Secret-Pass-1!");
    }
}
