# Current user

## What this is

`ICurrentUser` tells your code who is calling and what they may do. Name, email, scopes and "is it a program?" come from the token. Roles, permissions and groups are **async**, because they may need a call to the server.

## When to use it

In endpoints and services: "show the user's name", "does this user have `orders.write`?", "which groups is this user in?".

```csharp
app.MapGet("/me", async (ICurrentUser user) => new
{
    user.UserName,
    user.Email,
    Roles = await user.GetRolesAsync(),
    CanWrite = await user.HasPermissionAsync("orders.write"),
}).WithAuth2();
```

## The interface

| Member | Source |
|---|---|
| `IsAuthenticated`, `UserId`, `UserName`, `Email`, `PhoneNumber`, `FirstName`, `LastName`, `FullName`, `Scopes`, `IsMachine`, `IdentityProvider`, `AuthenticationMethods` | The token (no call) |
| `GetRolesAsync`, `GetPermissionsAsync`, `GetGroupsAsync`, `HasPermissionAsync`, `HasRoleAsync` | Access data (cache, else the server) |

`IdentityProvider` is the name of the external provider the user signed in with (claim `idp`, for example `google` or `corp-saml`), or null after a local sign-in. `AuthenticationMethods` is the list of `amr` values: `pwd`, `otp`, `fed` (an external provider), `swk` or `hwk` (a passkey) and `mfa` (several factors). Use them for example to ask for a stronger sign-in before a sensitive action:

```csharp
if (!user.AuthenticationMethods.Contains("mfa")) return Results.Forbid();
```

A **machine token** (client credentials) has no person: `IsMachine` is true, `UserId` is null, the lists are empty, and the server is never asked. Nothing throws.

## Where access data comes from

`EnableCurrentUser(o => { o.Source = ...; o.CacheTtl = ...; })`

| `Source` | Behaviour |
|---|---|
| `TokenAndServer` (default) | Roles, permissions and groups come from `GET /api/auth2/me`, cached. |
| `TokenOnly` | Roles come from the token. Permissions and groups are empty (tokens do not carry them). Never calls the server. Permission checks then always fail. |

### The cache

- Key: client id + user id. The default store is in memory (`MemoryCurrentUserStore`). For several instances use Redis:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "redis:6379");
builder.Services.AddAuth2Client(...).UseDistributedCurrentUserStore();
```

- **When is it read again?**
  1. when the cached version is older than the `pv` claim of the token (the token was issued after a change on the server), or
  2. when `CacheTtl` (default 10 minutes) is over.
- **Within one request** the answer is kept: at most one call per request.
- If the server cannot be asked and an old answer exists, the old answer is used (a warning is logged). With nothing cached the call throws `Auth2Exception` (IsTransient), and the authorization checks answer 403.

What this means in practice: when an admin gives a user a permission, the user's **current token keeps the old answer** until the cache time ends. A **new token** (login or refresh) has a newer `pv`, so the data is read again at once. Lower `CacheTtl` if you need faster changes.

The server raises the access version for every change that matters, see [authorization.md](authorization.md).

## Cookie sessions

With the hosted UI or the Self UI the user is in a cookie, not a token. `ICurrentUser` works the same way. The access token is kept in the cookie (`SaveTokens`) and used to call `/me`. The `AccessMode` of the cookie options decides when the data is loaded; see [client-setup.md](client-setup.md). Roles checks (`WithRole`) use role claims when there are some; a cookie without role claims (`Lazy` mode) gets its roles from the access data.

## Fetch by hand

`IAuth2CurrentUserService.FetchAsync(principal)` calls `/me` and **does not cache**. Use it after login if you want to keep the result somewhere yourself (session, Redis, ...). It uses the token of the current request (`Authorization` header, or the token saved by cookie login). A machine token gives the failure `forbidden`.

## Common problems

| Problem | Fix |
|---|---|
| `GetPermissionsAsync` throws `Auth2Exception` | The server cannot be reached and nothing is cached. Check `ServerUrl`, the handshake status and the log |
| Permissions never change | `CacheTtl` is long and the token is old. Renew the token or lower the TTL |
| Different instances show different data | Use `UseDistributedCurrentUserStore()` with Redis |
| `UserName` is null | The token has no `preferred_username`: ask for the `profile` scope |

## Related pages

- [client-authorization.md](client-authorization.md)
- [client-setup.md](client-setup.md)
- [authorization.md](authorization.md)
