# Registering the client: `Auth2ClientServiceCollectionExtensions`

## What this is

`Auth2ClientServiceCollectionExtensions` (file `src/SystemX.Auth2.Client/DependencyInjection/Auth2ClientServiceCollectionExtensions.cs`, namespace `Microsoft.Extensions.DependencyInjection`) is the **front door of the Client package**. Everything starts with one of its two `AddAuth2Client` methods. They register the client, return an `IAuth2ClientBuilder`, and every other method of the class (and of the UI and API authentication extensions) is chained onto that builder.

The file contains two classes:

| Class | Extends | Methods |
|---|---|---|
| `Auth2ClientServiceCollectionExtensions` | `IServiceCollection`, `IAuth2ClientBuilder`, `IHttpClientBuilder` | `AddAuth2Client` (2 overloads), `ServerWithMultiProject`, `EnablePermissions`, `EnableRoles`, `EnableScopes`, `EnableGroups`, `EnableMachineToMachine`, `EnableCurrentUser`, `UseDistributedCurrentUserStore`, `EnableResilience`, `EnableHealthCheck`, `AddAuth2MachineToken` |
| `Auth2ClientApplicationBuilderExtensions` | `IApplicationBuilder` | `UseAuth2` |

The sign-in methods (`UseApiAuthentication`, `UseCookieAuthentication`, `UseHostedUi`, `UseSelfUi`, `UseBlazor`) live in other classes and are described in [client-setup.md](client-setup.md) and [client-authorization.md](client-authorization.md). The signatures of everything are in [client-reference.md](client-reference.md).

## When to use it

Always. There is no other way to add the client. You call `AddAuth2Client` once, then chain only what your application needs. A minimal API that only protects endpoints needs `AddAuth2Client` + `ServerWithMultiProject` + `UseApiAuthentication`; a browser app adds `UseHostedUi` + `UseCookieAuthentication`; a background worker needs only `AddAuth2Client` + `EnableMachineToMachine`.

## How it works

```csharp
builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))   // 1. register and validate
    .ServerWithMultiProject("sample-project")                     // 2. chain what you need
    .UseApiAuthentication()
    .EnableCurrentUser();

var app = builder.Build();
app.UseAuth2();                                                   // 3. add it to the pipeline
```

- **The methods can be called in any order.** Each one only records settings or adds services; nothing runs until the host starts.
- **Nothing is checked when you call a method; everything is checked when the host starts.** The options are validated with `ValidateOnStart`, so a wrong `ServerUrl` or a missing `ClientSecret` stops the application at start with a message that names the option, not at the first request.
- **Two background services start with the host:** the **handshake** (calls the Server, checks that the client and project are right, [handshake-and-startup.md](handshake-and-startup.md)) and the **sync** (pushes and reads permissions, roles, scopes and groups after the application has started, [permission-sync.md](permission-sync.md)). The sync does nothing while every policy is `None`.

## `AddAuth2Client`

Two overloads. Both register the same services; they differ only in where the options come from.

```csharp
IAuth2ClientBuilder AddAuth2Client(this IServiceCollection services, string serverUrl, Action<Auth2ClientOptions>? configure = null)
IAuth2ClientBuilder AddAuth2Client(this IServiceCollection services, IConfigurationSection section)
```

### Overload 1: address and code

`serverUrl` becomes `Auth2ClientOptions.ServerUrl`; the optional `configure` sets the rest.

```csharp
builder.Services.AddAuth2Client("https://auth.example.com", o =>
{
    o.ApplicationName = "papertrade-api";
    o.ClientId = "papertrade-api";
    o.ClientSecret = builder.Configuration["Auth2:Secret"]!;   // from a secret store
    o.OnServerUnavailable = StartupFailureBehavior.Continue;
    o.OnProjectNotFound = StartupFailureBehavior.Throw;
});
```

Use it when the address is fixed in code, in a test, or when you build the options from several sources.

### Overload 2: a configuration section

The section is bound to `Auth2ClientOptions` (keys have the names of the properties, enums are written as names, times as `hh:mm:ss`). This is what both samples use.

