# Scope, permission, role, group: which is which

## What this is

SystemX.Auth2 has several words that all sound like "what may be done": **scope**, **permission**, **role**, **group**, **direct grant**, **token kind**, **membership**, **admin role**. They answer different questions and belong to different things. This page puts them side by side, so you pick the right one when you protect an endpoint, design a catalog or read an error.

The short version:

> A **scope** is what a *client application* may do with a token. A **permission** is what a *person* may do. A **role** and a **group** are two ways to hand permissions to people. Everything else (token kind, membership, admin roles) is a fact about the token or the person that you can also check.

## When to use it

- You are about to write `[Auth2Scope(...)]` or `.WithPermission(...)` and are not sure which one is right.
- You see `reports.read` as a permission **and** a scope in a sample and wonder why.
- You design the catalog of a new project (which permissions, which roles, which groups).
- A person "has the role but gets 403", or a client "has the permission but gets 403".

## The table

| | Belongs to | Limits | Who decides | Where it is defined | Is it in the token? | Read in code with |
|---|---|---|---|---|---|---|
| **Scope** | The **application** (OpenIddict client) | What a token may be **used** for | The developer, in the application's allowed scopes; the app asks for some of them when it signs in | `allowed scopes` of the application; custom scopes are pushed by `EnableScopes` ([permission-sync.md](permission-sync.md#custom-scopes)) | Yes: claim `scope` | `ICurrentClient.Scopes`, `HasScope` |
| **Permission** | The **project** (given to a person) | What a person may **do** | An admin (or the registration defaults) | Catalog of the project: pushed by `EnablePermissions` or typed in the admin console | **No** (too large). Only the version `pv` | `ICurrentUser.HasPermissionAsync`, `GetPermissionsAsync` |
| **Role** | The **project** (or global) | A named bundle of permissions | An admin | Catalog; pushed by `EnableRoles` | Yes: claim `role` (when the `roles` scope was granted) | `ICurrentUser.HasRoleAsync`, `GetRolesAsync` |
| **Group** | The **project** | A set of people that can carry roles and permissions | An admin (groups are **opt-in** to sync) | Catalog; pushed by `EnableGroups` only when allowed | No | `ICurrentUser.GetGroupsAsync` |
| **Direct grant** | One **person** in a project | One permission given to one person | An admin | The person's own permission list | No | The same as a permission (`WithOnlyUserPermission` can demand it) |
| **Token kind** | The **token** | User (a person) or machine (a program, client credentials) | The grant that made the token | Not stored | Yes: claim `machine` | `ICurrentClient.IsMachine` |
| **Membership** | The **person** in a project | Whether the person may sign in to the project at all | An admin, the join flow, the registration | `user_projects` ([database-tables.md](database-tables.md#user_projects)) | Indirectly (the token is for that project) | The sign-in itself |
| **Admin role** | The **person** | Who may use the admin API (`GlobalAdmin`, `ProjectAdmin`) | A global admin | Two reserved roles ([admin-api.md](admin-api.md)) | Yes, as roles | Server only |

### The same row, in words

- **Scope.** "This token may read reports." It says nothing about who the person is. A machine token can have `reports.read` and no person at all. A scope is part of the **application's** configuration: the same person signing in through two applications gets two different scope sets.
- **Permission.** "Sara may write orders." It says nothing about which application asked. The permission follows the person into every application of the project.
- **Role.** "Sara is an Editor", and `Editor` carries `orders.read` and `orders.write`. Handy when many people need the same permissions.
- **Group.** "Sara is in `finance`", and `finance` carries roles and permissions. Handy when people are organised by team.
- **Direct grant.** "Sara also gets `reports.read`, nobody else does." The exception to the role.

## How they combine in one request

```
 token arrives
    |
    +-- valid? (signature, issuer, audience, lifetime)  -> else 401
    |
    +-- token kind ok?  (machine / user)                -> else 403
    |
    +-- scope ok?       (claim `scope`, no server call) -> else 403     <- the CLIENT may
    |
    +-- role ok?        (claim `role`, no server call)  -> else 403     <- the PERSON is
    |
    +-- permission / group ok? (access data, cached)    -> else 403     <- the PERSON may
    |
 endpoint runs
```

Every rule on an endpoint must pass (**chaining is AND**). To say "a role **or** a permission" in one rule use the any-of family (`WithRoleOrPermission`, `WithAnyOf`; [client-endpoint-metadata.md](client-endpoint-metadata.md#any-of-rules-a-role-or-a-permission)). Inside one rule, several values mean **any of them** (`WithPermission("a", "b")` needs `a` or `b`). The order above is only a picture: they are separate checks, all required.

A user token that went through the hosted sign-in carries both worlds: the scopes of the application and the person. A machine token carries only the first: it has scopes, a client id and a project, and **no** person, so every permission, role and group check fails for it (the lists are empty). That is the right behaviour: a program is limited by its scopes only.

## Pick the right one

| I want to say | Use | Example |
|---|---|---|
| "Only programs may call this" | Token kind | `.WithMachineOnly()` |
| "Only people may call this" | Token kind | `.WithUserOnly()` |
| "This application may use this API at all" | Scope | `.WithScope("reports.read")` |
| "This person may do this thing" | Permission | `.WithPermission("orders.write")` |
| "Editors may do all the editing things" | Role (a bundle of permissions) | give `Editor` the permissions; protect with the permissions |
| "The finance team may see this page" | Group | `.WithGroup("finance")`, or a role the group carries |
| "Only Sara, not the whole role" | Direct grant | grant it, then `.WithPermission("x").WithOnlyUserPermission()` |
| "Admin screens" | Role | `.WithRole("Admin")` |
| "Sign-in by password plus a second factor" | The `amr` claim | `ICurrentClient.Amr.Contains("otp")` |

**Rule of thumb.** Protect the **thing** with a **permission** (`orders.write`), not with a role (`Editor`). Roles and groups then only decide who gets the permission, and you can change them in the admin console without a new release. Use a role in code only for coarse switches like `Admin`.

**Do not reuse a name.** The MinimalApi sample has a permission `reports.read` and also a scope `reports.read`. They are different things that happen to share a string: the scope is in the token of the *application*, the permission is in the access data of the *person*. When both exist, give them different names (`reports.api` for the scope, `reports.read` for the permission) so a log line or an error is not ambiguous.

## Scope in depth

The full guide is [scopes.md](scopes.md); here is the short version.

- A scope is a word that the application asks for at sign-in (`scope=openid profile reports.read`). The scopes must be in the application's allowed scopes; asking for one that is not allowed is refused ([authentication-flows.md](authentication-flows.md#step-by-step-client-credentials-m2m)).
- The server owns `auth2.*` (for example `auth2.me`, which the `/me` and password-change routes need) and the standard OpenID Connect scopes (`openid`, `profile`, `email`, `phone`, `roles`, `offline_access`). Your own scopes are free-form lowercase words (`reports.read`); the reserved ones are skipped by the sync.
- Standard scopes control **claims**: `profile` gives the name claims, `email` the email, `phone` the number, `roles` the `role` claim. No `profile` scope means `ICurrentUser.UserName` is `null`.
- A scope can also list **resources** (the APIs it is meant for). They become audiences (`aud`) of the tokens that have the scope, so an API can accept only tokens made for it: [scopes.md](scopes.md).
- Scopes belong to the application, so registration cannot "give a person a scope". That is why `RegistrationSettings` has `AssignedRoles`, `AssignedGroups` and `AssignedPermissions`, and no scopes ([server-settings.md](server-settings.md)).

## Permission in depth

- The key is a lowercase word with dots: `orders.read`. It is the same string in the attribute, the endpoint and the catalog ([client-authorization.md](client-authorization.md#the-same-constants-everywhere)).
- **Effective permissions** = direct grants + permissions of the person's roles + permissions of the roles of the person's groups + permissions of the groups. Duplicates are removed ([authorization.md](authorization.md#how-access-is-calculated)).
- Permissions are **not** in the token. The client asks the server (`GET /api/auth2/me`) and caches the answer by the access version `pv`. That is why a permission check is "cached, one call per cache time" and a scope check is free.
- A permission can come from a **role** (or a group's role) or be a **direct grant**. `WithOnlyRolePermission()` and `WithOnlyUserPermission()` demand one of the two sources.

## Role in depth

- A role is a name with a list of permissions. Roles are per project; two projects can both have `Editor` with different contents. Global roles (no project) only count in the `_global` project and in single-project mode.
- The `role` claim is in the token when the application was granted the `roles` scope. Without it the token has no roles, and `[Auth2Role]` reads the roles from the access data instead.
- **The role in a token is a snapshot.** If an admin gives or takes a role, an old token keeps its old `role` claim until it is renewed (a new sign-in or a refresh). `ICurrentUser.HasRoleAsync` reads the access data, which follows the project's access version, so it can be newer than the claim. For a check that must be current, protect with a **permission**.
- `GlobalAdmin` and `ProjectAdmin` are reserved roles for the admin API. Do not use them for your own screens.

## Group in depth

- A group is a set of people. A group can carry roles and permissions, so "everyone in finance gets `reports.read`" is one change on the group.
- Groups are decided by admins. A client may only add **missing** groups by sync and only when `Clients.GroupSyncAllowed` is on ([permission-sync.md](permission-sync.md#groups-push)).
- A group name is a lowercase key like a permission (`finance`, `team-a`). It is not a role: `.WithGroup("finance")` checks membership, not what the group may do. Prefer protecting with the permission the group carries.

## Token kind and the client

`ICurrentClient` describes the **token and the application**; `ICurrentUser` describes the **person** ([client-current-user.md](client-current-user.md)).

| Question | Ask |
|---|---|
| Which application got this token, and which project is it for? | `ICurrentClient.ClientId`, `ProjectKey` |
| May this token do X? | `ICurrentClient.HasScope("x")` |
| Is it a program? | `ICurrentClient.IsMachine` |
| How did the person sign in? | `ICurrentClient.Amr`, `Idp` |
| Who is the person? | `ICurrentUser.UserId`, `UserName`, `Email` |
| May the person do X? | `await ICurrentUser.HasPermissionAsync("x")` |

## Registration and defaults

New people can start with access. `RegistrationSettings.AssignedRoles`, `AssignedGroups` and `AssignedPermissions` are given when someone registers or joins ([server-settings.md](server-settings.md), [authentication-flows.md](authentication-flows.md#register)). They all go to the person **in that project**. Scopes are not part of it, because they are not a property of the person.

## Examples

All of these come from `samples/SystemX.Auth2.Samples.MinimalApi/Program.cs`:

```csharp
app.MapGet("/api/orders", ...).WithPermission("orders.read");               // a person who may read orders
app.MapGet("/api/admin", ...).WithRole("Admin");                            // a coarse switch
app.MapGet("/api/finance", ...).WithGroup("finance");                       // a team
app.MapGet("/api/reports", ...).WithScope("reports.read");                  // an application that may use the reports API
app.MapGet("/api/machine-only", ...).WithMachineOnly();                     // programs only
```

And all together on one endpoint (every rule must pass):

```csharp
app.MapGet("/api/reports/revenue", Revenue)
   .WithUserOnly()                       // a person, not a program
   .WithScope("reports.read")            // through an application that may use the reports API
   .WithPermission("reports.read")       // and a person who may read reports
   .WithAuth2Audit("reports.revenue");   // and write an audit event
```

## Common problems

| Problem | Cause |
|---|---|
| The person has the permission, the call is `403` | The **token** lacks the scope. Check `ICurrentClient.Scopes`, and the application's allowed scopes |
| The token has the scope, the call is `403` | The **person** lacks the permission, or the token is a machine token (no person) |
| A new role does not work yet | The `role` claim is a snapshot: renew the token. Or protect with the permission the role carries |
| A custom scope is not in the token | The application did not ask for it, or does not allow it. Syncing a scope allows it only for the **calling** application ([permission-sync.md](permission-sync.md#custom-scopes)) |
| `UserName` or `Email` is `null` | The `profile` / `email` scope was not granted |

## How to implement or extend

- Your own rule that is none of these (a time window, an owner check): write a normal ASP.NET Core policy and use `.WithAuth2Policy("name")` or `[Authorize(Policy = "name")]`; it combines with the Auth2 rules ([client-endpoint-metadata.md](client-endpoint-metadata.md)).
- Your own check in code: inject `ICurrentUser` and `ICurrentClient` ([client-current-user.md](client-current-user.md)).

## Related pages

- [Authorization on the Server (how access is calculated)](authorization.md)
- [Concepts](concepts.md)
- [Enums reference](enums-reference.md)
- [Documentation index](index.md)
- [The MVC sample: one action for every kind of rule](sample-mvc.md)
- [Seeding (first admin)](seeding.md)
- [Client setup](client-setup.md)
- [Getting started](getting-started.md)
