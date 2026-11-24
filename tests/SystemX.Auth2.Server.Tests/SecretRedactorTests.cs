using SystemX.Auth2.Server.Infrastructure.Operations;

namespace SystemX.Auth2.Server.Tests;

public class SecretRedactorTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("clientSecret")]
    [InlineData("AccessToken")]
    [InlineData("Authorization")]
    [InlineData("OtpCode")]
    [InlineData("RecoveryCodes")]
    [InlineData("AuthenticatorKey")]
    [InlineData("Cookie")]
    public void SecretLikeNames_AreSensitive(string name) => SecretRedactor.IsSensitive(name).ShouldBeTrue();

    [Theory]
    [InlineData("ProjectId")]
    [InlineData("StatusCode")]
    [InlineData("CorrelationId")]
    [InlineData("RequestPath")]
    [InlineData("TokenCount")]
    public void NormalNames_AreNotSensitive(string name) => SecretRedactor.IsSensitive(name).ShouldBeFalse();
}

public class RateLimitRuleTests
{
    [Fact]
    public void EveryPolicyHasADefault_AndConfiguredValuesWin()
    {
        var o = new Auth2ServerOptionsProbe().Options;
        foreach (var name in SystemX.Auth2.Server.Application.RateLimitingOptions.PolicyNames) o.RuleFor(name).PermitLimit.ShouldBeGreaterThan(0);
        o.Policies["login"] = new SystemX.Auth2.Server.Application.RateLimitRule { PermitLimit = 2, WindowSeconds = 5 };
        o.RuleFor("login").PermitLimit.ShouldBe(2);
        o.RuleFor("token").PermitLimit.ShouldBe(60);
    }

    private sealed class Auth2ServerOptionsProbe { public SystemX.Auth2.Server.Application.RateLimitingOptions Options { get; } = new(); }
}

public class MemoryRateLimiterTests
{
    [Fact]
    public async Task Counts_PerKey_AndTheWindowResets()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var limiter = new MemoryRateLimiter(time);
        (await limiter.HitAsync("a", 2, TimeSpan.FromSeconds(10))).Allowed.ShouldBeTrue();
        (await limiter.HitAsync("a", 2, TimeSpan.FromSeconds(10))).Allowed.ShouldBeTrue();
        var third = await limiter.HitAsync("a", 2, TimeSpan.FromSeconds(10));
        third.Allowed.ShouldBeFalse();
        third.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
        (await limiter.HitAsync("b", 2, TimeSpan.FromSeconds(10))).Allowed.ShouldBeTrue();   // another key
        time.Advance(TimeSpan.FromSeconds(11));
        (await limiter.HitAsync("a", 2, TimeSpan.FromSeconds(10))).Allowed.ShouldBeTrue();   // new window
    }
}

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
