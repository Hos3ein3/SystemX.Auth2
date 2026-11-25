using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The sign-in and sign-up pages draw their inputs and rules from the settings saved in the database, not from fixed text.</summary>
public class FormRulesPageTests : Phase6Base
{
    private async Task<string> PageAsync(string path)
    {
        var response = await new Browser(Factory).GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The <c>data-a2-rule</c> of the input with this id, as JSON.</summary>
    private static JsonElement RuleOf(string html, string inputId)
    {
        var tag = Regex.Match(html, "<(?:input|div)[^>]*\\bid=\"" + inputId + "\"[^>]*>|<div[^>]*data-a2-rule=\"[^\"]*\"[^>]*><input[^>]*\\bid=\"" + inputId + "\"");
        tag.Success.ShouldBeTrue($"no input {inputId}");
        var rule = Regex.Match(tag.Value, "data-a2-rule=\"([^\"]*)\"");
        rule.Success.ShouldBeTrue($"input {inputId} has no rule");
        return JsonDocument.Parse(WebUtility.HtmlDecode(rule.Groups[1].Value)).RootElement.Clone();
    }

    private static string InputTag(string html, string inputId) => Regex.Match(html, "<input[^>]*\\bid=\"" + inputId + "\"[^>]*>").Value;

    [Fact]
    public async Task TheLoginField_FollowsTheLoginMethods()
    {
        await PrepareAppsAsync(both: s => s.Methods.AllowedLoginMethods = [LoginMethod.UserNamePassword, LoginMethod.EmailPassword]);
        var both = await PageAsync("/Account/Login?ClientId=appa");
        both.ShouldContain("Username or email");
        InputTag(both, "Input_Login").ShouldContain("type=\"text\"");

        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.EmailPassword]);
        var email = await PageAsync("/Account/Login?ClientId=appa");
        var tag = InputTag(email, "Input_Login");
        tag.ShouldContain("type=\"email\""); tag.ShouldContain("autocomplete=\"email\""); tag.ShouldContain("inputmode=\"email\"");
        var emailAny = RuleOf(email, "Input_Login").GetProperty("any");
        emailAny.TryGetProperty("email", out _).ShouldBeTrue();      // one kind only: its format is checked while typing
        emailAny.TryGetProperty("userName", out _).ShouldBeFalse();

        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.PhonePassword]);
        var phone = await PageAsync("/Account/Login?ClientId=appa");
        InputTag(phone, "Input_Login").ShouldContain("type=\"tel\"");
        RuleOf(phone, "Input_Login").GetProperty("any").GetProperty("phone").GetProperty("regex").GetString().ShouldBe(@"^\+[1-9]\d{6,14}$");   // the regex saved in the settings

        await UpdateSettingsAsync(A, s => s.Methods.AllowedLoginMethods = [LoginMethod.UserNamePassword, LoginMethod.EmailPassword, LoginMethod.PhonePassword]);
        (await PageAsync("/Account/Login?ClientId=appa")).ShouldContain("Username, email or phone number");
    }

    [Fact]
    public async Task TheLoginUserName_FollowsTheUserNameRulesOfTheProject()
    {
        await PrepareAppsAsync(both: s => { s.UserName.MinLength = 5; s.UserName.MaxLength = 30; s.UserName.AllowedCharacters = "^[a-z0-9]+$"; });
        var any = RuleOf(await PageAsync("/Account/Login?ClientId=appa"), "Input_Login").GetProperty("any");
        var user = any.GetProperty("userName");
        (user.GetProperty("min").GetInt32(), user.GetProperty("max").GetInt32(), user.GetProperty("pattern").GetString()).ShouldBe((5, 30, "^[a-z0-9]+$"));
        user.GetProperty("minMsg").GetString()!.ShouldContain("5");
        any.TryGetProperty("email", out _).ShouldBeTrue();   // user name or email: either may fit
    }

    [Fact]
    public async Task TheLoginFields_AreRequired_WithTheTextInThePageLanguage()
    {
        await PrepareAppsAsync();
        var html = await PageAsync("/Account/Login?ClientId=appa");
        RuleOf(html, "Input_Login").GetProperty("required").GetString()!.ShouldContain("is required");
        RuleOf(html, "Input_Password").GetProperty("required").GetString()!.ShouldContain("Password");
        var password = RuleOf(html, "Input_Password");
        password.GetProperty("max").GetInt32().ShouldBe(128);
        password.GetProperty("min").GetInt32().ShouldBe(10);          // the sign-in password follows the same saved rules
        password.GetProperty("checks").GetArrayLength().ShouldBeGreaterThan(1);
        password.GetProperty("hideList").GetBoolean().ShouldBeTrue();   // told one message at a time, without the list
    }

    [Fact]
    public async Task TheRegisterPassword_ListsTheRulesOfTheProject()
    {
        await PrepareAppsAsync(both: s => { s.Password.MinLength = 14; s.Password.RequireNonAlphanumeric = true; s.Password.RequireUpper = false; s.Password.UniqueChars = 6; });
        var rule = RuleOf(await PageAsync("/Account/Register?ClientId=appa"), "Input_Password");
        rule.GetProperty("min").GetInt32().ShouldBe(14);
        rule.GetProperty("symbol").GetBoolean().ShouldBeTrue();
        rule.TryGetProperty("upper", out _).ShouldBeFalse();
        rule.GetProperty("unique").GetInt32().ShouldBe(6);
        var ids = rule.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        ids.ShouldBe(["length", "digit", "lower", "symbol", "unique"]);
        rule.GetProperty("checks")[0].GetProperty("msg").GetString()!.ShouldContain("14");
    }

    [Fact]
    public async Task TheRegisterUserNameEmailAndPhone_UseTheSavedRules()
    {
        await PrepareAppsAsync(both: s =>
        {
            s.Methods.AllowedRegisterMethods = [LoginMethod.UserNamePassword, LoginMethod.EmailPassword, LoginMethod.PhonePassword];
            s.UserName.MinLength = 5; s.UserName.MaxLength = 20; s.UserName.AllowedCharacters = "^[a-z]+$";
            s.Email.AllowedDomains = ["example.com"]; s.Email.BlockedDomains = ["bad.example.com"];
            s.Phone.PhoneNumberRegex = @"^\+98\d{10}$";
        });
        var html = await PageAsync("/Account/Register?ClientId=appa");
        var name = RuleOf(html, "Input_UserName");
        (name.GetProperty("min").GetInt32(), name.GetProperty("max").GetInt32(), name.GetProperty("pattern").GetString()).ShouldBe((5, 20, "^[a-z]+$"));
        var email = RuleOf(html, "Input_Email");
        email.GetProperty("allow")[0].GetString().ShouldBe("example.com");
        email.GetProperty("block")[0].GetString().ShouldBe("bad.example.com");
        RuleOf(html, "Input_PhoneNumber").GetProperty("phone").GetString().ShouldBe(@"^\+98\d{10}$");
        // several ways to register: none is required alone (one is enough)
        InputTag(html, "Input_Email").ShouldNotContain("required");
        RuleOf(html, "Input_ConfirmPassword").GetProperty("match").GetString().ShouldBe("Input_Password");
    }

    [Fact]
    public async Task WhenOnlyOneWayToRegisterExists_ThatFieldIsRequired_AndTheOthersAreNotShown()
    {
        await PrepareAppsAsync(both: s => s.Methods.AllowedRegisterMethods = [LoginMethod.EmailPassword]);
        var html = await PageAsync("/Account/Register?ClientId=appa");
        InputTag(html, "Input_Email").ShouldContain("required");
        InputTag(html, "Input_UserName").ShouldBeEmpty();
        InputTag(html, "Input_PhoneNumber").ShouldBeEmpty();
        RuleOf(html, "Input_Email").TryGetProperty("required", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task TheForgotPasswordField_FollowsTheLoginMethods()
    {
        await PrepareAppsAsync(both: s => s.Methods.AllowedLoginMethods = [LoginMethod.PhonePassword]);
        var tag = InputTag(await PageAsync("/Account/ForgotPassword?ClientId=appa"), "Input_Login");
        tag.ShouldContain("type=\"tel\"");
    }
}
