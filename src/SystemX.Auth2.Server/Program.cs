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
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "SystemX.Auth2 Server");
app.MapHealthChecks(Auth2Routes.HealthLive, new HealthCheckOptions { Predicate = _ => false });   // the process runs
app.MapHealthChecks(Auth2Routes.HealthReady, new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });   // database (and Redis) answer
app.MapAuth2Connect();
app.MapAuth2Account();
app.MapAuth2External();
app.MapAuth2Passkeys();
app.MapAuth2Client();
app.MapAuth2Admin();
app.MapRazorPages();
await app.RunAsync();
return 0;

/// <summary>Entry point type. Public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
