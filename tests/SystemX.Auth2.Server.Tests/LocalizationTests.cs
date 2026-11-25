using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Infrastructure.Identity;
using SystemX.Auth2.Server.Infrastructure.Localization;
using SystemX.Auth2.Server.Resources;

namespace SystemX.Auth2.Server.Tests;

public class LocalizationTests
{
    private static readonly ServiceProvider Provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();

    private static Auth2Localizer Localizer() =>
        new(Provider.GetRequiredService<IStringLocalizer<Errors>>(), Provider.GetRequiredService<IStringLocalizer<Validation>>());

    private static T With<T>(string culture, Func<T> action)
    {
        var old = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return action();
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = old; }
    }

    [Fact]
    public void Error_EveryErrorCode_HasAnEnglishText()
    {
        var l = Localizer();
        var missing = Auth2ErrorCodes.All.Where(code => With("en", () => l.Error(code, 1, 2)) == code).ToList();
        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Error_EveryIdentityKey_HasAnEnglishText()
    {
        var describer = new Auth2IdentityErrorDescriber(Localizer());
        var errors = With("en", () => new[]
        {
            describer.DefaultError(), describer.ConcurrencyFailure(), describer.PasswordMismatch(), describer.InvalidToken(),
            describer.RecoveryCodeRedemptionFailed(), describer.LoginAlreadyAssociated(), describer.DuplicateUserName("x"),
            describer.DuplicateEmail("x"), describer.InvalidRoleName("x"), describer.DuplicateRoleName("x"),
            describer.UserAlreadyHasPassword(), describer.UserLockoutNotEnabled(), describer.UserAlreadyInRole("x"), describer.UserNotInRole("x"),
            describer.PasswordTooShort(10), describer.PasswordTooLong(5), describer.PasswordRequiresDigit(), describer.PasswordRequiresLower(),
            describer.PasswordRequiresUpper(), describer.PasswordRequiresNonAlphanumeric(), describer.PasswordRequiresUniqueChars(3),
            describer.PasswordBanned(), describer.InvalidUserName("x"), describer.InvalidEmail("x"), describer.EmailDomainNotAllowed("x"),
            describer.InvalidPhoneNumber(),
        });
        errors.ShouldAllBe(e => !string.IsNullOrWhiteSpace(e.Description) && !e.Description.StartsWith("identity.") && e.Description != e.Code);
    }

    [Fact]
    public void Error_Persian_UsesTranslation() =>
        With("fa", () => Localizer().Error(Auth2ErrorCodes.InvalidCredentials)).ShouldNotBe(With("en", () => Localizer().Error(Auth2ErrorCodes.InvalidCredentials)));

    [Fact]
    public void Error_MissingKeyInCulture_FallsBackToEnglish() =>
        With("nl", () => Localizer().Error("test_english_fallback")).ShouldBe(With("en", () => Localizer().Error("test_english_fallback")));

    [Fact]
    public void Error_UnknownCulture_FallsBackToEnglish() =>
        With("ja", () => Localizer().Error(Auth2ErrorCodes.LockedOut)).ShouldBe(With("en", () => Localizer().Error(Auth2ErrorCodes.LockedOut)));

    [Fact]
    public void Error_UnknownKey_ReturnsTheKey() => With("en", () => Localizer().Error("no_such_key")).ShouldBe("no_such_key");

    [Fact]
    public void Error_WithArguments_FormatsInEveryCulture()
    {
        With("en", () => Localizer().Error(Auth2ErrorCodes.PasswordTooShort, 12)).ShouldContain("12");
        With("fa", () => Localizer().Error(Auth2ErrorCodes.PasswordTooShort, 12)).ShouldContain("12");
        With("it", () => Localizer().Error(Auth2ErrorCodes.PasswordTooShort, 12)).ShouldContain("12");
    }

    [Fact]
    public void Validation_Key_Works() => With("en", () => Localizer().Validation("required", "Name")).ShouldBe("Name is required.");

    [Theory]
    [InlineData("Errors")]
    [InlineData("Validation")]
    [InlineData("Pages")]
    [InlineData("Emails")]
    public void Resx_TranslationsHaveNoKeysThatEnglishLacks(string name)
    {
        var dir = Path.Combine(Path.GetDirectoryName(typeof(ArchitectureTests).Assembly.Location)!, "..", "..", "..", "..", "..", "src", "SystemX.Auth2.Server", "Resources");
        static HashSet<string> Keys(string file) => XDocument.Load(file).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
        var english = Keys(Path.Combine(dir, name + ".resx"));
        foreach (var file in Directory.GetFiles(dir, name + ".*.resx"))
            Keys(file).Except(english).ShouldBeEmpty($"{Path.GetFileName(file)} has keys that {name}.resx does not have");
    }
}

