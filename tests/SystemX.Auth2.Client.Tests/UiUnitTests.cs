using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SystemX.Auth2.Client.Tests;

public class RequestKindTests
{
    private static HttpContext Context(Action<HttpContext>? setup = null)
    {
        var http = new DefaultHttpContext();
        setup?.Invoke(http);
        return http;
    }

    [Fact]
    public void BearerHeader_Json_Ajax_AreApiRequests_BrowsersAreNot()
    {
        Auth2Http.IsApiRequest(Context(h => h.Request.Headers.Authorization = "Bearer abc")).ShouldBeTrue();
        Auth2Http.IsApiRequest(Context(h => h.Request.Headers.Accept = "application/json")).ShouldBeTrue();
        Auth2Http.IsApiRequest(Context(h => h.Request.Headers.Accept = "application/problem+json, */*")).ShouldBeTrue();
        Auth2Http.IsApiRequest(Context(h => h.Request.Headers["X-Requested-With"] = "XMLHttpRequest")).ShouldBeTrue();
        Auth2Http.IsApiRequest(Context(h => h.Request.Headers.Accept = "text/html,application/xhtml+xml,application/json;q=0.5")).ShouldBeFalse();
        Auth2Http.IsApiRequest(Context(h => h.Request.Headers.Accept = "text/html")).ShouldBeFalse();
        Auth2Http.IsApiRequest(Context()).ShouldBeFalse();
    }

    [Fact]
    public void EndpointMetadata_MarksAnApiEndpoint_ApiController_OrTheMarkerAttribute()
    {
        Auth2Http.IsApiRequest(Context(h => h.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new Auth2ApiEndpointAttribute()), "e")))).ShouldBeTrue();
        Auth2Http.IsApiRequest(Context(h => h.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ApiBehaviorMeta()), "e")))).ShouldBeTrue();
        Auth2Http.IsApiRequest(Context(h => h.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "e")))).ShouldBeFalse();
    }

    private sealed class ApiBehaviorMeta : IApiBehaviorMetadata;

    [Theory]
    [InlineData("/orders?page=2", "/orders?page=2")]
    [InlineData("/", "/")]
    [InlineData("//evil.test", "/")]
    [InlineData("/\\evil.test", "/")]
    [InlineData("https://evil.test/x", "/")]
    [InlineData("evil", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("/ok\r\nSet-Cookie: x=1", "/")]
    public void ReturnUrls_MustBePathsOnThisSite(string? input, string expected) => Auth2Http.SafeReturnUrl(input).ShouldBe(expected);
}

public class AccessLoaderTests
{
    private static string Jwt(string pv) => new JsonWebTokenHandler().CreateToken($$"""{"sub":"11111111-1111-1111-1111-111111111111","pv":"{{pv}}"}""");

    private static CurrentUserInfo Me() =>
        new(Guid.NewGuid(), "sara", null, null, null, null, null, ["Editor", "Writer"], ["a.b"], ["g"], [], 7, ["a.b"], []);

