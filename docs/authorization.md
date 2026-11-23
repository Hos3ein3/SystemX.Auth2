# Authorization: permissions, roles and groups

## What this is

How the Server decides **what a user may do** in one application. There are four building blocks:

| Block | Meaning | Example |
|---|---|---|
| Permission | One thing a user may do. A lowercase key | `orders.read` |
| Role | A named set of permissions | `Editor` |
| Group | A set of users. A group can have roles and permissions | `finance` |
| Direct grant | A permission given to one user | sara → `orders.read` |

All of them belong to **one application**. The role `Editor` in the blog application and the role `Editor` in the book application are two different roles with their own permissions.

## When to use it

- You build an admin screen or script that changes access (use `IAccessService` and `IGroupService`).
- Your client application needs to know what the signed-in user may do (call `GET /api/auth2/me`).

## How access is calculated

**Effective permissions** = direct grants **+** permissions of the user's roles **+** permissions of the roles of the user's groups **+** permissions of the user's groups. Duplicates are removed.

**Effective roles** = the user's own roles **+** the roles of the user's groups.

Only data of the current application counts. In multi-application mode a person who is not a member of the application has **no** access there. Global roles (roles with no application) count only in the `_global` project and in single-application mode.

A permission marked **obsolete** is still effective. Obsolete only tells admins "no code uses this key any more".

## Permission keys and group names

- 1 to 100 characters: `a-z`, `0-9`, `.`, `_`, `:`, `-`.
- Must start and end with a letter or digit.
- Input is trimmed and lower-cased (`" Orders.Read "` becomes `orders.read`).
- Not valid: `has space`, `.dot`, `end-`, `a/b`. Error code: `invalid_permission_key` (for groups: `validation_failed`).

## The access version and the cache

Every change that can alter access raises a counter. The version you see is:

`accessVersion = application version + user version in that application`

Both numbers only grow, so a version number belongs to exactly one state.

| Change | Raises |
|---|---|
| Grant or revoke a direct permission; assign or remove a role; add or remove a user from a group | the **user** version |
| Add or remove a permission of a role or group; add or remove a role of a group; delete a role, group or permission | the **application** version |
| Password reset or change, two-factor change (from phase 6) | the **user** version |

A change that does nothing (granting what is already granted) does **not** raise the version.

Effective access is cached in memory by `(application, user, accessVersion)`. The version is read from the database on every call (one small query), so a change made on another server instance is seen at once. Old cache entries are never read again and expire after 10 minutes.

In single-application mode there are no per-user version rows, so every change raises the application version.

## The token

The access token carries the **effective roles** (claim `role`) and the access version (claim `pv`). It does **not** carry the permission list, because that makes tokens large. A client keeps the permissions in a cache keyed by `pv` and calls `/me` when `pv` changes.

## `GET /api/auth2/me`

Needs a **user token** with scope `auth2.me`. The user comes from the token only. The route does not accept a user id.

```http
GET /api/auth2/me
Authorization: Bearer <user token>
```

```json
{ "userId": "0198...", "userName": "sara", "email": "sara@example.com", "phoneNumber": null,
  "firstName": "Sara", "lastName": "K", "fullName": "Sara K",
  "roles": ["Editor"], "permissions": ["orders.read"], "groups": ["finance"],
  "scopes": ["auth2.me", "openid"], "accessVersion": 7 }
```

| Status | Code | When |
|---|---|---|
| 401 | `invalid_token` | No token, a bad token, or the user is no longer valid |
| 403 | `forbidden` | A machine (client credentials) token |
| 403 | `insufficient_scope` | The token has no `auth2.me` scope |

Because the answer is calculated now, a role change shows up in `/me` at once, even for a token issued earlier. The `role` claim inside an old token stays as it was until the token is renewed.

## Example (C#, server side)

```csharp
// Inside a scope where the current application is known (a request with a token, or a scoped job).
await access.CreatePermissionAsync(new PermissionDefinition("orders.read"));
await access.CreateRoleAsync("Editor");
await access.AddRolePermissionAsync("Editor", "orders.read");
await access.AssignRoleAsync(user, "Editor");

await groups.CreateAsync("finance");
await groups.AddPermissionAsync("finance", "billing.pay");
await groups.AddUserAsync("finance", user);

var effective = await access.GetEffectiveAccessAsync(user);   // roles, permissions, groups, version
```

Load the `user` with the `UserManager` of the **same scope**. Passing a user loaded in another scope can fail because Entity Framework tracks one instance per key.

## Audit

Every real change writes an audit event: `permission.created`, `permission.obsolete`, `permission.restored`, `permission.deleted`, `permission.granted`, `permission.revoked`, `role.created`, `role.deleted`, `role.assigned`, `role.removed`, `role.permission_added`, `role.permission_removed`, `group.created`, `group.deleted`, `group.user_added`, `group.user_removed`, `group.role_added`, `group.role_removed`, `group.permission_added`, `group.permission_removed`. Events hold ids and keys, never secrets.

## Common problems

| Problem | Cause and fix |
|---|---|
| `not_found` when granting | The permission, role or group does not exist in **this** application, or the user is not a member of it |
| A new permission does not show in `/me` | You created it but did not grant it. Creating a permission changes nobody's access |
| `/me` shows old data | It does not. Check you call the right application: the application is the audience of the token |
| `forbidden` on `DeleteRoleAsync` | The role is a system role (`IsSystem`) |
| `conflict` creating a group | A group with that name exists in this application |

## Related pages

- [concepts.md](concepts.md)
- [credential-scope.md](credential-scope.md)
- [api-reference.md](api-reference.md)
- [authentication-flows.md](authentication-flows.md)
- [decisions.md](decisions.md) (ADR-052 to ADR-057)
