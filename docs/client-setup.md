# Client setup

## What this is

How to add the `SystemX.Auth2.Client` package to your application, and what it does at start-up.
This page covers the **core** of the client: options, the handshake, the Auth2 API service, health check and resilience.
Authorization attributes, the current user, cookie / OIDC login and sync services come in later versions of the package (see [roadmap.md](roadmap.md)).

## When to use it

Your application (an API, an MVC app, a Blazor app, a worker) wants to use a SystemX.Auth2 Server for login, tokens, permissions and roles.

## Step by step

1. Create the application on the server and keep the **client secret** (shown once). See [server-setup.md](server-setup.md).
2. Add the package: `dotnet add package SystemX.Auth2.Client`.
3. Put the secret in a secret store (`dotnet user-secrets`, an environment variable, Key Vault). **Never in the source code.**
4. Register the client in `Program.cs` (examples below).
5. Start the application. Look for the log line "Handshake with the Auth2 server worked."

## Which server mode do you have?

The server is either **multi-application** (many projects, each with a `ProjectKey`) or **single-application** (one project, no key).

- Multi-application server → call `.ServerWithMultiApplication("your-project-key")`.
- Single-application server → **do not** call it.

You do not have to guess right: the handshake asks the server and tells you if you chose wrong (see below).
`ServerWithSingleApplication()` exists only on the **server**. The client has no such method.

### Example: multi-application server

```csharp
builder.Services
    .AddAuth2Client("https://auth.example.com", o =>
    {
        o.ApplicationName = "PaperTrade.Api";          // the name of the application on the server
        o.ClientId = "papertrade-api";
        o.ClientSecret = builder.Configuration["Auth2:Secret"]!;
        o.OnServerUnavailable = StartupFailureBehavior.Continue;
        o.OnApplicationNotFound = StartupFailureBehavior.Throw;
    })
    .ServerWithMultiApplication("papertrade")          // the project key
    .EnableResilience()
    .EnableHealthCheck();

var app = builder.Build();
app.UseAuth2();                                         // UseAuthentication + UseAuthorization
```

### Example: single-application server

```csharp
builder.Services
    .AddAuth2Client("https://auth.example.com", o =>
    {
        o.ApplicationName = "Default";
        o.ClientId = "default";
        o.ClientSecret = builder.Configuration["Auth2:Secret"]!;
    })
    .EnableResilience()
    .EnableHealthCheck();
```

### From configuration

```csharp
builder.Services.AddAuth2Client(builder.Configuration.GetSection("Auth2")).ServerWithMultiApplication("papertrade");
```

```json
{ "Auth2": { "ServerUrl": "https://auth.example.com", "ApplicationName": "PaperTrade.Api", "ClientId": "papertrade-api" } }
```

(`ClientSecret` comes from the environment variable `Auth2__ClientSecret`.)

The builder methods can be called **in any order**.

## Options

| Option | Default | Meaning |
|---|---|---|
| `ServerUrl` | (required) | Address of the server |
| `ApplicationName` | (required) | Must equal the application name on the server |
| `ClientId`, `ClientSecret` | (required) | OAuth client of this application |
| `ProjectKey` | none | Set by `ServerWithMultiApplication`. Lowercase letters, digits, `-`, `_`; 3–64 characters; not `_global` |
| `HandshakeOnStartup` | `true` | Make the handshake when the application starts |
| `HandshakeTimeout` | 5 s | Time limit of one attempt |
| `OnServerUnavailable` | `Continue` | `Throw` stops the application when the server cannot be reached |
| `OnApplicationNotFound` | `Throw` | What to do when the server does not know the application, or rejects the client |
| `HandshakeMaxRetries` | 6 | Background retries (only with `Continue`) |
| `HandshakeInitialDelay` / `HandshakeMaxDelay` | 2 s / 60 s | Wait before the first retry; it doubles up to the maximum |
| `RequestTimeout` | 30 s | Time limit of one call to the server |

