using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemX.Auth2.Client;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.DependencyInjection;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Client.Sync;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Client.IntegrationTests;

/// <summary>A component or service with an attribute, found by the assembly scan.</summary>
[Auth2Permission("component.use")]
public sealed class IntegrationFixturePage
{
    [Auth2Role("Auditor")]
    public void Audit() { }
}

/// <summary>A browser: keeps cookies per site and never follows redirects.</summary>
internal sealed class Jar(HttpClient client)
{
    private readonly Dictionary<string, string> _cookies = [];
    public IReadOnlyDictionary<string, string> Cookies => _cookies;

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, string? accept = "text/html", HttpContent? content = null, params (string, string)[] headers)
    {
        using var request = new HttpRequestMessage(method, pathAndQuery) { Content = content };
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        if (_cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var set)) foreach (var line in set) Remember(line);
        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string pathAndQuery, string? accept = "text/html", params (string, string)[] headers) => SendAsync(HttpMethod.Get, pathAndQuery, accept, null, headers);

    /// <summary>The Location header as a path and query (ASP.NET Core writes absolute addresses).</summary>
    public static string Where(HttpResponseMessage response) => response.Headers.Location is { } l ? (l.IsAbsoluteUri && l.Host == "localhost" ? l.PathAndQuery : l.OriginalString) : "";

    public void Remember(string setCookie)
    {
        var parts = setCookie.Split(';').Select(p => p.Trim()).ToList();
        var pair = parts[0].Split('=', 2);
        var expired = parts.Any(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(p[8..], out var when) && when < DateTimeOffset.UtcNow);
        if (expired || pair.Length < 2 || pair[1].Length == 0) _cookies.Remove(pair[0]);
        else _cookies[pair[0]] = pair[1];
    }
}

public class SyncIntegrationTests : ClientAppTestBase
{
    private static void Map(IEndpointRouteBuilder e)
    {
        e.MapGet("/orders", () => "ok").WithPermission("orders.read");
        e.MapPost("/orders", () => "ok").WithPermission("orders.write").WithRole("Editor");
        e.MapGet("/audit", () => "ok").WithRole("Auditor");
    }

