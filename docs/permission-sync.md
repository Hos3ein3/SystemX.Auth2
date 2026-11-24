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
| `POST /api/auth2/groups/sync` | client | `auth2.groups.sync` | Add missing groups (push). Off unless `Clients.GroupSyncAllowed` is on |
| `POST /api/auth2/scopes/sync` | client | `auth2.scopes.sync` | Register custom scopes and allow them for this application (push) |
| `GET /api/auth2/scopes` | client | `auth2.scopes.sync` | List the custom scopes of this application (pull) |

"Client token" means a client-credentials token. The calling **application** is the one of the token, and the catalog is the catalog of its **project**. The body never chooses either. Everything pushed goes to the project catalog and records the source application (`ApplicationId` on the permission, role or group). Sync never deletes: an item that disappears is marked obsolete.

## When to use it

- You want permissions found in your code (attributes, endpoints) to appear in the admin screens without typing them twice.
- You want your application to know which roles and groups exist.

Groups and scopes have their own routes (below). Groups are **opt-in**: admins decide groups, so a client may only add missing groups when the application allows it (`Clients.GroupSyncAllowed`, off by default).

## Rules

1. **Never deletes.** A sync only adds and updates.
2. **Idempotent.** Sending the same list twice changes nothing the second time (the audit log gets no entry).
3. **All or nothing for validation.** One bad key or role name rejects the whole request and nothing is saved.
4. **Admin wins.** Text (display name, description, group) of a permission created by an admin is never overwritten by sync. Permissions made by sync (`Source = Sync`) are updated.
5. **Obsolete comes back.** If a permission is marked obsolete and the client sends it again, it is restored.
6. **Allowed by setting.** `Clients.PermissionSyncAllowed` and `Clients.RoleSyncAllowed` (both on by default). Off gives `403 sync_not_allowed`.
7. **Serialized per project.** Two syncs of one project run one after the other (a database advisory lock), so parallel starts of many instances are safe.

### Permissions

- Keys are trimmed and lower-cased, 1–100 characters, `a-z 0-9 . _ : -`, start and end with a letter or digit. Duplicates in the list are merged.
- At most **2000** items (`too_many_items`).
- Setting `Clients.AllowRemovePermissionsFromSync` (off by default): permissions with `Source = Sync` that are **missing** from the list are marked **obsolete**. They are still not deleted, and admin permissions are never touched.

### Roles

- Role names: 1–100 characters, letters, digits, space, `. _ : -`. `GlobalAdmin` and `ProjectAdmin` are reserved. Invalid → `validation_failed`.
- Roles are matched by name (ignoring case) **inside the project**. A global role with the same name is not touched.
- `permissions` in a role definition is **added**, never removed. An empty list removes nothing. `null` means "do not touch the links".
- A permission named in a role but unknown to the server is created as a sync permission, when `PermissionSyncAllowed` is on. If it is off: `sync_not_allowed`.
- `description` of an existing non-system role is updated when you send a different one. `displayName` is not stored for roles (roles have a name and a description only).
- New links raise the **project access version** (see [authorization.md](authorization.md)), so clients refresh their cache.

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

## Groups (push)

- Needs the setting `Clients.GroupSyncAllowed` (**off by default**, global admin only). Otherwise `403 sync_not_allowed`.
- Body: `{ "groups": [ { "name": "finance", "displayName": "Finance team" } ] }`, or the short form `{ "names": ["finance"] }`.
- Names follow the key rules (lowercase letters, digits, `. _ : -`). One invalid name rejects everything (`validation_failed`). Maximum 2000.
- **Only missing groups are created.** The display name of an existing group is never changed and nothing is deleted. A group made by sync has no members, roles or permissions: admins decide those.
- Audit event: `groups.synced` (only when something was added).

## Custom scopes

A *scope* says what a token may be used for. The server owns `auth2.*` and the standard OpenID Connect scopes (`openid`, `profile`, `email`, `phone`, `roles`, `offline_access`). Your application may have its own, for example `reports.read`.

- `POST /api/auth2/scopes/sync` with `{ "scopes": [ { "name": "reports.read", "displayName": "Read reports" } ] }`.
- The server **registers** the scope when it is new (the registry is shared, the first text stays) and **allows it for the calling application** (adds it to `the allowed scopes of the application`). The client can ask for it at once.
- Reserved names (`auth2.*`, standard scopes) and invalid names give `400 invalid_scope_name`, and nothing is saved. Maximum 500.
- `Clients.ScopeSyncAllowed` (default on). Another application (even of the same project) still has to ask for the same scope itself, because the allowed scopes are a field of each application.
- It never removes a scope. Pull (`GET /api/auth2/scopes`) lists the custom scopes of the calling application with their texts.
- Audit event: `scopes.synced`.

## Catalog sources (client side)

