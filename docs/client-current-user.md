# Current user and current client

## What this is

Two services tell your code **who is calling**. Both read the `ClaimsPrincipal` of the current request, and both are scoped (one instance per request):

| Service | About | Source | Sync or async |
|---|---|---|---|
| `ICurrentClient` | The calling **application** and its token: client id, project, scopes, "is it a program?", session, how the sign-in happened | The claims of the token. **Never calls the Server** | Synchronous |
| `ICurrentUser` | The **person**: name, email, roles, permissions, groups | Name and contact data from the token; roles, permissions and groups from the Server (`GET /api/auth2/me`), cached | Properties synchronous; access lists **async** |

A third thing sits behind them: **`CurrentUserInfo`**, the answer of `/me` (the Server's description of the person and their rights), and **`Auth2Access`**, the copy of it that the Client keeps in a cache.

The one rule to remember: a **scope** is what a *client* may do; a **permission** is what a *user* may do (all the words side by side: [access-concepts.md](access-concepts.md)). An endpoint can require both ([client-authorization.md](client-authorization.md)).

## When to use it

In endpoints, services and views whenever the code needs the caller: show the name, check a permission in code (when an attribute is too coarse), read the project key, see whether the token belongs to a program, ask for a stronger sign-in before a sensitive action.

```csharp
app.MapGet("/me", async (ICurrentUser user, ICurrentClient client) => new
{
    user.UserName,
    user.Email,
    Roles = await user.GetRolesAsync(),
    CanWrite = await user.HasPermissionAsync("orders.write"),
    client.ClientId,
    client.ProjectKey,
    MayReadReports = client.HasScope("reports.read"),
}).WithAuth2();
```

(`samples/SystemX.Auth2.Samples.MinimalApi` has `/api/me` and `/api/client` in this shape; the MVC sample's `HomeController` reads `GetRolesAsync` and `HasPermissionAsync`.)

You do not need to call `EnableCurrentUser()` for either service to work: they are always registered. Call it to change the options ([client-builder.md](client-builder.md#enablecurrentuser)).

## `ICurrentClient` in depth

| Member | Claim | What it is, and what you do with it |
|---|---|---|
| `IsTokenAuthenticated` | (the identity) | `true` when the request has a valid token of **any** kind (user or machine). Not the same as `ICurrentUser.IsAuthenticated` |
| `ClientId` | `client_id` | The OAuth client id of the application that asked for the token. Use it in logs and audit |
| `ApplicationId` | `application_id` | The id of the application on the Server (a GUID); `null` when the claim is missing or not a GUID |
| `ProjectId` | `project_id` | The id of the project (tenant) of that application |
| `ProjectKey` | `projectkey` | The key of the project, for example `papertrade`. **Omitted by a single-project Server**, then `null` |
| `Scopes` | `scope` and `scp` | Every scope of the token. The reader accepts one claim with spaces (`"openid auth2.me"`), several claims, and the `scp` name that some libraries use; duplicates are removed |
| `HasScope(scope)` | | Exact, case-sensitive match against `Scopes` |
| `IsMachine` | `machine` | `true` when the claim is `"true"`: the token came from the client-credentials grant (a program). User tokens never have it |
| `SessionId` | `sid` | The id of the sign-in session of a user token; `null` for a machine token |
| `Idp` | `idp` | The name of the **external provider** the person signed in with (the name your project gave it: `google`, `corp-saml`, `ad`). `null` after a local sign-in |
| `Amr` | `amr` | How the person proved who they are. The values are `pwd` (password), `otp` (one-time code), `fed` (external provider), `swk` or `hwk` (a passkey in software or hardware) and `mfa` (a user-verified passkey, or an external provider that reports several factors; a local password plus an authenticator code gives `pwd` and `otp`). Several claims are collected into one distinct list |

Everything is read from the claims **as they are in the request**. If a claim is missing the member is `null`, `false` or empty; nothing throws.

**Examples**

```csharp
// refuse a sensitive action unless the person used several factors
if (!client.Amr.Contains("mfa") && !client.Amr.Contains("otp")) return Results.Forbid();

// audit which application and which sign-in produced an action
logger.LogInformation("{Client} in {Project} via {Idp}", client.ClientId, client.ProjectKey, client.Idp ?? "local");

// a program must not use a user-only route (the same as .WithUserOnly())
if (client.IsMachine) return Results.StatusCode(StatusCodes.Status403Forbidden);
```

## `ICurrentUser` in depth

### The person: from the token, no call

| Member | Claim(s) read (first non-empty wins) |
|---|---|
| `IsAuthenticated` | The identity is authenticated **and** the token is not a machine token |
| `UserId` | `sub`, then the .NET name-identifier claim; parsed as a GUID. `null` for a machine token |
| `UserName` | `preferred_username`, then `name`. `null` for a machine token |
| `Email`, `PhoneNumber` | `email`, `phone_number` |
| `FirstName`, `LastName` | `given_name`, `family_name` |
| `FullName` | `name` |

A claim is only present when the token has the matching scope: ask for `profile` (names), `email` and `phone` ([troubleshooting at the end](#common-problems)). The hosted UI asks for `openid profile email offline_access auth2.me` by default.

### The rights: async, cached

| Member | Returns | Notes |
|---|---|---|
| `GetRolesAsync()` | The roles of the person in the project | Empty for a machine token |
| `GetPermissionsAsync()` | **Effective** permissions: direct grants + permissions of the person's roles + permissions of the roles of the person's groups + permissions of the groups | Lowercase keys, sorted |
| `GetGroupsAsync()` | The groups of the person | |
| `HasPermissionAsync(permission)` | `true` if the person has it | The key is trimmed and **lower-cased** before the check |
| `HasRoleAsync(role)` | `true` if the person has it | **Not** case sensitive |

For a machine token every list is empty, the Server is never asked, and nothing throws.

## What the Server answers: `CurrentUserInfo`

`GET /api/auth2/me` (scope `auth2.me`) returns this record (`SystemX.Auth2.Contracts.Users`). The Client turns it into `Auth2Access` for the cache; you can also read it yourself with `IAuth2CurrentUserService.FetchAsync` (below).

| Field | Meaning |
|---|---|
| `UserId`, `UserName`, `Email`, `PhoneNumber`, `FirstName`, `LastName` | The person (from the database, current at the time of the call) |
| `FullName` | The display name if there is one, else "first last", else the user name |
| `Roles` | **Effective roles** = the person's own roles + the roles of their groups, limited to the project (and the global roles where the project may use them). Sorted |
| `Permissions` | Everything the person may do (see the formula above). Sorted |
| `Groups` | The group names. Sorted |
| `Scopes` | The scopes of **the token that asked** (not a property of the person) |
| `AccessVersion` | A number that only grows: the project's access version + the person's access version in that project. It changes whenever roles, permissions or groups change |
| `DirectPermissions` | Permissions granted straight to the person. `null` with an older Server |
| `RolePermissions` | Permissions that come through roles (own and group roles). `null` with an older Server |

The two source lists let the Client answer `.WithOnlyRolePermission()` and `.WithOnlyUserPermission()` ([client-authorization.md](client-authorization.md)): "does this permission come from a role?" and "was it granted to this person directly?". A person who is **not a member** of the project gets empty lists.

## `Auth2Access`: what the Client keeps

`Auth2Access(Roles, Permissions, Groups, AccessVersion, DirectPermissions, RolePermissions)` is the cached copy. `Auth2Access.Empty` is "no access" (used for machine tokens and anonymous requests); `Auth2Access.From(CurrentUserInfo)` builds it, using empty lists where an old Server sent `null`.

## How the access data is found (`IAuth2AccessProvider`)

This is what runs behind every `GetRolesAsync` / `HasPermissionAsync` and behind the authorization attributes. In order:

1. **Nobody to ask for.** An anonymous request, a machine token, or a principal without `sub` gives `Auth2Access.Empty`. With `Source = TokenOnly` the answer is built from the `role` claims of the token alone (no permissions, no groups) and the Server is never called.
2. **Once per request.** The provider is scoped and keeps the task per user for a few seconds, so ten checks in one request make one lookup. (In a long-lived scope such as a Blazor circuit the memory lasts only 3 seconds, so it never hides a change for long.)
3. **The cache.** The key is `auth2:access:{clientId}:{userId}`. A cached entry is used when its `AccessVersion` is **the same as or newer than** the `pv` claim of the token. It lives for `CacheTtl` (default 10 minutes).
4. **Ask the Server.** Otherwise the Client calls `/me` with the request's access token (the `Authorization` header, or the token that the cookie sign-in saved) and stores the answer.
5. **When the Server cannot be asked.** If there is an older cached answer and the failure is "server unavailable", the old answer is used and a warning is logged. With nothing cached the call throws `Auth2Exception` (`IsTransient = true`), and the authorization checks answer `403` ([client-authorization.md](client-authorization.md)). An `invalid_token` answer (the person signed out or the token was revoked) is turned into a sign-in redirect or a `401` by `UseAuth2()` ([client-builder.md](client-builder.md#useauth2)).

**Why the `pv` check matters.** The token carries `pv`, the access version **when the token was issued**. When an admin changes rights, the Server's version goes up; a token issued later has a higher `pv`, so the cached entry (older) is thrown away at once and `/me` is read again. A token that was issued **before** the change keeps the old answer until `CacheTtl` ends. A new token (login or refresh) shows the change immediately. Lower `CacheTtl` if you need faster changes at the cost of more calls.

## `ICurrentUserStore`: where the cache lives

```csharp
Task<Auth2Access?> GetAsync(string key, CancellationToken ct = default);
Task SetAsync(string key, Auth2Access access, TimeSpan ttl, CancellationToken ct = default);
Task RemoveAsync(string key, CancellationToken ct = default);
```

| Implementation | Use |
|---|---|
| `MemoryCurrentUserStore` (default) | One instance of your application |
| `DistributedCurrentUserStore` | `IDistributedCache` (Redis, SQL Server): several instances share it. Entries are JSON with an absolute expiry; a broken entry counts as no entry |

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "redis:6379");
builder.Services.AddAuth2Client(section).UseDistributedCurrentUserStore();
```

Register your own `ICurrentUserStore` **before** `AddAuth2Client` to replace both ([client-builder.md](client-builder.md)).

## Related services

| Service | What it does |
|---|---|
| `IAuth2AccessTokenAccessor` | Finds the access token of the request: the `Authorization: Bearer` header first, else the token saved by the cookie or hosted sign-in (`SaveTokens`). Returns `null` when there is none |
| `IAuth2CurrentUserService` | `FetchAsync(principal)` and `FetchAsync(accessToken)` call `/me` and return `Result<CurrentUserInfo>`. **No cache.** A machine token gives a `forbidden` failure; no token gives `invalid_token` |
| `IAuth2AccessProvider` | The algorithm above. Inject it when you need the whole `Auth2Access` (including `DirectPermissions` and `RolePermissions`) in one call |
| `IAuth2Catalog` | Not about the person: the permissions, roles, groups and scopes that the last `Pull` read ([permission-sync.md](permission-sync.md)) |

```csharp
// everything in one call, with the same cache
var access = await accessProvider.GetAccessAsync(httpContext.User, ct);
var viaRoleOnly = access.RolePermissions.Contains("orders.write");

// the fresh answer of the Server, no cache (for example right after login)
var me = await currentUserService.FetchAsync(httpContext.User, ct);
if (me.IsSuccess) logger.LogInformation("{Name} has {Count} permissions", me.Value!.UserName, me.Value.Permissions.Count);
```

## Machine tokens

A token from the client-credentials grant belongs to a **program**. `ICurrentClient.IsMachine` is `true`, `ICurrentUser.IsAuthenticated` is `false`, `UserId` and `UserName` are `null`, the access lists are empty, and the Server is never asked. Combine the checks:

| Token | `.WithScope("reports.read")` | `.WithPermission("orders.read")` | Both |
|---|---|---|---|
| Machine token with the scope | passes | fails (no person) | fails |
| User token with the scope, user has no permission | passes | fails | fails |
| User token without the scope, user has the permission | fails | passes | fails |
| User token with the scope, user has the permission | passes | passes | passes |

## Cookie sessions (hosted UI and Self UI)

The person is in a cookie, not in a bearer token. The services work the same way:

- The cookie keeps the claims that matter (`sub`, names, `email`, `pv`, `project_id`, `projectkey`, `application_id`, `client_id`, `sid`, `idp`, `amr`, `scope`) and, with `SaveTokens`, the access token that is used to call `/me`.
- `Auth2CookieOptions.AccessMode` decides **when** the rights are loaded: `Lazy` (first use, then cached), `OnSignIn` (at sign-in into the store, so the first page needs no call), `RolesInCookie` (the roles become `role` claims, small data only because a cookie is limited to 4 KB), `None` (the package never loads them) ([client-setup.md](client-setup.md)).
- The access token in the cookie is **not renewed**. For `Lazy` mode a read after it expired fails and the cached or token-based data is used. Prefer `OnSignIn` for long sessions.

## Common problems

| Problem | Fix |
|---|---|
| `GetPermissionsAsync` throws `Auth2Exception` | The Server cannot be reached and nothing is cached. Check `ServerUrl`, the handshake status (`Auth2ClientState`, the `auth2` health check) and the log |
| Permissions never change | `CacheTtl` is long and the token is old. Renew the token (sign in again, refresh) or lower the TTL |
| Different instances show different data | Use `UseDistributedCurrentUserStore()` with Redis |
| `UserName` is `null` | The token has no `preferred_username`: ask for the `profile` scope |
| `Email` or `PhoneNumber` is `null` | Ask for the `email` / `phone` scope, and check that the person has one |
| `ProjectKey` is `null` | The Server is single-project (the claim is omitted) |
| `Scopes` is empty for a cookie user | Older versions: the identity token has no `scope` claim. The hosted UI now copies the scopes of the **access token** into the cookie at sign-in (the Self UI always did). Check which scopes the application asked for and is allowed |
| A role I just gave is not seen by `[Auth2Role]` | The `role` claim is a snapshot made when the token was issued (and only when the `roles` scope was granted). Renew the token, or protect with a permission. `HasRoleAsync` reads the access data and may already be newer ([access-concepts.md](access-concepts.md#role-in-depth)) |
| `HasRoleAsync("admin")` is `true` but `HasPermissionAsync("Orders.Read")` is also `true` | Both are normalised on purpose: roles ignore case, permission keys are lower-cased |

## How to implement or extend

- `CurrentClientImpl` and `CurrentUserImpl` (in `Client/CurrentUser/CurrentUser.cs`) are scoped services over `HttpContext.User`. A new claim belongs to `ICurrentClient` if it describes the client or the token, to `ICurrentUser` if it describes the person; add a unit test that builds a `ClaimsPrincipal`.
- To change where the data is cached, implement `ICurrentUserStore` and register it before `AddAuth2Client`.
- To read the claims you add on the Server, see `TokenIssuer` (`CreateUserPrincipalAsync`, `ApplyDestinations`) and [authentication-flows.md](authentication-flows.md#what-is-in-the-tokens).
- Tests: the current user and cache rules are in `tests/SystemX.Auth2.Client.Tests` and the end-to-end checks in `tests/SystemX.Auth2.Client.IntegrationTests`.

## Related pages

- [client-authorization.md](client-authorization.md)
- [client-setup.md](client-setup.md)
- [authorization.md](authorization.md)
- [Permission, role and group sync](permission-sync.md)
- [Admin JSON API](admin-api.md)
- [Concepts](concepts.md)
- [Client package reference](client-reference.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
- [Handshake and startup](handshake-and-startup.md)
- [Getting started](getting-started.md)
- [Contracts reference](contracts-reference.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Server settings](server-settings.md)
- [Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)](identity-stores.md)
- [Database tables](database-tables.md)
- [Credential scope](credential-scope.md)
- [Architecture](architecture.md)
- [Minimal API methods and attributes: when to use which](client-endpoint-metadata.md)
- [Enums reference](enums-reference.md)