    private static (ClientAccessLoader Loader, IAuth2ApiClientService Api, MemoryCurrentUserStoreProbe Store) Make(AccessLoadMode mode)
    {
        var api = Substitute.For<IAuth2ApiClientService>();
        api.GetMeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<CurrentUserInfo>.Success(Me()));
        var store = new MemoryCurrentUserStoreProbe();
        var loader = new ClientAccessLoader(api, store, MsOptions.Create(new Auth2CookieOptions { AccessMode = mode }), MsOptions.Create(new Auth2CurrentUserOptions()),
            MsOptions.Create(new Auth2ClientOptions { ClientId = "blog" }), NullLogger<ClientAccessLoader>.Instance);
        return (loader, api, store);
    }

    private sealed class MemoryCurrentUserStoreProbe : ICurrentUserStore
    {
        public Dictionary<string, Auth2Access> Items { get; } = [];
        public Task<Auth2Access?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(key));
        public Task SetAsync(string key, Auth2Access access, TimeSpan ttl, CancellationToken ct = default) { Items[key] = access; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct = default) { Items.Remove(key); return Task.CompletedTask; }
    }

    private static ClaimsPrincipal User() => new(new ClaimsIdentity([new Claim("sub", "11111111-1111-1111-1111-111111111111"), new Claim("role", "Old")], "test"));

    [Theory]
    [InlineData(AccessLoadMode.Lazy)]
    [InlineData(AccessLoadMode.None)]
    public async Task Lazy_AndNone_LoadNothing_ButKeepTheAccessVersion(AccessLoadMode mode)
    {
        var (loader, api, store) = Make(mode);
        var user = User();
        await loader.OnSignedInAsync(user, Jwt("12"), default);
        user.FindFirst("pv")!.Value.ShouldBe("12");
        await api.DidNotReceive().GetMeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        store.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task OnSignIn_PutsTheAccessDataInTheStore_UnderTheSameKeyTheProviderUses()
    {
        var (loader, _, store) = Make(AccessLoadMode.OnSignIn);
        await loader.OnSignedInAsync(User(), Jwt("5"), default);
        store.Items["auth2:access:blog:11111111-1111-1111-1111-111111111111"].Permissions.ShouldBe(["a.b"]);
        store.Items.Values.Single().AccessVersion.ShouldBe(7);
    }

    [Fact]
    public async Task RolesInCookie_ReplacesTheRoleClaims_WithTheServersRoles()
    {
        var (loader, _, store) = Make(AccessLoadMode.RolesInCookie);
        var user = User();
        await loader.OnSignedInAsync(user, Jwt("5"), default);
        user.FindAll("role").Select(c => c.Value).ShouldBe(["Editor", "Writer"]);
        store.Items.ShouldBeEmpty();   // only roles go to the cookie
    }

    [Fact]
    public async Task AServerError_DoesNotStopTheSignIn()
    {
        var (loader, api, _) = Make(AccessLoadMode.OnSignIn);
        api.GetMeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<CurrentUserInfo>.Failure(Auth2ClientErrorCodes.ServerUnavailable));
        await Should.NotThrowAsync(() => loader.OnSignedInAsync(User(), Jwt("5"), default));
        await Should.NotThrowAsync(() => loader.OnSignedInAsync(User(), "not a jwt", default));
    }
}

public class WebServiceTests
{
    private static string Jwt(string roleClaim = "Editor") => new JsonWebTokenHandler().CreateToken(
        $$"""{"sub":"11111111-1111-1111-1111-111111111111","preferred_username":"sara","name":"Sara","email":"sara@example.com","pv":"3","scope":"openid auth2.me","role":"{{roleClaim}}","iss":"x","aud":"blog"}""");

    private static (IAuth2WebService Web, IAuth2ApiClientService Api, IAuthenticationService Auth, DefaultHttpContext Http) Make(UiMode mode = UiMode.Self, AccessLoadMode access = AccessLoadMode.Lazy, bool saveTokens = true)
    {
        var api = Substitute.For<IAuth2ApiClientService>();
        var auth = Substitute.For<IAuthenticationService>();
        var services = new ServiceCollection().AddSingleton(auth).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var cookie = new Auth2CookieOptions { AccessMode = access, SaveTokens = saveTokens, LoginPath = "/my/login" };
        var loader = new ClientAccessLoader(api, Substitute.For<ICurrentUserStore>(), MsOptions.Create(cookie), MsOptions.Create(new Auth2CurrentUserOptions()),
            MsOptions.Create(new Auth2ClientOptions { ClientId = "blog" }), NullLogger<ClientAccessLoader>.Instance);
        var web = new WebService(api, loader, MsOptions.Create(cookie), MsOptions.Create(new Auth2ClientOptions()), MsOptions.Create(new Auth2AuthFlags { Mode = mode }));
        return (web, api, auth, http);
    }

