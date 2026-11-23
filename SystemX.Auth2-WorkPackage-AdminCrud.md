# SystemX.Auth2 — Work Package: Admin CRUD (API + empty pages) for every table

> Give this file to the implementing LLM **after** `SystemX.Auth2-Build-Specification-v2.md` (and next to the External Providers work package).
> It **extends** sections 5.10 (admin pages) and 5.12 (admin JSON API) of the main spec. If it disagrees with those sections, this file wins for admin CRUD.
> Main-spec rules still apply: code → tests → green → docs; **no HTML/CSS** (PageModel + empty `.cshtml` only); B1–B2 English docs; no secrets in logs.

## Why this file exists
The main spec listed admin pages and some routes, but it did not give a complete **CRUD plan for every table** (for example `user_permissions`, `role_permissions`, `user_groups`, `group_roles`, `group_permissions`, sessions, scopes). This file gives the full matrix, the routes, the DTOs, the code skeletons, the page models, the rules, the tests and the docs.

---

## 1. CRUD matrix (what can be done with each table)

Legend: **L** list, **G** get one, **C** create, **U** update, **D** delete, **S** set (replace a whole link list in one call), **A** add one link, **R** remove one link. "Global" = only GlobalAdmin. "Project" = GlobalAdmin and the ProjectAdmin of that application.

| Table / area | Operations | Who | Delete behavior and notes |
|---|---|---|---|
| `applications` (+ OpenIddict application) | L G C U D | L,G: Global (Project can G its own). C,D: Global. U: Global; Project only editable fields | Delete = disable first (`IsEnabled=false`), hard delete only with `?confirm=<ProjectKey>` and only if no users. Secret rotate and redirect URI edit are separate routes. `_global` can never be deleted |
| `applications.SettingsJson` | G U (by section) | G: Project. U: Global; Project only `ProjectAdminEditable` fields | Section routes (identity, lockout, 2FA, tokens, redirects, client, localization). Row version required. Bump `SettingsVersion` |
| `AspNetUsers` (+ `user_applications`) | L G C U D | Project (only users of the application). Cross-application list: Global | "Delete" = **remove from application** (delete `user_applications` row, revoke sessions). Deactivate/activate is separate. Hard delete or anonymize: Global only, with `?confirm=<userName>` |
| `user_applications` | G U (status), R | Project | Used by the user routes. Update only `Status`; credentials are never editable here (see reset-password and reset-2fa routes) |
| User credentials | special actions | Project | `set-password` (admin sets temporary password, force change), `reset-2fa`, `unlock`, `lock`, `confirm-email`, `confirm-phone`. Never return hashes or keys |
| `AspNetRoles` (`AppRole`) | L G C U D | Project | System roles (`IsSystem`) cannot be renamed or deleted. Delete refuses when users still have the role unless `?force=true` (then removes assignments and bumps access versions) |
| `AspNetUserRoles` (user ↔ role) | L S A R | Project | `S` replaces the user's roles in this application. Same-application check on both sides |
| `permissions` | L G C U D | Project | Delete = **mark obsolete** (`IsObsolete=true`). Hard delete: Global only, only if unused or `?force=true`. Key cannot change after create |
| `user_permissions` (user ↔ permission) | L S A R | Project | Direct grants. Store `GrantedBy` and `GrantedAtUtc` |
| `role_permissions` (role ↔ permission) | L S A R | Project | Bump access version for all users of the role |
| `groups` | L G C U D | Project | Delete removes memberships and mappings (confirm required) |
| `user_groups` (user ↔ group) | L S A R | Project | Members list is paged |
| `group_roles` (group ↔ role) | L S A R | Project | |
| `group_permissions` (group ↔ permission) | L S A R | Project | |
| `AspNetUserClaims`, `AspNetRoleClaims` | L A R (optional) | Global | Not needed by the Client. Provide only if trivial; otherwise document as "not exposed in v1" |
| `AspNetUserLogins` (external logins) | L R | Project | Admin can list and unlink (support case). Creating links happens only through the login flow |
| External provider settings (jsonb) | L G C U D + rotate-secret + test | Project | Defined in the External Providers work package |
| Sessions (OpenIddict authorizations/tokens, per user) | L R (revoke one), R-all (revoke all of a user) | Project | Never show token values. Show: id, client, created, expires, last used, IP if known |
| OpenIddict scopes (custom scopes) | L G C U D | Global | Standard `auth2.*` and OIDC scopes are read-only |
| Allowed scopes per application | G S | Project (only scopes the Global allowed) | Part of application settings |
| `audit_events` | L G (search, filter, export CSV) | L: Project (own application only); all: Global | **Read-only. No update, no delete from the API.** Retention is a background job |
| `user_application_recovery_codes` | none (R-all via reset-2fa) | – | Not listed, not editable |
| `otp_codes`, `data_protection_keys` | none | – | Internal only |
| Server options / key ring | G (read-only), rotate-keys action | Global | Show non-secret values and key ids/expiry only |

