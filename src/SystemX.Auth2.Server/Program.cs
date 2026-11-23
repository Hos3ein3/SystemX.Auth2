using SystemX.Auth2.Server.Endpoints;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Infrastructure.Commands;
using SystemX.Auth2.Server.Infrastructure.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSystemXAuth2Server(builder.Configuration);

// builder.Services.ServerWithSingleApplication();   // optional. Without it the server is multi-application.

var isCommand = Auth2Commands.IsCommand(args);
if (!isCommand) builder.Services.AddHostedService<Auth2StartupService>();

var app = builder.Build();

if (isCommand) return await Auth2Commands.RunAsync(args, app.Services);

app.UseAuth2Localization();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "SystemX.Auth2 Server");
app.MapAuth2Connect();
app.MapRazorPages();
await app.RunAsync();
return 0;

/// <summary>Entry point type. Public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
