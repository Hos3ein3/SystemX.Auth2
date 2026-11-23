using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Identity;
using NSubstitute;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Tests;

public class PasswordRuleTests
{
    private static Auth2IdentityErrorDescriber Describer()
    {
        var l = Substitute.For<IAuth2Localizer>();
        l.Error(Arg.Any<string>(), Arg.Any<object[]>()).Returns(ci => ci.Arg<string>());
        return new Auth2IdentityErrorDescriber(l);
    }

    private static string[] Codes(PasswordSettings rules, string password) =>
        Auth2PasswordValidator.Check(rules, password, Describer()).Select(e => e.Code).ToArray();

    [Fact]
    public void Check_GoodPassword_NoErrors() => Codes(new PasswordSettings(), "Abcdefgh12345").ShouldBeEmpty();

    [Fact]
    public void Check_TooShort_UsesStableCode() => Codes(new PasswordSettings(), "Ab1").ShouldContain(Auth2ErrorCodes.PasswordTooShort);

    [Fact]
    public void Check_TooLong_IsRejected() =>
        Codes(new PasswordSettings { MaxLength = 12 }, "Abcdefgh1234567").ShouldContain(Auth2ErrorCodes.PasswordTooLong);

    [Fact]
    public void Check_EachCharacterRule_ReportsItsOwnCode()
    {
        var rules = new PasswordSettings { RequireNonAlphanumeric = true, MinLength = 6, UniqueChars = 0 };
        Codes(rules, "abcdefgh").ShouldBe([Auth2ErrorCodes.PasswordRequiresDigit, Auth2ErrorCodes.PasswordRequiresUpper, Auth2ErrorCodes.PasswordRequiresNonAlphanumeric], ignoreOrder: true);
        Codes(rules, "ABCDEFG1!").ShouldBe([Auth2ErrorCodes.PasswordRequiresLower]);
    }

    [Fact]
    public void Check_Rules_CanBeSwitchedOff() =>
        Codes(new PasswordSettings { RequireDigit = false, RequireLower = false, RequireUpper = false, UniqueChars = 0, MinLength = 6 }, "aaaaaa").ShouldBeEmpty();

    [Fact]
    public void Check_UniqueChars_IsEnforced() =>
        Codes(new PasswordSettings { UniqueChars = 5, RequireDigit = false, RequireUpper = false }, "aaaaaaaaaaaa").ShouldContain(Auth2ErrorCodes.PasswordRequiresUniqueChars);

    [Fact]
    public void Check_BannedPassword_IsRejected_IgnoringCase() =>
        Codes(new PasswordSettings { BannedPasswords = ["Welcome12345"] }, "welcome12345").ShouldContain(Auth2ErrorCodes.PasswordBanned);
}

public class SafeRegexTests
{
    [Fact]
    public void SafeMatch_Match_IsTrue() => Auth2UserValidator.SafeMatch("^[a-z]+$", "abc", TimeSpan.FromMilliseconds(200)).ShouldBeTrue();

    [Fact]
    public void SafeMatch_NoMatch_IsFalse() => Auth2UserValidator.SafeMatch("^[a-z]+$", "ab1", TimeSpan.FromMilliseconds(200)).ShouldBeFalse();

    [Fact]
    public void SafeMatch_InvalidPattern_IsFalse_NotAnException() => Auth2UserValidator.SafeMatch("([", "x", TimeSpan.FromMilliseconds(200)).ShouldBeFalse();

    [Fact]
    public void SafeMatch_CatastrophicPattern_TimesOut_AndReturnsFalse()
    {
        var evil = new string('a', 40) + "!";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Auth2UserValidator.SafeMatch("^(a+)+$", evil, TimeSpan.FromMilliseconds(50)).ShouldBeFalse();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