The options are checked at start-up. A wrong option stops the application with a message that says what to fix.

## The handshake

At start-up the client asks the server "am I who I say I am?" (`POST /api/auth2/client/handshake`, with a machine token). The result is a `HandshakeStatus`:

| Status | When | What happens |
|---|---|---|
| `Ok` | The server accepted the client | Logged. `Auth2ClientState.Status` is `Ok` |
| `ServerUnavailable` | No answer, timeout, 5xx, 429 | `Throw`: the application does not start. `Continue`: a warning, the application starts, and it retries in the background (2 s, 4 s, 8 s ... up to 60 s, 6 tries) |
| `NotFound` | The server does not know the application (404) | Follows `OnApplicationNotFound`. Never retried |
| `Rejected` | Wrong secret (401/403), wrong name or key, or wrong server mode (409) | Follows `OnApplicationNotFound`. Never retried |

Messages you may see:

- "The server is single-application. Remove ServerWithMultiApplication."
- "The server is multi-application. Call ServerWithMultiApplication(projectKey)."
- "ProjectKey or ApplicationName does not match the application of this client id on the server."
- "The server refused the client credentials. Check ClientId and ClientSecret ..."

Read the state in your code:

```csharp
var state = app.Services.GetRequiredService<Auth2ClientState>();
state.Status;          // HandshakeStatus? (null while the first handshake runs)
state.LastResponse;    // server mode, issuer, cookie lifetime, single-active-session flag
await state.WaitUntilReadyAsync(ct);   // for services that must wait for the server
```

To try again by hand: `await api.HandshakeAsync()`.

## Health check

`.EnableHealthCheck()` adds a check named `auth2`: **Healthy** after a good handshake, **Degraded** otherwise. The application keeps running when it is degraded.

## The Auth2 API service

`IAuth2ApiClientService` calls the server: `RegisterAsync`, `LoginAsync` (+ `TwoFactorLoginAsync`, `OtpLoginAsync`), `RequestOtpAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync`, `ChangePasswordAsync`, `RefreshAsync`, `LogoutAsync`, `GetMeAsync`, `HandshakeAsync`, `SyncPermissionsAsync`, `SyncRolesAsync`, `GetPermissionsAsync`, `GetRolesAsync`, `GetGroupsAsync`.

**Results, not exceptions.** Every method returns `Result` or `Result<T>`. A wrong password or a server that is down is a normal answer:

```csharp
var login = await api.LoginAsync(new LoginRequest("sara", password));
if (!login.IsSuccess) return Results.Problem(login.Error!.Code);     // for example "invalid_credentials"
if (login.Value!.TwoFactorRequired)
    login = await api.TwoFactorLoginAsync(new TwoFactorRequest(login.Value.TwoFactorToken!, code));
```

A server that cannot be reached gives the code `server_unavailable`. Only low-level pieces (the token provider and the token handler) throw `Auth2Exception`. Exceptions never contain secrets.

Calls that **change something** (register, login, password calls, refresh) are **never retried automatically**. A repeated login would count twice towards the lockout.

## Resilience

`.EnableResilience()` adds retry with jitter, a timeout and a circuit breaker to the calls that are safe to repeat: the machine token, the handshake, sync and reading catalogs. It uses `Microsoft.Extensions.Http.Resilience`.

## Common problems

| Problem | Cause and fix |
|---|---|
| Start-up error "Auth2: ClientSecret is required" | The secret is not set. Check the user secret or environment variable |
| `Rejected` "The server refused the client credentials" | Wrong `ClientId` or `ClientSecret`, or the secret was rotated |
| `Rejected` single / multi message | Add or remove `ServerWithMultiApplication` |
| `Rejected` `project_key_or_name_mismatch` | `ApplicationName` or the project key differs from the server. Compare them in the admin API |
| `ServerUnavailable` at start | Wrong `ServerUrl`, a firewall, or the server is down. With `Continue` the client keeps trying |
| `insufficient_scope` / `invalid_scope` | The client is not allowed that scope. Add it to `Clients.AllowedScopes` of the application |
| Register / login return `server_unavailable` | Not retried on purpose. Show the user an error and let them try again |