    private static void ConfigureSync(IAuth2ClientBuilder b) => b
        .EnablePermissions(o =>
        {
            o.SyncPolicy = PermissionSyncPolicy.Push;
            o.ScanningPolicy = ScanningPolicy.Both;
            o.AssembliesToScan = [typeof(IntegrationFixturePage).Assembly];
            o.PermissionList = [new PermissionDefinition("orders.read", "Read orders", "Lets you read orders", "Orders")];
        })
        .EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.AssemblyScan; o.AssembliesToScan = [typeof(IntegrationFixturePage).Assembly]; })
        .EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Pull);

    private async Task<IReadOnlyList<string>> WaitForPermissionsAsync(int count)
    {
        var api = ApiClient.GetRequiredService<IAuth2ApiClientService>();
        for (var i = 0; i < 100; i++)
        {
            var list = (await api.GetPermissionsAsync()).Value!;
            if (list.Count >= count) return [.. list.Select(p => p.Key)];
            await Task.Delay(100);
        }
        return [.. (await api.GetPermissionsAsync()).Value!.Select(p => p.Key)];
    }

    private async Task<int> AuditCountAsync(string action)
    {
        await using var scope = Server.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().AuditEvents.CountAsync(e => e.Action == action);
    }

    [Fact]
    public async Task PushAfterStartup_SendsScannedAndManualEntries_AndAHostRestartChangesNothing()
    {
        await StartAppAsync(Map, ConfigureSync, handshake: true);
        var keys = await WaitForPermissionsAsync(3);
        keys.ShouldBe(["component.use", "orders.read", "orders.write"]);

        var api = ApiClient.GetRequiredService<IAuth2ApiClientService>();
        var read = (await api.GetPermissionsAsync()).Value!.Single(p => p.Key == "orders.read");
        read.DisplayName.ShouldBe("Read orders");      // the manual entry won over the endpoint's bare key
        read.Group.ShouldBe("Orders");
        // roles come from the endpoints and the attribute scan
        for (var i = 0; i < 50 && (await api.GetRolesAsync()).Value!.Count < 2; i++) await Task.Delay(100);
        (await api.GetRolesAsync()).Value!.Select(r => r.Name).ShouldBe(["Auditor", "Editor"]);
        (await AuditCountAsync("permissions.synced")).ShouldBe(1);

        // the application starts again with the same lists: nothing changes on the server
        await App.StopAsync();
        App.Dispose();
        await StartAppAsync(Map, ConfigureSync, handshake: true);
        await Task.Delay(1500);
        (await AuditCountAsync("permissions.synced")).ShouldBe(1);   // idempotent: the second sync changed nothing
        (await api.GetPermissionsAsync()).Value!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ScopesAndGroups_ArePushed_TheNewScopeWorksAtOnce_AndAnAdminGroupIsNotTouched()
    {
        await UpdateSettingsAsync(s => s.Clients.GroupSyncAllowed = true);
        await using (var scope = ServerScope())
            await scope.ServiceProvider.GetRequiredService<IGroupService>().CreateAsync("finance", "Admin's name");

        await StartAppAsync(e =>
        {
            e.MapGet("/reports", () => "ok").WithScope("reports.read").WithMachineOnly().WithGroup("hr");
            e.MapGet("/me-only", () => "ok").WithScope("auth2.me");   // reserved: never sent
        }, b => b
            .EnableScopes(o => { o.SyncPolicy = ScopeSyncPolicy.Both; o.ScanningPolicy = ScanningPolicy.Both; o.ScopeList = [new ScopeDefinition("reports.write", "Write reports")]; })
            .EnableGroups(o =>
            {
                o.SyncPolicy = GroupSyncPolicy.Both; o.ScanningPolicy = ScanningPolicy.Both;
                o.GroupList = [new GroupDefinition("finance", "Client's name"), new GroupDefinition("payroll", "Payroll")];
            }), handshake: true);

        var api = ApiClient.GetRequiredService<IAuth2ApiClientService>();
        for (var i = 0; i < 100 && (await api.GetGroupsAsync()).Value!.Count < 3; i++) await Task.Delay(100);
        var groups = (await api.GetGroupsAsync()).Value!;
        groups.Select(g => g.Name).ShouldBe(["finance", "hr", "payroll"]);
        groups.Single(g => g.Name == "finance").DisplayName.ShouldBe("Admin's name");   // the admin's text stays

        var scopes = (await api.GetScopesAsync()).Value!;
        scopes.Select(s => s.Name).ShouldBe(["reports.read", "reports.write"]);          // no auth2.me
        scopes.Single(s => s.Name == "reports.write").DisplayName.ShouldBe("Write reports");

        // the client can ask for the new scope straight away, and an endpoint that needs it accepts the token
        ApiClient.GetRequiredService<SystemX.Auth2.Client.Machine.IAuth2MachineTokenProvider>().Invalidate();
        var token = await MachineTokenAsync("reports.read");
        (await GetAsync("/me-only", token)).ShouldBe(System.Net.HttpStatusCode.Forbidden);   // the token has reports.read, not auth2.me
        var catalog = App.Services.GetRequiredService<IAuth2Catalog>();
        for (var i = 0; i < 50 && catalog.Scopes.Count < 2; i++) await Task.Delay(100);
        catalog.Scopes.Select(s => s.Name).ShouldBe(["reports.read", "reports.write"]);
        catalog.Groups.Select(g => g.Name).ShouldBe(["finance", "hr", "payroll"]);
    }

    [Fact]
    public async Task AnyOfRules_AreScannedAndSynced_RolesPermissionsGroupsAndScopes()
    {
        await UpdateSettingsAsync(s => s.Clients.GroupSyncAllowed = true);
        await StartAppAsync(e =>
        {
            e.MapGet("/rule", () => "ok").WithAnyOf(roles: ["Auditor"], permissions: ["Reports.Read"], groups: ["finance"], scopes: ["reports.api"]);
            e.MapGet("/rule-2", () => "ok").RequireAuthorization(new Auth2RoleOrPermissionAttribute { Roles = ["Reviewer"], Permissions = ["reviews.write"] });
        }, b => b
            .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.AssemblyScan; })
            .EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.AssemblyScan; })
            .EnableGroups(o => { o.SyncPolicy = GroupSyncPolicy.Both; o.ScanningPolicy = ScanningPolicy.AssemblyScan; })
            .EnableScopes(o => { o.SyncPolicy = ScopeSyncPolicy.Both; o.ScanningPolicy = ScanningPolicy.AssemblyScan; }), handshake: true);

        var api = ApiClient.GetRequiredService<IAuth2ApiClientService>();
        for (var i = 0; i < 100 && ((await api.GetScopesAsync()).Value!.Count < 1 || (await api.GetRolesAsync()).Value!.Count < 2
            || (await api.GetGroupsAsync()).Value!.Count < 1 || (await api.GetPermissionsAsync()).Value!.Count < 2); i++) await Task.Delay(100);
        (await api.GetPermissionsAsync()).Value!.Select(p => p.Key).ShouldBe(["reports.read", "reviews.write"]);
        (await api.GetRolesAsync()).Value!.Select(r => r.Name).ShouldBe(["Auditor", "Reviewer"]);
        (await api.GetGroupsAsync()).Value!.Select(g => g.Name).ShouldBe(["finance"]);
        (await api.GetScopesAsync()).Value!.Select(s => s.Name).ShouldBe(["reports.api"]);
    }

    [Fact]
    public async Task Pull_ReadsTheGroupsAndRolesOfTheServerIntoTheCatalog()
    {
        await using (var scope = ServerScope())
        {
            var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
            await groups.CreateAsync("finance");
        }
        await StartAppAsync(Map, b => b.EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Pull).EnablePermissions(o => o.SyncPolicy = PermissionSyncPolicy.Pull), handshake: true);
        var catalog = App.Services.GetRequiredService<IAuth2Catalog>();
        for (var i = 0; i < 50 && catalog.LastReadUtc is null; i++) await Task.Delay(100);
        catalog.Groups.Select(g => g.Name).ShouldBe(["finance"]);
        catalog.LastReadUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task ScanningWorksInAMinimalHostingApplication_TooWithWebApplication()
    {
        await UpdateSettingsAsync(s => s.App.AllowSelfUiLogin = true);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuth2Client("http://localhost", o => { o.ApplicationName = "default"; o.ClientId = "blog"; o.ClientSecret = ClientSecret; })
            .ServerWithMultiProject("blog")
            .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.AssemblyScan; });
        foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
            builder.Services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Server.Server.CreateHandler());
        var app = builder.Build();
        app.MapGet("/minimal", () => "ok").WithPermission("minimal.use");
        await app.StartAsync();

        ApiClient = BuildClient("blog");
        (await WaitForPermissionsAsync(1)).ShouldBe(["minimal.use"]);
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

