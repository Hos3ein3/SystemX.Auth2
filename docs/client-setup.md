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