## 2. Conventions for all admin routes

### 2.1 Routes
Base: `/api/auth2/admin`. Everything that belongs to one application is under `/applications/{applicationId}/...`. In single-application mode `applicationId` is the fixed application row (the API also accepts the alias `current`).

```
GET    /applications                                  (Global)
POST   /applications                                  (Global)
GET    /applications/{applicationId}
PUT    /applications/{applicationId}
DELETE /applications/{applicationId}?confirm=<ProjectKey>
POST   /applications/{applicationId}/secret/rotate
GET|PUT /applications/{applicationId}/redirect-uris
GET    /applications/{applicationId}/settings
GET|PUT /applications/{applicationId}/settings/{section}

GET    /applications/{applicationId}/users?search=&status=&role=&group=&page=&pageSize=&sort=
POST   /applications/{applicationId}/users
GET    /applications/{applicationId}/users/{userId}
PUT    /applications/{applicationId}/users/{userId}
DELETE /applications/{applicationId}/users/{userId}              (remove from application)
POST   /applications/{applicationId}/users/{userId}/activate | deactivate | lock | unlock
POST   /applications/{applicationId}/users/{userId}/set-password | reset-2fa | confirm-email | confirm-phone
GET|PUT /applications/{applicationId}/users/{userId}/roles          (PUT = set)
POST|DELETE /applications/{applicationId}/users/{userId}/roles/{roleId}
GET|PUT /applications/{applicationId}/users/{userId}/permissions
POST|DELETE /applications/{applicationId}/users/{userId}/permissions/{permissionId}
GET|PUT /applications/{applicationId}/users/{userId}/groups
POST|DELETE /applications/{applicationId}/users/{userId}/groups/{groupId}
GET    /applications/{applicationId}/users/{userId}/effective-access   (computed: roles, permissions with source, groups)
GET|DELETE /applications/{applicationId}/users/{userId}/external-logins[/{provider}]
GET|DELETE /applications/{applicationId}/users/{userId}/sessions[/{sessionId}]

GET|POST /applications/{applicationId}/roles
GET|PUT|DELETE /applications/{applicationId}/roles/{roleId}?force=
GET|PUT /applications/{applicationId}/roles/{roleId}/permissions
POST|DELETE /applications/{applicationId}/roles/{roleId}/permissions/{permissionId}
GET    /applications/{applicationId}/roles/{roleId}/users

GET|POST /applications/{applicationId}/permissions
GET|PUT|DELETE /applications/{applicationId}/permissions/{permissionId}?hard=&force=
POST   /applications/{applicationId}/permissions/{permissionId}/obsolete | restore

GET|POST /applications/{applicationId}/groups
GET|PUT|DELETE /applications/{applicationId}/groups/{groupId}
GET|PUT /applications/{applicationId}/groups/{groupId}/members | roles | permissions
POST|DELETE /applications/{applicationId}/groups/{groupId}/(members|roles|permissions)/{id}

GET    /applications/{applicationId}/audit-events?from=&to=&actor=&action=&target=&page=&pageSize=
GET    /applications/{applicationId}/audit-events/{id}
GET    /applications/{applicationId}/audit-events/export?format=csv

GET    /users?search=...                      (Global, across applications)
GET|POST /scopes ; GET|PUT|DELETE /scopes/{name}            (Global)
GET    /server/options ; POST /server/keys/rotate           (Global)
```