public class SelfUiIntegrationTests : ClientAppTestBase
{
    private static void Map(IEndpointRouteBuilder e)
    {
        e.MapPost("/login", async (HttpContext http, IAuth2WebClientService web) =>
        {
            var form = await http.Request.ReadFormAsync();
            var result = await web.LoginAsync(new LoginRequest(form["login"].ToString(), form["password"].ToString()));
            return result.IsSuccess ? Results.Ok(new { result.Value!.TwoFactorRequired }) : Results.BadRequest(new { result.Error!.Code });
        }).WithAnonymous();
        e.MapGet("/secure", () => "ok").WithAuth2();
        e.MapGet("/editors", () => "ok").WithRole("Editor");
        e.MapGet("/orders", () => "ok").WithPermission("orders.read");
        e.MapGet("/api/secure", () => "ok").WithAuth2().AsAuth2Api();
        e.MapGet("/who", (ICurrentUser u) => Results.Json(new { u.IsAuthenticated, u.UserName, u.Email })).WithAuth2();
    }

    private async Task<Jar> StartAsync(Action<SystemX.Auth2.Client.Options.Auth2CookieOptions>? cookie = null, bool createUser = true)
    {
        await StartAppAsync(Map, b => b.UseSelfUi().UseCookieAuthentication(o =>
        {
            o.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            o.LoginPath = "/login-page";
            cookie?.Invoke(o);
        }), handshake: true);
        if (createUser) await CreateUserAsync();
        return new Jar(App.GetTestClient());
    }

