using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Localization;
using SystemX.Auth2.Server.Pages.Account;
using SystemX.Auth2.Server.Resources;

namespace SystemX.Auth2.Server.Tests;

/// <summary>The rules the pages give to the browser come from the saved settings (login methods, user name, email, phone and password rules).</summary>
public class AccountFormRulesTests
{
    private static readonly ServiceProvider Provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();

    private static AccountFormRules Rules(Action<ProjectSettings>? change = null)
    {
        var settings = new ProjectSettings();
        change?.Invoke(settings);
        return new AccountFormRules(settings, new Auth2Localizer(Provider.GetRequiredService<IStringLocalizer<Errors>>(), Provider.GetRequiredService<IStringLocalizer<Validation>>()));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData(new[] { LoginMethod.UserNamePassword }, IdentifierKinds.UserName, "login.identifier_username", "text")]
    [InlineData(new[] { LoginMethod.EmailPassword }, IdentifierKinds.Email, "login.identifier_email", "email")]
    [InlineData(new[] { LoginMethod.PhonePassword }, IdentifierKinds.Phone, "login.identifier_phone", "tel")]
    [InlineData(new[] { LoginMethod.UserNamePassword, LoginMethod.EmailPassword }, IdentifierKinds.UserName | IdentifierKinds.Email, "login.username_or_email", "text")]
    [InlineData(new[] { LoginMethod.EmailPassword, LoginMethod.PhonePassword }, IdentifierKinds.Email | IdentifierKinds.Phone, "login.identifier_email_phone", "text")]
    [InlineData(new[] { LoginMethod.UserNamePassword, LoginMethod.PhonePassword }, IdentifierKinds.UserName | IdentifierKinds.Phone, "login.identifier_username_phone", "text")]
    [InlineData(new[] { LoginMethod.UserNamePassword, LoginMethod.EmailPassword, LoginMethod.PhonePassword }, IdentifierKinds.UserName | IdentifierKinds.Email | IdentifierKinds.Phone, "login.identifier_all", "text")]
    public void TheLoginInput_FollowsTheLoginMethods(LoginMethod[] methods, IdentifierKinds kinds, string labelKey, string type)
    {
        var rules = Rules(s => s.Methods.AllowedLoginMethods = [.. methods]);
        rules.LoginKinds().ShouldBe(kinds);
        var field = AccountFormRules.FieldFor(rules.LoginKinds());
        (field.LabelKey, field.Type).ShouldBe((labelKey, type));
    }

    [Fact]
    public void WithoutAnyPasswordMethod_TheLoginInputKeepsUserNameAndEmail_AndOtpCountsForLookups()
    {
        var rules = Rules(s => s.Methods.AllowedLoginMethods = [LoginMethod.PhoneOtp]);
        rules.LoginKinds().ShouldBe(IdentifierKinds.UserName | IdentifierKinds.Email);   // the password form still needs something to ask
        rules.LookupKinds().ShouldBe(IdentifierKinds.Phone);                              // forgot password / join can find a person by the phone of the code
        Rules(s => s.Methods.AllowedLoginMethods = [LoginMethod.Passkey]).LoginKinds().ShouldBe(IdentifierKinds.UserName | IdentifierKinds.Email);
    }

    [Fact]
    public void ThePasswordRule_HasOneCheckForEveryRuleThatIsOn()
    {
        var rule = Parse(Rules(s => { s.Password.MinLength = 12; s.Password.MaxLength = 40; s.Password.RequireDigit = false; s.Password.RequireUpper = true; s.Password.RequireNonAlphanumeric = true; s.Password.UniqueChars = 5; })
            .NewPassword("Password"));
        rule.GetProperty("min").GetInt32().ShouldBe(12);
        rule.GetProperty("max").GetInt32().ShouldBe(40);
        rule.TryGetProperty("digit", out _).ShouldBeFalse();
        rule.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ShouldBe(["length", "lower", "upper", "symbol", "unique"]);
        rule.GetProperty("checks")[0].GetProperty("msg").GetString()!.ShouldContain("12");
    }

