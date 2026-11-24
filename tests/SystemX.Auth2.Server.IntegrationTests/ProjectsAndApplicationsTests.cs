using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Project = tenant, Application = client of a project (the OpenIddict application). One table, one manager, one access rule.</summary>
public class ProjectsAndApplicationsTests : Phase6Base
{
    private async Task<(ClientApplicationInfo App, string Secret)> AddApplicationAsync(Project project, string name, bool selfUi = true)
    {
        await using var scope = NewScope();
        var created = await scope.ServiceProvider.GetRequiredService<IApplicationService>().CreateAsync(project.Id,
            new ApplicationSpec(name, Settings: new ClientApplicationSettings { AllowSelfUiLogin = selfUi }));
        return (created.Application, created.ClientSecret);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PasswordGrantAsync(string clientId, string secret, string login, string password, string scope = "openid offline_access auth2.me")
    {
        var r = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = login, ["password"] = password, ["scope"] = scope, ["client_id"] = clientId, ["client_secret"] = secret,
        });
        return (r.StatusCode, await r.JsonAsync());
    }

    // ------------------------------------------------------------------ one table, through the manager

    [Fact]
    public async Task Application_IsTheOpenIddictRow_WithTheExtraFieldsOnTheSameRow()
    {
        await SetUpAsync();
        var (web, _) = await AddApplicationAsync(A, "web");
        web.ClientId.ShouldBe("appa.web");   // {projectkey}.{name}

        await using var scope = NewScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var row = (Auth2Application)(await manager.FindByClientIdAsync("appa.web"))!;
        row.ProjectId.ShouldBe(A.Id);
        row.Name.ShouldBe("web");
        row.IsEnabled.ShouldBeTrue();
        row.ClientSettings.AllowSelfUiLogin.ShouldBeTrue();
        row.ClientType.ShouldBe(OpenIddictConstants.ClientTypes.Confidential);
        (await manager.HasPermissionAsync(row, OpenIddictConstants.Permissions.Endpoints.Token)).ShouldBeTrue();

        // there is no second client table: the OpenIddict row IS the application
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.Database.SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name LIKE '%application%'").ToListAsync())
            .ShouldBe(["applications"]);   // not openiddict_applications, not user_applications
    }

    [Fact]
    public async Task UpdateAndDelete_GoThroughTheManager_SoTheSecretAndTheCacheStayRight()
    {
        await SetUpAsync();
        var (web, secret) = await AddApplicationAsync(A, "web");
        await using (var scope = NewScope())
        {
            var apps = scope.ServiceProvider.GetRequiredService<IApplicationService>();
            var updated = await apps.UpdateAsync(web.Id, new ApplicationSpec("website", RedirectUris: ["https://web.test/cb"],
                Settings: new ClientApplicationSettings { AllowSelfUiLogin = true, CookieLifetimeMinutes = 30 }));
            updated.Name.ShouldBe("website");
            updated.RedirectUris.ShouldBe(["https://web.test/cb"]);
            updated.ClientId.ShouldBe("appa.web");   // the client id never changes with the name
            updated.Settings.CookieLifetimeMinutes.ShouldBe(30);
        }

        // the secret still works after the update (it is not rewritten)
        await CreateUserInAsync("appa");
        (await PasswordGrantAsync("appa.web", secret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);

        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IApplicationService>().DeleteAsync(web.Id);
        (await PasswordGrantAsync("appa.web", secret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.Unauthorized);   // gone, also from the OpenIddict cache
        await using (var scope = NewScope())
            (await scope.ServiceProvider.GetRequiredService<IApplicationService>().FindByClientIdAsync("appa.web")).ShouldBeNull();
    }

    [Fact]
    public async Task Name_IsUniqueOnlyInsideTheProject_AndTheClientIdIsUniqueOnTheWholeServer()
    {
        await SetUpAsync();
        await AddApplicationAsync(A, "web");
        await AddApplicationAsync(B, "web");   // the same name in another project works

        await using var scope = NewScope();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationService>();
        await Should.ThrowAsync<InvalidOperationException>(() => apps.CreateAsync(A.Id, new ApplicationSpec("web")));   // same name, same project
        await Should.ThrowAsync<Exception>(() => apps.CreateAsync(B.Id, new ApplicationSpec("other", ClientId: "appa.web")));   // client id of another project
    }

    // ------------------------------------------------------------------ the access rule

    [Fact]
    public async Task OneUser_OneProject_ThreeApplications_RegistersOnce_AndLogsInThroughAll()
    {
        await SetUpAsync();
        var (web, webSecret) = await AddApplicationAsync(A, "web");
        var (mobile, mobileSecret) = await AddApplicationAsync(A, "mobile");
        await UpdateSettingsAsync(A, s => s.App.AllowSelfUiLogin = true);   // the seeded application "default" of the project
        await CreateUserInAsync("appa");   // registered once, through the first application

        (await PasswordGrantAsync("appa", ASecret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);
        (await PasswordGrantAsync(web.ClientId, webSecret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);
        (await PasswordGrantAsync(mobile.ClientId, mobileSecret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);

        (await CountsAsync("sara@example.com")).ShouldBe((1, 1));   // one user, one membership row, no per-application access list
    }

    [Fact]
    public async Task SamePerson_InTwoProjects_HasSeparateMembershipsAndSeparatePasswords()
    {
        await PrepareAppsAsync();
        var user = await CreateUserInAsync("appa");
        await JoinAsync(user, B);
        (await InAsync("appb", async m => await m.AddPasswordAsync((await m.FindByNameAsync("sara"))!, PassB))).Succeeded.ShouldBeTrue();

        (await UserTokensAsync("sara", PassA, "appa")).Str("access_token").ShouldNotBeNullOrEmpty();
        (await UserTokensAsync("sara", PassB, "appb")).Str("access_token").ShouldNotBeNullOrEmpty();
        var wrong = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "sara", ["password"] = PassA, ["scope"] = "openid",
            ["client_id"] = "appb", ["client_secret"] = BSecret,
        });
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // the password of project A means nothing in project B
        (await CountsAsync("sara@example.com")).ShouldBe((1, 2));
    }

    // ------------------------------------------------------------------ enabled and disabled

    [Fact]
    public async Task ProjectDisabled_RefusesEverySignInPath_WithTheSameGenericAnswer_AndTheAuditLogKnowsWhy()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa");
        var tokens = await UserTokensAsync("sara", PassA, "appa", "openid offline_access auth2.me");
        var clientToken = await ClientTokenAsync("appa", "auth2.register auth2.login");
        var access = tokens.Str("access_token");

        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IProjectService>().SetEnabledAsync(A.Id, false, "test", null);

        // OAuth: password, client credentials, refresh token
        var password = await PasswordGrantAsync("appa", ASecret, "sara", PassA);
        password.Status.ShouldBe(HttpStatusCode.BadRequest);
        password.Body.Str("error").ShouldBe("access_denied");
        var machine = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "appa", ["client_secret"] = ASecret, ["scope"] = "auth2.client.handshake",
        });
        (await machine.JsonAsync()).Str("error").ShouldBe("access_denied");
        var refresh = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Str("refresh_token"), ["client_id"] = "appa", ["client_secret"] = ASecret,
        });
        refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refresh.JsonAsync()).TryGetProperty("access_token", out _).ShouldBeFalse();

        // the login service (password, OTP, external: social, OIDC, SAML, LDAP and passkey end in the same method, two factor too)
        await using (var scope = ScopeFor("appa"))
        {
            var login = scope.ServiceProvider.GetRequiredService<ILoginService>();
            var user = await UserAsync("sara@example.com");
            (await login.PasswordLoginAsync(new PasswordLoginRequest("sara", PassA))).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);
            (await login.OtpLoginAsync(new OtpLoginRequest("sara@example.com", "123456"))).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);
            (await login.ExternalLoginAsync(new ExternalSignInRequest(user, LoginMethod.External, "google", ["fed"], false))).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);
            (await login.ExternalLoginAsync(new ExternalSignInRequest(user, LoginMethod.External, "corp", ["fed"], false))).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);
            (await login.ExternalLoginAsync(new ExternalSignInRequest(user, LoginMethod.Ldap, "ad", ["pwd"], false))).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);
            (await login.ExternalLoginAsync(new ExternalSignInRequest(user, LoginMethod.Passkey, null, ["swk"], false))).ErrorCode.ShouldBe(Auth2ErrorCodes.AccessDenied);
            (await login.CompleteTwoFactorAsync("token", "123456", null)).ErrorCode.ShouldBeOneOf(Auth2ErrorCodes.AccessDenied, Auth2ErrorCodes.InvalidToken);
        }

        // registration and the API of the project
        var register = await new Browser(Factory).PostJsonAsync(Auth2Routes.Register, new { userName = "new", email = "new@example.com", password = "Pass-12345!x" }, clientToken);
        register.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await register.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.AccessDenied);

        // an access token issued BEFORE is refused at the next validation
        var me = await new Browser(Factory).GetAsync(Auth2Routes.Me, access);
        me.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        // another project is not affected
        (await UserTokensAsync("sara", PassA, "appa")).ToString().ShouldNotBeNull();
    }

    [Fact]
    public async Task ProjectDisabled_DoesNotTouchAnotherProject_AndReEnablingWorksAgain()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appb", "bob", PassB);
        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IProjectService>().SetEnabledAsync(A.Id, false, null, null);
        (await PasswordGrantAsync("appb", BSecret, "bob", PassB)).Status.ShouldBe(HttpStatusCode.OK);

        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IProjectService>().SetEnabledAsync(A.Id, true, null, null);
        await CreateUserInAsync("appa");
        (await PasswordGrantAsync("appa", ASecret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisablingAProject_RevokesTheTokensInOneTransaction_KeepsTheData_AndAuditsTheAdminAndTheReason()
    {
        await PrepareAppsAsync();
        var user = await CreateUserInAsync("appa");
        var tokens = await UserTokensAsync("sara", PassA, "appa");
        var adminId = Guid.NewGuid();

        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IProjectService>().SetEnabledAsync(A.Id, false, "contract ended", adminId);

        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            (await db.Users.AnyAsync(u => u.Id == user.Id)).ShouldBeTrue();   // nothing is deleted
            (await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == A.Id)).ShouldBeTrue();
            var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var live = 0;
            await foreach (var t in tokenManager.FindBySubjectAsync(user.Id.ToString("D"))) if (await tokenManager.GetStatusAsync(t) == OpenIddictConstants.Statuses.Valid) live++;
            live.ShouldBe(0);
        }
        var audit = (await AuditAsync("project.disabled")).Single();
        audit.ActorUserId.ShouldBe(adminId);
        audit.DataJson!.ShouldContain("contract ended");
        tokens.Str("access_token").ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ApplicationDisabled_BlocksOnlyThatApplication()
    {
        await SetUpAsync();
        var (web, webSecret) = await AddApplicationAsync(A, "web");
        await UpdateSettingsAsync(A, s => s.App.AllowSelfUiLogin = true);
        await CreateUserInAsync("appa");
        var before = await PasswordGrantAsync(web.ClientId, webSecret, "sara", PassA);
        before.Status.ShouldBe(HttpStatusCode.OK);

        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IApplicationService>().SetEnabledAsync(web.Id, false, "retired", null);

        var blocked = await PasswordGrantAsync(web.ClientId, webSecret, "sara", PassA);
        blocked.Status.ShouldBe(HttpStatusCode.BadRequest);
        blocked.Body.Str("error").ShouldBe("access_denied");
        (await PasswordGrantAsync("appa", ASecret, "sara", PassA)).Status.ShouldBe(HttpStatusCode.OK);   // the other application of the project works

        // the old token of the disabled application is refused too, the other one is not
        (await new Browser(Factory).GetAsync(Auth2Routes.Me, before.Body.Str("access_token"))).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        (await AuditAsync("application.disabled")).Single().DataJson!.ShouldContain("retired");
    }

    [Fact]
    public async Task GlobalProject_CannotBeDisabledOrDeleted()
    {
        await SetUpAsync();
        await using var scope = NewScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var global = (await projects.FindByProjectKeyAsync("_global"))!;
        await Should.ThrowAsync<InvalidOperationException>(() => projects.SetEnabledAsync(global.Id, false, null, null));
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationService>();
        var globalApp = (await apps.ListAsync(global.Id)).Single();
        await Should.ThrowAsync<InvalidOperationException>(() => apps.SetEnabledAsync(globalApp.Id, false, null, null));
        await Should.ThrowAsync<InvalidOperationException>(() => apps.DeleteAsync(globalApp.Id));
    }

    // ------------------------------------------------------------------ cookie

    [Fact]
    public async Task SessionCookie_BelongsToOneProject_SoALoginInOneProjectNeverCountsInAnother()
    {
        await PrepareAppsAsync();
        await CreateUserInAsync("appa");
        await CreateUserInAsync("appb");   // a user of project B (same login, other project)

        var b = new Browser(Factory);
        var (_, challengeA) = HttpExtensions.NewPkce();
        string Authorize(string client, string challenge) =>
            $"/connect/authorize?client_id={client}&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope=openid&state=s&code_challenge={challenge}&code_challenge_method=S256";

        var login = await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA });
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        b.CookieNames.ShouldContain(".SystemX.Auth2.Session.appa");
        b.CookieNames.ShouldNotContain(".SystemX.Auth2.Session");

        // project A: signed in (a code is issued). Project B on the same domain: the browser sends nothing it can use, so the login page comes.
        (await b.GetAsync(Authorize("appa", challengeA))).Location()!.ShouldStartWith(Redirect + "?");
        (await b.GetAsync(Authorize("appb", challengeA))).Location()!.ShouldContain("/Account/Login");
    }

    [Fact]
    public async Task SessionCookie_IsSharedByTheApplicationsOfOneProject()
    {
        await PrepareAppsAsync();
        var (web, _) = await AddApplicationAsync(A, "web");
        await UpdateSettingsAsync(A, s => s.RedirectUris.Add(Redirect));
        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IApplicationService>().UpdateAsync(web.Id, new ApplicationSpec("", RedirectUris: [Redirect]));
        await CreateUserInAsync("appa");

        var b = new Browser(Factory);
        var (_, challenge) = HttpExtensions.NewPkce();
        string Authorize(string client) =>
            $"/connect/authorize?client_id={client}&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&scope=openid&state=s&code_challenge={challenge}&code_challenge_method=S256";
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await b.GetAsync(Authorize(web.ClientId))).Location()!.ShouldStartWith(Redirect + "?");   // single sign-on inside the project
    }
}
