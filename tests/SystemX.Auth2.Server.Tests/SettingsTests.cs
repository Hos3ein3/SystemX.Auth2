using System.Reflection;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Tests;

public class ProjectSettingsValidatorTests
{
    private static IReadOnlyList<SettingsError> Check(Action<ProjectSettings> change)
    {
        var s = new ProjectSettings();
        change(s);
        return ProjectSettingsValidator.Validate(s);
    }

    [Fact]
    public void Validate_Defaults_HaveNoErrors() => ProjectSettingsValidator.Validate(new ProjectSettings()).ShouldBeEmpty();

    [Fact]
    public void Validate_MaxFailedAttemptsBelowOne_IsRejected() =>
        Check(s => s.Lockout.MaxFailedAttempts = 0).ShouldContain(e => e.Path == "Lockout.MaxFailedAttempts");

    [Fact]
    public void Validate_PasswordMinLengthBelowSix_IsRejected() =>
        Check(s => s.Password.MinLength = 5).ShouldContain(e => e.Path == "Password.MinLength");

    [Fact]
    public void Validate_PasswordMaxBelowMin_IsRejected() =>
        Check(s => s.Password.MaxLength = 8).ShouldContain(e => e.Path == "Password.MaxLength");

    private static List<SettingsError> CheckUris(params string[] uris)
    {
        var errors = new List<SettingsError>();
        ProjectSettingsValidator.CheckUris("RedirectUris", uris, (p, m) => errors.Add(new SettingsError(p, m)));
        return errors;
    }

    [Theory]
    [InlineData("https://*.example.com/cb")]
    [InlineData("https://example.com/*")]
    public void Validate_WildcardRedirectUri_IsRejected(string uri) =>
        CheckUris(uri).ShouldContain(e => e.Path == "RedirectUris");

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative")]
    [InlineData("https://example.com/cb#frag")]
    public void Validate_BadRedirectUri_IsRejected(string uri) =>
        CheckUris(uri).ShouldNotBeEmpty();

    [Fact]
    public void Validate_ExactHttpsRedirectUri_IsAccepted() =>
        CheckUris("https://app.example.com/signin-oidc").ShouldBeEmpty();

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
    public void Validate_CorsOriginWithWildcard_IsRejected()
    {
        var errors = new List<SettingsError>();
        ClientApplicationSettingsValidator.Validate(new ClientApplicationSettings { AllowedCorsOrigins = ["https://*.example.com"] }, (p, m) => errors.Add(new SettingsError(p, m)));
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Validate_RedirectAfterLoginProtocolRelative_IsRejected() =>
        Check(s => s.Redirects.RedirectAfterLogin = "//evil.com").ShouldContain(e => e.Path == "Redirects.RedirectAfterLogin");

    [Fact]
    public void Validate_RedirectAfterLoginLocalPath_IsAccepted() =>
        Check(s => s.Redirects.RedirectAfterLogin = "/dashboard").ShouldBeEmpty();

    [Fact]
    public void Validate_EnabledGenericOidcWithoutAuthority_IsRejected() =>
        Check(s => s.ExternalProviders.Add(new OidcProviderSettings { Name = "x", Type = ExternalProviderType.GenericOidc, ClientId = "c", ClientSecretProtected = "s", Enabled = true }))
            .ShouldContain(e => e.Path == "ExternalProviders[0].Authority");

    [Fact]
    public void Validate_ProviderNames_MustBeUniqueAndSafe()
    {
        var errors = Check(s =>
        {
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "a", Type = ExternalProviderType.Google });
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "a", Type = ExternalProviderType.GitHub });
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "Bad Name", Type = ExternalProviderType.GitHub });
        });
        errors.ShouldContain(e => e.Path == "ExternalProviders[1].Name");
        errors.ShouldContain(e => e.Path == "ExternalProviders[2].Name");
    }

    [Fact]
    public void Validate_MoreThanTwentyProviders_IsRejected() =>
        Check(s => { for (var i = 0; i < 21; i++) s.ExternalProviders.Add(new OidcProviderSettings { Name = "p" + i, Type = ExternalProviderType.Google }); })
            .ShouldContain(e => e.Path == "ExternalProviders");

    [Fact]
    public void Validate_EmailDomain_BelongsToOneProvider() =>
        Check(s =>
        {
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "a", Type = ExternalProviderType.Google, EmailDomains = ["corp.com"] });
            s.ExternalProviders.Add(new OidcProviderSettings { Name = "b", Type = ExternalProviderType.GitHub, EmailDomains = ["CORP.com"] });
        }).ShouldContain(e => e.Path == "ExternalProviders[1].EmailDomains");

    [Fact]
    public void Validate_LdapFilterNeedsPlaceholderOnce_AndBindPassword()
    {
        var errors = Check(s => s.ExternalProviders.Add(new LdapProviderSettings
        { Name = "d", Type = ExternalProviderType.Ldap, Enabled = true, Host = "h", BindDn = "cn=x", UserSearchBase = "dc=a", UserSearchFilter = "(uid={0})(cn={0})" }));
        errors.ShouldContain(e => e.Path == "ExternalProviders[0].UserSearchFilter");
        errors.ShouldContain(e => e.Path == "ExternalProviders[0].BindPassword");
    }

    [Fact]
    public void Validate_SamlWithoutSignedAssertionOrResponse_IsRejected() =>
        Check(s => s.ExternalProviders.Add(new SamlProviderSettings
        { Name = "s", Type = ExternalProviderType.Saml, Enabled = true, IdpMetadataUrl = "https://idp.test/m", WantAssertionsSigned = false }))
            .ShouldContain(e => e.Path == "ExternalProviders[0].WantAssertionsSigned");

    [Fact]
    public void Validate_HttpAuthority_IsRejected() =>
        Check(s => s.ExternalProviders.Add(new OidcProviderSettings { Name = "o", Type = ExternalProviderType.GenericOidc, Enabled = true, ClientId = "c", ClientSecretProtected = "s", Authority = "http://idp.test" }))
            .ShouldContain(e => e.Path == "ExternalProviders[0].Authority");

    [Fact]
    public void Validate_BadPasskeyLimits_AreRejected() =>
        Check(s => { s.Passkeys.MaxPasskeysPerUser = 0; s.Passkeys.PasskeyUserVerification = "no"; })
            .Select(e => e.Path).ShouldBe(["Passkeys.PasskeyUserVerification", "Passkeys.MaxPasskeysPerUser"], ignoreOrder: true);

    [Fact]
    public void ValidateOrThrow_BadSettings_ThrowsWithErrors()
    {
        var s = new ProjectSettings();
        s.Lockout.MaxFailedAttempts = 0;
        Should.Throw<SettingsValidationException>(() => ProjectSettingsValidator.ValidateOrThrow(s)).Errors.ShouldNotBeEmpty();
    }
}

