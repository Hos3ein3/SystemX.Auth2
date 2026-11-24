# SystemX.Auth2.Client

The client package of **SystemX.Auth2**, a self-hosted authentication and authorization server for .NET 10 (ASP.NET Core Identity, OpenIddict, PostgreSQL). Add it to your ASP.NET Core application and it signs users in against your Auth2 Server, protects your endpoints with roles, permissions, scopes and groups, and **publishes those roles, permissions, scopes and groups to the Server from your code**, so the admin pages always show what your application really uses.

It is not a replacement for Keycloak or Duende. It is one server and one package for small and medium .NET systems.

## What you get

| Need | What the package gives you |
|---|---|
| Sign users in | **Hosted UI** (users sign in on the Server's pages, your app never sees a password), **Self UI** (your own form), **cookie** apps (MVC, Razor Pages), **Blazor Server** helpers, **JWT bearer** APIs |
| Protect endpoints | `[Auth2Permission]`, `[Auth2Role]`, `[Auth2Scope]`, `[Auth2Group]` attributes and `WithPermission`, `WithRole`, `WithScope`, `WithGroup`, ... for Minimal APIs |
| Know who is calling | `ICurrentUser` (the person) and `ICurrentClient` (token, project, scopes, machine flag, `Idp`, `Amr`) |
| Keep the Server up to date | Scanners and sync: permissions, roles, scopes and groups are pushed at start (and read back) |
| Call other APIs | Machine-to-machine tokens (`AddAuth2MachineToken`) |
| Stay up | Start-up handshake with the Server, retry/timeout/circuit breaker (`EnableResilience`), a health check |

## Requirements

- .NET 10 and ASP.NET Core
- A running SystemX.Auth2 Server and an **application** registered on it (client id and secret). The Server seeds a sample project with two applications (`mvc-sample`, `minimal-api-sample`) so you can try everything at once: see [Getting started](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/getting-started.md).

## Install

Add a package reference to `SystemX.Auth2.Client` (or, when you build from source, a project reference to `src/SystemX.Auth2.Client`).

## Quick start: an API

```csharp
using SystemX.Auth2.Contracts.Enums;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject("sample-project")      // the project this application belongs to
    .UseApiAuthentication()                        // JWT bearer tokens from the Server
    .EnableCurrentUser()
    .EnablePermissions(o =>
    {
        o.SyncPolicy = PermissionSyncPolicy.Push;  // send the list to the Server at start
        o.ScanningPolicy = ScanningPolicy.Both;    // your list + attributes and endpoints
        o.PermissionList = [new("orders.read", "Read Orders", "Allows viewing customer orders", "Orders")];
    });

var app = builder.Build();
app.UseAuth2();                                    // UseAuthentication + UseAuthorization + /auth2/... routes

app.MapGet("/api/orders", (ICurrentUser user) => Results.Ok(new { user.UserName }))
   .WithPermission("orders.read");

app.Run();
```

`appsettings.json` (the secret belongs in an environment variable or a secret store, for example `Auth2__ClientSecret`):

```json
{
  "Auth2": {
    "ServerUrl": "http://localhost:5100",
    "ApplicationName": "minimal-api-sample",
    "ClientId": "minimal-api-sample",
    "ClientSecret": "<secret>",
    "ProjectKey": "sample-project"
  }
}
```

## Quick start: an MVC app with the hosted sign-in

```csharp
builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject("sample-project")
    .UseHostedUi()                                  // sign in on the Server's pages
    .UseCookieAuthentication(o => o.AccessMode = AccessLoadMode.OnSignIn)
    .EnableCurrentUser();
// ...
app.UseAuth2();
```

```csharp
[Auth2Role("Admin")]                      // on an action or controller
public IActionResult AdminPanel() => View();

[Auth2Permission("orders.read")]
public async Task<IActionResult> Orders(CancellationToken ct) => View(await currentUser.GetPermissionsAsync(ct));   // currentUser is the injected ICurrentUser
```

Register the callback address of your application (for example `http://localhost:5101/signin-oidc`) as a redirect URI of the application on the Server. The layout links *Sign In* to `/auth2/login` and *Logout* (a POST) to `/auth2/logout`. Both samples in the repository are complete, running versions of these snippets: [`samples/SystemX.Auth2.Samples.Mvc`](https://github.com/Hos3ein3/SystemX.Auth2/tree/main/samples/SystemX.Auth2.Samples.Mvc) and [`samples/SystemX.Auth2.Samples.MinimalApi`](https://github.com/Hos3ein3/SystemX.Auth2/tree/main/samples/SystemX.Auth2.Samples.MinimalApi).

## How it works

1. **Handshake at start.** The package calls the Server with your client id, secret, application name and project key. The Server checks that the client really belongs to that project. `OnServerUnavailable` and `OnProjectNotFound` decide whether a failure stops the app or only logs (`Continue`).
2. **Sync after start.** When the app is running and every endpoint is mapped, the scanners collect permissions, roles, scopes and groups (your lists, your attributes, your endpoint metadata) and push them. Nothing is deleted on the Server by a push, and an unchanged list is not sent again.
3. **Requests.** A request with a bearer token is checked against the Server's signing keys (cached, refreshed on a key rotation). A browser request uses the sign-in cookie. `ICurrentUser` reads name and email from the token and roles, permissions and groups from the Server (cached by the `pv` access version in the token).
4. **Your code** never calls the Server for sign-in checks: attributes and `With...` methods do it.

## Choosing the sign-in mode

| Mode | Builder method | Who shows the login form | Use it for |
|---|---|---|---|
| API | `UseApiAuthentication()` | nobody (a client sends a bearer token) | Web APIs |
| Hosted UI | `UseHostedUi()` + `UseCookieAuthentication()` | the Server | Browser apps. Recommended: your app never handles a password |
| Self UI | `UseSelfUi()` + `UseCookieAuthentication()` | your app | When you must own the page. Your app handles passwords; the application must have `AllowSelfUiLogin` on the Server |
| Blazor | `UseBlazor()` (with hosted or self) | as above | Blazor Server |

Modes can be combined: a request with a bearer token uses the token, everything else uses the cookie.

## The builder at a glance

```csharp
builder.Services
    .AddAuth2Client(section)                 // or AddAuth2Client("https://auth.example.com", o => { ... })
    .ServerWithMultiProject("papertrade")
    .UseHostedUi() / .UseSelfUi() / .UseBlazor()
    .UseCookieAuthentication(o => { ... })
    .UseApiAuthentication(o => { ... })
    .EnableCurrentUser(o => o.CacheTtl = TimeSpan.FromMinutes(10))
    .UseDistributedCurrentUserStore()        // share access data between instances (Redis)
    .EnablePermissions(o => { ... })  .EnableRoles(o => { ... })  .EnableScopes(o => { ... })  .EnableGroups(o => { ... })
    .EnableMachineToMachine(o => o.Scopes = ["reports.read"])
    .EnableResilience()
    .EnableHealthCheck();
```

Calls can be made in any order. Every method, every overload and the options of each: the [client reference](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/client-reference.md).

## Keeping the Server's lists in sync

Each catalog (permissions, roles, scopes, groups) has a `SyncPolicy` (`None`, `Push`, `Pull`, `Both`) and a `ScanningPolicy` (`Manual`, `AssemblyScan`, `Both`). Besides the list property you can add keys from a class of constants (`AddFromClass`), an enum (`AddFromEnum<T>()`), a text (`AddFromString`), a list (`AddFromListString`), configuration (`AddFromConfiguration`), a DI class (`AddProvider<T>()`) or a factory (`AddFactory`). A wrong key stops the start with a message that names the source. Details: [permission sync](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/permission-sync.md).

## Calling other APIs

```csharp
builder.Services.AddHttpClient("reports").AddAuth2MachineToken("reports.read");
// every request of this client now carries a client-credentials token
```

## Errors

Services such as `IAuth2ApiClientService` return `Result` / `Result<T>` with a stable error code and do not throw for a failed call. The low-level building blocks throw `Auth2Exception` (`StatusCode`, `ErrorCode`, `IsTransient`). When a user signs out on the Server while your app still has its own cookie, `UseAuth2()` removes the cookie and sends the browser to sign in again (an API call gets 401).

## Documentation

- [Getting started](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/getting-started.md)
- [Client setup: options, handshake, UI modes](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/client-setup.md)
- [Protecting endpoints](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/client-authorization.md)
- [The current user](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/client-current-user.md)
- [Permission, role, scope and group sync](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/permission-sync.md)
- [Machine to machine](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/machine-to-machine.md)
- [Client reference (every type, member and overload)](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/client-reference.md)
- [Concepts: project and application](https://github.com/Hos3ein3/SystemX.Auth2/blob/main/docs/concepts.md)

## License

MIT.
