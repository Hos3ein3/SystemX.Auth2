# Permission, role and group sync

## What this is

How a client application tells the Server which permissions and roles it uses, and how it reads the catalogs back.
The Server side is described here. The Client package (phases 11–13) calls these routes for you.

| Route | Token | Scope | Does |
|---|---|---|---|
| `POST /api/auth2/permissions/sync` | client | `auth2.permissions.sync` | Add or update permissions (push) |
| `POST /api/auth2/roles/sync` | client | `auth2.roles.sync` | Add or update roles and their permissions (push) |
| `GET /api/auth2/permissions` | client | `auth2.permissions.sync` | List permissions (pull) |
| `GET /api/auth2/roles` | client | `auth2.roles.sync` | List roles with permission keys (pull) |
| `GET /api/auth2/groups` | client | `auth2.groups.read` | List groups with roles and permission keys (pull) |

"Client token" means a client-credentials token. The application is always the one of the token. The body never chooses it.

## When to use it

- You want permissions found in your code (attributes, endpoints) to appear in the admin screens without typing them twice.
- You want your application to know which roles and groups exist.

Groups are **not** pushed. Admins decide groups. Clients can only read them (`GroupSyncPolicy` is `None` or `Pull`).

## Rules

1. **Never deletes.** A sync only adds and updates.
2. **Idempotent.** Sending the same list twice changes nothing the second time (the audit log gets no entry).
3. **All or nothing for validation.** One bad key or role name rejects the whole request and nothing is saved.
4. **Admin wins.** Text (display name, description, group) of a permission created by an admin is never overwritten by sync. Permissions made by sync (`Source = Sync`) are updated.
5. **Obsolete comes back.** If a permission is marked obsolete and the client sends it again, it is restored.
6. **Allowed by setting.** `Clients.PermissionSyncAllowed` and `Clients.RoleSyncAllowed` (both on by default). Off gives `403 sync_not_allowed`.
7. **Serialized per application.** Two syncs of one application run one after the other (a database advisory lock), so parallel starts of many instances are safe.

### Permissions

- Keys are trimmed and lower-cased, 1–100 characters, `a-z 0-9 . _ : -`, start and end with a letter or digit. Duplicates in the list are merged.
- At most **2000** items (`too_many_items`).
- Setting `Clients.AllowRemovePermissionsFromSync` (off by default): permissions with `Source = Sync` that are **missing** from the list are marked **obsolete**. They are still not deleted, and admin permissions are never touched.

### Roles

- Role names: 1–100 characters, letters, digits, space, `. _ : -`. `GlobalAdmin` and `ProjectAdmin` are reserved. Invalid → `validation_failed`.
- Roles are matched by name (ignoring case) **inside the application**. A global role with the same name is not touched.
- `permissions` in a role definition is **added**, never removed. An empty list removes nothing. `null` means "do not touch the links".
- A permission named in a role but unknown to the server is created as a sync permission, when `PermissionSyncAllowed` is on. If it is off: `sync_not_allowed`.
- `description` of an existing non-system role is updated when you send a different one. `displayName` is not stored for roles (roles have a name and a description only).
- New links raise the **application access version** (see [authorization.md](authorization.md)), so clients refresh their cache.

## Example

```http
POST /api/auth2/permissions/sync
Authorization: Bearer <client token with auth2.permissions.sync>

{ "permissions": [ { "key": "orders.read", "displayName": "Read orders", "group": "Orders" },
                   { "key": "orders.write" } ] }
```

```json
{ "added": 2, "updated": 0, "unchanged": 0, "addedKeys": ["orders.read", "orders.write"] }
```

```http
POST /api/auth2/roles/sync

{ "roles": [ { "name": "Editor", "description": "Edits orders", "permissions": ["orders.read", "orders.write"] } ] }
```

For roles, `addedKeys` holds the names of the roles that were created.

## The client side

The Client package does this for you: `EnablePermissions(...)`, `EnableRoles(...)`, `EnableGroups(...)`. It scans your endpoints and attributes, pushes after the application started and after a good handshake, skips unchanged lists, and retries failures. See [client-setup.md](client-setup.md) ("Permission, role and group sync").

## Common problems

| Problem | Cause |
|---|---|
| `403 sync_not_allowed` | The setting is off for this application |
| `403 insufficient_scope` | The client token lacks the scope. Add it to the client's `AllowedScopes` and request it |
| `400 invalid_permission_key` | A key has a space, a slash, or is longer than 100 characters. Nothing was saved |
| `400 validation_failed` on roles | A reserved or invalid role name |
| `400 too_many_items` | More than 2000 items |
| A removed permission stays on the server | By design. Mark it obsolete or delete it in the admin API (phase 9), or turn on `AllowRemovePermissionsFromSync` |

## Related pages

- [authorization.md](authorization.md)
- [handshake-and-startup.md](handshake-and-startup.md)
- [server-settings.md](server-settings.md)
- [api-reference.md](api-reference.md)