External provider routes are in the External Providers work package and use the same base.

### 2.2 Authorization (in code, not only in UI)
- Policy `AdminOfApplication`: allow when the caller is `GlobalAdmin`, **or** the caller is `ProjectAdmin` and the `app` claim equals `{applicationId}`. Return `404` (not `403`) for another application's id, so ids do not leak.
- Policy `GlobalAdminOnly` for global-only routes.
- A project admin cannot: assign or create `GlobalAdmin` or system roles of `_global`, edit `GlobalOnly` settings, delete the application, grant themselves permissions outside their application.
- The EF **global query filter** stays on as the second layer of protection. Admin handlers get the application from `IApplicationContext`/`IAdminScope`, never from the body.
- Every write creates an audit event with: actor, application, action (`role.created`, `role.permissions.set`, `user.removed_from_application`, ...), target type and id, and a **safe** diff (field names and old/new values for non-secret fields only).
- Every change that affects access (roles, permissions, groups, memberships, user status) bumps `UserAccessVersion` of the affected users (or the application `AccessVersion` for role/group/permission definition changes) and invalidates caches.

### 2.3 Lists, concurrency, errors
- Lists are paged: `page` (from 1), `pageSize` (default 25, max 200), `sort` (`name`, `-createdAt`), `search`, and filters per area. Response is `PagedResult<T>` (`items`, `page`, `pageSize`, `totalCount`).
- Updates need concurrency control. Use PostgreSQL `xmin` as the EF concurrency token (`UseXminAsConcurrencyToken` / `IsRowVersion`). Return `ETag`; require `If-Match` on `PUT`/`DELETE`; wrong version → `412 Precondition Failed` with code `concurrency_conflict`.
- `S` (set) routes are **idempotent**: `PUT` with the full list of ids replaces the links in one transaction and returns what was added and removed.
- Bulk limits: max 500 ids per call.
- Errors: `ApiError { code, message, details? }`, localized `message` (resx), codes such as `not_found`, `validation_failed`, `concurrency_conflict`, `role_in_use`, `system_role_protected`, `permission_in_use`, `confirm_required`, `cross_application_reference`, `cannot_modify_global_admin`.
- Validation: same-application check for every id in a link route (a role from App B can never be linked in App A). Tested.

## 3. DTOs (Contracts: `Contracts/Admin/`)

```csharp
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

// Roles
public sealed record RoleListItem(Guid Id, string Name, string? DisplayName, bool IsSystem, int UserCount, int PermissionCount);
public sealed record RoleDetail(Guid Id, string Name, string? DisplayName, string? Description, bool IsSystem,
    IReadOnlyList<PermissionRef> Permissions, string RowVersion);
public sealed record CreateRoleRequest(string Name, string? DisplayName, string? Description);
public sealed record UpdateRoleRequest(string? DisplayName, string? Description, string RowVersion); // Name of system roles is fixed

// Permissions
public sealed record PermissionRef(Guid Id, string Key);
public sealed record PermissionListItem(Guid Id, string Key, string? DisplayName, string? Group, bool IsObsolete, string Source);
public sealed record CreatePermissionRequest(string Key, string? DisplayName, string? Description, string? Group);
public sealed record UpdatePermissionRequest(string? DisplayName, string? Description, string? Group, string RowVersion); // Key is immutable

// Groups
public sealed record GroupListItem(Guid Id, string Name, string? DisplayName, int MemberCount);
public sealed record CreateGroupRequest(string Name, string? DisplayName, string? Description);
public sealed record UpdateGroupRequest(string? DisplayName, string? Description, string RowVersion);

// Link sets (used by every PUT .../roles|permissions|groups|members)
public sealed record SetLinksRequest(IReadOnlyList<Guid> Ids);
public sealed record SetLinksResponse(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed, int Total);

// Users
public sealed record AdminUserListItem(Guid Id, string? UserName, string? Email, string? PhoneNumber, string Status,
    bool TwoFactorEnabled, bool IsLockedOut, DateTime? LastLoginUtc);
public sealed record AdminUserDetail(Guid Id, string? UserName, string? Email, bool EmailConfirmed, string? PhoneNumber,
    bool PhoneConfirmed, string? FirstName, string? LastName, string Status, bool TwoFactorEnabled, bool IsLockedOut,
    DateTime? LockoutEnd, DateTime JoinedAtUtc, DateTime? LastLoginUtc, string RowVersion);
public sealed record CreateAdminUserRequest(string? UserName, string? Email, string? PhoneNumber, string? FirstName,
    string? LastName, string? TemporaryPassword, bool RequirePasswordChange, bool ConfirmEmail, IReadOnlyList<Guid>? RoleIds);
public sealed record UpdateAdminUserRequest(string? Email, string? PhoneNumber, string? FirstName, string? LastName, string RowVersion);
public sealed record SetPasswordRequest(string TemporaryPassword, bool RequireChange);

// Effective access (read-only)
public sealed record EffectiveAccess(IReadOnlyList<string> Roles, IReadOnlyList<EffectivePermission> Permissions, IReadOnlyList<string> Groups);
public sealed record EffectivePermission(string Key, IReadOnlyList<string> Sources); // "user", "role:Editor", "group:Finance"

// Sessions and audit
public sealed record SessionItem(string Id, string ClientId, DateTime CreatedUtc, DateTime? ExpiresUtc, string? Ip);
public sealed record AuditEventItem(long Id, DateTime OccurredAtUtc, string Action, string? ActorUserName, string? TargetType, string? TargetId, string? Ip, string? DataJson);
```

