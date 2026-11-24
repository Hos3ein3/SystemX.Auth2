using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Client.SystemNetHttp;
using OpenIddict.Client.WebIntegration;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// SPIKE 1 of the External Identity package: can OpenIddict.Client take its provider registrations from a store that changes at run time
/// (no restart), with two providers of the same kind for two projects at once and a different redirect URI for each?
/// Approach (a): <c>IConfigureOptions&lt;OpenIddictClientOptions&gt;</c> + <c>IOptionsChangeTokenSource&lt;OpenIddictClientOptions&gt;</c>.
/// The tests build a tiny host (not the Auth2 server) so that the result says what the library does, nothing else.
/// </summary>
public class OpenIddictClientSpikeTests
{
    /// <summary>The "database": provider rows. A change replaces the change token, which makes the options be built again.</summary>
    private sealed class Store : IOptionsChangeTokenSource<OpenIddictClientOptions>
    {
        private CancellationTokenSource _cts = new();
        public ConcurrentDictionary<string, Row> Rows { get; } = new();
        public string? Name => Options.DefaultName;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(_cts.Token);
        public void Set(Row row) { Rows[row.Name] = row; Fire(); }
        public void Remove(string name) { Rows.TryRemove(name, out _); Fire(); }
        private void Fire() { var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource()); old.Cancel(); }
    }

    private sealed record Row(string Name, string Issuer, string ClientId, string Secret, string Callback, string? Social = null);

    private sealed class FromStore(Store store) : IConfigureOptions<OpenIddictClientOptions>
    {
        public void Configure(OpenIddictClientOptions options)
        {
            foreach (var r in store.Rows.Values)
            {
                options.RedirectionEndpointUris.Add(new Uri(r.Callback, UriKind.Relative));
                if (r.Social == "GitHub")
                {
                    // FINDING 4: a WebIntegration provider is identified by an internal GUID (ProviderType), so it cannot be written by hand.
                    // The registration is made by the library's own public builder in a temporary container, then copied.
                    var temp = new ServiceCollection();
                    temp.AddLogging();
                    temp.AddOpenIddict().AddClient(c =>
                    {
                        c.AllowAuthorizationCodeFlow(); c.DisableTokenStorage(); c.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
                        c.UseAspNetCore(); c.UseSystemNetHttp();
                        c.UseWebProviders().AddGitHub(g => g.SetProviderName(r.Name).SetClientId(r.ClientId).SetClientSecret(r.Secret).SetRedirectUri(new Uri(r.Callback, UriKind.Relative)));
                    });
                    using var tp = temp.BuildServiceProvider();
                    options.Registrations.Add(tp.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue.Registrations.Single());
                    continue;
                }
                options.Registrations.Add(new OpenIddictClientRegistration
                {
                    Issuer = new Uri(r.Issuer, UriKind.Absolute),
                    ProviderName = r.Name,
                    ClientId = r.ClientId,
                    ClientSecret = r.Secret,
                    RedirectUri = new Uri(r.Callback, UriKind.Relative),
                    GrantTypes = { GrantTypes.AuthorizationCode },
                    ResponseTypes = { ResponseTypes.Code },
                    Scopes = { Scopes.OpenId, Scopes.Email, Scopes.Profile },
                });
            }
        }
    }

    private static async Task<(WebApplication App, HttpClient Client, Store Store, FakeOidcProvider Idp)> StartAsync()
    {
        var idp = new FakeOidcProvider();
        var store = new Store();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<IOptionsChangeTokenSource<OpenIddictClientOptions>>(store);
        builder.Services.AddSingleton<IConfigureOptions<OpenIddictClientOptions>>(new FromStore(store));
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddOpenIddict().AddClient(o =>
        {
            o.AllowAuthorizationCodeFlow();
            o.DisableTokenStorage();   // FINDING 3: without OpenIddict.Core the client needs this; in the Server it must be disabled too, or client token entries would land in the Server's own token table
            o.SetRedirectionEndpointUris("/callback/login/_unused");   // FINDING 1: the options cannot be built without one static endpoint, even when no provider exists yet
            o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            o.UseAspNetCore().EnableRedirectionEndpointPassthrough();
            o.UseSystemNetHttp();
            o.UseWebProviders();
        });
        builder.Services.ConfigureHttpClientDefaults(b => b.AddHttpMessageHandler(() => new Shortcut(idp)));   // test only. FINDING 2: OpenIddict accepts only an HttpClientHandler as the primary handler, so the fake provider is put in the pipeline instead
        var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseAuthentication();
        app.MapGet("/login", (string provider) => Results.Challenge(
            new AuthenticationProperties(new Dictionary<string, string?> { [OpenIddictClientAspNetCoreConstants.Properties.ProviderName] = provider }) { RedirectUri = "/" },
            [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]));
        app.Map("/callback/{**rest}", async (HttpContext http) =>
        {
            var result = await http.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
            return result.Succeeded
                ? Results.Json(new { sub = result.Principal!.FindFirst(Claims.Subject)?.Value, provider = result.Principal.FindFirst("provider")?.Value ?? result.Principal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value })
                : Results.Json(new { error = result.Failure?.Message }, statusCode: 400);
        });
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Spike", "1");
        return (app, new HttpClient(new NoRedirect(app.GetTestServer().CreateHandler())) { BaseAddress = new Uri("https://localhost") }, store, idp);
    }

    /// <summary>Answers every request itself (never calls the next handler): the fake provider stands in for the internet.</summary>
    private sealed class Shortcut(HttpMessageHandler fake) : DelegatingHandler
    {
        private readonly HttpMessageInvoker _invoker = new(fake, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => _invoker.SendAsync(request, ct);
    }

    private sealed class NoRedirect(HttpMessageHandler inner) : DelegatingHandler(inner);

    private static string Query(Uri u, string k) => HttpUtility.ParseQueryString(u.Query)[k]!;

    [Fact]
    public async Task ProviderAddedAtRunTime_IsUsedWithoutRestart()
    {
        var (app, http, store, idp0) = await StartAsync();
        await using var _app = app;

        // nothing registered yet: the challenge cannot work
        var before = await http.GetAsync("/login?provider=ext-appa-google");
        before.StatusCode.ShouldNotBe(HttpStatusCode.Redirect);

        store.Set(new("ext-appa-google", "https://idp.test/appa", "client-a", "secret-a", "/callback/login/appa/google"));
        var after = await http.GetAsync("/login?provider=ext-appa-google");
        string diag = "";
        try { var svc = app.Services.GetRequiredService<OpenIddictClientService>(); _ = await svc.GetServerConfigurationByProviderNameAsync("ext-appa-google"); }
        catch (Exception ex) { diag = ex.ToString(); }
        after.StatusCode.ShouldBe(HttpStatusCode.Redirect, diag + "\n" + "requests at the provider: [" + string.Join("; ", idp0.Requests) + "] " + (await after.Content.ReadAsStringAsync()));
        var location = after.Headers.Location!;
        location.Host.ShouldBe("idp.test");
        Query(location, "client_id").ShouldBe("client-a");
        Query(location, "redirect_uri").ShouldBe("https://localhost/callback/login/appa/google");
        Query(location, "code_challenge_method").ShouldBe("S256");
    }

    [Fact]
    public async Task TwoProjects_SameKindOfProvider_DifferentRedirectUris_BothWork_AndAChangeIsSeenAtOnce()
    {
        var (app, http, store, idp) = await StartAsync();
        await using var _app = app;
        store.Set(new("ext-appa-google", "https://idp.test/appa", "client-a", "secret-a", "/callback/login/appa/google"));
        store.Set(new("ext-appb-google", "https://idp.test/appb", "client-b", "secret-b", "/callback/login/appb/google"));

        var a = (await http.GetAsync("/login?provider=ext-appa-google")).Headers.Location!;
        var b = (await http.GetAsync("/login?provider=ext-appb-google")).Headers.Location!;
        Query(a, "client_id").ShouldBe("client-a");
        Query(b, "client_id").ShouldBe("client-b");
        Query(a, "redirect_uri").ShouldNotBe(Query(b, "redirect_uri"));

        // a change (a new client id) is used by the next challenge, with no restart
        store.Set(new("ext-appa-google", "https://idp.test/appa", "client-a2", "secret-a", "/callback/login/appa/google"));
        Query((await http.GetAsync("/login?provider=ext-appa-google")).Headers.Location!, "client_id").ShouldBe("client-a2");

        // a removed provider cannot start a login any more
        store.Remove("ext-appb-google");
        (await http.GetAsync("/login?provider=ext-appb-google")).StatusCode.ShouldNotBe(HttpStatusCode.Redirect);
        GC.KeepAlive(idp);
    }

    /// <summary>Keeps the cookies of one browser (the client stores its state and correlation in a cookie).</summary>
    private sealed class Browser(HttpClient http)
    {
        private readonly Dictionary<string, string> _cookies = [];
        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (_cookies.Count > 0) req.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            var res = await http.SendAsync(req);
            foreach (var sc in res.Headers.TryGetValues("Set-Cookie", out var v) ? v : [])
            {
                var kv = sc.Split(';')[0].Split('=', 2);
                if (sc.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)) _cookies.Remove(kv[0]); else _cookies[kv[0]] = kv[1];
            }
            return res;
        }
    }

    [Fact]
    public async Task FullLogin_CodeExchangeWithPkce_Works_ForADynamicProvider_AndAMixedUpOrReplayedCallbackFails()
    {
        var (app, http, store, idp) = await StartAsync();
        await using var _app = app;
        store.Set(new("ext-appa-google", "https://idp.test/appa", "client-a", "secret-a", "/callback/login/appa/google"));
        store.Set(new("ext-appb-google", "https://idp.test/appb", "client-b", "secret-b", "/callback/login/appb/google"));

        // the happy path
        var browser = new Browser(http);
        var start = (await browser.GetAsync("/login?provider=ext-appa-google")).Headers.Location!;
        var code = idp.Authorize(start, new FakeUser { Sub = "sara-1" });
        var back = await browser.GetAsync($"/callback/login/appa/google?code={code}&state={Query(start, "state")}");
        (await back.Content.ReadAsStringAsync()).ShouldContain("sara-1");
        back.StatusCode.ShouldBe(HttpStatusCode.OK);

        // a callback of provider A with the code and state that belong to provider B must fail (mix-up protection: one redirect URI per provider)
        var b = new Browser(http);
        var startB = (await b.GetAsync("/login?provider=ext-appb-google")).Headers.Location!;
        var codeB = idp.Authorize(startB, new FakeUser { Sub = "sara-b" });
        var mixed = await b.GetAsync($"/callback/login/appa/google?code={codeB}&state={Query(startB, "state")}");
        mixed.StatusCode.ShouldNotBe(HttpStatusCode.OK);

        // the same callback twice fails (single-use code and state)
        var replay = await browser.GetAsync($"/callback/login/appa/google?code={code}&state={Query(start, "state")}");
        replay.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AWebIntegrationProvider_GitHub_CanBeRegisteredFromARow_AtRunTime()
    {
        var (app, http, store, _) = await StartAsync();
        await using var _app = app;
        store.Set(new("ext-appa-github", "", "gh-client", "gh-secret", "/callback/login/appa/github", Social: "GitHub"));
        var res = await http.GetAsync("/login?provider=ext-appa-github");
        res.StatusCode.ShouldBe(HttpStatusCode.Redirect, await res.Content.ReadAsStringAsync());
        res.Headers.Location!.Host.ShouldBe("github.com");
        Query(res.Headers.Location, "client_id").ShouldBe("gh-client");
        Query(res.Headers.Location, "redirect_uri").ShouldBe("https://localhost/callback/login/appa/github");
    }
}
