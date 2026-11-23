using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class OtpServiceTests : IdentityTestBase
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    private async Task<(AsyncServiceScope Scope, OtpService Otp, AppUser User)> StartAsync(string clientId = "appa", Action<Domain.Settings.ApplicationSettings>? tune = null)
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        if (tune is not null) await UpdateSettingsAsync(A, tune);
        var scope = ScopeFor(clientId);
        var otp = new OtpService(scope.ServiceProvider.GetRequiredService<Auth2DbContext>(), scope.ServiceProvider.GetRequiredService<IAuthFlowResolver>(),
            scope.ServiceProvider.GetRequiredService<ISecretHasher>(), _time);
        return (scope, otp, user);
    }

    [Fact]
    public async Task Issue_MakesNumericCodeOfTheConfiguredLength_AndStoresOnlyAHash()
    {
        var (scope, otp, user) = await StartAsync(tune: s => s.TwoFactor.OtpLength = 8);
        await using var _ = scope;
        var issued = await otp.IssueAsync(user, "login");
        issued.Issued.ShouldBeTrue();
        issued.Code!.Length.ShouldBe(8);
        issued.Code.All(char.IsDigit).ShouldBeTrue();

        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var row = await db.OtpCodes.SingleAsync();
        row.CodeHash.ShouldStartWith("v1.");
        row.CodeHash.ShouldNotContain(issued.Code);
        row.Purpose.ShouldBe("login");
    }

    [Fact]
    public async Task Verify_RightCode_WorksOnce()
    {
        var (scope, otp, user) = await StartAsync();
        await using var _ = scope;
        var code = (await otp.IssueAsync(user, "login")).Code!;
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.Ok);
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.NotFound);   // one-time use
    }

    [Fact]
    public async Task Verify_AfterTheLifetime_IsExpired()
    {
        var (scope, otp, user) = await StartAsync(tune: s => s.TwoFactor.OtpLifetimeSeconds = 120);
        await using var _ = scope;
        var code = (await otp.IssueAsync(user, "login")).Code!;
        _time.Advance(TimeSpan.FromSeconds(119));
        (await otp.VerifyAsync(user, "login", "000000" == code ? "000001" : "000000")).ShouldBe(OtpVerifyResult.Invalid);   // still open
        _time.Advance(TimeSpan.FromSeconds(2));
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.Expired);
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.NotFound);
    }

    [Fact]
    public async Task Verify_TooManyWrongAttempts_KillsTheCode_EvenForTheRightCode()
    {
        var (scope, otp, user) = await StartAsync(tune: s => s.TwoFactor.OtpMaxAttempts = 3);
        await using var _ = scope;
        var code = (await otp.IssueAsync(user, "login")).Code!;
        var wrong = code == "111111" ? "222222" : "111111";
        (await otp.VerifyAsync(user, "login", wrong)).ShouldBe(OtpVerifyResult.Invalid);
        (await otp.VerifyAsync(user, "login", wrong)).ShouldBe(OtpVerifyResult.Invalid);
        (await otp.VerifyAsync(user, "login", wrong)).ShouldBe(OtpVerifyResult.TooManyAttempts);
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.NotFound);
    }

    [Fact]
    public async Task Issue_AgainTooSoon_IsRefused_ThenAllowed_AndTheOldCodeStopsWorking()
    {
        var (scope, otp, user) = await StartAsync(tune: s => s.TwoFactor.OtpResendSeconds = 60);
        await using var _ = scope;
        var first = (await otp.IssueAsync(user, "login")).Code!;
        var tooSoon = await otp.IssueAsync(user, "login");
        tooSoon.TooSoon.ShouldBeTrue();
        tooSoon.Code.ShouldBeNull();

        _time.Advance(TimeSpan.FromSeconds(61));
        var second = (await otp.IssueAsync(user, "login")).Code!;
        if (second != first) (await otp.VerifyAsync(user, "login", first)).ShouldBe(OtpVerifyResult.Invalid);   // the old one is closed
        (await otp.VerifyAsync(user, "login", second)).ShouldBe(OtpVerifyResult.Ok);
    }

    [Fact]
    public async Task Verify_CodeOfAnotherPurpose_IsNotAccepted()
    {
        var (scope, otp, user) = await StartAsync();
        await using var _ = scope;
        var code = (await otp.IssueAsync(user, "join")).Code!;
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.NotFound);
        (await otp.VerifyAsync(user, "join", code)).ShouldBe(OtpVerifyResult.Ok);
    }

    [Fact]
    public async Task Verify_CodeOfAnotherApplication_IsNotAccepted()
    {
        var (scope, otp, user) = await StartAsync();
        await using var _ = scope;
        await JoinAsync(user, B);
        var code = (await otp.IssueAsync(user, "login")).Code!;

        await using var scopeB = ScopeFor("appb");
        var otpB = new OtpService(scopeB.ServiceProvider.GetRequiredService<Auth2DbContext>(), scopeB.ServiceProvider.GetRequiredService<IAuthFlowResolver>(),
            scopeB.ServiceProvider.GetRequiredService<ISecretHasher>(), _time);
        (await otpB.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.NotFound);
        (await otp.VerifyAsync(user, "login", code)).ShouldBe(OtpVerifyResult.Ok);
    }

    [Fact]
    public async Task Verify_TheSameRightCodeInParallel_SucceedsExactlyOnce()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        string code;
        await using (var scope = ScopeFor("appa"))
            code = (await scope.ServiceProvider.GetRequiredService<IOtpService>().IssueAsync(user, "login")).Code!;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var scope = ScopeFor("appa");
            return await scope.ServiceProvider.GetRequiredService<IOtpService>().VerifyAsync(user, "login", code);
        }));
        results.Count(r => r == OtpVerifyResult.Ok).ShouldBe(1);
    }
}