Rules for DTOs: no password hashes, keys, secrets or raw tokens in any DTO; `TemporaryPassword` is input only and never logged.

## 4. Application layer (interfaces; implement with EF + stores)

```csharp
public interface IAdminRoleService
{
    Task<PagedResult<RoleListItem>> ListAsync(Guid appId, AdminQuery q, CancellationToken ct);
    Task<RoleDetail?> GetAsync(Guid appId, Guid roleId, CancellationToken ct);
    Task<Result<RoleDetail>> CreateAsync(Guid appId, CreateRoleRequest r, CancellationToken ct);
    Task<Result<RoleDetail>> UpdateAsync(Guid appId, Guid roleId, UpdateRoleRequest r, CancellationToken ct);
    Task<Result> DeleteAsync(Guid appId, Guid roleId, bool force, CancellationToken ct);
    Task<Result<SetLinksResponse>> SetPermissionsAsync(Guid appId, Guid roleId, SetLinksRequest r, CancellationToken ct);
    Task<Result> AddPermissionAsync(Guid appId, Guid roleId, Guid permissionId, CancellationToken ct);
    Task<Result> RemovePermissionAsync(Guid appId, Guid roleId, Guid permissionId, CancellationToken ct);
}
// Same shape: IAdminPermissionService, IAdminGroupService (+ members, roles, permissions),
// IAdminUserService (+ roles, permissions, groups, credentials actions, effective access, sessions, external logins),
// IAdminApplicationService (+ settings sections, redirect URIs, secret rotate), IAdminScopeService, IAdminAuditQueryService.

public sealed record AdminQuery(int Page = 1, int PageSize = 25, string? Search = null, string? Sort = null,
    IReadOnlyDictionary<string, string>? Filters = null);
```

Every service method: checks the application scope, validates ids belong to the same application, runs in one transaction, writes the audit event, bumps access versions, and invalidates caches **after commit**.

## 5. Endpoint code skeleton (Minimal API)