```json
"Auth2": {
  "ServerUrl": "http://localhost:5100",
  "ApplicationName": "MvcSample",
  "ClientId": "mvc-sample",
  "ClientSecret": "mvc-sample-secret",
  "ProjectKey": "sample-project",
  "OnServerUnavailable": "Continue",
  "OnProjectNotFound": "Continue",
  "HandshakeTimeout": "00:00:10"
}
```

```csharp
builder.Services.AddAuth2Client(builder.Configuration.GetSection("Auth2"));
```

Use it for everything that is not a test: the same code runs in every environment and only the configuration changes. Keep the secret out of the file: environment variable `Auth2__ClientSecret`, user secrets, a vault. The section also fills `ProjectKey`, so with it you may skip `ServerWithMultiProject` (the samples call it anyway so that the key is visible in code).

You can mix: call overload 2, then `builder.Services.Configure<Auth2ClientOptions>(o => ...)` for one value that must come from code.

### What the registration adds

| Added | Lifetime | Why |
|---|---|---|
| `Auth2ClientOptions` + a validator, `ValidateOnStart` | options | Wrong options stop the start (rules below) |
| Options for machine, permission, role, group, scope and current user | options | So the `Enable...` methods have somewhere to write |
| Two named `HttpClient`s (`Auth2ClientDefaults.HttpClientName`, `ResilientHttpClientName`), base address = `ServerUrl` | factory | The first is for calls that must **never** be repeated by themselves (register, login, password); the second is for calls that are safe to repeat (token, handshake, sync, reads) and gets the retry handler when you call `EnableResilience`. Their `Timeout` is infinite on purpose: the client applies its own time limits (`RequestTimeout`, `HandshakeTimeout`) |
| `IHttpContextAccessor`, `IMemoryCache`, `AddAuthorization()` | singleton | The current user and the policies need them |
| `Auth2PolicyProvider`, `Auth2AuthorizationResultHandler` (**replace** the defaults), `Auth2AuthorizationHandler` | singleton / scoped | They understand the policy names that `[Auth2Permission]`, `.WithRole(...)` and the others make (`auth2:perm:any:orders.read`) and answer 401 / 403 correctly. Other policy names still go to the normal ASP.NET Core provider |
| `ICurrentUser`, `ICurrentClient`, `IAuth2AccessProvider` | scoped | One instance per request; a request asks the Server at most once per user |
| `IAuth2AccessTokenAccessor`, `IAuth2CurrentUserService`, `ICurrentUserStore` (memory) | singleton | Finds the token of the request, reads `/me`, keeps the answers |
| `IAuth2AuditSink` (logging) | singleton | Receives `.WithAuth2Audit(...)` events |
| The four scanners and `IAuth2Catalog` | singleton | Find your permissions, roles, scopes and groups; hold what a pull read |
| `Auth2CatalogStartupService`, `Auth2SyncHostedService` | hosted | Check all catalog sources when the host starts; push and pull |
| `Auth2ClientState`, `HandshakeRunner`, `HandshakeHostedService` | singleton / hosted | The handshake and its result |
| `IAuth2MachineTokenProvider`, `IAuth2MachineService` | singleton | Client-credentials tokens |
| `IAuth2ApiClientService` | singleton | Every call to the Server ([client-reference.md](client-reference.md)) |

All of these use `TryAdd...`: **if you registered your own implementation of the same service before `AddAuth2Client`, yours stays.** That is how you replace `IAuth2AuditSink`, `ICurrentUserStore` or `IAuth2AccessTokenAccessor`. Only the policy provider and the result handler are replaced on purpose.

### The options (`Auth2ClientOptions`)

