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
    .UseHostedUi(o => o.Scopes.AddRange(["reports.read", "orders.read", "orders.write"]))   // sign in and out on the Auth2 server pages (/auth2/login and /auth2/logout); use .UseSelfUi() for your own form. The three scopes are asked for at every sign-in (the Showcase page shows what they do)
    .UseCookieAuthentication(o =>
    {
        o.AccessMode = AccessLoadMode.OnSignIn;
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
    .EnableHealthCheck(o =>
    {
        // Two-way: the Auth2 server also asks this application every minute whether it is healthy and ready (the answer is shown on the dashboard of the project in the admin console).
        // No PublicUrl: the server calls the address the handshake came from, at the port this application listens on. Set o.PublicUrl behind a proxy or a load balancer.
        o.Mode = Auth2HealthCheckMode.TwoWay;
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
