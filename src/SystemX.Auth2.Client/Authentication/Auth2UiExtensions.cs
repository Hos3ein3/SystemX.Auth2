using SystemX.Auth2.Client.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.DependencyInjection;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Enums;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Cookie, hosted UI, Self UI and Blazor features of the builder.</summary>
    public static class Auth2UiBuilderExtensions
    {
        /// <summary>
        /// Adds the sign-in cookie for browser apps (MVC, Razor Pages, Blazor). A request with a bearer token still uses the token, so this can be combined with
        /// <c>UseApiAuthentication</c>. Browsers that are not signed in are sent to <c>LoginPath</c>; APIs and AJAX calls get 401 / 403.
        /// </summary>
        public static IAuth2ClientBuilder UseCookieAuthentication(this IAuth2ClientBuilder builder, Action<Auth2CookieOptions>? configure = null)
        {
            if (configure is not null) builder.Services.Configure(configure);
            EnsureCookie(builder.Services);
            return builder;
        }

        /// <summary>
        /// The hosted UI: users sign in on the Auth2 Server (authorization code with PKCE). The package maps <c>/auth2/login</c> and <c>/auth2/logout</c>
        /// (with <c>app.UseAuth2()</c>) and the callback paths. <b>Register the callback address</b> (for example <c>https://app.example.com/signin-oidc</c>)
        /// as a redirect URI of the application on the server.
        /// </summary>
        public static IAuth2ClientBuilder UseHostedUi(this IAuth2ClientBuilder builder, Action<Auth2HostedUiOptions>? configure = null)
        {
            if (configure is not null) builder.Services.Configure(configure);
            builder.Services.AddOptions<Auth2HostedUiOptions>();
            EnsureCookie(builder.Services);
            builder.Services.Configure<Auth2AuthFlags>(f => f.Mode = UiMode.Hosted);
            builder.Services.AddAuthentication().AddOpenIdConnect(Auth2AuthSchemes.Oidc, _ => { });
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIdConnectOptions>, Auth2OidcConfigurator>());
            return builder;
        }

        /// <summary>
        /// The Self UI: your application shows its own login forms and calls <c>IAuth2WebClientService.LoginAsync</c>. <b>Your application handles passwords</b>, and the server must
        /// allow it (<c>Clients.AllowSelfUiLogin</c>). Set <c>LoginPath</c> to your login page.
        /// </summary>
        public static IAuth2ClientBuilder UseSelfUi(this IAuth2ClientBuilder builder)
        {
            EnsureCookie(builder.Services);
            builder.Services.Configure<Auth2AuthFlags>(f => f.Mode = UiMode.Self);
            return builder;
        }

        /// <summary>
        /// Blazor: cascading authentication state, <c>AuthorizeView</c> support with the Auth2 attributes, and <c>IAuth2BlazorService</c>.
        /// Pair it with <c>UseHostedUi()</c> or <c>UseSelfUi()</c>. In Blazor Server the access data is loaded at sign-in (<c>AccessMode.OnSignIn</c>) unless you choose another mode,
        /// because a circuit has no HTTP request to read the token from.
        /// </summary>
        public static IAuth2ClientBuilder UseBlazor(this IAuth2ClientBuilder builder)
        {
            EnsureCookie(builder.Services);
            builder.Services.Configure<Auth2AuthFlags>(f => f.BlazorDefaults = true);
            builder.Services.PostConfigure<Auth2CookieOptions>(_ => { });   // keeps the order simple: see the flag below
            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddAuthorizationCore();
            builder.Services.TryAddScoped<IAuth2BlazorService, BlazorService>();
            builder.Services.AddSingleton<IPostConfigureOptions<Auth2CookieOptions>, BlazorAccessModeDefault>();
            return builder;
        }

        private static void EnsureCookie(IServiceCollection services)
        {
            services.AddOptions<Auth2CookieOptions>();
            services.AddOptions<Auth2AuthFlags>();
            if (services.Any(d => d.ServiceType == typeof(Auth2CookieConfigurator))) return;

            services.Configure<Auth2AuthFlags>(f => f.HasCookie = true);
            services.AddSingleton<Auth2CookieConfigurator>();
            services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>>(sp => sp.GetRequiredService<Auth2CookieConfigurator>());
            services.TryAddSingleton<ClientAccessLoader>();
            services.TryAddSingleton<IAuth2WebClientService, Auth2WebClientService>();
            // The default scheme: a bearer token when the request has one, else the cookie. Challenges of the cookie redirect browsers and answer 401 to APIs.
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = Auth2AuthSchemes.Policy;
                o.DefaultAuthenticateScheme = Auth2AuthSchemes.Policy;
                o.DefaultChallengeScheme = Auth2AuthSchemes.Policy;
                o.DefaultForbidScheme = Auth2AuthSchemes.Policy;
                o.DefaultSignInScheme = Auth2AuthSchemes.Cookie;
                o.DefaultSignOutScheme = Auth2AuthSchemes.Cookie;
            })
            .AddCookie(Auth2AuthSchemes.Cookie, _ => { })
            .AddPolicyScheme(Auth2AuthSchemes.Policy, Auth2AuthSchemes.Policy, o =>
                o.ForwardDefaultSelector = ctx =>
                    Auth2Http.HasBearer(ctx.Request) && ctx.RequestServices.GetRequiredService<IOptions<Auth2AuthFlags>>().Value.HasBearer
                        ? JwtBearerDefaults.AuthenticationScheme : Auth2AuthSchemes.Cookie);
        }
    }

    internal sealed class BlazorAccessModeDefault : IPostConfigureOptions<Auth2CookieOptions>
    {
        public void PostConfigure(string? name, Auth2CookieOptions options)
        {
            if (options.AccessMode == AccessLoadMode.Lazy) options.AccessMode = AccessLoadMode.OnSignIn;
        }
    }
}

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>Routes of the hosted UI, and the API marker for endpoints.</summary>
    public static class Auth2EndpointRouteExtensions
    {
        /// <summary>Marks the endpoint as an API endpoint: when the user is not signed in it answers 401 / 403 and never redirects to the login page.</summary>
        public static TBuilder AsAuth2Api<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
            builder.WithMetadata(new Auth2ApiEndpointAttribute());

        /// <summary>
        /// Maps <c>GET /auth2/login?returnUrl=...</c> (starts the sign-in on the server), <c>/auth2/logout</c> and <c>/auth2/denied</c>. <c>UseAuth2()</c> calls it for you.
        /// Only paths on your own site are accepted as <c>returnUrl</c>.
        /// </summary>
        public static IEndpointRouteBuilder MapAuth2Endpoints(this IEndpointRouteBuilder routes)
        {
            // The answer to the server's health question (two-way health check). It is public and says one word.
            var health = routes.ServiceProvider.GetRequiredService<IOptions<Auth2HealthCheckOptions>>().Value;
            if (health.Mode == Auth2HealthCheckMode.TwoWay)
                routes.MapGet(health.Path, async (SystemX.Auth2.Client.HealthChecks.ClientHealthReporter reporter, CancellationToken ct) =>
                {
                    var (body, status) = await reporter.ReportAsync(ct);
                    return Results.Json(body, statusCode: status);
                }).AllowAnonymous().ExcludeFromDescription();

            var flags = routes.ServiceProvider.GetRequiredService<IOptions<Auth2AuthFlags>>().Value;
            if (!flags.HasCookie) return routes;

            if (flags.Mode == UiMode.Hosted)
            {
                routes.MapGet("/auth2/login", (HttpContext http, string? returnUrl) =>
                    Results.Challenge(new AuthenticationProperties { RedirectUri = Auth2Http.SafeReturnUrl(returnUrl) }, [Auth2AuthSchemes.Oidc])).AllowAnonymous();
                routes.MapMethods("/auth2/logout", ["GET", "POST"], (HttpContext http, string? returnUrl) =>
                    Results.SignOut(new AuthenticationProperties { RedirectUri = Auth2Http.SafeReturnUrl(returnUrl) }, [Auth2AuthSchemes.Cookie, Auth2AuthSchemes.Oidc])).AllowAnonymous();
            }
            else if (flags.Mode == UiMode.Self)
            {
                routes.MapMethods("/auth2/logout", ["GET", "POST"], async (HttpContext http, SystemX.Auth2.Client.Services.IAuth2WebClientService web, string? returnUrl) =>
                {
                    await web.LogoutAsync();
                    return Results.Redirect(Auth2Http.SafeReturnUrl(returnUrl));
                }).AllowAnonymous();
            }
            routes.MapGet("/auth2/denied", () => Results.Text("Access denied.", statusCode: StatusCodes.Status403Forbidden)).AllowAnonymous();
            return routes;
        }

        /// <summary>
        /// Adds authentication and authorization to the pipeline and, in a minimal-hosting app, the routes of <see cref="MapAuth2Endpoints"/>.
        /// </summary>
        public static IApplicationBuilder UseAuth2Routes(this IApplicationBuilder app)
        {
            if (app is IEndpointRouteBuilder routes) routes.MapAuth2Endpoints();
            return app;
        }
    }
}