```csharp
public static class AdminRoleEndpoints
{
    public static RouteGroupBuilder MapAdminRoles(this RouteGroupBuilder apps)
    {
        var g = apps.MapGroup("/{applicationId:guid}/roles")
                    .RequireAuthorization("AdminOfApplication")
                    .RequireRateLimiting("admin")
                    .WithTags("Admin: Roles");

        g.MapGet("/", async (Guid applicationId, [AsParameters] AdminQuery q, IAdminRoleService s, CancellationToken ct)
            => Results.Ok(await s.ListAsync(applicationId, q, ct)));

        g.MapGet("/{roleId:guid}", async (Guid applicationId, Guid roleId, IAdminRoleService s, HttpResponse res, CancellationToken ct) =>
        {
            var role = await s.GetAsync(applicationId, roleId, ct);
            if (role is null) return Results.NotFound(ApiError.NotFound());
            res.Headers.ETag = $"\"{role.RowVersion}\"";
            return Results.Ok(role);
        });

        g.MapPost("/", async (Guid applicationId, CreateRoleRequest r, IAdminRoleService s, CancellationToken ct) =>
        {
            var result = await s.CreateAsync(applicationId, r, ct);
            return result.Success
                ? Results.Created($"/api/auth2/admin/applications/{applicationId}/roles/{result.Value!.Id}", result.Value)
                : result.ToProblem();                    // maps error codes to 400/404/409/412
        });

        g.MapPut("/{roleId:guid}", async (Guid applicationId, Guid roleId, UpdateRoleRequest r, IAdminRoleService s, CancellationToken ct)
            => (await s.UpdateAsync(applicationId, roleId, r, ct)).ToHttp());

        g.MapDelete("/{roleId:guid}", async (Guid applicationId, Guid roleId, bool? force, IAdminRoleService s, CancellationToken ct)
            => (await s.DeleteAsync(applicationId, roleId, force ?? false, ct)).ToNoContent());

        // Link sets: replace the whole list in one call (idempotent)
        g.MapGet("/{roleId:guid}/permissions", async (Guid applicationId, Guid roleId, IAdminRoleService s, CancellationToken ct)
            => Results.Ok((await s.GetAsync(applicationId, roleId, ct))?.Permissions));
        g.MapPut("/{roleId:guid}/permissions", async (Guid applicationId, Guid roleId, SetLinksRequest r, IAdminRoleService s, CancellationToken ct)
            => (await s.SetPermissionsAsync(applicationId, roleId, r, ct)).ToHttp());
        g.MapPost("/{roleId:guid}/permissions/{permissionId:guid}", async (Guid applicationId, Guid roleId, Guid permissionId, IAdminRoleService s, CancellationToken ct)
            => (await s.AddPermissionAsync(applicationId, roleId, permissionId, ct)).ToNoContent());
        g.MapDelete("/{roleId:guid}/permissions/{permissionId:guid}", async (Guid applicationId, Guid roleId, Guid permissionId, IAdminRoleService s, CancellationToken ct)
            => (await s.RemovePermissionAsync(applicationId, roleId, permissionId, ct)).ToNoContent());

        return apps;
    }
}
```

`If-Match` is read by an endpoint filter (`ConcurrencyFilter`) that copies the value into the request DTO's `RowVersion` when the header is present. Write the same endpoint files for permissions, groups, users, applications, scopes, audit, sessions. Register all groups in `Endpoints/Admin/AdminEndpoints.cs`.

Core of the **set links** implementation (use it for every link table):

```csharp
internal static class LinkSetter
{
    // Replaces the links of one owner with the given ids. Returns what changed.
    public static async Task<SetLinksResponse> SetAsync<TLink>(
        DbContext db, IQueryable<TLink> current, Func<TLink, Guid> targetId,
        Func<Guid, TLink> create, IReadOnlyCollection<Guid> wanted, CancellationToken ct)
        where TLink : class
    {
        var existing = await current.ToListAsync(ct);
        var existingIds = existing.Select(targetId).ToHashSet();
        var toAdd = wanted.Where(id => !existingIds.Contains(id)).ToList();
        var toRemove = existing.Where(l => !wanted.Contains(targetId(l))).ToList();

        db.Set<TLink>().RemoveRange(toRemove);
        db.Set<TLink>().AddRange(toAdd.Select(create));
        await db.SaveChangesAsync(ct);
        return new(toAdd, toRemove.Select(targetId).ToList(), wanted.Count);
    }
}
```

Before calling it, the service must check that **every** id exists in the same application (`cross_application_reference` otherwise).

## 6. Page models (UI later; **no HTML/CSS now**)

For each admin area create the PageModels below with working handlers that call the same services as the API. The `.cshtml` file contains only `@page`, `@model` and `@* UI: to be done *@`.