| Option | Default | Meaning, and when to change it |
|---|---|---|
| `ServerUrl` | none, **required** | Address of the Server, `http` or `https`, absolute. A trailing `/` is fine |
| `ApplicationName` | none, **required** | The name of this application **inside its project** on the Server. The handshake compares it with the Server's name and refuses a different one (`Rejected`): this catches a wrong client id or a copy-pasted configuration |
| `ClientId` | none, **required** | The OAuth client id of the application |
| `ClientSecret` | none, **required** | The secret. Never logged. Keep it in a secret store; rotate it with the admin API or the command ([server-setup.md](server-setup.md)) |
| `ProjectKey` | `null` | The project of this application on a **multi-project** server. Set it with `ServerWithMultiProject`. Rules: lowercase letters, digits, `-`, `_`; 3 to 64 characters; not `_global`. It is an identifier check, not a secret: the Server finds the project from the client id and checks that your key matches |
| `OnServerUnavailable` | `Continue` | When the Server cannot be reached at start: `Continue` (start, retry in the background) or `Throw` (stop the application). Use `Throw` when the application is useless without the Server |
| `OnProjectNotFound` | `Throw` | When the Server answers `NotFound` or `Rejected` (wrong secret, name, key or server mode). It is a configuration error and is **never retried**. `Throw` is right for production; `Continue` lets the application start and log the problem (both samples use it) |
| `HandshakeOnStartup` | `true` | Make the handshake when the host starts. Set `false` when you want to choose the moment: then call `IAuth2ApiClientService.HandshakeAsync()` yourself; the sync waits for a good handshake |
| `HandshakeTimeout` | 5 s | Time limit of **one** handshake attempt |
| `HandshakeMaxRetries` | 6 | Background retries after an unreachable Server (only with `OnServerUnavailable = Continue`) |
| `HandshakeInitialDelay` | 2 s | Wait before the first retry. It doubles each time (2, 4, 8, 16, 32 s ...) |
| `HandshakeMaxDelay` | 60 s | The longest wait between two retries |
| `ClientVersion` | the package version | Text sent in the handshake |
| `RequestTimeout` | 30 s | Time limit of one call to the Server that is not the handshake |
| `CatalogRefreshInterval` | 10 min | How often permissions, roles, scopes and groups are read again when a policy is `Pull` or `Both` |
| `SyncMaxRetries` | 5 | How many times a failed sync is tried again (waits grow like the handshake retries) |
| `FailOnSyncError` | `false` | When `true`, a sync that still fails after the retries **stops the application** with `Auth2StartupException`. Use it when the Server must always have your list (for example in a deployment check) |

**What the start-up validation refuses** (all of them stop the host with a message that starts with `Auth2:`): `ServerUrl` empty or not an absolute `http(s)` address; empty `ApplicationName`, `ClientId` or `ClientSecret`; a bad `ProjectKey`; `HandshakeTimeout` or `RequestTimeout` not longer than zero; negative `HandshakeMaxRetries`; `HandshakeInitialDelay` not longer than zero; `HandshakeMaxDelay` shorter than `HandshakeInitialDelay`.

**The handshake result** decides what happens:

| Status | Meaning | What the client does |
|---|---|---|
| `Ok` | The Server knows the client, project and mode | Logs it (a warning if you sent a `ProjectKey` to a single-project server: it is ignored). The sync may run |
| `ServerUnavailable` | No answer, a 5xx, or too slow | `OnServerUnavailable`: `Throw`, or `Continue` and retry in the background |
| `NotFound` | The application does not exist (404) | `OnProjectNotFound`; never retried |
| `Rejected` | Wrong secret (401/403), wrong name or key, or wrong server mode (409) | `OnProjectNotFound`; never retried |

`Auth2ClientState` (injectable) shows the last result (`Status`, `Message`); `EnableHealthCheck` turns it into a health check.

## `ServerWithMultiProject`

```csharp
IAuth2ClientBuilder ServerWithMultiProject(this IAuth2ClientBuilder builder, string projectKey)
```

