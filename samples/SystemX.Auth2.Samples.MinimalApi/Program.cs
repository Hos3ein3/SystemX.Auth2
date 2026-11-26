using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Client.Sync;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Enums;

var builder = WebApplication.CreateBuilder(args);

// Setup Auth2 Client for Minimal API (API authentication mode with JWT Bearer tokens)
var auth2Section = builder.Configuration.GetSection("Auth2");
builder.Services
    .AddAuth2Client(auth2Section)
    .ServerWithMultiProject(builder.Configuration["Auth2:ProjectKey"] ?? "sample-project")
    .UseApiAuthentication()
    .EnableCurrentUser(o =>
    {
        o.Source = CurrentUserSource.TokenAndServer;
        o.CacheTtl = TimeSpan.FromMinutes(10);
    })
    .EnablePermissions(o =>
    {
        o.SyncPolicy = PermissionSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.PermissionList =
        [
            new("orders.read", "Read Orders", "Permission to read and search customer orders", "Orders"),
            new("orders.write", "Write Orders", "Permission to create, update, or cancel customer orders", "Orders"),
            new("reports.read", "Read Reports", "Permission to view operational and revenue reports", "Reports")
        ];
    })
    .EnableRoles(o =>
    {
        o.SyncPolicy = RoleSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.RoleList =
        [
            new("Admin", "Full administrative control"),
            new("Manager", "Management access to view data and orders")
        ];
    })
    .EnableScopes(o =>
    {
        o.SyncPolicy = ScopeSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.ScopeList =
        [
            new("reports.read", "Access to reporting endpoints")
        ];
    })
    .EnableGroups(o =>
    {
        o.SyncPolicy = GroupSyncPolicy.Both;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.GroupList =
        [
            new("finance", "Finance operations group")
        ];
    })
    .EnableResilience()
    .EnableHealthCheck(o =>
    {
        // Two-way: the Auth2 server also asks this application every minute whether it is healthy and ready (the answer is shown on the dashboard of the project in the admin console).
        // No PublicUrl: the server calls the address the handshake came from, at the port this application listens on. Set o.PublicUrl behind a proxy or a load balancer.
        o.Mode = Auth2HealthCheckMode.TwoWay;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SystemX.Auth2 Minimal API Sample",
        Version = "v1",
        Description = "Minimal API sample showcasing SystemX.Auth2 client integration with JWT Bearer tokens, permission checks, and catalog sync."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT Bearer token to authorize requests. Example: 'Bearer {token}'"
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SystemX.Auth2 Minimal API v1");
    c.RoutePrefix = "swagger";
});

// Auth2 middleware adds authentication, authorization, and route conventions
app.UseAuth2();

// Health check endpoint (checks handshake status: Healthy / Degraded)
app.MapHealthChecks("/health");

// In-memory sample data store
var orders = new List<MinimalOrderDto>
{
    new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "ORD-M101", "Cloud Server License", 249.00m, "Active"),
    new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "ORD-M102", "Database Storage Add-on", 59.00m, "Active")
};

// -------------------------------------------------------------
// Public Endpoints
// -------------------------------------------------------------
app.MapGet("/", () => Results.Ok(new
{
    Application = "SystemX.Auth2.Samples.MinimalApi",
    Description = "SystemX.Auth2 Minimal API Client Sample",
    Endpoints = new[]
    {
        "GET /swagger (Swagger UI)",
        "GET /health",
        "GET /api/public",
        "GET /api/status",
        "POST /api/auth/login",
        "GET /api/me (Auth2)",
        "GET /api/client (Auth2)",
        "GET /api/orders (perm: orders.read)",
        "POST /api/orders (perm: orders.write)",
        "GET /api/orders/role-only (perm: orders.read, role-only source)",
        "GET /api/orders/user-only (perm: orders.read, user-only source)",
        "GET /api/admin (role: Admin)",
        "GET /api/super-admin (roles: Admin AND Manager)",
        "GET /api/reports (scope: reports.read)",
        "GET /api/machine-only (machine tokens only)",
        "GET /api/user-only (user tokens only)",
        "GET /api/finance (group: finance)",
        "GET /api/catalog (synced catalogs)"
    }
})).WithAnonymous();

app.MapGet("/api/public", () => Results.Ok(new
{
    Message = "Public Minimal API endpoint. Anyone can call this without a token.",
    ServerTimeUtc = DateTimeOffset.UtcNow
})).WithAnonymous();

app.MapGet("/api/status", (Auth2ClientState state) => Results.Ok(new
{
    HandshakeStatus = state.Status?.ToString(),
    state.LastResponse?.ServerMode,
    state.LastResponse?.Issuer,
    state.LastResponse?.CookieLifetime,
    state.LastResponse?.SingleActiveSession
})).WithAnonymous();

// Programmatic login helper via IAuth2ApiClientService
app.MapPost("/api/auth/login", async ([FromBody] LoginRequest request, IAuth2ApiClientService api, CancellationToken ct) =>
{
    var result = await api.LoginAsync(request, ct);
    if (!result.IsSuccess)
    {
        return Results.Problem(
            detail: result.Error?.Message ?? "Authentication failed",
            statusCode: StatusCodes.Status400BadRequest,
            title: result.Error?.Code ?? "invalid_credentials");
    }

    return Results.Ok(result.Value);
}).WithAnonymous();