    [Fact]
    public async Task Login_SignsInWithTheCookie_ClaimsFromTheToken_AndSavedTokens()
    {
        var (web, api, auth, http) = Make();
        api.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Success(new LoginResponse(Jwt(), "refresh", "idt", 900)));
        var result = await web.LoginAsync(http, new LoginRequest("sara", "x"), isPersistent: true);
        result.IsSuccess.ShouldBeTrue();

        await auth.Received(1).SignInAsync(http, Auth2AuthSchemes.Cookie,
            Arg.Is<ClaimsPrincipal>(p => p.FindFirst("sub")!.Value == "11111111-1111-1111-1111-111111111111" && p.FindFirst("email")!.Value == "sara@example.com"
                && p.FindFirst("pv")!.Value == "3" && p.FindFirst("scope")!.Value == "openid auth2.me" && !p.HasClaim(c => c.Type == "iss" || c.Type == "aud" || c.Type == "exp")),
            Arg.Is<AuthenticationProperties>(a => a.IsPersistent && a.GetTokenValue("access_token") != null && a.GetTokenValue("refresh_token") == "refresh" && a.GetTokenValue("id_token") == "idt"));
    }

    [Fact]
    public async Task RolesInTheToken_AreNotCopiedToTheCookie_UnlessTheModeIsRolesInCookie()
    {
        var (web, api, auth, http) = Make();
        api.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Success(new LoginResponse(Jwt(), null, null, 900)));
        await web.LoginAsync(http, new LoginRequest("sara", "x"));
        await auth.Received().SignInAsync(http, Auth2AuthSchemes.Cookie, Arg.Is<ClaimsPrincipal>(p => !p.HasClaim(c => c.Type == "role")), Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task TokensAreNotSaved_WhenSaveTokensIsOff()
    {
        var (web, api, auth, http) = Make(saveTokens: false);
        api.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Success(new LoginResponse(Jwt(), "r", null, 900)));
        await web.LoginAsync(http, new LoginRequest("sara", "x"));
        await auth.Received().SignInAsync(http, Auth2AuthSchemes.Cookie, Arg.Any<ClaimsPrincipal>(), Arg.Is<AuthenticationProperties>(a => a.GetTokenValue("access_token") == null));
    }

    [Fact]
    public async Task FailedLogin_AndPendingTwoFactor_MakeNoCookie()
    {
        var (web, api, auth, http) = Make();
        api.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Failure("invalid_credentials"));
        (await web.LoginAsync(http, new LoginRequest("sara", "bad"))).Error!.Code.ShouldBe("invalid_credentials");

        api.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Success(new LoginResponse(null, null, null, null, true, "tft")));
        var pending = await web.LoginAsync(http, new LoginRequest("sara", "x"));
        pending.Value!.TwoFactorRequired.ShouldBeTrue();
        await auth.DidNotReceive().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());

        api.TwoFactorLoginAsync(Arg.Any<TwoFactorRequest>(), Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Success(new LoginResponse(Jwt(), "r", null, 900)));
        (await web.TwoFactorLoginAsync(http, new TwoFactorRequest("tft", "123456"))).IsSuccess.ShouldBeTrue();
        await auth.Received(1).SignInAsync(http, Auth2AuthSchemes.Cookie, Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task Otp_SignsIn()
    {
        var (web, api, auth, http) = Make();
        api.OtpLoginAsync("sara@example.com", "123456", Arg.Any<CancellationToken>()).Returns(Result<LoginResponse>.Success(new LoginResponse(Jwt(), "r", null, 900)));
        (await web.OtpLoginAsync(http, "sara@example.com", "123456")).IsSuccess.ShouldBeTrue();
        await auth.Received(1).SignInAsync(http, Auth2AuthSchemes.Cookie, Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken_AndRemovesTheCookie()
    {
        var (web, api, auth, http) = Make();
        auth.AuthenticateAsync(http, Auth2AuthSchemes.Cookie).Returns(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("x")),
            new AuthenticationProperties(new Dictionary<string, string?> { [".Token.refresh_token"] = "the-refresh" }), Auth2AuthSchemes.Cookie)));
        api.LogoutAsync("the-refresh", Arg.Any<CancellationToken>()).Returns(Result.Success());
        await web.LogoutAsync(http);
        await api.Received(1).LogoutAsync("the-refresh", Arg.Any<CancellationToken>());
        await auth.Received(1).SignOutAsync(http, Auth2AuthSchemes.Cookie, Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task Challenge_Hosted_StartsOidc_Self_GoesToTheLoginPath_AndUnsafeReturnUrlsAreDropped()
    {
        var (hosted, _, auth, http) = Make(UiMode.Hosted);
        await hosted.ChallengeAsync(http, "/orders?x=1");
        await auth.Received(1).ChallengeAsync(http, Auth2AuthSchemes.Oidc, Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/orders?x=1"));
        await hosted.ChallengeAsync(http, "https://evil.test");
        await auth.Received(1).ChallengeAsync(http, Auth2AuthSchemes.Oidc, Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/"));

        var (self, _, _, http2) = Make(UiMode.Self);
        await self.ChallengeAsync(http2, "/orders");
        http2.Response.Headers.Location.ToString().ShouldBe("/my/login?returnUrl=%2Forders");
    }
}

public class BlazorServiceTests
{
    private static (IAuth2BlazorService Service, IAuth2AccessProvider Access) Make(ClaimsPrincipal? user)
    {
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync().Returns(new AuthenticationState(user ?? new ClaimsPrincipal(new ClaimsIdentity())));
        var access = Substitute.For<IAuth2AccessProvider>();
        access.GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(new Auth2Access(["Editor"], ["orders.read"], ["finance"], 3, [], []));
        return (new BlazorService(provider, access), access);
    }

    private static ClaimsPrincipal Sara() => new(new ClaimsIdentity([new Claim("sub", "11111111-1111-1111-1111-111111111111"), new Claim("preferred_username", "sara"), new Claim("email", "s@e.com")], "x"));

    [Fact]
    public async Task SignedInUser_HasAccessData_AndHelpersAnswer()
    {
        var (service, _) = Make(Sara());
        var user = await service.GetUserAsync();
        (user.IsAuthenticated, user.UserName, user.Roles.Single(), user.Groups.Single()).ShouldBe((true, "sara", "Editor", "finance"));
        (await service.HasPermissionAsync("Orders.Read")).ShouldBeTrue();
        (await service.HasPermissionAsync("orders.write")).ShouldBeFalse();
        (await service.HasRoleAsync("editor")).ShouldBeTrue();
        (await service.IsInGroupAsync("FINANCE")).ShouldBeTrue();
    }

    [Fact]
    public async Task Anonymous_HasNothing_AndNoServerCall()
    {
        var (service, access) = Make(null);
        (await service.GetUserAsync()).ShouldBe(Auth2BlazorUser.Anonymous);
        (await service.HasPermissionAsync("orders.read")).ShouldBeFalse();
        await access.DidNotReceive().GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public string? Last { get; private set; }
        public bool Forced { get; private set; }
        public FakeNavigation() => Initialize("https://app.test/", "https://app.test/orders/5?tab=2");
        protected override void NavigateToCore(string uri, bool forceLoad) { Last = uri; Forced = forceLoad; }
    }

    [Fact]
    public void Login_ComesBackToTheCurrentPage_ThroughTheNavigationManager()
    {
        var (service, _) = Make(null);
        var nav = new FakeNavigation();
        service.NavigateToLogin(nav);
        nav.Last.ShouldBe("auth2/login?returnUrl=%2Forders%2F5%3Ftab%3D2");
        nav.Forced.ShouldBeTrue();
        service.NavigateToLogin(nav, "https://evil.test");
        nav.Last.ShouldBe("auth2/login?returnUrl=%2F");
        service.NavigateToLogout(nav);
        nav.Last.ShouldBe("auth2/logout");
    }
}
