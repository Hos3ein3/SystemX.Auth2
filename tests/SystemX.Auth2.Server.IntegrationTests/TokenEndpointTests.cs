using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class TokenEndpointTests : IdentityTestBase
{
    private async Task<(HttpStatusCode Status, JsonElement Body)> TokenAsync(Browser b, IDictionary<string, string> form, string clientId = "appa", string? secret = null)
    {
        var all = new Dictionary<string, string>(form) { ["client_id"] = clientId, ["client_secret"] = secret ?? (clientId == "appa" ? ASecret : BSecret) };
        var r = await b.PostFormAsync("/connect/token", all);
        return (r.StatusCode, await r.JsonAsync());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> PasswordGrantAsync(Browser b, string user, string password, string clientId = "appa", string scope = "openid offline_access roles auth2.me") =>
        TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "password", ["username"] = user, ["password"] = password, ["scope"] = scope }, clientId);

    private async Task PrepareAsync(bool selfUi = true, Action<TestEdit>? more = null)
    {
        await SetUpAsync();
        await CreateUserInAsync("appa");
        await UpdateSettingsAsync(A, s => { s.App.AllowSelfUiLogin = selfUi; more?.Invoke(s); });
    }

    // ---------------------------------------------------------------- client credentials

    [Fact]
    public async Task ClientCredentials_ReturnsMachineToken_WithoutUserClaims()
    {
        await PrepareAsync(selfUi: false);
        var b = new Browser(Factory);
        var (status, body) = await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "auth2.client.handshake auth2.permissions.sync" });
        status.ShouldBe(HttpStatusCode.OK);
        body.TryGetProperty("refresh_token", out _).ShouldBeFalse();

        var claims = HttpExtensions.JwtPayload(body.Str("access_token"));
        claims.Str("machine").ShouldBe("true");
        claims.Str("sub").ShouldBe("appa");
        claims.Str("project_id").ShouldBe(A.Id.ToString("D"));
        claims.Values("scope").SelectMany(x => x.Split(' ')).ShouldBe(["auth2.client.handshake", "auth2.permissions.sync"], ignoreOrder: true);
        claims.TryGetProperty("sid", out _).ShouldBeFalse();
        claims.TryGetProperty("amr", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ClientCredentials_ScopeNotAllowedForTheApplication_IsRefused()
    {
        await PrepareAsync(selfUi: false);
        var b = new Browser(Factory);
        foreach (var scope in new[] { "auth2.admin", "auth2.admin.global", "unknown.scope" })
        {
            var (status, body) = await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = scope });
            status.ShouldBe(HttpStatusCode.BadRequest);
            body.Str("error").ShouldBeOneOf("invalid_scope", "invalid_request");   // not allowed for this client, or not a known scope
            body.TryGetProperty("access_token", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ClientCredentials_WrongSecret_IsRefused()
    {
        await PrepareAsync(selfUi: false);
        var (status, body) = await TokenAsync(new Browser(Factory), new Dictionary<string, string> { ["grant_type"] = "client_credentials" }, secret: "wrong");
        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.Str("error").ShouldBe("invalid_client");
    }

    [Fact]
    public async Task ClientCredentials_DisabledApplication_IsRefused()
    {
        await PrepareAsync(selfUi: false);
        await DisableAsync("appa");
        var (status, body) = await TokenAsync(new Browser(Factory), new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error_code").ShouldBe(Auth2ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task ClientSecret_StillWorks_AfterSettingsAreUpdated_AndRotationReplacesIt()
    {
        await PrepareAsync(selfUi: false);   // settings were updated once already in PrepareAsync
        var b = new Browser(Factory);
        (await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "client_credentials" })).Status.ShouldBe(HttpStatusCode.OK);

        string newSecret;
        await using (var scope = NewScope())
            newSecret = await scope.ServiceProvider.GetRequiredService<IApplicationService>().RotateSecretAsync("appa");
        newSecret.ShouldNotBe(ASecret);
        (await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "client_credentials" })).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "client_credentials" }, secret: newSecret)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClientSecret_IsStoredHashed_NotInPlainText()
    {
        await PrepareAsync(selfUi: false);
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var stored = await db.Database.SqlQuery<string>($"SELECT client_secret AS \"Value\" FROM applications WHERE client_id = 'appa'").SingleAsync();
        stored.ShouldNotBeNullOrEmpty();
        stored.ShouldNotContain(ASecret);
    }

    [Fact]
    public async Task ClientCredentials_ClientOfAppA_CannotBeUsedToTalkAsAppB()
    {
        await PrepareAsync(selfUi: false);
        var (_, body) = await TokenAsync(new Browser(Factory), new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "auth2.client.handshake" });
        var claims = HttpExtensions.JwtPayload(body.Str("access_token"));
        claims.Str("project_id").ShouldBe(A.Id.ToString("D"));
        claims.Str("project_id").ShouldNotBe(B.Id.ToString("D"));
        claims.Values("aud").ShouldBe(["appa"]);
        // appa's secret does not open appb
        (await TokenAsync(new Browser(Factory), new Dictionary<string, string> { ["grant_type"] = "client_credentials" }, clientId: "appb", secret: ASecret)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- password grant

    [Fact]
    public async Task PasswordGrant_IsOffByDefault()
    {
        await PrepareAsync(selfUi: false);
        var (status, body) = await PasswordGrantAsync(new Browser(Factory), "sara", PassA);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error").ShouldBe("unauthorized_client");
    }

    [Fact]
    public async Task PasswordGrant_WhenAllowed_IssuesTokens_WithRolesOfThisApplicationOnly()
    {
        await PrepareAsync();
        await using (var scope = ScopeFor("appa"))
        {
            var roles = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<AppRole>>();
            await roles.CreateAsync(new AppRole("writer") { ProjectId = A.Id });
        }
        await InAsync("appa", async m => await m.AddToRoleAsync((await m.FindByNameAsync("sara"))!, "writer"));

        var (status, body) = await PasswordGrantAsync(new Browser(Factory), "sara", PassA);
        status.ShouldBe(HttpStatusCode.OK);
        body.TryGetProperty("refresh_token", out _).ShouldBeTrue();
        var claims = HttpExtensions.JwtPayload(body.Str("access_token"));
        claims.Values("role").ShouldBe(["writer"]);
        claims.Values("amr").ShouldBe(["pwd"]);
        claims.Str("preferred_username").ShouldBe("sara");
    }

    [Fact]
    public async Task PasswordGrant_UnknownUserAndWrongPassword_GiveIdenticalResponses()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var wrong = await PasswordGrantAsync(b, "sara", "Wrong-Password-1");
        var unknown = await PasswordGrantAsync(b, "nobody", "Wrong-Password-1");
        wrong.Status.ShouldBe(HttpStatusCode.BadRequest);
        wrong.Body.ToString().ShouldBe(unknown.Body.ToString());
        wrong.Body.Str("error").ShouldBe("invalid_grant");
        wrong.Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task PasswordGrant_ResponsesAreNotCacheable()
    {
        await PrepareAsync();
        var r = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "sara", ["password"] = PassA, ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        r.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task PasswordGrant_UserOfAppAIsNotAcceptedByAppB()
    {
        await PrepareAsync();
        await UpdateSettingsAsync(B, s => s.App.AllowSelfUiLogin = true);
        var (status, body) = await PasswordGrantAsync(new Browser(Factory), "sara", PassA, clientId: "appb");
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task PasswordGrant_LockoutAfterRepeatedFailures()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        for (var i = 0; i < 5; i++) await PasswordGrantAsync(b, "sara", "Wrong-Password-1");
        var (_, body) = await PasswordGrantAsync(b, "sara", PassA);
        body.Str("error_code").ShouldBe(Auth2ErrorCodes.LockedOut);
    }

    [Fact]
    public async Task PasswordGrant_DisabledApplication_IsRefused()
    {
        await PrepareAsync();
        await DisableAsync("appa");
        var (_, body) = await PasswordGrantAsync(new Browser(Factory), "sara", PassA);
        body.Str("error_code").ShouldBe(Auth2ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task PasswordGrant_TurningTheSettingOffAgain_RemovesThePermission()
    {
        await PrepareAsync();
        (await PasswordGrantAsync(new Browser(Factory), "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);
        await UpdateSettingsAsync(A, s => s.App.AllowSelfUiLogin = false);
        var (status, body) = await PasswordGrantAsync(new Browser(Factory), "sara", PassA);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error").ShouldBe("unauthorized_client");
    }

    // ---------------------------------------------------------------- two-factor grant

    [Fact]
    public async Task TwoFactorGrant_FirstStepAsksForIt_SecondStepIssuesTokens()
    {
        await PrepareAsync();
        string key;
        List<string> codes;
        await using (var scope = ScopeFor("appa"))
        {
            var m = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var u = (await m.FindByNameAsync("sara"))!;
            await m.ResetAuthenticatorKeyAsync(u);
            key = (await m.GetAuthenticatorKeyAsync(u))!;
            await m.SetTwoFactorEnabledAsync(u, true);
            codes = (await m.GenerateNewTwoFactorRecoveryCodesAsync(u, 2))!.ToList();
        }
        var b = new Browser(Factory);

        var (status, body) = await PasswordGrantAsync(b, "sara", PassA);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error").ShouldBe("two_factor_required");
        body.Str("error_code").ShouldBe(Auth2ErrorCodes.TwoFactorRequired);
        var token = body.Str("two_factor_token");
        body.TryGetProperty("access_token", out _).ShouldBeFalse();

        (await TwoFactorAsync(b, token, code: "000000")).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
        var ok = await TwoFactorAsync(b, token, code: Totp.Now(key));
        ok.Status.ShouldBe(HttpStatusCode.OK);
        HttpExtensions.JwtPayload(ok.Body.Str("access_token")).Values("amr").ShouldBe(["pwd", "otp"], ignoreOrder: true);

        var again = await PasswordGrantAsync(b, "sara", PassA);
        var viaRecovery = await TwoFactorAsync(b, again.Body.Str("two_factor_token"), recovery: codes[0]);
        viaRecovery.Status.ShouldBe(HttpStatusCode.OK);
        var reuse = await PasswordGrantAsync(b, "sara", PassA);
        (await TwoFactorAsync(b, reuse.Body.Str("two_factor_token"), recovery: codes[0])).Body.Str("error_code").ShouldBe(Auth2ErrorCodes.InvalidOtp);
    }

    [Fact]
    public async Task TwoFactorGrant_NotAvailable_WhenSelfUiLoginIsOff()
    {
        await PrepareAsync(selfUi: false);
        var (status, body) = await TwoFactorAsync(new Browser(Factory), "x", code: "123456");
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.Str("error").ShouldBe("unauthorized_client");
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> TwoFactorAsync(Browser b, string token, string? code = null, string? recovery = null)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = Auth2GrantTypes.TwoFactor, ["two_factor_token"] = token, ["scope"] = "openid" };
        if (code is not null) form["code"] = code;
        if (recovery is not null) form["recovery_code"] = recovery;
        return TokenAsync(b, form);
    }

    // ---------------------------------------------------------------- refresh tokens

    [Fact]
    public async Task Refresh_Rotates_AndReuseOfAnOldTokenRevokesTheWholeChain()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var first = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        var r1 = first.Str("refresh_token");

        var second = await RefreshAsync(b, r1);
        second.Status.ShouldBe(HttpStatusCode.OK);
        var r2 = second.Body.Str("refresh_token");
        r2.ShouldNotBe(r1);                                    // rotation
        HttpExtensions.JwtPayload(second.Body.Str("access_token")).Str("preferred_username").ShouldBe("sara");

        var replay = await RefreshAsync(b, r1);                 // reuse of the old token
        replay.Status.ShouldBe(HttpStatusCode.BadRequest);
        replay.Body.Str("error").ShouldBe("invalid_grant");

        (await RefreshAsync(b, r2)).Status.ShouldBe(HttpStatusCode.BadRequest);   // the new one was revoked too (reuse detection)
    }

    [Fact]
    public async Task Refresh_UpdatesRolesInTheNewAccessToken()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var first = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        HttpExtensions.JwtPayload(first.Str("access_token")).Values("role").ShouldBeEmpty();

        await using (var scope = ScopeFor("appa"))
            await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<AppRole>>().CreateAsync(new AppRole("editor") { ProjectId = A.Id });
        await InAsync("appa", async m => await m.AddToRoleAsync((await m.FindByNameAsync("sara"))!, "editor"));

        var refreshed = await RefreshAsync(b, first.Str("refresh_token"));
        HttpExtensions.JwtPayload(refreshed.Body.Str("access_token")).Values("role").ShouldBe(["editor"]);
        HttpExtensions.JwtPayload(refreshed.Body.Str("access_token")).Values("amr").ShouldBe(["pwd"]);   // the sign-in method is kept
    }

    [Fact]
    public async Task Refresh_IsRefused_ForALockedOrDisabledUser()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var first = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            (await db.Users.SingleAsync(u => u.UserName == "sara")).IsActive = false;
            await db.SaveChangesAsync();
        }
        var r = await RefreshAsync(b, first.Str("refresh_token"));
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_TokenOfAppA_IsRefusedByAppB()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var first = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        var r = await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = first.Str("refresh_token") }, clientId: "appb");
        r.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(Browser b, string refreshToken) =>
        TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken });

    // ---------------------------------------------------------------- single active session

    [Fact]
    public async Task SingleActiveSession_NewLoginRevokesTheOldSession_OfThisApplicationOnly()
    {
        await PrepareAsync(more: s => s.Sessions.SingleActiveSession = true);
        var user = await InAsync("appa", async m => (await m.FindByNameAsync("sara"))!);
        await JoinAsync(user, B);
        await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB));
        await UpdateSettingsAsync(B, s => { s.App.AllowSelfUiLogin = true; s.Sessions.SingleActiveSession = true; });

        var b = new Browser(Factory);
        var a1 = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        var b1 = (await PasswordGrantAsync(b, "sara", PassB, clientId: "appb")).Body;

        // a second login in appa kills the first appa session, but not the appb session
        var a2 = await PasswordGrantAsync(b, "sara", PassA);
        a2.Status.ShouldBe(HttpStatusCode.OK);
        (await RefreshAsync(b, a1.Str("refresh_token"))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await TokenAsync(b, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = b1.Str("refresh_token") }, clientId: "appb")).Status.ShouldBe(HttpStatusCode.OK);
        (await RefreshAsync(b, a2.Body.Str("refresh_token"))).Status.ShouldBe(HttpStatusCode.OK);

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.AuditEvents.CountAsync(e => e.Action == "session.revoked_others" && e.ProjectId == A.Id)).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SingleActiveSession_OldAccessTokenStopsWorking_AtUserInfo()
    {
        await PrepareAsync(more: s => s.Sessions.SingleActiveSession = true);
        var b = new Browser(Factory);
        var first = (await PasswordGrantAsync(b, "sara", PassA)).Body.Str("access_token");
        (await b.GetAsync("/connect/userinfo", first)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = (await PasswordGrantAsync(b, "sara", PassA)).Body.Str("access_token");
        (await b.GetAsync("/connect/userinfo", first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await b.GetAsync("/connect/userinfo", second)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SingleActiveSession_Off_KeepsBothSessions()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var a1 = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        await PasswordGrantAsync(b, "sara", PassA);
        (await RefreshAsync(b, a1.Str("refresh_token"))).Status.ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- userinfo and misc

    [Fact]
    public async Task UserInfo_WithoutToken_IsUnauthorized() =>
        (await new Browser(Factory).GetAsync("/connect/userinfo")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task UserInfo_ReturnsOnlyClaimsOfTheGrantedScopes()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var token = (await PasswordGrantAsync(b, "sara", PassA, scope: "openid email")).Body.Str("access_token");
        var info = await (await b.GetAsync("/connect/userinfo", token)).JsonAsync();
        info.TryGetProperty("email", out _).ShouldBeTrue();
        info.TryGetProperty("preferred_username", out _).ShouldBeFalse();   // "profile" was not requested
    }

    [Fact]
    public async Task Discovery_PublishesTheEndpointsAndKeys()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var doc = await (await b.GetAsync("/.well-known/openid-configuration")).JsonAsync();
        doc.Str("token_endpoint").ShouldEndWith("/connect/token");
        doc.Str("authorization_endpoint").ShouldEndWith("/connect/authorize");
        doc.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()).ShouldContain("client_credentials");
        doc.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()).ShouldBe(["S256"]);
        var keys = await (await b.GetAsync(new Uri(doc.Str("jwks_uri")).PathAndQuery)).JsonAsync();
        keys.GetProperty("keys").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Revocation_OfARefreshToken_StopsIt()
    {
        await PrepareAsync();
        var b = new Browser(Factory);
        var first = (await PasswordGrantAsync(b, "sara", PassA)).Body;
        var revoke = await b.PostFormAsync("/connect/revoke", new Dictionary<string, string>
        {
            ["token"] = first.Str("refresh_token"), ["token_type_hint"] = "refresh_token", ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefreshAsync(b, first.Str("refresh_token"))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task DisableAsync(string projectKey)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.Projects.SingleAsync(a => a.ProjectKey == projectKey)).IsEnabled = false;
        await db.SaveChangesAsync();
    }
}
