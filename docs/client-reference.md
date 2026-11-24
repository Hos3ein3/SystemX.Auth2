# Client package reference

## What this is

Every public type and member of the `SystemX.Auth2.Client` package, with the overloads, and a sample for each group. The samples are taken from code that runs in this repository: `samples/SystemX.Auth2.Samples.Mvc/Program.cs`, `samples/SystemX.Auth2.Samples.MinimalApi/Program.cs` and the tests in `tests/SystemX.Auth2.Client.Tests`.
The tables at the end are generated from the XML comments of the code (see [tools/docs/README.md](../tools/docs/README.md)); the text above them is written by hand.

## When to use it

When you know what you want to do and need the exact method, its overloads and the options it takes. For the story (what the handshake is, which UI mode to pick) read [client-setup.md](client-setup.md) first.

## How to use it

### 1. Register the client: `AddAuth2Client` (2 overloads)

| Overload | Use it when |
|---|---|
| `AddAuth2Client(IServiceCollection, string serverUrl, Action<Auth2ClientOptions>? configure = null)` | You set the options in code |
| `AddAuth2Client(IServiceCollection, IConfigurationSection section)` | The options are in configuration (`Auth2` section). The secret comes from an environment variable or a secret store |

```csharp
// from configuration: samples/SystemX.Auth2.Samples.Mvc/Program.cs
builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject(builder.Configuration["Auth2:ProjectKey"] ?? "sample-project");

// in code: docs/client-setup.md
builder.Services.AddAuth2Client("https://auth.example.com", o =>
{
    o.ApplicationName = "papertrade-api";
    o.ClientId = "papertrade-api";
    o.ClientSecret = builder.Configuration["Auth2:Secret"]!;
    o.OnServerUnavailable = StartupFailureBehavior.Continue;
    o.OnProjectNotFound = StartupFailureBehavior.Throw;
}).ServerWithMultiProject("papertrade");
```

The sample's `appsettings.json` section:

```json
"Auth2": { "ServerUrl": "http://localhost:5100", "ApplicationName": "MvcSample", "ClientId": "mvc-sample",
           "ClientSecret": "mvc-sample-secret", "ProjectKey": "sample-project",
           "OnServerUnavailable": "Continue", "OnProjectNotFound": "Continue" }
```

The builder methods can be called in any order. Each returns the builder.

### 2. The builder methods

| Method | Options class | What it turns on |
|---|---|---|
| `ServerWithMultiProject(string projectKey)` | none | Names the project of this application. Do not call it against a single-project server |
| `UseApiAuthentication(Action<Auth2ApiOptions>?)` | `Auth2ApiOptions` | JWT bearer authentication: discovery, cached keys, audience = the client id |
| `UseCookieAuthentication(Action<Auth2CookieOptions>?)` | `Auth2CookieOptions` | The sign-in cookie of browser apps (`LoginPath`, `AccessDeniedPath`, `AccessMode`, `SaveTokens`) |
| `UseHostedUi(Action<Auth2HostedUiOptions>?)` | `Auth2HostedUiOptions` | Sign in on the Server's pages (code flow with PKCE). Maps `/auth2/login` and `/auth2/logout` |
| `UseSelfUi()` | none | Your own login form; `IAuth2WebService` signs the cookie in |
| `UseBlazor()` | none | `IAuth2BlazorService` and the access mode that fits a circuit |
| `EnableCurrentUser(Action<Auth2CurrentUserOptions>?)` | `Auth2CurrentUserOptions` | `ICurrentUser` and `ICurrentClient`: `Source`, `CacheTtl` |
| `UseDistributedCurrentUserStore()` | none | Keeps access data in `IDistributedCache` (Redis) instead of memory |
| `EnablePermissions(Action<Auth2PermissionOptions>?)` | `Auth2PermissionOptions` | The permission catalog: `SyncPolicy`, `ScanningPolicy`, sources |
| `EnableRoles(Action<Auth2RoleOptions>?)` | `Auth2RoleOptions` | The same for roles |
| `EnableScopes(Action<Auth2ScopeOptions>?)` | `Auth2ScopeOptions` | The same for custom scopes |
| `EnableGroups(Action<Auth2GroupOptions>?)` | `Auth2GroupOptions` | The same for groups |
| `EnableMachineToMachine(Action<Auth2MachineOptions>?)` | `Auth2MachineOptions` | Client-credentials tokens for calling other APIs (`Scopes`) |
| `EnableResilience()` | none | Retries, timeouts and a circuit breaker on the calls that can safely be repeated |
| `EnableHealthCheck()` | none | A health check named `auth2`: healthy when the last handshake worked, degraded otherwise |

Application part of the pipeline: `app.UseAuth2()` (= `UseAuthentication()` + `UseAuthorization()` + the `/auth2/...` routes). The whole chain, as the MVC sample writes it:

```csharp
builder.Services
    .AddAuth2Client(auth2Section)
    .ServerWithMultiProject(builder.Configuration["Auth2:ProjectKey"] ?? "sample-project")
    .UseHostedUi()
    .UseCookieAuthentication(o =>
    {
        o.AccessMode = AccessLoadMode.OnSignIn;
        o.AccessDeniedPath = "/Account/AccessDenied";
    })
    .UseApiAuthentication()
    .EnableCurrentUser(o => o.CacheTtl = TimeSpan.FromMinutes(10))
    .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; })
    .EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; })
    .EnableScopes(o => { o.SyncPolicy = ScopeSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; })
    .EnableGroups(o => { o.SyncPolicy = GroupSyncPolicy.Both; o.ScanningPolicy = ScanningPolicy.Both; })
    .EnableResilience()
    .EnableHealthCheck();
// ...
app.UseAuth2();
```

Policies: `SyncPolicy` is `None`, `Push`, `Pull` or `Both`. `ScanningPolicy` is `Manual` (only your list and sources), `AssemblyScan` (attributes in `AssembliesToScan` and on endpoints) or `Both`. `AccessLoadMode` is `Lazy`, `OnSignIn`, `RolesInCookie` or `None` ([client-current-user.md](client-current-user.md)). `CurrentUserSource` is `TokenOnly` or `TokenAndServer`.

### 3. Catalog sources: the overloads of `Add...`

Every catalog (`Auth2PermissionOptions`, `Auth2RoleOptions`, `Auth2ScopeOptions`, `Auth2GroupOptions`) inherits `CatalogOptions<T>`. All methods return the options, so they chain; the same key from two sources is merged (the same data is ignored, different data fails unless `DuplicatePolicy.FirstWins`). A wrong key stops the start with a message that names the source ([permission-sync.md](permission-sync.md)).

| Method (overloads) | Reads | Sample |
|---|---|---|
| `Add(TDefinition)` and the `...List` property | one definition | `o.Add(new RoleDefinition("Added", "Added role", null, ["orders.read"]))`, `o.PermissionList = [ new("orders.read", "Read Orders", "...", "Orders") ]` |
| `AddFromClass(Type)` | `const string` and `static readonly string` fields, also in nested classes (not properties) | `o.AddFromClass(typeof(OrderKeys))` |
| `AddFromEnum<TEnum>(KeyStyle = DotCase)` and `AddFromEnum(Type, KeyStyle)` | the members of an enum | `p.AddFromEnum<ReportKeys>(KeyStyle.SnakeCase)` |
| `AddFromString(string, char separator = ',', string? category = null)` | one text | `o.AddFromString("a.read;b.read; c.read", ';', "Reports")` |
| `AddFromListString(IEnumerable<string>, string? category = null)` | a list of texts | `o.AddFromListString(["finance", " support ", ""])` |
| `AddFromConfiguration(IConfigurationSection)` | plain values and sections | `p.AddFromConfiguration(config.GetSection("Perm"))` |
| `AddProvider<TProvider>()` | a class resolved from DI at start (`IPermissionProvider`, `IRoleProvider`, `IScopeProvider`, `IGroupProvider`) | `p.AddProvider<StaticProvider>()` |
| `AddFactory(Func<IServiceProvider, IEnumerable<T>>)` and `AddFactory(Func<IServiceProvider, CancellationToken, Task<IEnumerable<T>>>)` | a factory, sync or async | `.AddFactory(async (_, ct) => { await Task.Delay(1, ct); return [new PermissionDefinition("async.read")]; })` |

