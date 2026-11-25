# Client authorization

## What this is

The words (scope, permission, role, group) are explained side by side in [access-concepts.md](access-concepts.md). The Minimal API methods and the attributes, and when to use which, are in [client-endpoint-metadata.md](client-endpoint-metadata.md). This page is the reference for the rules.

How your application decides **who may call what**, using the roles, permissions, groups and scopes that the Auth2 Server knows.
It is built on normal ASP.NET Core authorization (requirements, handlers, policies). There is no separate engine.

## When to use it

You have an API (Minimal API, MVC controllers, Razor Pages) and you want rules such as "needs permission `orders.write`" or "only machines".

## Step by step

1. Set up the client and API authentication (see [client-setup.md](client-setup.md)):

```csharp
builder.Services
    .AddAuth2Client("https://auth.example.com", o => { /* name, client id, secret */ })
    .ServerWithMultiProject("papertrade")
    .UseApiAuthentication(o => o.Audience = "papertrade-api")   // Audience defaults to ClientId
    .EnableCurrentUser(o => o.CacheTtl = TimeSpan.FromMinutes(10));

var app = builder.Build();
app.UseAuth2();     // UseAuthentication + UseAuthorization
```

2. Mark your endpoints (Minimal API):

```csharp
app.MapGet("/public", () => "hello").WithAnonymous();
app.MapGet("/orders", () => orders).WithPermission("orders.read");
app.MapPost("/orders", Create).WithPermission("orders.write").WithAuth2Audit("orders.create");
app.MapGet("/admin", () => "admin").WithRole("Admin");
app.MapGet("/report", () => data).WithScope("reports.read").WithMachineOnly();
```

3. Or use attributes (controllers, Razor Page models, Blazor components, even lambdas):

```csharp
[Auth2Permission("orders.read")]
public IActionResult Index() => View();
```

## The rules

