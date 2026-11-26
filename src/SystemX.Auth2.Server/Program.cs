using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Endpoints;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.Commands;
using SystemX.Auth2.Server.Infrastructure.Localization;
using SystemX.Auth2.Server.Infrastructure.Operations;

var builder = WebApplication.CreateBuilder(args);

builder.AddAuth2Observability();
builder.Services.AddSystemXAuth2Server(builder.Configuration);

// builder.Services.ServerWithSingleProject();   // optional. Without it the server is multi-project.

var isCommand = Auth2Commands.IsCommand(args);
if (!isCommand) builder.Services.AddHostedService<Auth2StartupService>();

var app = builder.Build();

if (isCommand) return await Auth2Commands.RunAsync(args, app.Services);

var server = app.Services.GetRequiredService<IOptions<Auth2ServerOptions>>().Value;
if (server.Security.UseForwardedHeaders) app.UseForwardedHeaders();   // first: the client address and scheme come from the proxy
app.UseAuth2RequestLogging();
app.UseExceptionHandler("/Error");
app.UseStatusCodePages(async context =>
{
    var path = context.HttpContext.Request.Path.Value ?? "";
    if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/connect/", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase))
    {
        if (context.HttpContext.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            context.HttpContext.Response.Redirect("/NotFound?code=404");
        }
        else if (context.HttpContext.Response.StatusCode == StatusCodes.Status403Forbidden)
        {
            context.HttpContext.Response.Redirect("/NotAllowed?code=403");
        }
    }
});
if (HardeningSetup.RequireHttps(app.Environment, server))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseAuth2Localization();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<ProjectCookieMiddleware>();   // which project's session cookie to read
// The answer of an upstream provider (/callback/login/...) is read by Auth2's own endpoint through OpenIddict.Client (see OpenIddictIdentitySource), whose
// state lives in Auth2's sealed state cookie. The OpenIddict middleware must not handle that request itself: it would look for its own correlation cookie and answer 400.
app.UseWhen(http => !http.Request.Path.StartsWithSegments("/callback/login"), branch => branch.UseAuthentication());
// WebApplication adds its own UseAuthentication at the start of the pipeline unless the application says it has done so. A branch does not say it.
((IApplicationBuilder)app).Properties["__AuthenticationMiddlewareSet"] = true;
app.UseAuthorization();

app.MapHealthChecks(Auth2Routes.HealthLive, new HealthCheckOptions { Predicate = _ => false });   // the process runs
app.MapHealthChecks(Auth2Routes.HealthReady, new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });   // database (and Redis) answer
app.MapAuth2Connect();
app.MapAuth2Account();
app.MapAuth2External();
app.MapAuth2Passkeys();
app.MapAuth2Client();
app.MapAuth2Admin();
app.MapAuth2Branding();
app.MapRazorPages();
await app.RunAsync();

return 0;

/// <summary>Entry point type. Public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