public class CultureResolverTests
{
    private static string Resolve(string? ui = null, string? user = null, string? cookie = null, string[]? accept = null, string? appDefault = null, string[]? supported = null) =>
        Auth2CultureResolver.Resolve(ui, user, cookie, accept ?? [], appDefault, supported);

    [Fact]
    public void Resolve_NothingGiven_IsEnglish() => Resolve().ShouldBe("en");

    [Fact]
    public void Resolve_UiLocalesWinsOverEverything() =>
        Resolve(ui: "nl", user: "fa", cookie: "it", accept: ["fa"], appDefault: "it").ShouldBe("nl");

    [Fact]
    public void Resolve_UserCultureComesBeforeCookie() => Resolve(user: "fa", cookie: "it", accept: ["nl"]).ShouldBe("fa");

    [Fact]
    public void Resolve_CookieComesBeforeAcceptLanguage() => Resolve(cookie: "it", accept: ["nl"]).ShouldBe("it");

    [Fact]
    public void Resolve_AcceptLanguageComesBeforeApplicationDefault() => Resolve(accept: ["nl"], appDefault: "it").ShouldBe("nl");

    [Fact]
    public void Resolve_ApplicationDefault_IsUsedLast() => Resolve(appDefault: "it").ShouldBe("it");

    [Fact]
    public void Resolve_UiLocalesTakesFirstSupportedValue() => Resolve(ui: "ja xx fa nl").ShouldBe("fa");

    [Fact]
    public void Resolve_RegionTag_MatchesNeutralCulture() => Resolve(accept: ["fa-IR"]).ShouldBe("fa");

    [Fact]
    public void Resolve_ApplicationDoesNotSupportCulture_IsSkipped() =>
        Resolve(ui: "nl", accept: ["fa"], supported: ["en", "fa"]).ShouldBe("fa");

    [Fact]
    public void Resolve_UnsupportedEverywhere_IsEnglish() => Resolve(ui: "ja", accept: ["zh"], appDefault: "ko").ShouldBe("en");

    [Fact]
    public void Resolve_ServerWithoutResxForCulture_IsSkippedEvenIfApplicationListsIt() =>
        Resolve(ui: "ja", supported: ["en", "ja"]).ShouldBe("en");
}

public class SecretHasherTests
{
    private readonly Sha256SecretHasher _hasher = new();

    [Fact]
    public void Hash_ThenVerify_Works()
    {
        var h = _hasher.Hash("ABCD-1234");
        h.ShouldStartWith("v1.");
        h.ShouldNotContain("ABCD-1234");
        _hasher.Verify("ABCD-1234", h).ShouldBeTrue();
        _hasher.Verify("ABCD-1235", h).ShouldBeFalse();
    }

    [Fact]
    public void Hash_SameSecretTwice_GivesDifferentHashes() => _hasher.Hash("x").ShouldNotBe(_hasher.Hash("x"));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1.zz.zz")]
    [InlineData("v2.00.00")]
    public void Verify_BadHashFormat_IsFalse(string hash) => _hasher.Verify("x", hash).ShouldBeFalse();
}