| Minimal API | Attribute | Needs |
|---|---|---|
| `.WithAuth2()` | `[Auth2Authorize]` | A valid token |
| `.WithAnonymous()` | `[Auth2Anonymous]` | Nothing |
| `.WithRole("A","B")` | `[Auth2Role("A","B")]` | **Any** of the roles (`WithAllRoles` = all) |
| `.WithPermission("a","b")` | `[Auth2Permission("a","b")]` | **Any** of the permissions (`WithAllPermissions` = all) |
| `.WithOnlyRolePermission()` | `[Auth2OnlyRolePermission]` | The permissions above must come from a **role** (or the role of a group), not a direct grant |
| `.WithOnlyUserPermission()` | `[Auth2OnlyUserPermission]` | The permissions above must be **direct grants** |
| `.WithScope("s")` | `[Auth2Scope("s")]` | Any of the scopes in the token (`ICurrentClient`) |
| `.WithGroup("g")` | `[Auth2Group("g")]` | The user is in any of the groups |
| `.WithMachineOnly()` / `.WithUserOnly()` | `[Auth2MachineOnly]` / `[Auth2UserOnly]` | A client-credentials token / a user token (`ICurrentClient.IsMachine`) |
| `.WithRoleOrPermission(roles, permissions)` and `.WithAnyOf(...)` | `[Auth2RoleOrPermission(Roles = ..., Permissions = ...)]`, `[Auth2AnyOf(...)]` | A role **or** a permission (also `OrGroup`, `ScopeOrRole`): OR **across kinds**. See [client-endpoint-metadata.md](client-endpoint-metadata.md#any-of-rules-a-role-or-a-permission) |
| `.WithAuth2Policy("name")` | (use `[Authorize(Policy="name")]`) | One of your own policies |
| `.WithAuth2Audit("action")` | `[Auth2Audit("action")]` | Raises an audit event (see below) |

**Chaining means AND**: `.WithRole("Editor").WithPermission("orders.write")` needs both. Scopes belong to the **client** and permissions to the **user**, so `.WithScope("reports.read").WithPermission("orders.read")` needs a user token that has the scope and whose user has the permission (a machine token fails the permission, a user token without the scope fails the scope). It also works on route groups: `app.MapGroup("/admin").WithRole("Admin")`.
A permission group inside the same call is OR (`WithPermission("a","b")`).

Where each check reads its data:

| Check | Reads | Cost |
|---|---|---|
| Role, scope, machine / user | The claims of the token (`ICurrentClient` for scope and machine / user). The `role` claim is in the token only when the `roles` scope was granted; a token without role claims makes the role rule read the access data instead (one cached call). A role given or taken after the token was issued shows only when the token is renewed | none |
| Permission, group | The access data from the server (`/api/auth2/me`), cached | one call per cache time |

## Answers

- No token, or a bad token: **401** (never a redirect on an API).
- Valid token but not allowed: **403**.
- The server cannot be asked for permissions and nothing is cached: **403** (fail closed), and a warning in the log. Checks that use only the token still work.

## Audit hook

`WithAuth2Audit("orders.create")` raises an `Auth2AuthorizationEvent` (action, allowed or refused, user id, machine or not, path, time) to `IAuth2AuditSink`. The default sink writes a log line. Register your own to write to your audit log:

```csharp
builder.Services.AddSingleton<IAuth2AuditSink, MyAuditSink>();
```

## Keep permission names in one place

```csharp
public static class Perms
{
    public const string OrdersRead = "orders.read";
    public const string OrdersWrite = "orders.write";
}

app.MapGet("/orders", Get).WithPermission(Perms.OrdersRead);
```

Permission keys are lowercase letters, digits and `. _ : -`.

## API authentication

`UseApiAuthentication` accepts the JWT access tokens of the server:

- the signing keys come from the server's discovery document and are cached;
- a token with an unknown key id makes the client read the keys again and wait up to 2 seconds for them, so key rotation needs no restart and the first request with the new key works. This happens at most once per `KeyRefreshInterval` (30 s), so made-up key ids cannot make your server wait;
- the audience must be this application's client id, so a token for another application is refused;
- machine tokens and user tokens are both accepted. Limit them per endpoint.

Limits: the client checks the token's signature, issuer, audience and lifetime. It does **not** ask the server whether the token was revoked, so a revoked token works until it expires. Keep access token lifetimes short (the default is 15 minutes).

## Common problems

| Problem | Cause and fix |
|---|---|
| Always 401 | Wrong `Authority` or `Audience`; the token's issuer is not the server address you set. Use the address the server writes as issuer (`Auth2:Issuer`) |
| Always 403 for a permission | The user does not have it, the key has a typo (keys are lowercase), or the server could not be reached (see the log) |
| A role I just gave does not work | Roles are in the token: log in again. Permissions refresh when the token is renewed or the cache time ends (see [client-current-user.md](client-current-user.md)) |
| `WithOnlyRolePermission` always 403 | The server is older than this version and does not send the source lists. Update the server |
| 401 right after a key rotation | Should not happen. If the server was unreachable for the key read, the next request after `KeyRefreshInterval` works |

## Related pages

- [client-current-user.md](client-current-user.md)
- [client-setup.md](client-setup.md)
- [authorization.md](authorization.md) (how the server calculates access)
- [The key ring and its snapshot](key-ring.md)
- [Security](security.md)
- [Deployment and scaling](deployment-and-scaling.md)
- [Server setup](server-setup.md)
- [Observability](observability.md)
- [Admin JSON API](admin-api.md)
- [Concepts](concepts.md)
- [Client package reference](client-reference.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
- [Handshake and startup](handshake-and-startup.md)
- [Getting started](getting-started.md)
- [Contracts reference](contracts-reference.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Enums reference](enums-reference.md)

## The same constants everywhere

Put your keys into constants once and use them in the attribute, the endpoint extension and the catalog:

```csharp
public static class OrderPermissions
{
    public const string Read = "orders.read";
    [Auth2Permission(DisplayName = "Write orders", Description = "Create and change orders")]
    public const string Write = "orders.write";
}

[Auth2Permission(OrderPermissions.Read)]                 // the policy name is made from the key; [Authorize(Policy = "auth2:perm:any:orders.read")] is the same
public IActionResult Index() => View();

app.MapPost("/orders", Create).WithPermission(OrderPermissions.Write);

builder.Services.AddAuth2Client(...).EnablePermissions(o => o.AddFromClass(typeof(OrderPermissions)));
```

See [permission-sync.md](permission-sync.md) for all catalog sources.
