using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.Tests;

public class ApiClientServiceTests
{
    private static (IAuth2ApiClientService Api, FakeServer Server) Start(Func<HttpRequestMessage, string, HttpResponseMessage> api, Action<SystemX.Auth2.Client.DependencyInjection.IAuth2ClientBuilder>? configure = null)
    {
        var server = new FakeServer
        {
            Respond = (r, body) => Task.FromResult(r.RequestUri!.AbsolutePath == "/connect/token" && body.Contains("client_credentials") ? FakeServer.Token() : api(r, body)),
        };
        return (TestHost.Build(server, configure).GetRequiredService<IAuth2ApiClientService>(), server);
    }

    [Fact]
    public async Task Register_SendsTheBodyWithAMachineToken_AndReadsTheAnswer()
    {
        var (api, server) = Start((_, _) => FakeServer.Json(HttpStatusCode.OK, """{"userId":"0198a1b2-0000-7000-8000-000000000009","userWasCreated":true,"joinedProject":true,"confirmationRequired":false}"""));
        var result = await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", null, "Pass-12345!"));
        result.IsSuccess.ShouldBeTrue();
        result.Value!.UserWasCreated.ShouldBeTrue();
        var call = server.Calls.Last();
        (call.Path, call.Authorization).ShouldBe((Auth2Routes.Register, "Bearer machine-token"));
        call.Body.ShouldContain("\"email\":\"sara@example.com\"");
        server.Calls[0].Body.ShouldContain("scope=auth2.register");
    }

    [Fact]
    public async Task ServerErrors_BecomeResultFailures_WithTheServerCode()
    {
        var (api, _) = Start((_, _) => FakeServer.Json(HttpStatusCode.Forbidden, """{"code":"registration_disabled","message":"Registration is off."}"""));
        var result = await api.RegisterAsync(new RegisterRequest("a", null, null, "x"));
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("registration_disabled");
        result.Error.Message.ShouldBe("Registration is off.");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ServerDown_IsServerUnavailable_NotAnException(int status)
    {
        var (api, _) = Start((_, _) => FakeServer.Json((HttpStatusCode)status, "oops"));
        (await api.ForgotPasswordAsync(new ForgotPasswordRequest("sara"))).Error!.Code.ShouldBe(Auth2ClientErrorCodes.ServerUnavailable);
    }

    [Fact]
    public async Task NetworkError_AndAWrongSecret_AreResults_NotExceptions()
    {
        var down = new FakeServer { Respond = (_, _) => throw new HttpRequestException("x") };
        var api = TestHost.Build(down).GetRequiredService<IAuth2ApiClientService>();
        (await api.RequestOtpAsync(new OtpRequest("a@b.c"))).Error!.Code.ShouldBe(Auth2ClientErrorCodes.ServerUnavailable);

        var refused = new FakeServer { Respond = (_, _) => Task.FromResult(FakeServer.Json(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_code":"invalid_client"}""")) };
        var r = await TestHost.Build(refused).GetRequiredService<IAuth2ApiClientService>().RequestOtpAsync(new OtpRequest("a@b.c"));
        r.Error!.Code.ShouldBe("invalid_client");
        r.Error.Message!.ShouldNotContain(TestHost.Secret);
    }

    [Fact]
    public async Task Login_SendsThePasswordGrantWithTheClient_AndReturnsTokens()
    {
        var (api, server) = Start((_, body) => FakeServer.Json(HttpStatusCode.OK, """{"access_token":"a","refresh_token":"r","id_token":"i","expires_in":900}"""));
        var result = await api.LoginAsync(new LoginRequest("sara", "Pass-12345!"));
        result.Value.ShouldBe(new LoginResponse("a", "r", "i", 900));
        var body = server.Calls.Last().Body;
        body.ShouldContain("grant_type=password");
        body.ShouldContain("username=sara");
        body.ShouldContain("client_id=blog");
    }

    [Fact]
    public async Task Login_WhenTwoFactorIsNeeded_Succeeds_WithAToken_AndTheSecondStepWorks()
    {
        var step = 0;
        var (api, server) = Start((_, body) => ++step == 1
            ? FakeServer.Json(HttpStatusCode.BadRequest, """{"error":"two_factor_required","error_code":"two_factor_required","two_factor_token":"tft"}""")
            : FakeServer.Json(HttpStatusCode.OK, """{"access_token":"a","refresh_token":"r","expires_in":900}"""));
        var first = await api.LoginAsync(new LoginRequest("sara", "x"));
        first.Value!.TwoFactorRequired.ShouldBeTrue();
        first.Value.TwoFactorToken.ShouldBe("tft");
        var second = await api.TwoFactorLoginAsync(new TwoFactorRequest("tft", "123456"));
        second.Value!.AccessToken.ShouldBe("a");
        server.Calls.Last().Body.ShouldContain("grant_type=urn%3Asystemx%3Aauth2%3Agrant%3Atwo_factor");
        server.Calls.Last().Body.ShouldContain("two_factor_token=tft");
    }

    [Fact]
    public async Task Login_WrongPassword_IsAResultWithTheCode_AndOtpLoginUsesItsGrant()
    {
        var (api, server) = Start((_, _) => FakeServer.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_code":"invalid_credentials"}"""));
        (await api.LoginAsync(new LoginRequest("sara", "bad"))).Error!.Code.ShouldBe("invalid_credentials");
        await api.OtpLoginAsync("sara@example.com", "123456");
        server.Calls.Last().Body.ShouldContain("grant_type=urn%3Asystemx%3Aauth2%3Agrant%3Aotp");
        server.Calls.Last().Body.ShouldContain("code=123456");
    }