`KeyStyle`: `DotCase` (`OrdersRead` becomes `orders.read`, the default), `AsIs` (the member name; roles only), `SnakeCase` (`orders_read`) and `KebabCase` (`orders-read`). Metadata for a key comes from attributes on the field or enum member: `[Auth2Permission(DisplayName = "...", Description = "...", Group = "...")]`, `[Auth2Role]`, `[Auth2Scope]`, `[Auth2Group]`, `[Auth2Key("custom.key")]`, `[Auth2Category("Orders")]`.
The samples use the two simplest ways: a list (`PermissionList`, `RoleList`, `ScopeList`, `GroupList`) and `AssembliesToScan = [typeof(Program).Assembly]` so that attributes are found.

### 4. Protecting endpoints

**Attributes** (controllers, Razor Pages, Blazor): `[Auth2Permission("orders.read")]` (also `(bool requireAll, params string[])`), `[Auth2Role("Admin")]`, `[Auth2Scope("reports.read")]`, `[Auth2Group("finance")]`, `[Auth2Authorize]`, `[Auth2Anonymous]`, `[Auth2MachineOnly]`, `[Auth2UserOnly]`, `[Auth2OnlyRolePermission]`, `[Auth2OnlyUserPermission]`, `[Auth2Audit("name")]`, `[Auth2ApiEndpoint]`. Several attributes on one endpoint are AND. The MVC sample:

```csharp
[Auth2Permission("orders.read")]  // HomeController.Orders
[Auth2Role("Admin")]              // HomeController.AdminPanel
```

**Minimal API extensions** (endpoints and route groups; chaining is AND). From the Minimal API sample:

| Extension | Needs |
|---|---|
| `.WithAnonymous()` | nobody (public) |
| `.WithAuth2()` | a signed-in user (valid token) |
| `.WithRole("Admin")`, `.WithAllRoles("Admin", "Manager")` | any of the roles / all of them (checked in the token) |
| `.WithPermission("orders.read")`, `.WithAllPermissions(...)` | one permission / all |
| `.WithScope("reports.read")` | a scope in the token |
| `.WithGroup("finance")` | one of the groups |
| `.WithMachineOnly()`, `.WithUserOnly()` | a machine token / a user token |
| `.WithOnlyRolePermission()`, `.WithOnlyUserPermission()` | with `WithPermission`: only roles / only direct grants count |
| `.WithAuth2Policy("my-policy")` | a policy you registered with `AddAuthorization` |
| `.AsAuth2Api()` | marks an API endpoint: not signed in answers 401/403, never a redirect |
| `.WithAuth2Audit("orders.read")` | raises an `Auth2AuthorizationEvent` to `IAuth2AuditSink` every time the endpoint is used |

```csharp
app.MapGet("/api/orders", (ICurrentUser user) => Results.Ok(/* ... */)).WithPermission("orders.read").WithAuth2Audit("orders.read");
app.MapGet("/api/finance", (ICurrentUser user) => Results.Ok(/* ... */)).WithGroup("finance");
app.MapGet("/api/reports", () => Results.Ok(/* ... */)).WithScope("reports.read");
```

### 5. Who is calling: `ICurrentUser` and `ICurrentClient`

`ICurrentClient` (synchronous, from the claims): token, client id, application id, project id and key, scopes, `HasScope`, machine flag, session id, `Idp`, `Amr`. `ICurrentUser` (the person): `IsAuthenticated`, `UserId`, `UserName`, `Email`, `FullName`, `GetRolesAsync`, `GetPermissionsAsync`, `GetGroupsAsync`, `HasPermissionAsync`, `HasRoleAsync`. See [client-current-user.md](client-current-user.md). Sample (`HomeController`): `await currentUser.GetRolesAsync(ct)`, `await currentUser.HasPermissionAsync("orders.read", ct)`.

### 6. Calling the Server: `IAuth2ApiClientService`

Returns `Result` / `Result<T>` and never throws for a failed call. The Minimal API sample's `/api/auth/login` is one line: `var result = await api.LoginAsync(request, ct);`. Methods for login, two-factor, OTP, register, password and token calls, `/me`, sync, pull and the external provider list ([api-reference.md](api-reference.md)).

### 7. Browser sign-in: `IAuth2WebService`

`LoginAsync`, `TwoFactorLoginAsync`, `OtpLoginAsync` (Self UI: sign the cookie in), `LogoutAsync`, `ChallengeAsync`. Sample (`AccountController` of the MVC sample with `.UseSelfUi()`):

```csharp
var result = await web.LoginAsync(HttpContext, new LoginRequest(model.Login, model.Password), model.RememberMe, ct);
```

### 8. Machine to machine

`IAuth2MachineService` and `IAuth2MachineTokenProvider` give client-credentials tokens; `AddAuth2MachineToken(params string[] scopes)` on an `IHttpClientBuilder` adds them to every request of that client ([machine-to-machine.md](machine-to-machine.md)).

### Errors

Services return `Result`. Building blocks (token provider, handler, start-up) throw `Auth2Exception` (`StatusCode`, `ErrorCode`, `IsTransient`). `Auth2CatalogException` stops the start for a wrong catalog entry. A session that ended on the Server (`invalid_token` while reading access data) is handled by `UseAuth2()`: the cookie is removed and the browser goes to sign in again (an API call gets 401).

## Related pages

- [client-setup.md](client-setup.md)
- [client-authorization.md](client-authorization.md)
- [client-current-user.md](client-current-user.md)
- [permission-sync.md](permission-sync.md)
- [contracts-reference.md](contracts-reference.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Admin JSON API](admin-api.md)
- [Error codes](error-codes.md)
- [Authentication flows](authentication-flows.md)
- [Handshake and startup](handshake-and-startup.md)
- [Getting started](getting-started.md)
- [Server reference](server-reference.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Server settings](server-settings.md)

## API tables (generated)

<!-- generated:start -->
## `Microsoft.AspNetCore.Builder`


### Auth2EndpointExtensions

Authorization for Minimal API endpoints (and route groups). Chaining means AND: `.WithRole("Admin").WithPermission("orders.write")` needs both. Each call adds metadata, so the scanners can read what your endpoints need.

| Member | What it does |
|---|---|
| `WithAuth2(this T)` | A signed-in user (valid token) is needed. |
| `WithAnonymous(this T)` | Anyone may call, even without a token. |
| `WithRole(this T, string[])` | The user needs **any** of these roles (checked in the token). |
| `WithAllRoles(this T, string[])` | The user needs **all** of these roles. |
| `WithPermission(this T, string[])` | The user needs **any** of these permissions. |
| `WithAllPermissions(this T, string[])` | The user needs **all** of these permissions. |
| `WithOnlyRolePermission(this T)` | The permissions of this endpoint must come from a role (or the role of a group). A direct grant is not enough. |
| `WithOnlyUserPermission(this T)` | The permissions of this endpoint must be direct grants to the user. A role is not enough. |
| `WithScope(this T, string[])` | The token needs **any** of these scopes. |
| `WithMachineOnly(this T)` | Only machine tokens (client credentials) are accepted. |
| `WithUserOnly(this T)` | Only user tokens are accepted. |
| `WithGroup(this T, string[])` | The user must be in **one** of these groups. |
| `WithAuth2Policy(this T, string)` | Uses a policy you registered with `AddAuthorization`. |
| `WithAuth2Audit(this T, string)` | Raises an audit event (see `IAuth2AuditSink`) every time this endpoint is authorized or refused. |


### Auth2EndpointRouteExtensions

Routes of the hosted UI, and the API marker for endpoints.

| Member | What it does |
|---|---|
| `AsAuth2Api(this T)` | Marks the endpoint as an API endpoint: when the user is not signed in it answers 401 / 403 and never redirects to the login page. |
| `MapAuth2Endpoints(this IEndpointRouteBuilder)` | Maps `GET /auth2/login?returnUrl=...` (starts the sign-in on the server), `/auth2/logout` and `/auth2/denied`. `UseAuth2()` calls it for you. Only paths on your own site are accepted as `returnUrl`. |
| `UseAuth2Routes(this IApplicationBuilder)` | Adds authentication and authorization to the pipeline and, in a minimal-hosting app, the routes of `MapAuth2Endpoints`. |