| Area | Pages (under `Pages/Admin/`) |
|---|---|
| Applications | `Applications/Index`, `Create`, `Edit`, `Details`, `Secret`, `RedirectUris` |
| Settings | `Settings/Edit` (one handler per section: Identity, Lockout, TwoFactor, Tokens, Redirects, Client, Localization, ExternalProviders) |
| Users | `Users/Index`, `Create`, `Details`, `Edit`, `Roles`, `Permissions`, `Groups`, `Sessions`, `ExternalLogins`, `EffectiveAccess` |
| Roles | `Roles/Index`, `Create`, `Edit`, `Details`, `Permissions` (role ↔ permission), `Users` |
| Permissions | `Permissions/Index`, `Create`, `Edit`, `Details` |
| Groups | `Groups/Index`, `Create`, `Edit`, `Details`, `Members`, `Roles`, `Permissions` |
| Audit | `AuditLog/Index`, `AuditLog/Details` |
| Scopes (Global) | `Scopes/Index`, `Create`, `Edit` |
| Server (Global) | `Server/Options`, `Server/Keys` |

Pattern for a list page model:

```csharp
[Authorize(Policy = "AdminOfApplication")]
public class RolesIndexModel(IAdminRoleService roles) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public AdminQueryInput Query { get; set; } = new();
    public PagedResult<RoleListItem> Result { get; private set; } = default!;

    // Flags for the future UI (computed with plain if/else, no HTML here)
    public bool CanCreate => IsGlobalAdmin || IsProjectAdmin;
    public bool CanDeleteSystemRoles => false;

    public async Task OnGetAsync(CancellationToken ct)
        => Result = await roles.ListAsync(ApplicationId, Query.ToQuery(), ct);

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string rowVersion, bool force, CancellationToken ct)
    {
        var r = await roles.DeleteAsync(ApplicationId, id, force, ct);
        if (!r.Success) AddErrors(r);                      // messages from resx keys
        return r.Success ? RedirectToPage() : Page();
    }
}
```

Pattern for an edit page model: `[BindProperty] public InputModel Input`, `OnGetAsync(Guid id)` loads the item and `RowVersion`, `OnPostAsync` validates with data annotations and the service, handles `concurrency_conflict` by reloading and adding a model error. A **link page model** (for example `Roles/Permissions`) exposes `AllItems`, `SelectedIds`, and `OnPostAsync` calls the `SetPermissionsAsync` service.

`AdminPageModel` base class provides: `ApplicationId` (from route or `current`), `IsGlobalAdmin`, `IsProjectAdmin`, `AddErrors(result)`, and the localized messages. Document every page model's properties, handlers, flags and validation rules in `docs/ui-handover.md`.

## 7. Business rules (write a test for each)

1. Role names are unique **inside one application** (same name in two applications is fine).
2. System roles and the `GlobalAdmin` role: not editable or deletable through project admin routes.
3. Permission `Key` is lowercase, pattern `a.b` (letters, digits, `.`, `-`, `_`), max length 100, immutable after create.
4. Deleting a permission through the API marks it obsolete. Obsolete permissions are not granted to new users/roles but existing links stay until removed. `restore` reverses it.
5. Hard delete of a permission is global-only and refused if links exist unless `force=true`; then all links are removed and access versions bump.
6. Removing a user from an application revokes all sessions of that application and removes direct grants and group memberships of that application; the global user and other applications are untouched.
7. A project admin cannot remove or deactivate **the last ProjectAdmin** of their application (`cannot_remove_last_admin`).
8. Setting a temporary password is allowed only in `PerApplication` or `Shared` credential mode for users of that application; it forces a password change at the next login when `RequireChange=true`; it never appears in logs or audit data.
9. `effective-access` shows each permission with its sources (`user`, `role:<name>`, `group:<name>`) so admins can see why access exists.
10. Concurrent edits return `412`, never silently overwrite.
11. Audit events are append-only; there are no update/delete routes or service methods.
12. All list endpoints enforce the page size limit and never return other applications' rows (test with two applications).

## 8. Tests (minimum)

Unit: each service (list filters/sorting, create validation, update with row version, delete rules, link set add/remove/no-change, same-application checks, audit written, access version bumped), `LinkSetter`, query parsing, policy handler (`AdminOfApplication`).