Sets `Auth2ClientOptions.ProjectKey`. **Call it when the Server is multi-project** (the default), and leave it out when the Server was started with `ServerWithSingleProject()`. The handshake compares: a key sent to a single-project server is ignored with a warning; no key sent to a multi-project server fails with a clear message. See [concepts.md](concepts.md#single-or-multi-project).

```csharp
builder.Services.AddAuth2Client(section).ServerWithMultiProject("papertrade");
```

## `EnablePermissions`, `EnableRoles`, `EnableScopes`, `EnableGroups`

```csharp
IAuth2ClientBuilder EnablePermissions(this IAuth2ClientBuilder builder, Action<Auth2PermissionOptions>? configure = null)
// the same shape for EnableRoles (Auth2RoleOptions), EnableScopes (Auth2ScopeOptions) and EnableGroups (Auth2GroupOptions)
```

These four **only set options**. The scanners and the sync are always registered; the options decide whether they do anything. Each options class has the same members:

| Option | Default | Meaning |
|---|---|---|
| `SyncPolicy` | `None` | `None` (nothing moves), `Push` (send your list to the Server at start), `Pull` (read the Server's list now and every `CatalogRefreshInterval` into `IAuth2Catalog`), `Both` (push, then pull). The enum is `PermissionSyncPolicy`, `RoleSyncPolicy`, `ScopeSyncPolicy` or `GroupSyncPolicy` |
| `ScanningPolicy` | `Manual` | Where entries come from: `Manual` (only what you list and the sources below), `AssemblyScan` (attributes in `AssembliesToScan` and the metadata of your endpoints), `Both` |
| `PermissionList` / `RoleList` / `ScopeList` / `GroupList` | empty | The entries you list by hand; the same as calling `Add(...)` for each |
| `AssembliesToScan` | empty | Assemblies searched for `[Auth2Permission]`, `[Auth2Role]`, `[Auth2Scope]`, `[Auth2Group]` on types and methods (components, services). Endpoints are scanned without it |

Besides the list there are more **sources**: `AddFromClass`, `AddFromEnum`, `AddFromString`, `AddFromListString`, `AddFromConfiguration`, `AddProvider<T>()` and `AddFactory(...)` ([client-reference.md](client-reference.md#3-catalog-sources-the-overloads-of-add)).

How the sync behaves: it runs **after the application has started** (so every endpoint is mapped) and after a good handshake; an unchanged list is not sent again (it is hashed); a failure is retried with growing waits and only logged unless `FailOnSyncError`; the Server never deletes by a push; manual entries win over scanned ones ([permission-sync.md](permission-sync.md)). The Server must allow it: `Clients.PermissionSyncAllowed`, `RoleSyncAllowed`, `ScopeSyncAllowed`, and `GroupSyncAllowed` (off by default) in the project settings ([server-settings.md](server-settings.md)).

```csharp
.EnablePermissions(o =>
{
    o.SyncPolicy = PermissionSyncPolicy.Push;
    o.ScanningPolicy = ScanningPolicy.Both;
    o.PermissionList = [new("orders.read", "Read Orders", "Allows viewing customer orders", "Orders")];
    o.AssembliesToScan = [typeof(Program).Assembly];
})
.EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; o.RoleList = [new("Admin", "Full administrative access")]; })
.EnableScopes(o => { o.SyncPolicy = ScopeSyncPolicy.Push; o.ScopeList = [new("reports.read", "Read reports scope")]; })
.EnableGroups(o => { o.SyncPolicy = GroupSyncPolicy.Both; o.GroupList = [new("finance", "Finance and accounting department")]; })
```

(This is the shape of `samples/SystemX.Auth2.Samples.Mvc/Program.cs`.) **When to use which policy:** `Push` for the normal case (your code is the source of truth); `Pull` when admins define the list and your UI shows it; `Both` when both are true; `None` (do not call the method) when admins manage everything by hand.

## `EnableMachineToMachine`

```csharp
IAuth2ClientBuilder EnableMachineToMachine(this IAuth2ClientBuilder builder, Action<Auth2MachineOptions>? configure = null)
```

One option: `Scopes` (default empty), the scopes used when a call names none. The client asks the Server for a **client-credentials** token with the client id and secret (`grant_type=client_credentials`). Tokens are **cached per set of scopes** until they expire, one request serves all callers waiting for the same set, and `IAuth2MachineTokenProvider.Invalidate()` forgets them (use it after a `401`). See [machine-to-machine.md](machine-to-machine.md).

```csharp
builder.Services.AddAuth2Client(section).EnableMachineToMachine(o => o.Scopes = ["reports.read"]);
```

Use it for a worker, a scheduled job or a service that calls another API **as itself**. Without a user. The application needs those scopes in its allowed scopes on the Server.

## `AddAuth2MachineToken`

```csharp
IHttpClientBuilder AddAuth2MachineToken(this IHttpClientBuilder builder, params string[] scopes)
```

Adds a message handler that puts `Authorization: Bearer <machine token>` on **every request** of that `HttpClient`. Name the scopes the called API needs; with none, the scopes of `EnableMachineToMachine` are used.

```csharp
builder.Services.AddHttpClient("reports", c => c.BaseAddress = new Uri("https://reports.example.com"))
                .AddAuth2MachineToken("reports.read");

// later
var http = factory.CreateClient("reports");
var rows = await http.GetFromJsonAsync<Report[]>("/api/reports");   // the token is added for you
```

Why a handler: you never handle a token in your code, the cache is shared, and a token that expires is replaced without a failed call.

## `EnableCurrentUser`

```csharp
IAuth2ClientBuilder EnableCurrentUser(this IAuth2ClientBuilder builder, Action<Auth2CurrentUserOptions>? configure = null)
```

| Option | Default | Meaning |
|---|---|---|
| `Source` | `TokenAndServer` | Where roles and permissions come from. `TokenAndServer`: name and email from the token, roles, permissions and groups from the Server (`/me`), cached. `TokenOnly`: only the claims of the token (no call; roles only; no permissions or groups) |
| `CacheTtl` | 10 min | How long access data is kept before it is read again. A newer `pv` (access version) claim in the token also forces a re-read |

`ICurrentUser` and `ICurrentClient` work without calling this method (the services are always registered); call it to **change the options**. How the data is kept and refreshed: [client-current-user.md](client-current-user.md).

```csharp
.EnableCurrentUser(o => { o.Source = CurrentUserSource.TokenAndServer; o.CacheTtl = TimeSpan.FromMinutes(10); })
```

Choose `TokenOnly` for a small API that only checks roles in the token and must not depend on the Server per request; choose `TokenAndServer` (default) when you check permissions. A shorter `CacheTtl` makes role changes appear sooner but costs more calls.

## `UseDistributedCurrentUserStore`

```csharp
IAuth2ClientBuilder UseDistributedCurrentUserStore(this IAuth2ClientBuilder builder)
```

Replaces the memory store with `DistributedCurrentUserStore`, which keeps access data (as JSON, with an absolute expiry = `CacheTtl`) in `IDistributedCache`. It registers `AddDistributedMemoryCache()` as the default (fine for one instance and tests); add a real cache to share it:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "redis:6379");
builder.Services.AddAuth2Client(section).UseDistributedCurrentUserStore();
```

Use it when you run **several instances** of your application, so that one read of a user's access serves all of them and a change is seen by all after the same time. Not needed for one instance.

## `EnableResilience`

```csharp
IAuth2ClientBuilder EnableResilience(this IAuth2ClientBuilder builder)
```

Adds the standard resilience handler of `Microsoft.Extensions.Http.Resilience` (retry with jitter, per-attempt and total timeouts, circuit breaker) to the **resilient** named client only. That client is used for token requests, the handshake, sync and reads. Register, login and password calls use the other client and are **never** repeated: repeating them could create a user twice or count a wrong password twice.

Use it in production, so that a short network problem or a Server restart does not fail your requests. Without it each call is tried once.

## `EnableHealthCheck`

```csharp
IAuth2ClientBuilder EnableHealthCheck(this IAuth2ClientBuilder builder)
```

Adds a health check named `auth2`: **Healthy** when the last handshake worked, **Degraded** (with the status text) when it has not finished or failed. Map your health endpoints as usual:

```csharp
builder.Services.AddAuth2Client(section).EnableHealthCheck();
app.MapHealthChecks("/health");     // as in both samples
```

The result is Degraded, not Unhealthy: with `OnServerUnavailable = Continue` your application can still serve requests that do not need the Server. ASP.NET Core answers 200 for a Degraded check unless you change the status mapping, so a readiness probe keeps passing; map Degraded to 503 yourself if you want it to fail.

## `UseAuth2`

```csharp
IApplicationBuilder UseAuth2(this IApplicationBuilder app)    // class Auth2ClientApplicationBuilderExtensions
```

Put it where you would put `UseAuthentication()` and `UseAuthorization()`; **it replaces both**. In order it adds:

1. `UseAuthentication()`
2. `UseAuthorization()`
3. A middleware for a session that ended on the Server. When reading the user's access fails with `invalid_token` (the user signed out on the Server, or the token was revoked) it removes the cookie and sends the browser to sign in again; an API call gets `401` ([client-reference.md](client-reference.md#errors)). Other errors are not touched.
4. The `/auth2/login`, `/auth2/logout` and `/auth2/denied` routes of the hosted UI (when a UI mode is on).

Call it after `UseRouting()` and before the endpoints (as both samples do).

```csharp
app.UseStaticFiles();
app.UseRouting();
app.UseAuth2();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
```

## Complete examples

**An API** (`samples/SystemX.Auth2.Samples.MinimalApi`):

```csharp
builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject("sample-project")
    .UseApiAuthentication()
    .EnableCurrentUser(o => { o.Source = CurrentUserSource.TokenAndServer; o.CacheTtl = TimeSpan.FromMinutes(10); })
    .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; })
    .EnableResilience()
    .EnableHealthCheck();
```

**A browser app with the Server's pages** (`samples/SystemX.Auth2.Samples.Mvc`):

```csharp
builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject("sample-project")
    .UseHostedUi()
    .UseCookieAuthentication(o => o.AccessMode = AccessLoadMode.OnSignIn)
    .UseApiAuthentication()          // bearer tokens work too
    .EnableCurrentUser()
    .EnableResilience()
    .EnableHealthCheck();
```

**A worker that calls an API as itself:**

```csharp
builder.Services
    .AddAuth2Client("https://auth.example.com", o => { o.ApplicationName = "nightly-report"; o.ClientId = "nightly-report"; o.ClientSecret = secret; })
    .ServerWithMultiProject("papertrade")
    .EnableMachineToMachine(o => o.Scopes = ["reports.read"])
    .EnableResilience();
builder.Services.AddHttpClient("reports", c => c.BaseAddress = new Uri("https://reports.example.com")).AddAuth2MachineToken();
```

**Several instances behind a load balancer:** add `AddStackExchangeRedisCache(...)` and `.UseDistributedCurrentUserStore()`.

## Common problems

| Symptom | Cause and fix |
|---|---|
| The application stops at start with `Auth2: ClientSecret is required...` | An option is missing. The messages name the option; the environment variable for a section key is `Auth2__ClientSecret` |
| `Auth2: ProjectKey may only have lowercase letters...` | The key does not follow the rule, or the Server is single-project (leave `ServerWithMultiProject` out) |
| Start fails with `Auth2StartupException` | `OnServerUnavailable` or `OnProjectNotFound` is `Throw` and the handshake failed; the message has the status |
| `401` on every call, no sign-in | `UseAuth2()` (or `UseAuthentication()`) is missing, or it comes before `UseRouting()` |
| Permissions never appear on the Server | `SyncPolicy` is `None`; the Server has `Clients.PermissionSyncAllowed` off; or the handshake did not succeed (the sync waits for it) |
| Access data differs between instances | Use `UseDistributedCurrentUserStore()` with Redis |
| A retry created two users | Register and login are never retried by the client; look at your own retry code |

## How to implement or extend

- Add a method in the same style: write an extension on `IAuth2ClientBuilder`, put its work on `builder.Services` (`Configure<T>`, `TryAdd...`), return the builder. Keep it free of side effects at call time, so that call order does not matter.
- To replace a part, register your own implementation **before** `AddAuth2Client` (the registrations use `TryAdd`): `IAuth2AuditSink`, `ICurrentUserStore`, `IAuth2AccessTokenAccessor`.
- Tests: `OptionsValidationTests` (every start-up rule, and that invalid options stop the host) and `BuilderTests` (the order of the calls does not matter, a configuration section works, no project key without `ServerWithMultiProject`) in `tests/SystemX.Auth2.Client.Tests/OptionsAndBuilderTests.cs`; `tests/SystemX.Auth2.Client.IntegrationTests` runs the client against a real Server.

## Related pages

- [client-reference.md](client-reference.md)
- [client-setup.md](client-setup.md)
- [handshake-and-startup.md](handshake-and-startup.md)
- [permission-sync.md](permission-sync.md)
- [client-current-user.md](client-current-user.md)
- [machine-to-machine.md](machine-to-machine.md)
- [getting-started.md](getting-started.md)
- [Documentation index](index.md)
- [Contracts reference](contracts-reference.md)
- [Server settings](server-settings.md)