## `Microsoft.Extensions.DependencyInjection`


### Auth2ApiAuthenticationExtensions

Authentication features of the builder.

| Member | What it does |
|---|---|
| `UseApiAuthentication(this IAuth2ClientBuilder, Action<Auth2ApiOptions>)` | Accepts the JWT access tokens of the Auth2 Server (for APIs). The keys are found through the server's discovery document, cached, and read again when a token with a new key id arrives (so key rotation needs no restart). Machine tokens and user tokens are both accepted; limit them per endpoint with `WithUserOnly()` / `WithMachineOnly()`. |


### Auth2ClientApplicationBuilderExtensions

Middleware registration of the client.

| Member | What it does |
|---|---|
| `UseAuth2(this IApplicationBuilder)` | Adds authentication and authorization to the pipeline, and the `/auth2/...` routes of the hosted UI. Call it where you would call `UseAuthentication()` and `UseAuthorization()`. |


### Auth2ClientServiceCollectionExtensions

Registration of the SystemX.Auth2 client. Every method can be called in any order.

| Member | What it does |
|---|---|
| `AddAuth2Client(this IServiceCollection, string, Action<Auth2ClientOptions>)` | Adds the client. `serverUrl` is the address of the Auth2 Server. |
| `AddAuth2Client(this IServiceCollection, IConfigurationSection)` | Adds the client with options from configuration (keys such as `ServerUrl`, `ApplicationName`, `ClientId`, `ClientSecret`, `ProjectKey`). |
| `ServerWithMultiProject(this IAuth2ClientBuilder, string)` | Says that the server is **multi-project** and what the project key of this application is. Leave this call out for a single-project server. The handshake checks that you chose right. |
| `EnablePermissions(this IAuth2ClientBuilder, Action<Auth2PermissionOptions>)` | Sets how permissions are found and moved. The sync itself runs from a hosted service (see `permission-sync`). |
| `EnableRoles(this IAuth2ClientBuilder, Action<Auth2RoleOptions>)` | Sets how roles are found and moved. |
| `EnableScopes(this IAuth2ClientBuilder, Action<Auth2ScopeOptions>)` | Sets how custom scopes (for example `reports.read`) are found and moved. The sync registers them on the server and allows them for this application. |
| `EnableGroups(this IAuth2ClientBuilder, Action<Auth2GroupOptions>)` | Sets how groups are found and moved (read, and with `Push` / `Both` also sent). |
| `EnableMachineToMachine(this IAuth2ClientBuilder, Action<Auth2MachineOptions>)` | Turns on machine-to-machine tokens. Scopes you set here are used when a call names none. |
| `EnableCurrentUser(this IAuth2ClientBuilder, Action<Auth2CurrentUserOptions>)` | Sets how the current user's access data is read and cached. |
| `UseDistributedCurrentUserStore(this IAuth2ClientBuilder)` | Keeps the access data of users in `IDistributedCache` (add Redis with `AddStackExchangeRedisCache`), so all instances of your application share it. |
| `EnableResilience(this IAuth2ClientBuilder)` | Adds retry (with jitter), timeout and circuit breaker to the calls that can safely be repeated (token, handshake, sync, reads). Register, login and password calls are never retried. |
| `EnableHealthCheck(this IAuth2ClientBuilder)` | Adds a health check named `auth2`: healthy when the last handshake worked, degraded otherwise. |
| `AddAuth2MachineToken(this IHttpClientBuilder, string[])` | Makes an HTTP client send a machine token with every request. Name the scopes the called API needs. |


### Auth2UiBuilderExtensions

Cookie, hosted UI, Self UI and Blazor features of the builder.

| Member | What it does |
|---|---|
| `UseCookieAuthentication(this IAuth2ClientBuilder, Action<Auth2CookieOptions>)` | Adds the sign-in cookie for browser apps (MVC, Razor Pages, Blazor). A request with a bearer token still uses the token, so this can be combined with `UseApiAuthentication`. Browsers that are not signed in are sent to `LoginPath`; APIs and AJAX calls get 401 / 403. |
| `UseHostedUi(this IAuth2ClientBuilder, Action<Auth2HostedUiOptions>)` | The hosted UI: users sign in on the Auth2 Server (authorization code with PKCE). The package maps `/auth2/login` and `/auth2/logout` (with `app.UseAuth2()`) and the callback paths. **Register the callback address** (for example `https://app.example.com/signin-oidc`) as a redirect URI of the application on the server. |
| `UseSelfUi(this IAuth2ClientBuilder)` | The Self UI: your application shows its own login forms and calls `IAuth2WebService.LoginAsync`. **Your application handles passwords**, and the server must allow it (`Clients.AllowSelfUiLogin`). Set `LoginPath` to your login page. |
| `UseBlazor(this IAuth2ClientBuilder)` | Blazor: cascading authentication state, `AuthorizeView` support with the Auth2 attributes, and `IAuth2BlazorService`. Pair it with `UseHostedUi()` or `UseSelfUi()`. In Blazor Server the access data is loaded at sign-in (`AccessMode.OnSignIn`) unless you choose another mode, because a circuit has no HTTP request to read the token from. |


## `SystemX.Auth2.Client`


### Auth2ClientDefaults

Names used by the client. Tests and advanced setups can configure the named HTTP clients with them.

| Member | What it does |
|---|---|
| `HttpClientName` | Name of the HTTP client for calls that must not be repeated automatically (register, login, password change). |
| `ResilientHttpClientName` | Name of the HTTP client for calls that can safely be repeated (token, handshake, sync, reads). Gets the resilience handler with `EnableResilience()`. |


### Auth2ClientErrorCodes

Stable error codes the client itself makes (the server codes are in `Auth2ErrorCodes`).

| Member | What it does |
|---|---|
| `ServerUnavailable` | The server could not be reached, or answered with a 5xx error, or was too slow. |
| `InvalidResponse` | The answer of the server could not be understood. |


### Auth2Exception

Error of a low-level building block (token provider, token handler, start-up). The services (`IAuth2ApiClientService`) never throw it: they return `Result` / `Result<T>`. It never holds secrets.

| Member | What it does |
|---|---|
| `(constructor)(Nullable<int>, string, string, bool, Exception)` | Error of a low-level building block (token provider, token handler, start-up). The services (`IAuth2ApiClientService`) never throw it: they return `Result` / `Result<T>`. It never holds secrets. |
| `StatusCode` | HTTP status of the answer, or null when there was none (network error, timeout). |
| `ErrorCode` | Stable error code. |
| `IsTransient` | True when trying again later may work (network error, timeout, 5xx). |


## `SystemX.Auth2.Client.Authentication`


### Auth2ApiEndpointAttribute

Marks an endpoint as an API endpoint: a failed sign-in answers 401 / 403 and never redirects.


### Auth2AuthSchemes

Names of the authentication schemes the client adds.

| Member | What it does |
|---|---|
| `Policy` | The default scheme when cookies are used: bearer token when the request has one, else the cookie. |
| `Cookie` | The sign-in cookie. |
| `Oidc` | The OpenID Connect sign-in (hosted UI). |


### Auth2BlazorUser

The user as a Blazor component sees it.

| Member | What it does |
|---|---|
| `(constructor)(bool, bool, Nullable<Guid>, string, string, string, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>)` | The user as a Blazor component sees it. |
| `Anonymous` | Nobody is signed in. |


### IAuth2BlazorService

Helpers for Blazor. Use it in components; it reads the user from the `AuthenticationStateProvider`.

| Member | What it does |
|---|---|
| `GetUserAsync(CancellationToken)` | The user of the current `AuthenticationState` and their access data. |
| `HasPermissionAsync(string, CancellationToken)` | True when the user has the permission. |
| `HasRoleAsync(string, CancellationToken)` | True when the user has the role. |
| `IsInGroupAsync(string, CancellationToken)` | True when the user is in the group. |
| `NavigateToLogin(NavigationManager, string)` | Goes to the sign-in route and comes back to the current page (or `returnUrl`). |
| `NavigateToLogout(NavigationManager)` | Goes to the sign-out route. |


