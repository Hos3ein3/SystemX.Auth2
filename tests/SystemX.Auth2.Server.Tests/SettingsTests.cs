using System.Reflection;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Tests;

public class ApplicationSettingsValidatorTests
{
    private static IReadOnlyList<SettingsError> Check(Action<ApplicationSettings> change)
    {
        var s = new ApplicationSettings();
        change(s);
        return ApplicationSettingsValidator.Validate(s);
    }

    [Fact]
    public void Validate_Defaults_HaveNoErrors() => ApplicationSettingsValidator.Validate(new ApplicationSettings()).ShouldBeEmpty();

    [Fact]
    public void Validate_MaxFailedAttemptsBelowOne_IsRejected() =>
        Check(s => s.Lockout.MaxFailedAttempts = 0).ShouldContain(e => e.Path == "Lockout.MaxFailedAttempts");

    [Fact]
    public void Validate_PasswordMinLengthBelowSix_IsRejected() =>
        Check(s => s.Password.MinLength = 5).ShouldContain(e => e.Path == "Password.MinLength");

    [Fact]
    public void Validate_PasswordMaxBelowMin_IsRejected() =>
        Check(s => s.Password.MaxLength = 8).ShouldContain(e => e.Path == "Password.MaxLength");

    [Theory]
    [InlineData("https://*.example.com/cb")]
    [InlineData("https://example.com/*")]
    public void Validate_WildcardRedirectUri_IsRejected(string uri) =>
        Check(s => s.Redirects.AllowedRedirectUris.Add(uri)).ShouldContain(e => e.Path == "Redirects.AllowedRedirectUris");

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative")]
    [InlineData("https://example.com/cb#frag")]
    public void Validate_BadRedirectUri_IsRejected(string uri) =>
        Check(s => s.Redirects.AllowedPostLogoutRedirectUris.Add(uri)).ShouldNotBeEmpty();

    [Fact]
    public void Validate_ExactHttpsRedirectUri_IsAccepted() =>
        Check(s => s.Redirects.AllowedRedirectUris.Add("https://app.example.com/signin-oidc")).ShouldBeEmpty();

    [Fact]
    public void Validate_InvalidRegex_IsRejected() =>
        Check(s => s.Phone.PhoneNumberRegex = "([unclosed").ShouldContain(e => e.Path == "Phone.PhoneNumberRegex");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5000)]
    public void Validate_RegexTimeoutOutOfRange_IsRejected(int ms) =>
        Check(s => s.Phone.RegexTimeoutMs = ms).ShouldContain(e => e.Path == "Phone.RegexTimeoutMs");

    [Fact]
    public void Validate_InvalidUserNameRegex_IsRejected() =>
        Check(s => s.UserName.AllowedCharacters = "[").ShouldContain(e => e.Path == "UserName.AllowedCharacters");

    [Fact]
    public void Validate_NoLoginMethod_IsRejected() =>
        Check(s => s.Methods.AllowedLoginMethods.Clear()).ShouldContain(e => e.Path == "Methods.AllowedLoginMethods");

    [Fact]
    public void Validate_DefaultCultureNotSupported_IsRejected() =>
        Check(s => s.Localization.DefaultCulture = "fa").ShouldContain(e => e.Path == "Localization.DefaultCulture");

    [Fact]
    public void Validate_CorsOriginWithWildcard_IsRejected() =>
        Check(s => s.Clients.AllowedCorsOrigins.Add("https://*.example.com")).ShouldNotBeEmpty();

    [Fact]
    public void Validate_RedirectAfterLoginProtocolRelative_IsRejected() =>
        Check(s => s.Redirects.RedirectAfterLogin = "//evil.com").ShouldContain(e => e.Path == "Redirects.RedirectAfterLogin");

    [Fact]
    public void Validate_RedirectAfterLoginLocalPath_IsAccepted() =>
        Check(s => s.Redirects.RedirectAfterLogin = "/dashboard").ShouldBeEmpty();

    [Fact]
    public void Validate_EnabledGenericOidcWithoutAuthority_IsRejected() =>
        Check(s => s.ExternalProviders.Add(new ExternalProviderSettings { Name = "x", Type = ExternalProviderType.GenericOidc, ClientId = "c", Enabled = true }))
            .ShouldContain(e => e.Path == "ExternalProviders[0].Authority");

    [Fact]
    public void ValidateOrThrow_BadSettings_ThrowsWithErrors()
    {
        var s = new ApplicationSettings();
        s.Lockout.MaxFailedAttempts = 0;
        Should.Throw<SettingsValidationException>(() => ApplicationSettingsValidator.ValidateOrThrow(s)).Errors.ShouldNotBeEmpty();
    }
}