    private static Task<HttpResponseMessage> PostLogin(Jar jar, string password = Password) => jar.SendAsync(HttpMethod.Post, "/login", "application/json",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["login"] = "sara", ["password"] = password }));

    [Fact]
    public async Task Login_MakesTheCookie_TheCookieOpensProtectedEndpoints_AndLogoutRemovesIt()
    {
        var jar = await StartAsync();
        (await PostLogin(jar, "wrong-password-1A!")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        jar.Cookies.ShouldBeEmpty();

        var login = await PostLogin(jar);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        jar.Cookies.ShouldContainKey(".SystemX.Auth2.Client");

        (await jar.GetAsync("/secure")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var who = await (await jar.GetAsync("/who")).Content.ReadFromJsonAsync<JsonElement>();
        who.GetProperty("userName").GetString().ShouldBe("sara");
        who.GetProperty("email").GetString().ShouldBe("sara@example.com");

        var logout = await jar.GetAsync("/auth2/logout");
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Jar.Where(logout).ShouldBe("/");
        (await jar.GetAsync("/secure", "application/json")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheCookieLifetime_ComesFromTheServer_UnlessYouSetIt()
    {
        await UpdateSettingsAsync(s => s.Sessions.CookieLifetimeMinutes = 90);
        var jar = await StartAsync();
        var login = await PostLogin(jar);
        var cookieLine = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".SystemX.Auth2.Client="));
        var expires = DateTimeOffset.Parse(cookieLine.Split(';').Select(p => p.Trim()).Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))[8..]);
        (expires - DateTimeOffset.UtcNow).ShouldBeInRange(TimeSpan.FromMinutes(88), TimeSpan.FromMinutes(91));
    }

    [Fact]
    public async Task ARoleFromTheServer_IsFoundForCookieSessions_InLazyAndRolesInCookieMode()
    {
        var first = true;
        foreach (var mode in new[] { AccessLoadMode.Lazy, AccessLoadMode.RolesInCookie })
        {
            var jar = await StartAsync(o => o.AccessMode = mode, createUser: first);
            if (first) await AccessAsync("sara", async (a, g, u) => { await a.CreateRoleAsync("Editor"); await a.AssignRoleAsync(u, "Editor"); });
            first = false;
            (await PostLogin(jar)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await jar.GetAsync("/editors")).StatusCode.ShouldBe(HttpStatusCode.OK, mode.ToString());
            await TearDownAppAsync();
        }
    }

    [Fact]
    public async Task OnSignInMode_LoadsPermissionsIntoTheStore_SoTheNextRequestAsksNoOne()
    {
        var jar = await StartAsync(o => o.AccessMode = AccessLoadMode.OnSignIn);
        await AccessAsync("sara", async (a, g, u) => { await a.CreatePermissionAsync(new PermissionDefinition("orders.read")); await a.GrantPermissionAsync(u, "orders.read"); });
        await PostLogin(jar);
        var calls = Counting.MeCalls;
        (await jar.GetAsync("/orders")).StatusCode.ShouldBe(HttpStatusCode.OK);
        Counting.MeCalls.ShouldBe(calls);   // read from the store: no new call
    }

    [Fact]
    public async Task NotSignedIn_BrowsersAreRedirected_ApisAndAjaxGet401_MissingPermissionIsDeniedPageOr403()
    {
        var jar = await StartAsync();
        var browser = await jar.GetAsync("/secure?x=1");
        browser.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Jar.Where(browser).ShouldBe("/login-page?returnUrl=%2Fsecure%3Fx%3D1");   // page B is kept

        (await jar.GetAsync("/secure", "application/json")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await jar.GetAsync("/secure", "text/html", ("X-Requested-With", "XMLHttpRequest"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await jar.GetAsync("/api/secure", "text/html")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // marked with AsAuth2Api()
        (await jar.GetAsync("/secure", "text/html", ("Authorization", "Bearer garbage"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await PostLogin(jar);
        var denied = await jar.GetAsync("/orders");
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Jar.Where(denied).ShouldStartWith("/auth2/denied");
        (await jar.GetAsync("/orders", "application/json")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await jar.GetAsync("/auth2/denied")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BearerTokens_AndTheCookie_WorkTogether_OnOneApplication()
    {
        var jar = await StartAsync();
        var token = await LoginAsync();
        (await jar.GetAsync("/secure", "application/json", ("Authorization", "Bearer " + token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await jar.GetAsync("/secure", "application/json")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await PostLogin(jar);
        (await jar.GetAsync("/secure", "application/json")).StatusCode.ShouldBe(HttpStatusCode.OK);   // the cookie, no header
    }

    [Fact]
    public async Task Logout_NeverRedirectsToAnotherSite()
    {
        var jar = await StartAsync();
        await PostLogin(jar);
        var r = await jar.GetAsync("/auth2/logout?returnUrl=https%3A%2F%2Fevil.test%2F");
        Jar.Where(r).ShouldBe("/");
        var r2 = await jar.GetAsync("/auth2/logout?returnUrl=%2F%2Fevil.test");
        Jar.Where(r2).ShouldBe("/");
    }

    private async Task TearDownAppAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        App.Dispose();
        await ApiClient.DisposeAsync();
    }
}

public class HostedUiIntegrationTests : ClientAppTestBase
{
    private const string Callback = "http://localhost/signin-oidc";

    private static void Map(IEndpointRouteBuilder e)
    {
        e.MapGet("/secure", () => "secure page").WithAuth2();
        e.MapGet("/orders", () => "orders").WithPermission("orders.read");
        e.MapGet("/who", async (ICurrentUser u) => Results.Json(new { u.UserName, u.Email, Roles = await u.GetRolesAsync(), Permissions = await u.GetPermissionsAsync() })).WithAuth2();
        e.MapGet("/by-scope", () => "scope page").WithScope("auth2.me");
        e.MapGet("/by-other-scope", () => "other scope page").WithScope("reports.write");
        e.MapGet("/client", (ICurrentClient c) => Results.Json(new { c.Scopes, c.ClientId })).WithAuth2();
    }

    private async Task<(Jar App, Jar Srv, HttpClient ServerClient)> StartAsync(Action<SystemX.Auth2.Client.Options.Auth2CookieOptions>? cookie = null)
    {
        await UpdateSettingsAsync(s => { s.App.AllowSelfUiLogin = true; s.RedirectUris.Add(Callback); s.PostLogoutRedirectUris.Add("http://localhost/signout-callback-oidc"); });
        await StartAppAsync(Map, b => b.UseHostedUi(o => o.BackchannelHandler = Server.Server.CreateHandler()).UseCookieAuthentication(o =>
        {
            o.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            cookie?.Invoke(o);
        }), handshake: true);
        await CreateUserAsync();
        var serverClient = Server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        return (new Jar(App.GetTestClient()), new Jar(serverClient), serverClient);
    }

    private static string PathOf(string location) => new Uri(new Uri("http://localhost"), location).PathAndQuery;

    /// <summary>The whole sign-in as a browser does it. Returns the address the user ends up on (page B).</summary>
    private async Task<string> SignInAsync(Jar app, Jar srv, string startPath)
    {
        // 1. not signed in: the app sends the browser to its own login route, which starts the sign-in on the server
        var r1 = await app.GetAsync(startPath);
        r1.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Jar.Where(r1).ShouldStartWith("/auth2/login?returnUrl=");
        var r2 = await app.GetAsync(Jar.Where(r1));
        r2.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var authorize = r2.Headers.Location!.OriginalString;
        authorize.ShouldStartWith("http://localhost/connect/authorize");
        authorize.ShouldContain("code_challenge_method=S256");
        authorize.ShouldContain("client_id=blog");
        authorize.ShouldContain("response_type=code");
        Uri.UnescapeDataString(authorize).ShouldContain("redirect_uri=" + Callback);

        // 2. the server has no session: login page
        var r3 = await srv.GetAsync(PathOf(authorize));
        Jar.Where(r3).ShouldStartWith("/Account/Login?ReturnUrl=");
        var loginUrl = Jar.Where(r3);

        // 3. the user types login and password (the page needs an anti-forgery token)
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
            foreach (var cookie in ctx.Response.Headers.SetCookie) srv.Remember(cookie!);
            var r4 = await srv.SendAsync(HttpMethod.Post, loginUrl, "text/html", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Login"] = "sara", ["Input.Password"] = Password, [tokens.FormFieldName] = tokens.RequestToken!,
            }));
            r4.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            Jar.Where(r4).ShouldContain("/connect/authorize");
        }

        // 4. back to authorize: the server answers with a code for the app's callback
        var r5 = await srv.GetAsync(PathOf(authorize));
        r5.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r5.Headers.Location!.OriginalString.ShouldStartWith(Callback + "?");

        // 5. the app's callback exchanges the code (backchannel), makes the cookie and sends the user to page B
        var r6 = await app.GetAsync(PathOf(r5.Headers.Location.OriginalString));
        r6.StatusCode.ShouldBe(HttpStatusCode.Redirect, await r6.Content.ReadAsStringAsync());
        return r6.Headers.Location!.OriginalString;
    }

    [Fact]
    public async Task AuthorizationCodeWithPkce_SignsTheUserIn_AndKeepsTheOriginalPage()
    {
        var (app, srv, _) = await StartAsync();
        var landing = await SignInAsync(app, srv, "/secure?tab=2");
        landing.ShouldBe("/secure?tab=2");   // page B survived the whole round trip

        app.Cookies.ShouldContainKey(".SystemX.Auth2.Client");
        var page = await app.GetAsync("/secure?tab=2");
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).ShouldBe("secure page");

        var who = await (await app.GetAsync("/who", "application/json")).Content.ReadFromJsonAsync<JsonElement>();
        who.GetProperty("userName").GetString().ShouldBe("sara");
        who.GetProperty("email").GetString().ShouldBe("sara@example.com");
    }

    [Fact]
    public async Task TheScopesOfTheAccessToken_AreInTheCookie_SoScopeRulesWorkForAHostedUiUser()
    {
        var (app, srv, _) = await StartAsync();
        await SignInAsync(app, srv, "/secure");

        (await app.GetAsync("/by-scope")).StatusCode.ShouldBe(HttpStatusCode.OK);                  // the token has auth2.me
        var refused = await app.GetAsync("/by-other-scope");                                       // and not this one: a cookie user is sent to the access denied page
        refused.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Jar.Where(refused).ShouldContain("/auth2/denied");
        var client = await (await app.GetAsync("/client", "application/json")).Content.ReadFromJsonAsync<JsonElement>();
        client.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ShouldContain("auth2.me");
        client.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ShouldContain("openid");
        client.GetProperty("clientId").GetString().ShouldBe("blog");
    }

    [Fact]
    public async Task OnTokenValidated_LoadsTheAccessData_BySettingAccessMode()
    {
        var (app, srv, _) = await StartAsync(o => o.AccessMode = AccessLoadMode.OnSignIn);
        await AccessAsync("sara", async (a, g, u) =>
        {
            await a.CreatePermissionAsync(new PermissionDefinition("orders.read"));
            await a.GrantPermissionAsync(u, "orders.read");
        });
        await SignInAsync(app, srv, "/orders");
        var calls = Counting.MeCalls;     // the one call of the sign-in hook is already counted
        (await app.GetAsync("/orders")).StatusCode.ShouldBe(HttpStatusCode.OK);
        Counting.MeCalls.ShouldBe(calls); // the permission came from the store
    }

    [Fact]
    public async Task Logout_SignsOutOfTheApp_AndTheServer()
    {
        var (app, srv, _) = await StartAsync();
        await SignInAsync(app, srv, "/secure");
        var r = await app.GetAsync("/auth2/logout");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Headers.Location!.OriginalString.ShouldStartWith("http://localhost/connect/endsession");
        app.Cookies.ShouldNotContainKey(".SystemX.Auth2.Client");
    }

    [Fact]
    public async Task TheLoginRoute_NeverSendsTheUserToAnotherSite()
    {
        var (app, _, _) = await StartAsync();
        var r = await app.GetAsync("/auth2/login?returnUrl=https%3A%2F%2Fevil.test%2F");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Headers.Location!.OriginalString.ShouldStartWith("http://localhost/connect/authorize");
        // after the sign-in the user would land on "/", not on evil.test (the state carries the safe value)
        r.Headers.Location.OriginalString.ShouldNotContain("evil.test");
    }

    private async Task TearDown()
    {
        Http.Dispose();
        await App.StopAsync();
        App.Dispose();
        await ApiClient.DisposeAsync();
    }
}