### IAuth2WebService

Sign-in and sign-out for apps that show their own forms (Self UI), and for the hosted UI routes.

| Member | What it does |
|---|---|
| `LoginAsync(HttpContext, LoginRequest, bool, CancellationToken)` | Signs a user in with login and password (password grant; the server must allow `AllowSelfUiLogin`) and makes the cookie. When two-factor is needed the result succeeds with `TwoFactorRequired = true` and **no cookie is made**: call `TwoFactorLoginAsync` next. |
| `TwoFactorLoginAsync(HttpContext, TwoFactorRequest, bool, CancellationToken)` | Second step of a login. Makes the cookie when it works. |
| `OtpLoginAsync(HttpContext, string, string, bool, CancellationToken)` | Signs a user in with a one-time code. Makes the cookie when it works. |
| `LogoutAsync(HttpContext, CancellationToken)` | Ends the server session (revokes the refresh token) and removes the cookie. For the hosted UI use the `/auth2/logout` route, which also signs out on the server. |
| `ChallengeAsync(HttpContext, string)` | Sends the browser to sign in: the hosted UI starts the sign-in on the server; the Self UI goes to your `LoginPath`. After sign-in the user returns to `returnUrl` (a path on this site; anything else is ignored). |


## `SystemX.Auth2.Client.Authorization`


### Auth2AnonymousAttribute

Anyone may call this, even without a token.


### Auth2AuditAttribute

Raises an audit event (see `IAuth2AuditSink`) when this endpoint is authorized or refused.

| Member | What it does |
|---|---|
| `(constructor)(string)` | Raises an audit event (see `IAuth2AuditSink`) when this endpoint is authorized or refused. |
| `Action` |  |


### Auth2AuthorizationEvent

What happened to a request that reached an endpoint marked with `WithAuth2Audit` / `[Auth2Audit]`.

| Member | What it does |
|---|---|
| `(constructor)(string, bool, Nullable<Guid>, bool, string, DateTimeOffset)` | What happened to a request that reached an endpoint marked with `WithAuth2Audit` / `[Auth2Audit]`. |
| `Action` | The event name. |
| `Succeeded` | True when access was granted. |
| `UserId` | The user, or null (machine token, nobody). |
| `IsMachine` | True for a machine token. |
| `Path` | The request path (no query string). |
| `AtUtc` | When. |


### Auth2AuthorizeAttribute

The user must be signed in (a valid token). Use on controllers, Razor Page models and Blazor components.


### Auth2GroupAttribute

On a class or method: the user must be in one of these groups. On a `const string` field or an enum member: no names, only the data of the group (`DisplayName`).

| Member | What it does |
|---|---|
| `(constructor)(string[])` | Needs any of the groups. With no name (on a field) it only carries the data of the group. |
| `DisplayName` | Display name of the group (on a field or enum member). |
| `Groups` |  |


### Auth2MachineOnlyAttribute

Only machine tokens (client credentials) are accepted.

| Member | What it does |
|---|---|
| `(constructor)()` | Creates the attribute. |
| `MachineOnly` |  |


### Auth2OnlyRolePermissionAttribute

The permissions of this endpoint must come from a role (or the role of a group), not from a direct grant.

| Member | What it does |
|---|---|
| `Source` |  |


### Auth2OnlyUserPermissionAttribute

The permissions of this endpoint must be direct grants to the user, not come from a role.

| Member | What it does |
|---|---|
| `Source` |  |


### Auth2PermissionAttribute

On a class or method: the user needs a permission (one of the keys, or all of them with `RequireAll`). On a `const string` field (with `AddFromClass`) or an enum member (with `AddFromEnum`): no keys, only the data of the permission (`DisplayName`, `Description`); the value of the field is the key.

| Member | What it does |
|---|---|
| `(constructor)(string[])` | Needs any of the permissions. With no key (on a field) it only carries the data of the permission. |
| `(constructor)(bool, string[])` | Needs any (or all) of the permissions. |
| `DisplayName` | Display name of the permission (on a field or enum member). |
| `Description` | Description of the permission (on a field or enum member). |
| `Group` | Group label for screens. Without it the category (the class or enum name) is used. |
| `Permissions` |  |
| `RequireAll` |  |


### Auth2PermissionSource

Where a required permission must come from.

| Member | What it does |
|---|---|
| `Any` | Anywhere (direct grant, role, group). The default. |
| `RoleOnly` | From a role, or from the role of a group. A direct grant is not enough. |
| `UserOnly` | From a direct grant to the user. A role is not enough. |


### Auth2RoleAttribute

On a class or method: the user needs a role (one of the names, or all with `requireAll`). On a `const string` field or an enum member: no names, only the data of the role (`DisplayName`, `Description`, `Permissions`).

| Member | What it does |
|---|---|
| `(constructor)(string[])` | Needs any of the roles. With no name (on a field) it only carries the data of the role. |
| `(constructor)(bool, string[])` | Needs any (or all) of the roles. |
| `DisplayName` | Display name of the role (on a field or enum member). |
| `Description` | Description of the role (on a field or enum member). |
| `Permissions` | Permission keys the role carries (on a field). Not possible with string or enum sources. |
| `RoleNames` | The role names. |
| `RequireAll` |  |


### Auth2ScopeAttribute

On a class or method: the token needs one of these scopes. On a `const string` field or an enum member: no names, only the data of the scope (`DisplayName`, `Description`).

| Member | What it does |
|---|---|
| `(constructor)(string[])` | Needs any of the scopes. With no name (on a field) it only carries the data of the scope. |
| `DisplayName` | Display name of the scope (on a field or enum member). |
| `Description` | Description of the scope (on a field or enum member). |
| `Scopes` |  |


### Auth2UserOnlyAttribute

Only user tokens are accepted (not machine tokens).

| Member | What it does |
|---|---|
| `(constructor)()` | Creates the attribute. |
| `MachineOnly` |  |


### IAuth2AuditMetadata

Endpoint metadata: name of an audit event to raise when the endpoint is authorized or refused.

| Member | What it does |
|---|---|
| `Action` | Event name, for example `orders.delete`. |


### IAuth2AuditSink

Receives audit events of authorization. Register your own to write them to your audit log. The default writes a log line.

| Member | What it does |
|---|---|
| `OnAuthorizationAsync(Auth2AuthorizationEvent, CancellationToken)` | Called for every authorization decision of an endpoint that has an audit action. |


### IAuth2GroupMetadata

Endpoint metadata: the user must be in one of these groups.

| Member | What it does |
|---|---|
| `Groups` | Group names (one is enough). |


### IAuth2PermissionMetadata

Endpoint metadata: the endpoint needs these permissions. Scanners read it to find the permissions of your application.

| Member | What it does |
|---|---|
| `Permissions` | Permission keys. |
| `RequireAll` | True when all are needed, false when one is enough. |


### IAuth2PermissionSourceMetadata

Endpoint metadata: where the permissions of this endpoint must come from.

| Member | What it does |
|---|---|
| `Source` | The source. |


### IAuth2RoleMetadata

Endpoint metadata: the endpoint needs these roles.

| Member | What it does |
|---|---|
| `Roles` | Role names. |
| `RequireAll` | True when all are needed, false when one is enough. |


### IAuth2ScopeMetadata

Endpoint metadata: the token needs these scopes.

| Member | What it does |
|---|---|
| `Scopes` | Scope names (one is enough). |


### IAuth2TokenKindMetadata

Endpoint metadata: only machine tokens (client credentials), or only user tokens.

| Member | What it does |
|---|---|
| `MachineOnly` | True for machine tokens only, false for user tokens only. |


## `SystemX.Auth2.Client.CurrentUser`


### Auth2Access