Integration (Testcontainers PostgreSQL, two applications `appA`, `appB`, users: global admin, project admin A, project admin B, normal user):
- CRUD happy path for each area, with real HTTP and tokens.
- Project admin A calling any `applications/{appB}/...` route → `404`; global admin → `200`.
- Project admin cannot create or assign `GlobalAdmin`, cannot edit `GlobalOnly` settings, cannot delete the application.
- Link routes reject ids from another application (`cross_application_reference`) for every link table: `user_roles`, `user_permissions`, `role_permissions`, `user_groups`, `group_roles`, `group_permissions`.
- `PUT` set is idempotent (two calls, same result, second returns no changes).
- Access version bump and cache invalidation: after changing `role_permissions`, `/api/auth2/me` of an affected user shows the new permission after token refresh or version check.
- Row version: stale `PUT` → `412 concurrency_conflict`.
- Pagination limits (`pageSize=1000` is capped), sorting, search.
- Delete rules: role in use, system role, last project admin, permission obsolete vs hard delete, user removal revokes sessions.
- Audit: every write produced an event; events contain no secrets (log-capture test); no route can modify audit events.
- Single-application mode: `current` alias works; routes still work with the fixed application.
- Every PageModel: handler tests with a fake service (list, create, edit, delete, link set) including error display via `ModelState`; a test that every page file has an empty view (only `@page`, `@model`, one comment) so no HTML sneaks in.

## 9. Build steps — each step = code → tests → green → docs

| # | Step | Docs to write after tests pass (How to use **and** How to implement) |
|---|---|---|
| 1 | Conventions: `AdminOfApplication` policy, `AdminQuery`, `PagedResult`, error mapping, concurrency token + `ETag`/`If-Match` filter, audit helper, `LinkSetter` | `admin-api.md` (conventions, auth, errors, paging, concurrency) |
| 2 | Applications + settings sections + redirect URIs + secret rotate | `admin-api.md` (applications), `server-settings.md` (which fields project admins can edit) |
| 3 | Permissions and roles (CRUD, obsolete, role ↔ permission) | `authorization.md`, `admin-api.md` |
| 4 | Groups (CRUD, members, group ↔ role, group ↔ permission) | `authorization.md`, `admin-api.md` |
| 5 | Users (CRUD as membership, credentials actions, user ↔ role/permission/group, effective access, sessions, external logins) | `admin-api.md`, `credential-scope.md` (admin password actions) |
| 6 | Audit query/export, scopes, server options | `admin-api.md`, `security.md` (audit rules) |
| 7 | All PageModels + empty pages | `ui-handover.md` (every page, properties, handlers, flags, validation) |
| 8 | Final pass | `api-reference.md` (every route with request/response/error examples), `error-codes.md`, `feature-status.md`, `troubleshooting.md`, `CHANGELOG.md` |

Docs rules: B1–B2 English; every page has *What this is*, *How to use*, *How to implement / extend*, *Common problems*, *Related pages*; every `curl` example is run against the test host or copied from a test; every documented route, error code and setting must exist in the code (add a check).

## 10. Definition of done
- Every table in the matrix has the listed operations (or an explicit "not exposed in v1" line in the docs).
- All tests in section 8 pass; no cross-application leak; no secret in logs or responses.
- All page models exist with empty views; no HTML/CSS.
- Docs for every step exist in two parts (use and implement) and match the code.
- `ui-handover.md` lets the UI LLM build every screen without reading the code.

## 11. Pitfalls
1. Trusting `applicationId` from the route without the policy → always run `AdminOfApplication` and the query filter.
2. Linking ids from another application → validate every id (tests for all six link tables).
3. Hard-deleting roles or permissions silently removes access → default to obsolete/guarded deletes, bump access versions.
4. Forgetting to bump access versions → users keep old permissions in caches.
5. Returning hashes, keys or tokens in DTOs → DTOs are reviewed by a test that scans property names (`PasswordHash`, `Secret`, `Key` of TOTP, `Token`).
6. Lost updates from two admins → row version everywhere.
7. Audit data with secrets (`TemporaryPassword`, secrets) → whitelist fields in diffs.
8. Project admin removing the last admin → guard it.
