using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The rules of the OpenIddict.Client integration itself: run-time registrations, isolation of a broken provider, no token storage, the HTTP guard.</summary>
public class ExternalClientIntegrationTests : OidcTestBase
{
    private static OidcProviderSettings Generic(string name, string authority = "https://idp.corp.test") =>
        new() { Name = name, Type = ExternalProviderType.GenericOidc, ClientId = "client-" + name, Authority = authority };

    [Fact]
    public async Task AProviderAddedAfterStart_IsUsedAtOnce_AndOneRemovedStopsAtOnce()
    {
        await SetUpAsync();
        var first = await AddAsync(A, Generic("one"));
        (await SignInAsync(first, new FakeUser())).Identity.Provider.ShouldBe("one");
        var second = await AddAsync(A, Generic("two"));   // added while the server runs: no restart
        (await SignInAsync(second, new FakeUser())).Identity.Provider.ShouldBe("two");

        await UpdateSettingsAsync(A, s => s.ExternalProviders.RemoveAll(p => p.Name == "two"));
        await Should.ThrowAsync<ExternalLoginException>(() => Source.ChallengeAsync(new ExternalChallengeContext(second, Issuer, "/", null), default));
        (await SignInAsync(first, new FakeUser())).Identity.Provider.ShouldBe("one");   // the other one is not touched
    }

    [Fact]
    public async Task ABrokenProvider_IsSkipped_AndDoesNotStopTheOthers()
    {
        await SetUpAsync();
        var good = await AddAsync(A, Generic("good"));
        // A secret that cannot be decrypted (for example after the keys of another environment were copied) must not reach OpenIddict, which would throw for everybody.
        var broken = Generic("broken");
        broken.ClientSecretProtected = "this-is-not-a-protected-value";
        broken.Enabled = true;
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(broken));
        var registered = Factory.Services.GetRequiredService<IExternalProviderRegistry>().Get(A.Id, "broken")!;

        await Should.ThrowAsync<ExternalLoginException>(() => Source.ChallengeAsync(new ExternalChallengeContext(registered, Issuer, "/", null), default));
        (await SignInAsync(good, new FakeUser())).Identity.Subject.ShouldBe("user-1");
        Factory.Services.GetRequiredService<ExternalClientRegistrations>().FailureOf(registered.RegistrationName).ShouldNotBeNull();
    }

    [Fact]
    public async Task TwoProjects_WithTheSameProviderName_GetTheirOwnClientAndRedirectUri()
    {
        await SetUpAsync();
        var inA = await AddAsync(A, Generic("corp"));
        var inB = await AddAsync(B, Generic("corp"));
        var a = Fake.Parse((await Source.ChallengeAsync(new ExternalChallengeContext(inA, Issuer, "/", null), default)).RedirectUri);
        var b = Fake.Parse((await Source.ChallengeAsync(new ExternalChallengeContext(inB, Issuer, "/", null), default)).RedirectUri);
        a.RedirectUri.ShouldBe("https://auth.test/callback/login/appa/corp");
        b.RedirectUri.ShouldBe("https://auth.test/callback/login/appb/corp");
        a.State.ShouldNotBe(b.State);
    }

    [Fact]
    public async Task TheClientDoesNotWriteIntoTheTokenTableOfTheServer()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic("corp"));
        async Task<int> Count()
        {
            await using var scope = NewScope();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            return await db.Set<Server.Infrastructure.OpenIddict.Auth2Token>().CountAsync() + await db.Set<Server.Infrastructure.OpenIddict.Auth2Authorization>().CountAsync();
        }
        var before = await Count();
        await SignInAsync(p, new FakeUser());
        (await Count()).ShouldBe(before);   // upstream tokens are not stored (DisableTokenStorage)
    }

    [Fact]
    public async Task ASocialProviderMadeByTheLibraryBuilder_StillUsesTheGuardedHttpClient()
    {
        await SetUpAsync();
        var google = await AddAsync(A, new OidcProviderSettings { Name = "google", Type = ExternalProviderType.Google, ClientId = "g" });
        // Discovery of a built-in provider goes through the fake provider (the filter of the main container is copied into the small container of the builder),
        // never to the real internet.
        await Source.ChallengeAsync(new ExternalChallengeContext(google, Issuer, "/", null), default);
        Fake.Requests.ShouldContain(r => r.Contains("accounts.google.com"));
    }

    // ---- the HTTP guard ------------------------------------------------------------------------------------------------------------------------------------

    private sealed class Ok : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }

    private static HttpMessageInvoker Guard(bool allowPrivate, Ok inner) => new(new SsrfGuardHandler(allowPrivate) { InnerHandler = inner });

    [Theory]
    [InlineData("https://10.0.0.5/.well-known/openid-configuration")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://192.168.1.10/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("http://8.8.8.8/x")]               // plain HTTP
    [InlineData("https://user:pw@8.8.8.8/x")]      // user info in the URL
    public async Task TheGuard_RefusesPrivateAddresses_PlainHttp_AndUserInfo(string url)
    {
        var inner = new Ok();
        await Should.ThrowAsync<Exception>(() => Guard(false, inner).SendAsync(new HttpRequestMessage(HttpMethod.Get, url), default));
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task TheGuard_LetsAPublicHttpsAddressThrough_AndAllowsPrivateOnlyWhenTheSwitchIsOn()
    {
        var inner = new Ok();
        (await Guard(false, inner).SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://8.8.8.8/x"), default)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Guard(true, inner).SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://10.0.0.5/x"), default)).StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.Calls.ShouldBe(2);
    }
}