    [Fact]
    public void TheLoginPassword_HasTheSameRulesAsAChosenPassword_WithoutTheList()
    {
        var rules = Rules(s => { s.Password.MinLength = 9; s.Password.RequireUpper = true; });
        var login = Parse(rules.LoginPassword("Password"));
        var chosen = Parse(rules.NewPassword("Password"));
        login.GetProperty("min").GetInt32().ShouldBe(chosen.GetProperty("min").GetInt32());
        login.GetProperty("checks").GetArrayLength().ShouldBe(chosen.GetProperty("checks").GetArrayLength());
        login.GetProperty("hideList").GetBoolean().ShouldBeTrue();
        chosen.TryGetProperty("hideList", out _).ShouldBeFalse();
    }

    [Fact]
    public void TheUserNameRule_CarriesLengthAndPattern_AndRequiredOnlyWhenAsked()
    {
        var rules = Rules(s => { s.UserName.MinLength = 4; s.UserName.MaxLength = 12; s.UserName.AllowedCharacters = "^[a-z]+$"; });
        var rule = Parse(rules.UserName("User name", required: true));
        (rule.GetProperty("min").GetInt32(), rule.GetProperty("max").GetInt32(), rule.GetProperty("pattern").GetString()).ShouldBe((4, 12, "^[a-z]+$"));
        rule.GetProperty("required").GetString()!.ShouldContain("User name");
        Parse(rules.UserName("User name", required: false)).TryGetProperty("required", out _).ShouldBeFalse();
    }

    [Fact]
    public void TheEmailRule_CarriesTheDomainLists_AndThePhoneRuleTheRegex()
    {
        var rules = Rules(s => { s.Email.AllowedDomains = ["a.com"]; s.Email.BlockedDomains = ["b.com"]; s.Phone.PhoneNumberRegex = @"^09\d{9}$"; });
        var email = Parse(rules.Email("Email", true));
        email.GetProperty("allow")[0].GetString().ShouldBe("a.com");
        email.GetProperty("block")[0].GetString().ShouldBe("b.com");
        Parse(rules.Phone("Phone", true)).GetProperty("phone").GetString().ShouldBe(@"^09\d{9}$");
        Parse(Rules().Email("Email", true)).TryGetProperty("allow", out _).ShouldBeFalse();   // no lists: nothing to check
    }

    [Fact]
    public void TheAuthenticatorCodeRule_NeedsSixToEightDigits_SpacesAllowed()
    {
        var rule = Parse(Rules().TotpCode("Authenticator code"));
        rule.GetProperty("required").GetString()!.ShouldContain("Authenticator code");
        var pattern = new System.Text.RegularExpressions.Regex(rule.GetProperty("pattern").GetString()!);
        foreach (var good in new[] { "123456", "123 456", "12345678" }) pattern.IsMatch(good).ShouldBeTrue(good);
        foreach (var bad in new[] { "12345", "abcdef", "12-3456", "" }) pattern.IsMatch(bad).ShouldBeFalse(bad);
    }

    [Fact]
    public void TheConfirmPasswordRule_PointsAtThePasswordInput() =>
        Parse(Rules().ConfirmPassword("Confirm", "Input_NewPassword")).GetProperty("match").GetString().ShouldBe("Input_NewPassword");

    [Fact]
    public void TheMessages_AreInThePageLanguage()
    {
        var fa = new System.Globalization.CultureInfo("fa");
        var old = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = fa;
            var persian = Parse(Rules().ConfirmPassword("x", "Input_Password")).GetProperty("matchMsg").GetString();
            persian.ShouldBe("رمز عبور و تکرار آن یکسان نیستند.");
        }
        finally { (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture) = old; }
    }
}