## Related pages

- [machine-to-machine.md](machine-to-machine.md)
- [handshake-and-startup.md](handshake-and-startup.md) (the server side)
- [permission-sync.md](permission-sync.md)
- [authentication-flows.md](authentication-flows.md)

## Sign-in for browser apps (UI modes)

The client supports three ways to sign users in. Choose one.

| Call | Mode | Who shows the login form | Use it for |
|---|---|---|---|
| (none) | `UiMode.None` | nobody | APIs: 401 / 403 only |
| `.UseHostedUi()` | `Hosted` | **the Auth2 Server** | MVC, Razor Pages, Blazor. Recommended: your app never sees a password |
| `.UseSelfUi()` | `Self` | **your application** | When you need your own forms. Your app handles passwords; the server must allow it (`Clients.AllowSelfUiLogin`) |

Both modes make a sign-in **cookie** in your application (`UseCookieAuthentication` is added for you).

### Hosted UI

```csharp
builder.Services.AddAuth2Client("https://auth.example.com", o => { /* name, client id, secret */ })
    .ServerWithMultiApplication("papertrade")
    .UseHostedUi()                                   // authorization code + PKCE
    .UseCookieAuthentication(o => o.AccessMode = AccessLoadMode.OnSignIn);

app.UseAuth2();                                      // also maps /auth2/login and /auth2/logout
```

1. **Register the callback address** on the server as a redirect URI of your application, exactly: `https://app.example.com/signin-oidc`. Also register `https://app.example.com/signout-callback-oidc` as a post-logout redirect URI.
2. A user who is not signed in is sent to `/auth2/login?returnUrl=...`, which sends the browser to the server. After login the browser comes back to `/signin-oidc` and then to the page the user wanted (**page B is kept**).
3. Sign out with a link to `/auth2/logout`. It signs out of your app and of the server.

`returnUrl` must be a path on your own site. Anything else (`https://evil.test`, `//evil.test`) is replaced by `/`.

What `OnTokenValidated` is: ASP.NET Core raises this event when the identity token from the server has been checked and before your cookie is made. The package uses it for you: it keeps the access version and loads the user's access data (see `AccessMode` below). You do not write this hook.

### Self UI

```csharp
builder.Services.AddAuth2Client(...).UseSelfUi()
    .UseCookieAuthentication(o => o.LoginPath = "/login");

app.MapPost("/login", async (HttpContext http, IAuth2WebService web, LoginForm form) =>
{
    var result = await web.LoginAsync(http, new LoginRequest(form.Login, form.Password), form.RememberMe);
    if (!result.IsSuccess) return Results.BadRequest(result.Error!.Code);
    return result.Value!.TwoFactorRequired
        ? Results.Ok(new { twoFactor = result.Value.TwoFactorToken })          // show the code form, then call TwoFactorLoginAsync
        : Results.Redirect("/");
});
```

`IAuth2WebService`: `LoginAsync`, `TwoFactorLoginAsync`, `OtpLoginAsync` (each makes the cookie when it works), `LogoutAsync` (revokes the refresh token and removes the cookie), `ChallengeAsync(http, returnUrl)`. `/auth2/logout` is mapped for you.

### Cookie options (`UseCookieAuthentication`)

| Option | Default | Meaning |
|---|---|---|
| `CookieName` | `.SystemX.Auth2.Client` | |
| `ExpireTimeSpan` | **null = the value of the server** | The server's `Sessions.CookieLifetimeMinutes` is read at the handshake (8 hours when the server has none) |
| `SlidingExpiration` | true | |
| `SameSite` | `Lax` | Keep `Lax`: the sign-in redirect needs it |
| `SecurePolicy` | `Always` | Use `SameAsRequest` only in development |
| `LoginPath` / `AccessDeniedPath` | `/auth2/login` / `/auth2/denied` | |
| `SaveTokens` | true | Keeps the access, refresh and identity tokens in the cookie (needed to read access data later) |
| `AccessMode` | `Lazy` | When roles, permissions and groups are loaded |