// -------------------------------------------------------------
// Authenticated User & Client Endpoints
// -------------------------------------------------------------
app.MapGet("/api/me", async (ICurrentUser user, CancellationToken ct) => Results.Ok(new
{
    user.IsAuthenticated,
    user.UserId,
    user.UserName,
    user.Email,
    user.PhoneNumber,
    user.FirstName,
    user.LastName,
    user.FullName,
    Roles = await user.GetRolesAsync(ct),
    Permissions = await user.GetPermissionsAsync(ct),
    Groups = await user.GetGroupsAsync(ct)
})).WithAuth2();

app.MapGet("/api/client", (ICurrentClient client) => Results.Ok(new
{
    client.IsTokenAuthenticated,
    client.ClientId,
    client.ApplicationId,
    client.ProjectId,
    client.ProjectKey,
    client.Scopes,
    client.IsMachine,
    client.SessionId,
    client.Idp,
    client.Amr
})).WithAuth2();

// -------------------------------------------------------------
// Authorization Gated Endpoints
// -------------------------------------------------------------
// Role-based
app.MapGet("/api/admin", (ICurrentUser user) => Results.Ok(new
{
    Message = "Admin access granted.",
    user.UserName,
    TimestampUtc = DateTimeOffset.UtcNow
})).WithRole("Admin");

app.MapGet("/api/super-admin", (ICurrentUser user) => Results.Ok(new
{
    Message = "Super Admin (Admin AND Manager) access granted.",
    user.UserName,
    TimestampUtc = DateTimeOffset.UtcNow
})).WithAllRoles("Admin", "Manager");

// Permission-based with audit logging
app.MapGet("/api/orders", (ICurrentUser user) => Results.Ok(new
{
    RequestedBy = user.UserName,
    Count = orders.Count,
    Items = orders
})).WithPermission("orders.read").WithAuth2Audit("orders.read");

app.MapPost("/api/orders", ([FromBody] CreateMinimalOrderRequest request, ICurrentUser user) =>
{
    var newOrder = new MinimalOrderDto(Guid.NewGuid(), $"ORD-M{Random.Shared.Next(103, 999)}", request.Item, request.Amount, "Active");
    orders.Add(newOrder);
    return Results.Created($"/api/orders/{newOrder.Id}", newOrder);
}).WithPermission("orders.write").WithAuth2Audit("orders.create");

// A code of the authenticator app. An API has no page to send the person to, so the caller sends the code in the header X-Auth2-Totp
// (the person needs two-factor turned on at the Auth2 server). Without a code, with a wrong one, or without two-factor the answer is 403 with
// the body { "code": "totp_required" | "invalid_otp" | "totp_setup_required", "message": ... } and the header X-Auth2-Totp-Status.
app.MapPost("/api/transfers", ([FromBody] MinimalTransferRequest request, ICurrentUser user) => Results.Ok(new
{
    Message = $"Transfer of {request.Amount:0.##} accepted: the Server checked a code of your authenticator app just now.",
    RequestedBy = user.UserName,
    TimestampUtc = DateTimeOffset.UtcNow
})).WithTotp().WithAuth2Audit("transfers.create");

// The same, but only people who use two-factor are asked: a person without it passes without a code.
app.MapPost("/api/notes", ([FromBody] MinimalNoteRequest request, ICurrentUser user) => Results.Ok(new
{
    Message = "Note saved.",
    request.Text,
    RequestedBy = user.UserName
})).WithOptionalTotp();

// Permission source enforcement
app.MapGet("/api/orders/role-only", () => Results.Ok(new
{
    Message = "This permission must originate from a role or group, not a direct user grant."
})).WithPermission("orders.read").WithOnlyRolePermission();

app.MapGet("/api/orders/user-only", () => Results.Ok(new
{
    Message = "This permission must originate from a direct user grant, not from a role."
})).WithPermission("orders.read").WithOnlyUserPermission();

// Scope-based
app.MapGet("/api/reports", () => Results.Ok(new
{
    Report = "Financial Summary",
    TotalRevenue = orders.Sum(o => o.Amount),
    GeneratedAtUtc = DateTimeOffset.UtcNow
})).WithScope("reports.read");

// Token Kind: Machine vs User
app.MapGet("/api/machine-only", () => Results.Ok(new
{
    Message = "Authenticated with a Machine (M2M) token."
})).WithMachineOnly();

app.MapGet("/api/user-only", (ICurrentUser user) => Results.Ok(new
{
    Message = "Authenticated with a User (Person) token.",
    user.UserName
})).WithUserOnly();

// Group-based
app.MapGet("/api/finance", (ICurrentUser user) => Results.Ok(new
{
    Message = "Finance group member authorized.",
    user.UserName
})).WithGroup("finance");

// Synced Catalogs
app.MapGet("/api/catalog", (IAuth2Catalog catalog) => Results.Ok(new
{
    catalog.LastReadUtc,
    catalog.Permissions,
    catalog.Roles,
    catalog.Groups
})).WithAuth2();

app.Run();

// Data DTOs
public sealed record MinimalOrderDto(Guid Id, string OrderNumber, string Item, decimal Amount, string Status);
public sealed record CreateMinimalOrderRequest(string Item, decimal Amount);
public sealed record MinimalTransferRequest(decimal Amount);
public sealed record MinimalNoteRequest(string Text);