public class ProjectSettingsModelTests
{
    private static IEnumerable<PropertyInfo> LeafProperties(Type type)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var t = p.PropertyType;
            var element = t.IsGenericType ? t.GetGenericArguments()[0] : t;
            if (element == typeof(ExternalProviderSettings))
            {
                foreach (var kind in new[] { typeof(ExternalProviderSettings), typeof(OidcProviderSettings), typeof(SamlProviderSettings), typeof(LdapProviderSettings) })
                    foreach (var inner in kind.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) yield return inner;
            }
            else if (t.Namespace == typeof(ProjectSettings).Namespace && t.IsClass && !t.IsGenericType)
                foreach (var inner in LeafProperties(t)) yield return inner;
            else yield return p;
        }
    }

    [Fact]
    public void EveryLeafSetting_HasEditabilityAttribute()
    {
        var missing = LeafProperties(typeof(ProjectSettings))
            .Where(p => p.GetCustomAttribute<SettingEditabilityAttribute>() is null)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}").ToList();
        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Json_RoundTrip_PreservesValuesAndUsesStringEnums()
    {
        var s = new ProjectSettings();
        s.Password.MinLength = 14;
        s.Methods.AllowedLoginMethods = [LoginMethod.PhoneOtp];
        s.ExternalProviders.Add(new OidcProviderSettings { Name = "g", Type = ExternalProviderType.Google, ClientId = "id", ClientSecretProtected = "enc", HostedDomain = "corp.com" });
        s.ExternalProviders.Add(new SamlProviderSettings { Name = "s", Type = ExternalProviderType.Saml, IdpMetadataUrl = "https://idp.test/m", AttributeMappings = { ["mail"] = "email" } });
        s.ExternalProviders.Add(new LdapProviderSettings { Name = "d", Type = ExternalProviderType.Ldap, Host = "ldap.test", Mode = LdapMode.LdapThenLocal });
        s.Passkeys.AllowPasskeys = true;
        var json = SettingsJson.Serialize(s);
        json.ShouldContain("\"PhoneOtp\"");
        json.ShouldContain("\"schemaVersion\":1");
        var back = SettingsJson.Deserialize(json);
        back.Password.MinLength.ShouldBe(14);
        back.Methods.AllowedLoginMethods.ShouldBe([LoginMethod.PhoneOtp]);
        back.ExternalProviders.Count.ShouldBe(3);
        back.ExternalProviders[0].ShouldBeOfType<OidcProviderSettings>().HostedDomain.ShouldBe("corp.com");
        back.ExternalProviders[1].ShouldBeOfType<SamlProviderSettings>().AttributeMappings["mail"].ShouldBe("email");
        back.ExternalProviders[2].ShouldBeOfType<LdapProviderSettings>().Mode.ShouldBe(LdapMode.LdapThenLocal);
        back.Passkeys.AllowPasskeys.ShouldBeTrue();
        json.ShouldContain("\"kind\":\"saml\"");
    }

    [Fact]
    public void Json_EmptyOrNull_GivesDefaults()
    {
        SettingsJson.Deserialize(null).SchemaVersion.ShouldBe(ProjectSettings.CurrentSchemaVersion);
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
            var section = typeof(ProjectSettings).GetProperty(path)!.PropertyType;
            LeafProperties(section).ShouldAllBe(p => p.GetCustomAttribute<SettingEditabilityAttribute>()!.Editor == SettingEditor.GlobalOnly);
        }
        typeof(ClientApplicationSettings).GetProperty(nameof(ClientApplicationSettings.AllowSelfUiLogin))!
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
        ProjectKeyRules.IsValidForNewProject(key).ShouldBe(expected);

    [Fact]
    public void IsValidForNewApplication_GlobalKey_IsReserved()
    {
        ProjectKeyRules.IsValidShape("_global").ShouldBeTrue();
        ProjectKeyRules.IsValidForNewProject("_global").ShouldBeFalse();
    }

    [Fact]
    public void IsValidShape_TooLong_IsFalse() => ProjectKeyRules.IsValidShape(new string('a', 65)).ShouldBeFalse();

    [Fact]
    public void IsValidShape_Null_IsFalse() => ProjectKeyRules.IsValidShape(null).ShouldBeFalse();
}