`AccessMode`:

| Mode | What happens |
|---|---|
| `Lazy` | Loaded when first needed (from the server, then cached) |
| `OnSignIn` | Loaded at sign-in into the store. The first page is fast and needs no extra call |
| `RolesInCookie` | The roles become claims in the cookie. Small data only: a cookie is limited to 4 KB |
| `None` | Never loaded by the package |

Limit: the access token in the cookie is not renewed. For `Lazy` mode, the data is read with it, so after it expires (default 15 minutes) a **new** read fails and the cached or token-based data is used. Prefer `OnSignIn` for long sessions, and keep `CacheTtl` long enough.

### What a failed sign-in answers

| Request | Not signed in | Signed in, not allowed |
|---|---|---|
| Browser (`Accept: text/html`, or no `Accept`) | redirect to `LoginPath?returnUrl=...` | redirect to `AccessDeniedPath` |
| Bearer token, `Accept: application/json`, AJAX (`X-Requested-With`), `[ApiController]`, or `.AsAuth2Api()` | `401` | `403` |

A request with an `Authorization: Bearer` header uses the token even when cookies are on, so `UseApiAuthentication()` and the cookie work together in one application.

### Blazor

```csharp
builder.Services.AddAuth2Client(...).UseHostedUi().UseBlazor();
```

- `AuthorizeView` and `[Authorize(Policy=...)]` work with the Auth2 attributes (`[Auth2Permission("...")]` on a component).
- `IAuth2BlazorService`: `GetUserAsync()`, `HasPermissionAsync`, `HasRoleAsync`, `IsInGroupAsync`, `NavigateToLogin(navigationManager)`, `NavigateToLogout(...)`. After sign-in the user comes back to the current page through the `NavigationManager`.
- In Blazor Server a circuit has no HTTP request, so the access data is loaded **at sign-in** (`AccessMode.OnSignIn` is the default with `UseBlazor()`).

## Permission, role and group sync

Set the policies:

```csharp
.EnablePermissions(o =>
{
    o.SyncPolicy = PermissionSyncPolicy.Push;        // None, Push, Pull, Both
    o.ScanningPolicy = ScanningPolicy.Both;          // Manual, AssemblyScan, Both
    o.PermissionList = [new("orders.read", "Read orders", "Lets you read orders", "Orders")];
    o.AssembliesToScan = [typeof(Program).Assembly]; // for [Auth2Permission] on components and services
})
.EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.AssemblyScan; })
.EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Pull)
```

- **Scanning** finds `WithPermission` / `WithRole` on your endpoints (Minimal API, controllers, Razor Pages) and `[Auth2Permission]` / `[Auth2Role]` on types and methods of `AssembliesToScan`. With `Both` the lists are merged and **your manual entries win** (they have names and descriptions). Invalid keys are skipped with a warning.
- **Sync runs after the application started** (all endpoints are mapped) and after a good handshake. With `HandshakeOnStartup = false` it waits until you call `HandshakeAsync()`.
- **Push** sends permissions, then roles. The list is hashed: it is sent again only when it changed since the last success. A failure is retried with growing waits (`SyncMaxRetries`, default 5). By default a failure is only logged (`FailOnSyncError = false`).
- **Pull** reads the catalogs at start and every `CatalogRefreshInterval` (10 minutes) into `IAuth2Catalog` (`Permissions`, `Roles`, `Groups`, `LastReadUtc`).
- **Both** pushes first, then pulls. **Groups** can only be pulled: admins decide them.

The server rules (never deletes, admin text wins) are in [permission-sync.md](permission-sync.md).