The access data of one user that the client keeps. The two source lists can be empty with an older server.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, int, IReadOnlyList<string>, IReadOnlyList<string>)` | The access data of one user that the client keeps. The two source lists can be empty with an older server. |
| `Empty` | No access. |
| `From(CurrentUserInfo)` | Makes it from the answer of `/me`. |


### DistributedCurrentUserStore

Access data in `IDistributedCache` (Redis, SQL Server, ...), so every instance of your application shares it.

| Member | What it does |
|---|---|
| `(constructor)(IDistributedCache)` | Access data in `IDistributedCache` (Redis, SQL Server, ...), so every instance of your application shares it. |
| `GetAsync(string, CancellationToken)` |  |
| `SetAsync(string, Auth2Access, TimeSpan, CancellationToken)` |  |
| `RemoveAsync(string, CancellationToken)` |  |


### IAuth2AccessProvider

Gets the access data of a principal: from the cache, else from the server. A cached entry is thrown away when its version is older than the `pv` claim of the token, or when its time is over. Within one request the answer is kept (one call at most).

| Member | What it does |
|---|---|
| `GetAccessAsync(ClaimsPrincipal, CancellationToken)` | The access data for the principal. Empty for a machine token or no user. |


### IAuth2AccessTokenAccessor

Finds the access token of the current request.

| Member | What it does |
|---|---|
| `GetAccessTokenAsync(CancellationToken)` | The bearer token of the request, or the token saved by the cookie sign-in, or null. |


### IAuth2CurrentUserService

Fetches the signed-in user from `/api/auth2/me`. It does **not** cache: call it after login and keep the result wherever you like.

| Member | What it does |
|---|---|
| `FetchAsync(ClaimsPrincipal, CancellationToken)` | Asks the server for the user of the current request's access token (the `Authorization` header, or the token saved by the cookie login). A machine token gives a `forbidden` failure: there is no user. |
| `FetchAsync(string, CancellationToken)` | Asks the server for the user of this access token. |


### ICurrentClient

The calling **client** of the current request, read from the token. Everything here is synchronous (no call to the server) and says nothing about a person: use `ICurrentUser` for that. Scopes belong here, because a scope is what the client may do; a permission belongs to the user.

| Member | What it does |
|---|---|
| `IsTokenAuthenticated` | True when the request carries a valid token (user token or machine token). |
| `ClientId` | OAuth client id (`client_id`) of the application that got the token. |
| `ApplicationId` | Id of the application (`application_id`). |
| `ProjectId` | Id of the project the application belongs to (`project_id`). |
| `ProjectKey` | Key of the project (`projectkey`). |
| `Scopes` | Scopes of the token. |
| `HasScope(string)` | True when the token has the scope (exact match). |
| `IsMachine` | True when the token belongs to a program (client credentials), not a person. |
| `SessionId` | Session id (`sid`) of a user token, or null. |
| `Idp` | The external provider the user signed in with (claim `idp`, for example `google` or `corp-saml`), or null for a local sign-in. The name is the one the project chose for the provider. |
| `Amr` | How the user proved who they are (claim `amr`): `pwd`, `otp`, `fed` (external provider), `swk`/`hwk` (passkey), `mfa` (a user-verified passkey, or an external provider that reports several factors; a local password plus an authenticator code gives `pwd` and `otp`). |


### ICurrentUser

The signed-in **person**. Name and email come from the token (no call). Roles, permissions and groups are **async** because they may need a call to the server (`/api/auth2/me`); the answer is cached. A machine token (client credentials) is not a person: `IsAuthenticated` is false, and the access lists are empty. Scopes, the client and the sign-in method are on `ICurrentClient`.

| Member | What it does |
|---|---|
| `IsAuthenticated` | True when the request has a valid token of a person. False for no token and for a machine token. |
| `UserId` | User id (`sub`), or null for a machine token or no user. |
| `UserName` | User name. |
| `Email` | Email. |
| `PhoneNumber` | Phone number. |
| `FirstName` | First name. |
| `LastName` | Last name. |
| `FullName` | Full name. |
| `GetRolesAsync(CancellationToken)` | Roles. Empty for a machine token. |
| `GetPermissionsAsync(CancellationToken)` | Permissions (effective). Empty for a machine token. |
| `GetGroupsAsync(CancellationToken)` | Groups. Empty for a machine token. |
| `HasPermissionAsync(string, CancellationToken)` | True when the user has the permission. |
| `HasRoleAsync(string, CancellationToken)` | True when the user has the role. |


### ICurrentUserStore

Keeps access data between requests. `MemoryCurrentUserStore` is the default; `DistributedCurrentUserStore` uses `IDistributedCache` (Redis).

| Member | What it does |
|---|---|
| `GetAsync(string, CancellationToken)` | Reads an entry, or null. |
| `SetAsync(string, Auth2Access, TimeSpan, CancellationToken)` | Saves an entry for `ttl`. |
| `RemoveAsync(string, CancellationToken)` | Removes an entry. |


### MemoryCurrentUserStore

Access data in the memory of this process.

| Member | What it does |
|---|---|
| `(constructor)(IMemoryCache)` | Access data in the memory of this process. |
| `GetAsync(string, CancellationToken)` |  |
| `SetAsync(string, Auth2Access, TimeSpan, CancellationToken)` |  |
| `RemoveAsync(string, CancellationToken)` |  |


## `SystemX.Auth2.Client.DependencyInjection`


### IAuth2ClientBuilder

Returned by `AddAuth2Client`. The feature methods (`EnablePermissions`, `UseApiAuthentication`, ...) extend it. Order does not matter.

| Member | What it does |
|---|---|
| `Services` | The service collection. |
| `Configuration` | The configuration section the client was set up from, or null. |


## `SystemX.Auth2.Client.Handshake`


### Auth2ClientState

What the client knows about the server. One instance for the whole application.

| Member | What it does |
|---|---|
| `Status` | The result of the last handshake, or null while none has finished. |
| `LastResponse` | The answer of the last successful handshake. |
| `Message` | A short explanation of the last failure (never holds secrets), or null. |
| `IsReady` | True when the last handshake worked. |
| `Set(HandshakeStatus, ClientHandshakeResponse, string)` | Records a result. Wakes everybody waiting for `WaitUntilReadyAsync` when it is `Ok`. |
| `WaitUntilReadyAsync(CancellationToken)` | Waits until a handshake worked. Sync services use it. Stops with an exception when `ct` is cancelled. |


### Auth2StartupException

Thrown at start-up when the handshake fails and the options say the application must not start.

| Member | What it does |
|---|---|
| `(constructor)(string, HandshakeStatus, Exception)` | Thrown at start-up when the handshake fails and the options say the application must not start. |
| `Status` | Why the start-up failed. |


### HandshakeHostedService

Makes the handshake when the application starts. What happens on failure is set by `OnServerUnavailable` and `OnProjectNotFound`. With `Continue` and an unreachable server it retries in the background (2 s, 4 s, ... up to 60 s, 6 tries by default). A wrong configuration (not found, rejected) is never retried.

| Member | What it does |
|---|---|
| `(constructor)(HandshakeRunner, Auth2ClientState, IOptions<Auth2ClientOptions>, TimeProvider, ILogger<HandshakeHostedService>)` | Makes the handshake when the application starts. What happens on failure is set by `OnServerUnavailable` and `OnProjectNotFound`. With `Continue` and an unreachable server it retries in the background (2 s, 4 s, ... up to 60 s, 6 tries by default). A wrong configuration (not found, rejected) is never retried. |
| `StartAsync(CancellationToken)` |  |
| `StopAsync(CancellationToken)` |  |


### HandshakeOutcome

Result of one handshake attempt.

| Member | What it does |
|---|---|
| `(constructor)(HandshakeStatus, ClientHandshakeResponse, string, string)` | Result of one handshake attempt. |
| `Status` | The outcome. |
| `Response` | The server answer (only for `Ok`). |
| `Code` | The stable error code, or null. |
| `Message` | What is wrong and what to do. Never holds secrets. |


### HandshakeRunner

Makes one handshake attempt and turns every possible answer into a `HandshakeStatus`.

| Member | What it does |
|---|---|
| `(constructor)(IHttpClientFactory, IAuth2MachineTokenProvider, IOptions<Auth2ClientOptions>)` | Makes one handshake attempt and turns every possible answer into a `HandshakeStatus`. |
| `SingleServerMessage` | Warning when the client has a project key but the server has a single project. The key is ignored. |
| `MultiServerMessage` | Message when the client has no project key but the server serves many projects. |
| `RunAsync(CancellationToken)` | Makes one attempt. Never throws (except when `ct` is cancelled). |


## `SystemX.Auth2.Client.HealthChecks`


### Auth2ClientHealthCheck

`Healthy` when the last handshake worked, `Degraded` otherwise (the application still runs).

| Member | What it does |
|---|---|
| `(constructor)(Auth2ClientState)` | `Healthy` when the last handshake worked, `Degraded` otherwise (the application still runs). |
| `CheckHealthAsync(HealthCheckContext, CancellationToken)` |  |


## `SystemX.Auth2.Client.Machine`


### IAuth2MachineService

Helpers for programs that call other APIs with a machine token (background services).

| Member | What it does |
|---|---|
| `CreateClient(string)` | An `HttpClient` from `IHttpClientFactory` by name. Register it with `services.AddHttpClient(name).AddAuth2MachineToken("scope")`, then every request carries a machine token. |
| `GetTokenAsync(IEnumerable<string>, CancellationToken)` | A token for the scopes (cached). |


### IAuth2MachineTokenProvider

Gets client-credentials tokens ("machine tokens") for this application. Tokens are cached until shortly before they expire.

| Member | What it does |
|---|---|
| `GetTokenAsync(IEnumerable<string>, CancellationToken)` | A valid access token for the scopes. Cached until `expires_in - 60 seconds` (at least 30 seconds), one request at a time per scope set. |
| `Invalidate()` | Forgets cached tokens. The next call asks the server again (used after a 401). |


### MachineTokenHandler

Adds a machine token to every request of an HTTP client. On a 401 the cached token is dropped, so the next call gets a new one.

| Member | What it does |
|---|---|
| `(constructor)(IAuth2MachineTokenProvider, IEnumerable<string>)` | Adds a machine token to every request of an HTTP client. On a 401 the cached token is dropped, so the next call gets a new one. |
| `SendAsync(HttpRequestMessage, CancellationToken)` |  |


## `SystemX.Auth2.Client.Options`


### Auth2ApiOptions

Options of API authentication (`UseApiAuthentication`): checking the JWT access tokens of the Auth2 Server.

| Member | What it does |
|---|---|
| `Authority` | The address of the server, used to find the signing keys. Default: `ServerUrl`. It must be the issuer the server writes in its tokens. |
| `Audience` | The audience a token must have. Tokens of the server carry the client id of the application. Default: `ClientId`. |
| `RequireHttpsMetadata` | Insist on https for the key address. Default: true, except when the server address is http (development). |
| `ClockSkew` | Allowed difference between clocks. Default 30 seconds. |
| `KeyRefreshInterval` | The shortest time between two reads of the server's keys. When a token with an unknown key id arrives (after a key rotation) the keys are read again, but not more often than this. Default 30 seconds. |
| `BackchannelHandler` | Handler for the calls that get the keys (tests, proxies). Default: none. |


### Auth2CatalogException

Something is wrong with a catalog source. The message names the source, class, field or part that is wrong. The application does not start.

| Member | What it does |
|---|---|
| `(constructor)(string, Exception)` | Something is wrong with a catalog source. The message names the source, class, field or part that is wrong. The application does not start. |


### Auth2CategoryAttribute

Overrides the category of the constants of a class (the default is the class name). Used for the group label of permissions.

| Member | What it does |
|---|---|
| `(constructor)(string)` | Overrides the category of the constants of a class (the default is the class name). Used for the group label of permissions. |
| `Category` | The category. |


### Auth2ClientOptions

Main options of the client. Section name `Auth2` when you use `AddAuth2Client(IConfigurationSection)`. `ProjectKey` is set with `ServerWithMultiProject(projectKey)` and only when the server serves many projects. It is an identifier check, not a secret.

| Member | What it does |
|---|---|
| `ServerUrl` | Address of the SystemX.Auth2 Server, for example `https://auth.example.com`. |
| `ApplicationName` | Name of this application (the client) inside its project. The handshake checks it against the name on the server. |
| `ClientId` | OAuth client id of this application. |
| `ClientSecret` | OAuth client secret. Keep it in a secret store. It is never logged. |
| `ProjectKey` | Project key of this application on a multi-project server. Null for a single-project server (a key sent there is ignored with a warning). Set it with `ServerWithMultiProject(projectKey)`. It is not a secret and not proof of anything: the server finds the project from the client id. |
| `OnServerUnavailable` | What to do when the server cannot be reached at start-up. Default `Continue`. |
| `OnProjectNotFound` | What to do when the application is not found or the server rejects the client. Default `Throw`. |
| `HandshakeOnStartup` | Make the handshake when the application starts. Default true. |
| `HandshakeTimeout` | Time limit of one handshake attempt. Default 5 seconds. |
| `HandshakeMaxRetries` | How many background retries after an unreachable server (only with `Continue`). Default 6. |
| `HandshakeInitialDelay` | Wait before the first retry. It doubles each time. Default 2 seconds. |
| `HandshakeMaxDelay` | Longest wait between two retries. Default 60 seconds. |
| `ClientVersion` | Version text sent in the handshake. Default: the version of this package. |
| `RequestTimeout` | Time limit of one call to the server (not the handshake). Default 30 seconds. |
| `CatalogRefreshInterval` | How often permissions, roles and groups are read again from the server when their policy is `Pull` or `Both`. Default 10 minutes. |
| `SyncMaxRetries` | How many times a failed sync is tried again (waits grow like the handshake retries). Default 5. |
| `FailOnSyncError` | When true, a sync that still fails after the retries stops the application. Default false: log it and carry on. |