    [Fact]
    public async Task Refresh_AndLogout_UseTheTokenAndRevokeEndpoints()
    {
        var (api, server) = Start((_, _) => FakeServer.Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":900}"""));
        (await api.WithTokens(null, "r1").RefreshAsync()).Value!.RefreshToken.ShouldBe("r2");
        server.Calls.Last().Body.ShouldContain("grant_type=refresh_token");
        (await api.WithTokens(null, "r2").LogoutAsync()).IsSuccess.ShouldBeTrue();
        (server.Calls.Last().Path, server.Calls.Last().Body.Contains("token=r2")).ShouldBe((Auth2Routes.Revoke, true));
    }

    [Fact]
    public async Task WithoutATokenAndWithoutARequest_APersonCallIsInvalidToken_AndTheServerIsNotAsked()
    {
        var (api, server) = Start((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        (await api.GetMeAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await api.ChangePasswordAsync(new ChangePasswordRequest("old", "new"))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await api.RefreshAsync()).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await api.LogoutAsync()).IsSuccess.ShouldBeTrue();   // no refresh token: nothing to revoke
        server.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task UserTokenCalls_UseTheGivenToken_NotAMachineToken()
    {
        var (api, server) = Start((r, _) => r.RequestUri!.AbsolutePath == Auth2Routes.Me
            ? FakeServer.Json(HttpStatusCode.OK, """{"userId":"0198a1b2-0000-7000-8000-000000000009","userName":"sara","roles":["Editor"],"permissions":["a.b"],"groups":[],"scopes":["auth2.me"],"accessVersion":3}""")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        var me = await api.WithTokens("user-token").GetMeAsync();
        me.Value!.Permissions.ShouldBe(["a.b"]);
        server.Calls.Last().Authorization.ShouldBe("Bearer user-token");
        (await api.WithTokens("user-token").ChangePasswordAsync(new ChangePasswordRequest("old", "new"))).IsSuccess.ShouldBeTrue();
        server.Calls.ShouldNotContain(c => c.Path == "/connect/token");   // no machine token was needed
    }

    [Fact]
    public async Task SyncAndPull_UseTheirScopes()
    {
        var (api, server) = Start((r, _) => r.Method == HttpMethod.Post
            ? FakeServer.Json(HttpStatusCode.OK, """{"added":1,"updated":0,"unchanged":0,"addedKeys":["a.b"]}""")
            : FakeServer.Json(HttpStatusCode.OK, r.RequestUri!.AbsolutePath == Auth2Routes.Groups
                ? """[{"name":"finance","displayName":null,"description":null,"roles":["Editor"],"permissions":[]}]"""
                : """[{"key":"a.b","displayName":null,"description":null,"group":null,"isObsolete":false}]"""));
        (await api.SyncPermissionsAsync(new PermissionSyncRequest([new PermissionDefinition("a.b")]))).Value!.Added.ShouldBe(1);
        (await api.SyncRolesAsync(new RoleSyncRequest([new RoleDefinition("Editor")]))).IsSuccess.ShouldBeTrue();
        (await api.GetPermissionsAsync()).Value!.Single().Key.ShouldBe("a.b");
        (await api.GetGroupsAsync()).Value!.Single().Roles.ShouldBe(["Editor"]);
        var scopes = server.Calls.Where(c => c.Path == "/connect/token").Select(c => c.Body).ToList();
        scopes.ShouldContain(b => b.Contains("scope=auth2.permissions.sync"));
        scopes.ShouldContain(b => b.Contains("scope=auth2.roles.sync"));
        scopes.ShouldContain(b => b.Contains("scope=auth2.groups.read"));
    }

    [Fact]
    public async Task WithResilience_ReadsAreRetried_ButRegisterAndLoginAreNot()
    {
        var reads = 0;
        var (api, server) = Start((r, _) =>
        {
            if (r.Method == HttpMethod.Get) return ++reads < 3 ? FakeServer.Json(HttpStatusCode.ServiceUnavailable, "{}") : FakeServer.Json(HttpStatusCode.OK, "[]");
            return FakeServer.Json(HttpStatusCode.ServiceUnavailable, "{}");
        }, b => b.EnableResilience());

        (await api.GetPermissionsAsync()).IsSuccess.ShouldBeTrue();                 // 503, 503, 200
        server.CountTo(Auth2Routes.Permissions).ShouldBe(3);

        (await api.RegisterAsync(new RegisterRequest("a", null, null, "x"))).IsSuccess.ShouldBeFalse();
        server.CountTo(Auth2Routes.Register).ShouldBe(1);                            // never repeated
        (await api.LoginAsync(new LoginRequest("a", "b"))).IsSuccess.ShouldBeFalse();
        server.CountTo("/connect/token").ShouldBe(3);                                // 1 machine token + 1 token for the register call + 1 login
    }

    [Fact]
    public async Task ARejectedMachineToken_IsRenewedOnce_ForCallsThatAreSafeToRepeat()
    {
        var calls = 0;
        var (api, server) = Start((r, _) => ++calls == 1 ? FakeServer.Json(HttpStatusCode.Unauthorized, """{"code":"invalid_token"}""") : FakeServer.Json(HttpStatusCode.OK, "[]"));
        (await api.GetRolesAsync()).IsSuccess.ShouldBeTrue();
        server.CountTo("/connect/token").ShouldBe(2);
    }

    [Fact]
    public async Task Handshake_UpdatesTheState()
    {
        var (api, _) = Start((_, _) => FakeServer.Json(HttpStatusCode.NotFound, """{"code":"project_not_found"}"""), b => b.ServerWithMultiProject("blog"));
        var result = await api.HandshakeAsync();
        result.Error!.Code.ShouldBe("project_not_found");
    }
}
