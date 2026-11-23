using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Tests;

public class ReturnUrlValidatorTests
{
    private static readonly string[] Allowed = ["https://app.example.com/signin-oidc", "https://app.example.com/callback?x=1"];
    private readonly ReturnUrlValidator _v = new();

    [Theory]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/connect/authorize?client_id=app&state=a%2Fb&redirect_uri=https%3A%2F%2Fapp.example.com%2Fsignin-oidc")]
    [InlineData("/a/b?c=d#e")]
    public void IsAllowed_LocalPath_IsTrue(string url) => _v.IsAllowed(url, Allowed).ShouldBeTrue();

    [Theory]
    [InlineData("https://app.example.com/signin-oidc")]
    [InlineData("https://app.example.com/callback?x=1")]
    public void IsAllowed_ExactAllowedAbsolute_IsTrue(string url) => _v.IsAllowed(url, Allowed).ShouldBeTrue();

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("https://evil.com/signin-oidc")]
    [InlineData("//evil.com")]
    [InlineData("///evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("\\/evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("evil.com")]
    [InlineData("http://app.example.com/signin-oidc")]        // other scheme: not an exact match
    [InlineData("https://app.example.com/signin-oidc/")]       // trailing slash: not an exact match
    [InlineData("https://app.example.com/signin-oidc?x=1")]   // extra query: not an exact match
    [InlineData("https://app.example.com.evil.com/signin-oidc")]
    [InlineData("https://app.example.com@evil.com/")]
    public void IsAllowed_EvilOrNotListed_IsFalse(string url) => _v.IsAllowed(url, Allowed).ShouldBeFalse();

    [Theory]
    [InlineData("/%2Fevil.com")]            // encoded second slash
    [InlineData("/%2fevil.com")]
    [InlineData("/%5Cevil.com")]            // encoded backslash
    [InlineData("/%5cevil.com")]
    [InlineData("/%252Fevil.com")]          // double encoded
    [InlineData("/%255Cevil.com")]
    [InlineData("%2F%2Fevil.com")]
    [InlineData("/%09/evil.com")]           // tab (browsers strip it)
    [InlineData("/%0d%0a/evil.com")]
    public void IsAllowed_EncodedVariants_IsFalse(string url) => _v.IsAllowed(url, Allowed).ShouldBeFalse();

    [Theory]
    [InlineData("/ /evil.com")]
    [InlineData(" /evil.com")]
    [InlineData("/\t/evil.com")]
    [InlineData("/\n/evil.com")]
    [InlineData("/\r\n/evil.com")]
    [InlineData("/a\0b")]
    public void IsAllowed_WhitespaceOrControlCharacters_IsFalse(string url) => _v.IsAllowed(url, Allowed).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAllowed_NullOrEmpty_IsFalse(string? url) => _v.IsAllowed(url, Allowed).ShouldBeFalse();

    [Fact]
    public void IsAllowed_VeryLongUrl_IsFalse() => _v.IsAllowed("/" + new string('a', 5000), Allowed).ShouldBeFalse();

    [Fact]
    public void IsAllowed_EmptyAllowedList_OnlyLocalPathsPass()
    {
        _v.IsAllowed("/ok", []).ShouldBeTrue();
        _v.IsAllowed("https://app.example.com/signin-oidc", []).ShouldBeFalse();
    }

    private static ApplicationSettings Settings()
    {
        var s = new ApplicationSettings();
        s.Redirects.AllowedRedirectUris.Add("https://app.example.com/signin-oidc");
        return s;
    }

    [Fact]
    public void Resolve_ValidReturnUrl_Wins() =>
        _v.Resolve("/page-b", "/after-login", Settings()).ShouldBe("/page-b");

    [Fact]
    public void Resolve_InvalidReturnUrl_UsesApplicationFallback() =>
        _v.Resolve("https://evil.com", "/after-login", Settings()).ShouldBe("/after-login");

    [Fact]
    public void Resolve_NoReturnUrl_UsesApplicationFallback() =>
        _v.Resolve(null, "/after-login", Settings()).ShouldBe("/after-login");

    [Fact]
    public void Resolve_InvalidFallback_UsesHome() =>
        _v.Resolve("//evil.com", "//also-evil.com", Settings()).ShouldBe("/");

    [Fact]
    public void Resolve_NothingGiven_UsesCustomHome() =>
        _v.Resolve(null, null, Settings(), "/home").ShouldBe("/home");

    [Fact]
    public void Resolve_AbsoluteReturnUrl_MustBeListed()
    {
        _v.Resolve("https://app.example.com/signin-oidc", null, Settings()).ShouldBe("https://app.example.com/signin-oidc");
        _v.Resolve("https://app.example.com/other", null, Settings()).ShouldBe("/");
    }
}
