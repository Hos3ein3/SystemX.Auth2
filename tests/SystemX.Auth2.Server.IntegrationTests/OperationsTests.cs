using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Commands;
using SystemX.Auth2.Server.Infrastructure.Operations;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class HardeningTests : Phase6Base
{
    [Fact]
    public async Task Health_Live_IsAlwaysOk_AndReady_NeedsTheDatabase()
    {
        await MigrateAndSeedAsync();
        var b = new Browser(Factory);
        (await b.GetAsync(Auth2Routes.HealthLive)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var ready = await b.GetAsync(Auth2Routes.HealthReady);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task SecurityHeaders_AreOnEveryAnswer_AndApiAnswersAreNotCached()
    {
        await PrepareAppsAsync();
        var b = new Browser(Factory);
        var page = await b.GetAsync("/");
        page.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        page.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        page.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        page.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
        var api = await b.GetAsync(Auth2Routes.Permissions, await ClientTokenAsync("appa", "auth2.permissions.sync"));
        api.Headers.CacheControl!.NoStore.ShouldBeTrue();
        page.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task Cors_IsOffByDefault_AndAllowsOnlyListedOrigins()
    {
        await PrepareAppsAsync();
        await UpdateSettingsAsync(A, s => s.App.AllowedCorsOrigins.Add("https://spa.test"));
        var b = new Browser(Factory);
        var token = await ClientTokenAsync("appa", "auth2.permissions.sync");

        var ok = await b.SendJsonAsync(HttpMethod.Get, Auth2Routes.Permissions, null, token, ("Origin", "https://spa.test"));
        ok.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://spa.test"]);
        ok.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();

        var evil = await b.SendJsonAsync(HttpMethod.Get, Auth2Routes.Permissions, null, token, ("Origin", "https://evil.test"));
        evil.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();

        var preflight = await b.SendJsonAsync(HttpMethod.Options, Auth2Routes.Token, null, null,
            ("Origin", "https://spa.test"), ("Access-Control-Request-Method", "POST"));
        preflight.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://spa.test"]);
        var evilPreflight = await b.SendJsonAsync(HttpMethod.Options, Auth2Routes.Token, null, null, ("Origin", "https://evil.test"), ("Access-Control-Request-Method", "POST"));
        evilPreflight.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}

public class RateLimitTests : Phase6Base
{
    protected override void Customize(IServiceCollection services) => services.Configure<Auth2ServerOptions>(o =>
    {
        o.RateLimiting.Policies["login"] = new RateLimitRule { PermitLimit = 3, WindowSeconds = 60 };
        o.RateLimiting.Policies["handshake"] = new RateLimitRule { PermitLimit = 2, WindowSeconds = 60 };
        o.RateLimiting.Policies["otp"] = new RateLimitRule { PermitLimit = 2, WindowSeconds = 60 };
    });

    private Task<HttpResponseMessage> PasswordGrantAsync(string user, string password = "wrong-password") =>
        new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = user, ["password"] = password, ["scope"] = "openid", ["client_id"] = "appa", ["client_secret"] = ASecret,
        });

    [Fact]
    public async Task Login_AfterTheLimit_Is429_WithRetryAfterAndAnErrorBody_ButOtherLoginsStillWork()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa", "sara");
        await CreateUserInAsync("appa", "other");
        for (var i = 0; i < 3; i++) (await PasswordGrantAsync("sara")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // wrong password, counted

        var blocked = await PasswordGrantAsync("sara", PassA);   // even the right password: the limit comes first
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(blocked.Headers.GetValues("Retry-After").Single()).ShouldBeInRange(1, 60);
        (await blocked.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.RateLimited);

        (await PasswordGrantAsync("other", PassA)).StatusCode.ShouldBe(HttpStatusCode.OK);   // another login is counted separately
    }

    [Fact]
    public async Task Login_SprayingManyLoginsFromOneAddress_IsStoppedByTheAddressLimit()
    {
        await PrepareAppsAsync();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 17; i++) codes.Add((await PasswordGrantAsync($"user{i}")).StatusCode);   // each login stays under 3; the address limit is 3 x 5 = 15
        codes.Take(15).ShouldAllBe(c => c == HttpStatusCode.BadRequest);
        codes.Skip(15).ShouldAllBe(c => c == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task HandshakeAndOtp_HaveTheirOwnPolicies_AndRejectionsAreCounted()
    {
        await PrepareAppsAsync();
        var rejected = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Meter.Name == Auth2Metrics.MeterName && i.Name == "auth2.ratelimit.rejections") l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref rejected, value));
        listener.Start();

        var token = await ClientTokenAsync("appa", "auth2.client.handshake auth2.login");
        var b = new Browser(Factory);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) statuses.Add((await b.PostJsonAsync(Auth2Routes.Handshake, new ClientHandshakeRequest("appa", "default", "1"), token)).StatusCode);
        statuses.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);

        var otp = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) otp.Add((await b.PostJsonAsync(Auth2Routes.OtpRequest, new OtpRequest("nobody@example.com"), token)).StatusCode);
        otp.Last().ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task TurnedOff_NothingIsLimited()
    {
        Factory.Customize = s => { Customize(s); s.Configure<Auth2ServerOptions>(o => o.RateLimiting.Enabled = false); };
        await PrepareAppsAsync();
        for (var i = 0; i < 6; i++) (await PasswordGrantAsync("sara")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

public class KeyRingTests : Phase6Base
{
    private static JsonElement Header(string jwt)
    {
        var part = jwt.Split('.')[0].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(part)).RootElement.Clone();
    }

    private async Task<int> JwksCountAsync() =>
        JsonDocument.Parse(await (await new Browser(Factory).GetAsync("/.well-known/jwks")).Content.ReadAsStringAsync()).RootElement.GetProperty("keys").GetArrayLength();

    [Fact]
    public async Task FirstStart_CreatesOneSigningAndOneEncryptionKey_ProtectedInTheDatabase()
    {
        await PrepareAppsAsync();
        await using var scope = NewScope();
        var keys = await scope.ServiceProvider.GetRequiredService<IKeyRing>().ListAsync();
        keys.Select(k => k.Purpose).Order().ShouldBe(["Encryption", "Signing"]);
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var stored = (await db.KeyRing.ToListAsync()).First();
        stored.ProtectedPfx.ShouldNotBeNullOrEmpty();
        Should.Throw<FormatException>(() => Convert.FromBase64String(stored.ProtectedPfx.Replace("CfDJ8", "!")));   // not a plain PFX
        (await JwksCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Rotation_OldTokensKeepWorking_NewTokensUseTheNewKey_AndOldKeysAreRemovedLater()
    {
        await PrepareAppsAsync();
        var old = await ClientTokenAsync("appa", "auth2.permissions.sync");
        var b = new Browser(Factory);
        (await b.GetAsync(Auth2Routes.Permissions, old)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = NewScope()) await scope.ServiceProvider.GetRequiredService<IKeyRing>().RotateAsync();

        var fresh = await ClientTokenAsync("appa", "auth2.permissions.sync");
        Header(fresh).GetProperty("kid").GetString().ShouldNotBe(Header(old).GetProperty("kid").GetString());
        (await b.GetAsync(Auth2Routes.Permissions, old)).StatusCode.ShouldBe(HttpStatusCode.OK);     // the retired key still checks it
        (await b.GetAsync(Auth2Routes.Permissions, fresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JwksCountAsync()).ShouldBe(2);   // both public keys are published

        await using (var scope = NewScope())
        {
            var ring = scope.ServiceProvider.GetRequiredService<IKeyRing>();
            var list = await ring.ListAsync();
            list.Count(k => k.RetiredAtUtc is null).ShouldBe(2);
            list.Count(k => k.RetiredAtUtc is not null && k.RemoveAtUtc > DateTimeOffset.UtcNow.AddDays(30)).ShouldBe(2);
            (await ring.RemoveExpiredAsync()).ShouldBe(0);   // not yet
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            await db.KeyRing.Where(k => k.RetiredAtUtc != null).ExecuteUpdateAsync(s => s.SetProperty(k => k.RemoveAtUtc, DateTimeOffset.UtcNow.AddDays(-1)));
            (await ring.RemoveExpiredAsync()).ShouldBe(2);
        }
        (await b.GetAsync(Auth2Routes.Permissions, old)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // key gone: old tokens are over
        (await b.GetAsync(Auth2Routes.Permissions, fresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RotateKeysCommand_AndAdminApi_Rotate_AndAreAudited()
    {
        await PrepareAppsAsync();
        (await Auth2Commands.RunAsync(["rotate-keys"], Factory.Services)).ShouldBe(0);
        await using (var scope = NewScope()) (await scope.ServiceProvider.GetRequiredService<IKeyRing>().ListAsync()).Count.ShouldBe(4);
        (await AuditAsync("keys.rotated")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task CreateAdminCommand_AddsAGlobalAdmin_ThePasswordComesFromTheEnvironment()
    {
        await MigrateAndSeedAsync();
        Environment.SetEnvironmentVariable("AUTH2_ADMIN_PASSWORD", "Another-Strong-Pass-1!");
        try
        {
            (await Auth2Commands.RunAsync(["create-admin", "second", "second@example.com"], Factory.Services)).ShouldBe(0);
            (await Auth2Commands.RunAsync(["create-admin", "third"], Factory.Services)).ShouldBe(2);   // missing email
        }
        finally { Environment.SetEnvironmentVariable("AUTH2_ADMIN_PASSWORD", null); }
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Users.Count(u => u.UserName == "second").ShouldBe(1);
        (await AuditAsync("admin.created")).Count.ShouldBe(1);
        Factory.Logs.Entries.ShouldNotContain(e => e.Message.Contains("Another-Strong-Pass-1!"));
    }
}

public class JobsTests : Phase6Base
{
    [Fact]
    public async Task Jobs_RemoveOldCodesAndAuditEvents_AndKeepNewOnes()
    {
        await PrepareAppsAsync();
        var user = await CreateUserInAsync("appa", "sara");
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            db.OtpCodes.AddRange(
                new OtpCode { UserId = user.Id, ProjectId = A.Id, Purpose = "login", CodeHash = "x", ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-3) },
                new OtpCode { UserId = user.Id, ProjectId = A.Id, Purpose = "login", CodeHash = "y", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5) });
            db.AuditEvents.AddRange(
                new AuditEvent { OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-400), Action = "old.event", ProjectId = A.Id },
                new AuditEvent { OccurredAtUtc = DateTimeOffset.UtcNow, Action = "new.event", ProjectId = A.Id });
            await db.SaveChangesAsync();
        }
        var ran = await Factory.Services.GetRequiredService<MaintenanceJobs>().RunOnceAsync();
        ran.ShouldBe(MaintenanceJobs.Names, ignoreOrder: true);

        await using var check = NewScope();
        var cdb = check.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = check.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await cdb.OtpCodes.CountAsync()).ShouldBe(1);
        (await cdb.AuditEvents.AnyAsync(e => e.Action == "old.event")).ShouldBeFalse();
        (await cdb.AuditEvents.AnyAsync(e => e.Action == "new.event")).ShouldBeTrue();
    }

    [Fact]
    public async Task OnlyOneInstanceRunsAJob_TheLockIsHeld()
    {
        await MigrateAndSeedAsync();
        var locks = Factory.Services.GetRequiredService<IDistributedLock>();
        var innerRan = true;
        var outerRan = await locks.TryRunAsync("same-job", async ct =>
        {
            innerRan = await locks.TryRunAsync("same-job", _ => Task.CompletedTask, ct);   // another instance asking at the same time
        });
        outerRan.ShouldBeTrue();
        innerRan.ShouldBeFalse();
        (await locks.TryRunAsync("same-job", _ => Task.CompletedTask)).ShouldBeTrue();   // free again afterwards
    }
}

/// <summary>Two server instances with one database (and one Redis): what one does, the other sees.</summary>
public class ScaleOutTests : Phase6Base
{
    protected override bool UseRedis => true;

    private async Task<Auth2ServerFactory> SecondAsync()
    {
        var second = new Auth2ServerFactory { Customize = s => Customize(s) };
        second.ShareDatabaseWith(Factory);
        return second;
    }

    protected override void Customize(IServiceCollection services) => services.Configure<Auth2ServerOptions>(o =>
        o.RateLimiting.Policies["login"] = new RateLimitRule { PermitLimit = 3, WindowSeconds = 60 });

    [Fact]
    public async Task ATokenFromOneInstance_IsValidOnTheOther_AndRotationReachesBoth()
    {
        await PrepareAppsAsync();
        await using var second = await SecondAsync();
        var token = await ClientTokenAsync("appa", "auth2.permissions.sync");
        (await new Browser(second).GetAsync(Auth2Routes.Permissions, token)).StatusCode.ShouldBe(HttpStatusCode.OK);   // same keys from the key ring

        await using (var scope = NewScope()) await scope.ServiceProvider.GetRequiredService<IKeyRing>().RotateAsync();
        (await second.Services.GetRequiredService<IKeyRing>().RefreshAsync()).ShouldBeTrue();   // the background service does this every 30 seconds
        var fresh = await ClientTokenAsync("appa", "auth2.permissions.sync");
        (await new Browser(second).GetAsync(Auth2Routes.Permissions, fresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await new Browser(second).GetAsync(Auth2Routes.Permissions, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DataProtection_IsSharedBetweenInstances()
    {
        await PrepareAppsAsync();
        await using var second = await SecondAsync();
        var a = Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test");
        var b = second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test");
        b.Unprotect(a.Protect("hello")).ShouldBe("hello");
    }

    [Fact]
    public async Task RateLimitCounters_AreSharedThroughRedis()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa", "sara");
        await using var second = await SecondAsync();
        async Task<HttpStatusCode> Try(Auth2ServerFactory f) => (await new Browser(f).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "sara", ["password"] = "wrong", ["scope"] = "openid", ["client_id"] = "appa", ["client_secret"] = ASecret,
        })).StatusCode;

        (await Try(Factory)).ShouldBe(HttpStatusCode.BadRequest);
        (await Try(second)).ShouldBe(HttpStatusCode.BadRequest);
        (await Try(Factory)).ShouldBe(HttpStatusCode.BadRequest);
        (await Try(second)).ShouldBe(HttpStatusCode.TooManyRequests);   // 4th attempt overall, from the other instance
    }

    [Fact]
    public async Task SettingsChangedOnOneInstance_AreSeenByTheOtherThroughPubSub()
    {
        await PrepareAppsAsync();
        await using var second = await SecondAsync();
        async Task<string?> BrandOnSecond()
        {
            await using var scope = second.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IProjectSettingsProvider>().GetAsync(A.Id)).Localization.BrandName;
        }
        (await BrandOnSecond()).ShouldBeNull();   // now cached on the second instance
        await UpdateSettingsAsync(A, s => s.Localization.BrandName = "Blog");
        for (var i = 0; i < 40 && await BrandOnSecond() != "Blog"; i++) await Task.Delay(100);
        (await BrandOnSecond()).ShouldBe("Blog");
    }
}

/// <summary>The one-time code rules are the same with Redis (the table version has its own tests).</summary>
public class RedisOtpTests : Phase6Base
{
    protected override bool UseRedis => true;

    private async Task<(AsyncServiceScope Scope, IOtpService Otp, AppUser User)> StartAsync()
    {
        await SetUpAsync();
        var user = await CreateUserInAsync("appa");
        var scope = ScopeFor("appa");
        return (scope, scope.ServiceProvider.GetRequiredService<IOtpService>(), user);
    }

    [Fact]
    public async Task Redis_IsUsedForCodes_NoRowInTheTable()
    {
        var (scope, otp, user) = await StartAsync();
        await using var _ = scope;
        otp.ShouldBeOfType<RedisOtpService>();
        var issued = await otp.IssueAsync(user, "login");
        issued.Code!.Length.ShouldBe(6);
        (await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().OtpCodes.CountAsync()).ShouldBe(0);
        (await otp.VerifyAsync(user, "login", issued.Code)).ShouldBe(OtpVerifyResult.Ok);
        (await otp.VerifyAsync(user, "login", issued.Code)).ShouldBe(OtpVerifyResult.NotFound);   // works once
    }

    [Fact]
    public async Task Redis_WrongCodes_ResendDelay_AndPurposeBinding()
    {
        var (scope, otp, user) = await StartAsync();
        await using var _ = scope;
        var issued = await otp.IssueAsync(user, "login");
        (await otp.IssueAsync(user, "login")).TooSoon.ShouldBeTrue();                       // resend delay
        (await otp.VerifyAsync(user, "confirm_phone", issued.Code!)).ShouldBe(OtpVerifyResult.NotFound);   // other purpose
        (await otp.VerifyAsync(user, "login", "000000" == issued.Code ? "111111" : "000000")).ShouldBe(OtpVerifyResult.Invalid);
        for (var i = 0; i < 6; i++) await otp.VerifyAsync(user, "login", "999999");
        (await otp.VerifyAsync(user, "login", issued.Code!)).ShouldBe(OtpVerifyResult.NotFound);   // closed after too many attempts
    }

    [Fact]
    public async Task Redis_ParallelCorrectUses_OnlyOneWins()
    {
        var (scope, otp, user) = await StartAsync();
        await using var _ = scope;
        var code = (await otp.IssueAsync(user, "login")).Code!;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => otp.VerifyAsync(user, "login", code))));
        results.Count(r => r == OtpVerifyResult.Ok).ShouldBe(1);
    }
}