public class ApplicationSettingsModelTests
{
    private static IEnumerable<PropertyInfo> LeafProperties(Type type)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var t = p.PropertyType;
            var element = t.IsGenericType ? t.GetGenericArguments()[0] : t;
            if (element == typeof(ExternalProviderSettings))
                foreach (var inner in LeafProperties(element)) yield return inner;
            else if (t.Namespace == typeof(ApplicationSettings).Namespace && t.IsClass && !t.IsGenericType)
                foreach (var inner in LeafProperties(t)) yield return inner;
            else yield return p;
        }
    }

    [Fact]
    public void EveryLeafSetting_HasEditabilityAttribute()
    {
        var missing = LeafProperties(typeof(ApplicationSettings))
            .Where(p => p.GetCustomAttribute<SettingEditabilityAttribute>() is null)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}").ToList();
        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Json_RoundTrip_PreservesValuesAndUsesStringEnums()
    {
        var s = new ApplicationSettings();
        s.Password.MinLength = 14;
        s.Methods.AllowedLoginMethods = [LoginMethod.PhoneOtp];
        s.ExternalProviders.Add(new ExternalProviderSettings { Name = "g", Type = ExternalProviderType.Google, ClientId = "id" });
        var json = SettingsJson.Serialize(s);
        json.ShouldContain("\"PhoneOtp\"");
        json.ShouldContain("\"schemaVersion\":1");
        var back = SettingsJson.Deserialize(json);
        back.Password.MinLength.ShouldBe(14);
        back.Methods.AllowedLoginMethods.ShouldBe([LoginMethod.PhoneOtp]);
        back.ExternalProviders.Single().Type.ShouldBe(ExternalProviderType.Google);
    }

    [Fact]
    public void Json_EmptyOrNull_GivesDefaults()
    {
        SettingsJson.Deserialize(null).SchemaVersion.ShouldBe(ApplicationSettings.CurrentSchemaVersion);
        SettingsJson.Deserialize("").Password.MinLength.ShouldBe(10);
    }

    [Fact]
    public void Json_MissingSections_KeepDefaults() =>
        SettingsJson.Deserialize("{\"schemaVersion\":1}").Lockout.MaxFailedAttempts.ShouldBe(5);

    [Fact]
    public void SecuritySensitiveSettings_AreGlobalOnly()
    {
        foreach (var path in new[] { "Lockout", "Password", "TwoFactor", "Sessions" })
        {
            var section = typeof(ApplicationSettings).GetProperty(path)!.PropertyType;
            LeafProperties(section).ShouldAllBe(p => p.GetCustomAttribute<SettingEditabilityAttribute>()!.Editor == SettingEditor.GlobalOnly);
        }
        typeof(ClientSettings).GetProperty(nameof(ClientSettings.AllowSelfUiLogin))!
            .GetCustomAttribute<SettingEditabilityAttribute>()!.Editor.ShouldBe(SettingEditor.GlobalOnly);
    }
}

public class ProjectKeyRulesTests
{
    [Theory]
    [InlineData("papertrade", true)]
    [InlineData("a-b_c1", true)]
    [InlineData("ab", false)]
    [InlineData("UPPER", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void IsValidForNewApplication_Cases(string key, bool expected) =>
        ProjectKeyRules.IsValidForNewApplication(key).ShouldBe(expected);

    [Fact]
    public void IsValidForNewApplication_GlobalKey_IsReserved()
    {
        ProjectKeyRules.IsValidShape("_global").ShouldBeTrue();
        ProjectKeyRules.IsValidForNewApplication("_global").ShouldBeFalse();
    }

    [Fact]
    public void IsValidShape_TooLong_IsFalse() => ProjectKeyRules.IsValidShape(new string('a', 65)).ShouldBeFalse();

    [Fact]
    public void IsValidShape_Null_IsFalse() => ProjectKeyRules.IsValidShape(null).ShouldBeFalse();
}
