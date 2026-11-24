using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Enums;

var builder = WebApplication.CreateBuilder(args);

// Add MVC controllers with Razor views
builder.Services.AddControllersWithViews();

// Register SystemX.Auth2 client
var auth2Section = builder.Configuration.GetSection("Auth2");
builder.Services
    .AddAuth2Client(auth2Section)
    .ServerWithMultiProject(builder.Configuration["Auth2:ProjectKey"] ?? "sample-project")
    .UseHostedUi()
    .UseCookieAuthentication(o =>
    {
        o.AccessMode = AccessLoadMode.OnSignIn;
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
    })
    .UseApiAuthentication()
    .EnableCurrentUser(o => o.CacheTtl = TimeSpan.FromMinutes(10))
    .EnablePermissions(o =>
    {
        o.SyncPolicy = PermissionSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.PermissionList =
        [
            new("orders.read", "Read Orders", "Allows viewing customer orders", "Orders"),
            new("orders.write", "Write Orders", "Allows creating and updating customer orders", "Orders"),
            new("reports.read", "Read Reports", "Allows generating and reading business reports", "Reports")
        ];
        o.AssembliesToScan = [typeof(Program).Assembly];
    })
    .EnableRoles(o =>
    {
        o.SyncPolicy = RoleSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.RoleList =
        [
            new("Admin", "Full administrative access"),
            new("Manager", "Operations management access")
        ];
        o.AssembliesToScan = [typeof(Program).Assembly];
    })
    .EnableScopes(o =>
    {
        o.SyncPolicy = ScopeSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.ScopeList =
        [
            new("reports.read", "Read reports scope")
        ];
    })
    .EnableGroups(o =>
    {
        o.SyncPolicy = GroupSyncPolicy.Both;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.GroupList =
        [
            new("finance", "Finance and accounting department")
        ];
    })
    .EnableResilience()
    .EnableHealthCheck();

// Configure OpenID Connect public endpoints for browser redirects (e.g. Docker host port 5100 vs container port 5000)
builder.Services.Configure<OpenIdConnectOptions>(Auth2AuthSchemes.Oidc, options =>
{
    var publicServerUrl = builder.Configuration["Auth2:PublicServerUrl"];
    if (!string.IsNullOrEmpty(publicServerUrl))
    {
        var publicUri = new Uri(publicServerUrl);
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            if (!string.IsNullOrEmpty(context.ProtocolMessage.IssuerAddress))
            {
                var uri = new Uri(context.ProtocolMessage.IssuerAddress);
                var replaced = new UriBuilder(uri)
                {
                    Scheme = publicUri.Scheme,
                    Host = publicUri.Host,
                    Port = publicUri.Port
                };
                context.ProtocolMessage.IssuerAddress = replaced.ToString();
            }
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            if (!string.IsNullOrEmpty(context.ProtocolMessage.IssuerAddress))
            {
                var uri = new Uri(context.ProtocolMessage.IssuerAddress);
                var replaced = new UriBuilder(uri)
                {
                    Scheme = publicUri.Scheme,
                    Host = publicUri.Host,
                    Port = publicUri.Port
                };
                context.ProtocolMessage.IssuerAddress = replaced.ToString();
            }
            return Task.CompletedTask;
        };

        var serverUrl = builder.Configuration["Auth2:ServerUrl"] ?? "http://server:5000";
        options.TokenValidationParameters.ValidIssuers =
        [
            serverUrl.TrimEnd('/') + "/",
            serverUrl.TrimEnd('/'),
            publicServerUrl.TrimEnd('/') + "/",
            publicServerUrl.TrimEnd('/')
        ];
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// SystemX.Auth2 middleware: UseAuthentication, UseAuthorization, and /auth2/ routes
app.UseAuth2();

app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

app.Run();