All four catalogs (permissions, roles, scopes, groups) share one set of sources. They are methods of the options you get in `EnablePermissions(o => ...)`, `EnableRoles`, `EnableScopes` and `EnableGroups`. `SyncPolicy` (`None`, `Push`, `Pull`, `Both`) and `ScanningPolicy` (`Manual`, `AssemblyScan`, `Both`) work as before; the explicit sources below count when the scanning policy is `Manual` or `Both`.

| Source | What it does |
|---|---|
| `PermissionList` (and `RoleList`, `ScopeList`, `GroupList`) | A list of full definitions. A shortcut for `Add` |
| `Add(definition)` | One full definition |
| `AddFromClass(typeof(X))` | Reads the `public const string` and `public static readonly string` fields of `X`, nested classes too. The **value** is the key. Display name and description come from `[Auth2Permission]`, `[Auth2Role]`, `[Auth2Scope]` or `[Auth2Group]` on the field; otherwise the display name is made from the field name (`OrdersRead` becomes "Orders read") and the description is empty. The category (the permission group) is the class name; `[Auth2Category("Billing")]` on the class overrides it. You pass the type: nothing scans the whole assembly (trimming friendly) |
| `AddFromEnum<TEnum>(KeyStyle)` / `AddFromEnum(Type)` | The key of a member is `[Auth2Key("x")]` when present, else the name by the key style: `DotCase` (default, `OrdersRead` becomes `orders.read`), `AsIs`, `SnakeCase`, `KebabCase`. Metadata from the attribute on the member; the category is the enum name. A `[Flags]` enum, or two members with the same key, stop the start |
| `AddFromString("a, b;c", ';', "Category")` | Splits by the separator (default `,`), trims, drops empty parts, merges duplicates. Key only: the name is the key, the description is empty. A part that is not a valid key stops the start and the message names it |
| `AddFromListString(list, "Category")` | The same rules, from a list. An element that contains the separator `,` is an error (most likely a joined text) |
| `AddProvider<T>()` | `T` implements `IPermissionProvider`, `IRoleProvider`, `IScopeProvider` or `IGroupProvider`. It is resolved from DI when the host starts, just before the first sync. Async (`GetAsync`) |
| `AddFactory(sp => ...)` / `AddFactory(async (sp, ct) => ...)` | The same with a delegate |
| `AddFromConfiguration(section)` | Each child is a plain value (the key) or a section with `Key`/`Name`, `DisplayName`, `Description`, and `Group` (permissions) or `Permissions` (roles) |

```csharp
public static class OrderPermissions
{
    public const string Read = "orders.read";

    [Auth2Permission(DisplayName = "Write orders", Description = "Create and change orders")]
    public const string Write = "orders.write";
}
public enum ReportScopes { ReportsRead, ReportsWrite }

builder.Services.AddAuth2Client(...)
    .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.AddFromClass(typeof(OrderPermissions)); })
    .EnableScopes(o => { o.SyncPolicy = ScopeSyncPolicy.Push; o.AddFromEnum<ReportScopes>(); })
    .EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.AddFromString("Editor, Viewer"); o.AddProvider<RolesFromDatabase>(); });
```

The same constants work in `[Auth2Permission(OrderPermissions.Read)]` and `.WithPermission(OrderPermissions.Read)`.

**Merging.** Definitions from all sources are merged by key (ordinal, case-sensitive). The same key with the same data is ignored. The same key with different data stops the start, and the message names both sources, unless `DuplicatePolicy = FirstWins` (then the first source stays).

**Validation.** Keys are checked when a source is added (static sources) or when the host starts (providers and factories). A wrong key names its source, class, field or part. Scope keys must not contain a space, a double quote or a backslash (the OAuth scope rules) and are kept apart from permission keys. A provider that throws gives a startup error that names the catalog and shows the reason.

**Not through string or enum sources:** a role that carries permission keys. Use a full definition (`Add`), the attribute (`[Auth2Role(Permissions = ["orders.read"])]` on a field), a provider or configuration.

## The client side

The Client package does this for you: `EnablePermissions(...)`, `EnableRoles(...)`, `EnableGroups(...)`. It scans your endpoints and attributes, pushes after the application started and after a good handshake, skips unchanged lists, and retries failures. See [client-setup.md](client-setup.md) ("Permission, role and group sync").

## Common problems

| Problem | Cause |
|---|---|
| `403 sync_not_allowed` | The setting is off for this project |
| `403 insufficient_scope` | The client token lacks the scope. Add it to the allowed scopes of the application and request it |
| `400 invalid_scope_name` | A scope name is invalid or reserved (`auth2.*`, `openid`, ...) |
| `400 invalid_permission_key` | A key has a space, a slash, or is longer than 100 characters. Nothing was saved |
| `400 validation_failed` on roles | A reserved or invalid role name |
| `400 too_many_items` | More than 2000 items |
| A removed permission stays on the server | By design. Mark it obsolete or delete it in the admin API, or turn on `AllowRemovePermissionsFromSync` |

## Related pages

- [authorization.md](authorization.md)
- [handshake-and-startup.md](handshake-and-startup.md)
- [server-settings.md](server-settings.md)
- [api-reference.md](api-reference.md)