### Auth2ClientOptionsValidator

Checks `Auth2ClientOptions` when the application starts and says clearly what is wrong.

| Member | What it does |
|---|---|
| `IsValidProjectKey(string)` | True when the project key follows the server rule: lowercase letters, digits, `-`, `_`, 3–64 characters, not `_global`. |
| `Validate(string, Auth2ClientOptions)` |  |


### Auth2CookieOptions

Options of the sign-in cookie (`UseCookieAuthentication`, `UseHostedUi`, `UseSelfUi`).

| Member | What it does |
|---|---|
| `CookieName` | Cookie name. Default `.SystemX.Auth2.Client`. |
| `ExpireTimeSpan` | How long the cookie lasts. **Null (default) uses the value of the server** (`Sessions.CookieLifetimeMinutes`, read at the handshake), and 8 hours when the server has none. |
| `SlidingExpiration` | Renew the cookie while the user is active. Default true. |
| `SameSite` | SameSite mode. Default `Lax` (it must be Lax or None for the sign-in redirect to work). |
| `SecurePolicy` | When the cookie is sent only over https. Default `Always`. Use `SameAsRequest` only in development. |
| `LoginPath` | Where browsers go to sign in. Default `/auth2/login` (provided by the package for the hosted UI). With the Self UI, set your own page. |
| `AccessDeniedPath` | Where signed-in users go when they may not do something. Default `/auth2/denied`. |
| `SaveTokens` | Keep the access, refresh and identity tokens in the cookie. Default true (needed to call the server for the user's access data later). |
| `AccessMode` | When the user's roles, permissions and groups are loaded. `Lazy` (default): when first needed. `OnSignIn`: at sign-in, into the store. `RolesInCookie`: roles go into the cookie as claims (the cookie has a 4 KB limit: keep it for small data). `None`: never. |


### Auth2CurrentUserOptions

Options of the current-user feature.

| Member | What it does |
|---|---|
| `Source` | Where roles and permissions come from. Default `TokenAndServer`. |
| `CacheTtl` | How long access data is kept before it is read again. Default 10 minutes. |


### Auth2GroupOptions

Options of group handling. Pushing groups needs `Clients.GroupSyncAllowed` on the server (off by default); it only adds missing groups.

| Member | What it does |
|---|---|
| `SyncPolicy` | How groups move between this application and the server. Default `None`. |
| `ScanningPolicy` | Where groups come from when they are pushed. Default `Manual`. |
| `GroupList` | Groups you list by hand. A shortcut: the same as `Add(...)` for each item. |
| `AssembliesToScan` | Assemblies to scan for `[Auth2Group]` attributes. |
| `Kind` |  |
| `KeyOf(GroupDefinition)` |  |
| `CheckKey(string)` |  |
| `SameData(GroupDefinition, GroupDefinition)` |  |
| `FromKey(string, string)` |  |
| `FromMember(string, string, MemberInfo, string)` |  |
| `FromConfiguration(IConfigurationSection)` |  |


### Auth2HostedUiOptions

Options of the hosted UI (`UseHostedUi`): sign in on the Auth2 Server with the authorization code flow and PKCE.

| Member | What it does |
|---|---|
| `Scopes` | Scopes to ask for. Default: `openid profile email offline_access auth2.me`. |
| `CallbackPath` | Where the server sends the browser after sign-in. Register it as a redirect URI of the application on the server. Default `/signin-oidc`. |
| `SignedOutCallbackPath` | Where the server sends the browser after sign-out. Default `/signout-callback-oidc`. |
| `RequireHttpsMetadata` | Insist on https for the discovery address. Default: true, except when the server address is http. |
| `BackchannelHandler` | Handler for the calls to the server (tests, proxies). Default: none. |


### Auth2KeyAttribute

Overrides the key of an enum member (`[Auth2Key("orders.read")]`). Without it the key comes from the member name and the `KeyStyle`.

| Member | What it does |
|---|---|
| `(constructor)(string)` | Overrides the key of an enum member (`[Auth2Key("orders.read")]`). Without it the key comes from the member name and the `KeyStyle`. |
| `Key` | The key. |


### Auth2MachineOptions

Options of the machine-to-machine (client credentials) feature.

| Member | What it does |
|---|---|
| `Scopes` | Scopes a program asks for when it does not name any. Default: none. |


### Auth2PermissionOptions

Options of permission handling.

| Member | What it does |
|---|---|
| `SyncPolicy` | How permissions move between this application and the server. Default `None`. |
| `ScanningPolicy` | Where permissions come from. Default `Manual`. |
| `PermissionList` | Permissions you list by hand. A shortcut: the same as `Add(...)` for each item. See also `AddFromClass`, `AddFromEnum`, `AddFromString`, `AddProvider` and the other sources. |
| `AssembliesToScan` | Assemblies to scan for attributes (used with `AssemblyScan` or `Both`). |
| `Kind` |  |
| `KeyOf(PermissionDefinition)` |  |
| `CheckKey(string)` |  |
| `SameData(PermissionDefinition, PermissionDefinition)` |  |
| `FromKey(string, string)` |  |
| `FromMember(string, string, MemberInfo, string)` |  |
| `FromConfiguration(IConfigurationSection)` |  |


### Auth2RoleOptions

Options of role handling.

| Member | What it does |
|---|---|
| `SyncPolicy` | How roles move between this application and the server. Default `None`. |
| `ScanningPolicy` | Where roles come from. Default `Manual`. |
| `RoleList` | Roles you list by hand. A shortcut: the same as `Add(...)` for each item. |
| `AssembliesToScan` | Assemblies to scan for attributes. |
| `Kind` |  |
| `KeyOf(RoleDefinition)` |  |
| `CheckKey(string)` |  |
| `SameData(RoleDefinition, RoleDefinition)` |  |
| `FromKey(string, string)` |  |
| `FromMember(string, string, MemberInfo, string)` |  |
| `FromConfiguration(IConfigurationSection)` |  |


### Auth2ScopeOptions

Options of custom scopes (for example `reports.read`). Names starting with `auth2.` and the standard OpenID Connect scopes are never sent.

| Member | What it does |
|---|---|
| `SyncPolicy` | How scopes move between this application and the server. Default `None`. |
| `ScanningPolicy` | Where scopes come from. Default `Manual`. |
| `ScopeList` | Scopes you list by hand. A shortcut: the same as `Add(...)` for each item. |
| `AssembliesToScan` | Assemblies to scan for `[Auth2Scope]` attributes. |
| `Kind` |  |
| `KeyOf(ScopeDefinition)` |  |
| `CheckKey(string)` |  |
| `SameData(ScopeDefinition, ScopeDefinition)` |  |
| `FromKey(string, string)` |  |
| `FromMember(string, string, MemberInfo, string)` |  |
| `FromConfiguration(IConfigurationSection)` |  |


### CatalogOptions<T>

The sources of one catalog (permissions, roles, scopes or groups). All four catalogs share these methods and these rules: definitions from all sources are merged by key (ordinal, case-sensitive); the same key with the same data is ignored; the same key with different data stops the start unless `DuplicatePolicy` is `FirstWins`. Keys are checked when a source is added (static sources) or when the host starts (providers and factories). A wrong key names its source. Sync never deletes anything on the server: a key that is not listed any more is marked obsolete there.

| Member | What it does |
|---|---|
| `DuplicatePolicy` | What happens when two sources give the same key with different data. Default `Fail`. |
| `Kind` | "permission", "role", "scope" or "group" (used in messages). |
| `KeyOf(T)` | The key of a definition. |
| `FromKey(string, string)` | Makes a definition from a key only (name = key, description empty). |
| `FromMember(string, string, MemberInfo, string)` | Makes a definition from a key and the attribute of a field or an enum member (display name, description, ...). |
| `FromConfiguration(IConfigurationSection)` | Makes a definition from a configuration section (a plain value is a key, a section has the fields). |
| `CheckKey(string)` | Returns a message when the key is not valid for this catalog, else null. |
| `SameData(T, T)` | True when two definitions with the same key carry the same data. |
| `Add(T)` | Adds one full definition. |
| `AddFromClass(Type)` | Adds the `public const string` and `public static readonly string` fields of a class, nested classes included. The **value** is the key. Display name and description come from `[Auth2Permission]`, `[Auth2Role]`, `[Auth2Scope]` or `[Auth2Group]` on the field; without it the display name is made from the field name. The category is the class name (`Auth2CategoryAttribute` on the class overrides it). You pass the type: nothing scans the whole assembly (trimming friendly). |
| `AddFromEnum(KeyStyle)` | Adds the members of an enum. The key is `[Auth2Key]` when present, else the member name in the given `KeyStyle`. A `[Flags]` enum or two members with the same key stop the start. |
| `AddFromEnum(Type, KeyStyle)` | Adds the members of an enum (see the generic overload). |
| `AddFromString(string, Char, string)` | Adds keys from a text such as `"orders.read, orders.write"`: split by the separator, trimmed, empty parts dropped, duplicates merged. Key only: the name is the key and the description is empty. A part that is not a valid key stops the start and the message names it. |
| `AddFromListString(IEnumerable<string>, string)` | Adds keys from a list (the same rules as `AddFromString`). A key that contains the separator `,` is an error: it is most likely a joined text in one element. |
| `AddProvider()` | Adds a provider that is resolved from DI when the host starts (just before the first sync). |
| `AddFactory(Func<IServiceProvider, IEnumerable<T>>)` | Adds a factory that is called with the service provider when the host starts. |
| `AddFactory(Func<IServiceProvider, CancellationToken, Task<IEnumerable<T>>>)` | Adds an async factory that is called with the service provider when the host starts. |
| `AddFromConfiguration(IConfigurationSection)` | Adds definitions from a configuration section. Each child is a plain value (the key) or a section with the fields (`Key`/`Name`, `DisplayName`, `Description`, `Permissions` for roles, `Group` for permissions). |
| `ResolveAsync(IServiceProvider, CancellationToken)` | Merges the static sources (already checked) with the providers and factories. Called when the host starts. A provider that throws gives an `Auth2CatalogException` with its name; two sources with different data for one key do too (unless `FirstWins`). |
| `Merge()` | The merged definitions of the static sources and of the providers resolved so far. |
| `HasLateSources` | True when providers or factories were added. |
| `Humanize(string)` | "OrdersRead" gives "Orders read". |


### DuplicatePolicy

What happens when two sources give the same key with different data.

| Member | What it does |
|---|---|
| `Fail` | The application does not start. The message names both sources. The default. |
| `FirstWins` | The first source wins. Later definitions of the same key are ignored. |


### ICatalogProvider<T>

A source of catalog definitions that needs services (a database, a file, another service). Async on purpose.

| Member | What it does |
|---|---|
| `GetAsync(CancellationToken)` | Returns the definitions. |


### IGroupProvider

Gives groups, resolved from DI when the host starts.


### IPermissionProvider

Gives permissions, resolved from DI when the host starts (just before the first sync). Register it with `AddProvider<T>()`.


### IRoleProvider

Gives roles, resolved from DI when the host starts.


### IScopeProvider

Gives custom scopes, resolved from DI when the host starts.


### KeyStyle

How an enum member name becomes a key.

| Member | What it does |
|---|---|
| `DotCase` | `OrdersRead` becomes `orders.read`. The default. |
| `AsIs` | The member name as it is: `OrdersRead`. |
| `SnakeCase` | `OrdersRead` becomes `orders_read`. |
| `KebabCase` | `OrdersRead` becomes `orders-read`. |


## `SystemX.Auth2.Client.Services`


### IAuth2ApiClientService

Calls the Auth2 Server. Every method returns `Result` or `Result` and never throws for server errors (a wrong password is a normal answer). The error code is in `Result.Error.Code`; a server that cannot be reached gives `ServerUnavailable`. Calls that change something (register, login, password change) are never repeated automatically.

| Member | What it does |
|---|---|
| `RegisterAsync(RegisterRequest, CancellationToken)` | Registers a user (or adds an existing person; see `RegisterResponse`). Needs the client scope `auth2.register`. |
| `LoginAsync(LoginRequest, CancellationToken)` | Signs a user in with the password grant (the server must allow `AllowSelfUiLogin`). When two-factor is needed the result succeeds with `TwoFactorRequired = true` and a `TwoFactorToken`: call `TwoFactorLoginAsync` next. |
| `TwoFactorLoginAsync(TwoFactorRequest, CancellationToken)` | Second step of a login: the authenticator code or a recovery code. |
| `OtpLoginAsync(string, string, CancellationToken)` | Signs a user in with a one-time code (ask for it with `RequestOtpAsync` first). |
| `RequestOtpAsync(OtpRequest, CancellationToken)` | Sends a one-time code by email or SMS. The answer is always the same, so nobody can find out who has an account. |
| `ForgotPasswordAsync(ForgotPasswordRequest, CancellationToken)` | Starts a password reset. The answer is always the same. |
| `ResetPasswordAsync(ResetPasswordRequest, CancellationToken)` | Finishes a password reset with the token from the email. |
| `ChangePasswordAsync(string, ChangePasswordRequest, CancellationToken)` | Changes the password of the user who owns `accessToken`. |
| `RefreshAsync(string, CancellationToken)` | Gets new tokens with a refresh token. The refresh token can be used once; keep the new one. |
| `LogoutAsync(string, CancellationToken)` | Revokes a refresh token (sign-out on the server). |
| `GetMeAsync(string, CancellationToken)` | The signed-in user and their roles, permissions and groups (`GET /api/auth2/me`). |
| `HandshakeAsync(CancellationToken)` | Makes a handshake now and updates `Auth2ClientState`. Use it to try again after a start-up failure. |
| `SyncPermissionsAsync(PermissionSyncRequest, CancellationToken)` | Sends permissions to the server (adds and updates, never deletes). |
| `SyncRolesAsync(RoleSyncRequest, CancellationToken)` | Sends roles to the server (adds and updates, never deletes). |
| `SyncGroupsAsync(GroupSyncRequest, CancellationToken)` | Sends groups to the server. They are only added when missing. Needs `Clients.GroupSyncAllowed` on the server. |
| `SyncScopesAsync(ScopeSyncRequest, CancellationToken)` | Sends custom scopes to the server: they are registered and allowed for this application. |
| `GetScopesAsync(CancellationToken)` | Reads the custom scopes this application allows. |
| `GetExternalProvidersAsync(CancellationToken)` | Reads the external sign-in providers that are on for this application (name, button text, kind, whether a user name and password form is needed). Use it with `UseSelfUi` to show the choices; then send the user to the hosted login of the Server. Secrets are never part of the list. |
| `GetPermissionsAsync(CancellationToken)` | Reads the permissions of this application. |
| `GetRolesAsync(CancellationToken)` | Reads the roles of this application with their permission keys. |
| `GetGroupsAsync(CancellationToken)` | Reads the groups of this application with their roles and permission keys. |


## `SystemX.Auth2.Client.Sync`


### Auth2Keys

The same key rules as the server: lowercase, 1–100 characters of `a-z 0-9 . _ : -`, starting and ending with a letter or digit.

| Member | What it does |
|---|---|
| `MaxLength` | Longest key. |
| `NormalizePermissionKey(string)` | Trims and lower-cases. Returns null when the key is not valid. |
| `IsReservedScope(string)` | True for scopes that belong to the server (`auth2.*` and the standard OpenID Connect scopes). They are never sent by sync. |
| `NormalizeScopeName(string)` | Scope names follow the rules of permission keys. Returns null when the name is invalid. |
| `NormalizeGroupName(string)` | Group names follow the rules of permission keys (lowercase). Returns null when the name is invalid. |
| `NormalizeRoleName(string)` | Role names: 1–100 letters, digits, space or `. _ : -`. Returns the trimmed name, or null. |


### Auth2SyncHostedService

Runs the sync **after the application started** (so every endpoint is mapped and can be scanned) and after a successful handshake. Push: sends scopes, permissions, roles, then groups. Pull: reads scopes, permissions, roles and groups now and every `CatalogRefreshInterval`. A failure is logged and the application carries on, unless `FailOnSyncError` is true.

| Member | What it does |
|---|---|
| `(constructor)(IHostApplicationLifetime, Auth2ClientState, IPermissionScanner, IRoleScanner, IScopeScanner, IGroupScanner, IAuth2ApiClientService, IOptions<Auth2ClientOptions>, IOptions<Auth2PermissionOptions>, IOptions<Auth2RoleOptions>, IOptions<Auth2GroupOptions>, IOptions<Auth2ScopeOptions>, IAuth2Catalog, TimeProvider, ILoggerFactory)` | Runs the sync **after the application started** (so every endpoint is mapped and can be scanned) and after a successful handshake. Push: sends scopes, permissions, roles, then groups. Pull: reads scopes, permissions, roles and groups now and every `CatalogRefreshInterval`. A failure is logged and the application carries on, unless `FailOnSyncError` is true. |
| `ExecuteAsync(CancellationToken)` |  |


### CatalogSyncService<T>

Syncs one kind of catalog (permissions or roles) with the server. It sends the list only when it changed since the last success (a hash of the sorted list), and tries again with a growing wait when the server fails.

| Member | What it does |
|---|---|
| `(constructor)(string, Func<IReadOnlyList<T>>, Func<IReadOnlyList<T>, CancellationToken, Task<Result<SyncResponse>>>, IOptions<Auth2ClientOptions>, TimeProvider, ILogger)` | Syncs one kind of catalog (permissions or roles) with the server. It sends the list only when it changed since the last success (a hash of the sorted list), and tries again with a growing wait when the server fails. |
| `PushAsync(CancellationToken)` | Sends the list when it changed. Returns true when the server has the current list (sent now, or unchanged since the last success). |


### IAuth2Catalog

What the client last read from the server (policies `Pull` and `Both`). Handy for screens that list permissions, roles and groups.

| Member | What it does |
|---|---|
| `Permissions` | Permissions of the application, or empty before the first read. |
| `Roles` | Roles with their permission keys. |
| `Groups` | Groups with their roles and permission keys. |
| `Scopes` | Custom scopes this application allows. |
| `LastReadUtc` | When the last successful read finished, or null. |


### IGroupScanner

Finds the groups of this application (for `GroupSyncPolicy.Push` and `Both`).

| Member | What it does |
|---|---|
| `Scan()` | The groups, sorted by name. Manual entries win over scanned ones. |


### IPermissionScanner

Finds the permissions of this application: your list, and/or what your endpoints and attributes ask for.

| Member | What it does |
|---|---|
| `Scan()` | The permissions, sorted by key. Manual entries win over scanned ones (they have names and descriptions). |


### IRoleScanner

Finds the roles of this application.

| Member | What it does |
|---|---|
| `Scan()` | The roles, sorted by name. Manual entries win over scanned ones. |


### IScopeScanner

Finds the custom scopes of this application (for example `reports.read`). Reserved scopes are skipped.

| Member | What it does |
|---|---|
| `Scan()` | The scopes, sorted by name. Manual entries win over scanned ones. |
<!-- generated:end -->
