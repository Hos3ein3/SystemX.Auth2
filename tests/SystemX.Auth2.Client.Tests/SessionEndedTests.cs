using SystemX.Auth2.Client.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.Tests;

/// <summary>The user signed out on the server while this application still has a cookie: not an error, the session is over.</summary>
public class SessionEndedTests
{
    private readonly IAuth2WebClientService _web = Substitute.For<IAuth2WebClientService>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();

    private DefaultHttpContext Context(Action<DefaultHttpContext>? setup = null)
    {
        var schemes = Substitute.For<IAuthenticationSchemeProvider>();
        schemes.GetSchemeAsync(Auth2AuthSchemes.Cookie).Returns(new AuthenticationScheme(Auth2AuthSchemes.Cookie, null, typeof(IAuthenticationHandler)));
        var services = new ServiceCollection().AddSingleton(_web).AddSingleton(_authentication).AddSingleton(schemes).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Path = "/Home/Privacy";
        setup?.Invoke(http);
        return http;
    }

    private static Task Run(HttpContext http, string? code)
    {
        var middleware = new Auth2SessionEndedMiddleware(_ => code is null ? Task.CompletedTask : throw new Auth2Exception(401, code, "x"));
        return middleware.InvokeAsync(http);
    }

    [Fact]
    public async Task InvalidToken_InABrowser_RemovesTheCookie_AndSendsTheUserToSignIn()
    {
        var http = Context();
        await Run(http, Auth2ErrorCodes.InvalidToken);
        await _authentication.Received(1).SignOutAsync(http, Auth2AuthSchemes.Cookie, Arg.Any<AuthenticationProperties?>());
        await _web.Received(1).ChallengeAsync("/Home/Privacy");
    }

    [Fact]
    public async Task InvalidToken_ForAnApiCall_Answers401_AndNoRedirect()
    {
        var http = Context(h => h.Request.Headers.Authorization = "Bearer abc");
        await Run(http, Auth2ErrorCodes.InvalidToken);
        http.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        await _web.DidNotReceiveWithAnyArgs().ChallengeAsync(default);
    }

    [Fact]
    public async Task OtherErrors_AreNotHidden()
    {
        var http = Context();
        await Should.ThrowAsync<Auth2Exception>(() => Run(http, Auth2ErrorCodes.ServerError));
        await _authentication.DidNotReceiveWithAnyArgs().SignOutAsync(default!, default, default);
    }

    [Fact]
    public async Task WithoutAnError_NothingHappens()
    {
        var http = Context();
        await Run(http, null);
        http.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        await _web.DidNotReceiveWithAnyArgs().ChallengeAsync(default);
    }
}
