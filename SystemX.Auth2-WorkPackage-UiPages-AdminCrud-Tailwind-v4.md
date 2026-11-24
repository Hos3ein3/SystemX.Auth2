# SystemX.Auth2 - Work Package: Server UI Pages and Admin CRUD (in depth, Tailwind CSS)

Status: written to replace the UI pages, admin CRUD, branding and audit parts of `UiPages-Branding-AdminCrud-v3`. Nothing here has been compiled or run. The code and markup sketches show intended shape only; verify every one by building it.
How to use with an LLM: give the main specification first, then this file. This file overrides the main specification and earlier work packages where they differ. Branding rules (tokens, uploads, SVG, email templates) from v3 section 5 still apply and are summarised in section 13.

## 1. Terms and fixed rules
- **Project**: the tenant (example: PaperTrade). Owns users, credentials, roles, permissions, groups, providers, settings, branding, sessions and audit.
- **Application**: a client of a project (papertrade-api, papertrade-mvc). It is the extended OpenIddict application table.
- A user with a `user_projects` row can use every application of that project. No per-application access list.
- Every project data row has `ProjectId`. Audit rows and sessions also keep an optional `ApplicationId`.
- The server has ONE page per purpose (one `Login.cshtml`, one `Register.cshtml`, and so on). Title, logo, colors, texts, input fields and login methods come from the effective settings of the project and application (section 6). Pages are never copied per project.
- Roles: `GlobalAdmin` (role `global-admin` in `_global`) and `ProjectAdmin` (role `project-admin` in the project of the route). A ProjectAdmin of project A gets 404, not 403, for anything of project B. No ApplicationAdmin in v1.
- A disabled project blocks every login and every admin call of its own admins (GlobalAdmin can still manage it).

## 2. Working method (mandatory)
1. Change the code. 2. Add tests. 3. Build and run ALL tests (including the Tailwind build). 4. Fix the code until everything passes. 5. Only then write the docs ("How to use" and "How to implement/extend").
Never claim a build or test result that you did not run. Record decisions in `docs/decisions.md`. Implement in the phases of section 17.

## 3. Tailwind CSS setup
Use Tailwind CSS v4 with the CLI. Verify each step by running it.
- Install in the Server project folder: `npm install tailwindcss @tailwindcss/cli`.
- Input file `Styles/site.css`:
```css
@import "tailwindcss";
@source "../Pages";
@source "../Admin";
@source "../Components";
```
- Build: `npx @tailwindcss/cli -i ./Styles/site.css -o ./wwwroot/css/site.css --minify` (add `--watch` for development).
- v4 detects class names automatically in source files. Use `@source` to add folders that it must scan (Razor `.cshtml` and tag helper `.cs` files), because Razor files may not be detected by default.
- Run the build in the MSBuild pipeline (a target before `Build`/`Publish` that calls the npm script) and in a Node build stage of the Dockerfile. The generated `wwwroot/css/site.css` is NOT committed.
- No Tailwind CDN script in any environment. The CSP forbids it and it is not for production.
- The admin area and the public pages may use two output files (`admin.css`, `public.css`) to keep the public pages light.

Theme and branding bridge:
- Branding writes runtime CSS variables (`--a2-color-primary`, `--a2-color-primary-text`, `--a2-color-background`, `--a2-color-surface`, `--a2-color-text`, `--a2-color-muted`, `--a2-color-error`, `--a2-color-success`, `--a2-radius`, `--a2-font-family`) in a `<style nonce>` block, per request, from the effective branding.
- Components use these variables through arbitrary values, for example `bg-[var(--a2-color-primary)] text-[var(--a2-color-primary-text)]`, or through `@theme` entries that point to the variables. Spike S1 decides which way works (record it): check that a color change at runtime changes the rendered page without a CSS rebuild.
- Wrap repeated class strings in tag helpers or partials (`a2-button`, `a2-input`, ...), not in `@apply` (it behaves differently in v4 and needs an explicit reference to the theme; avoid it).
- The admin console uses the default Server theme, not a project's branding, except for a small project name and logo in the header.

Rules for all markup:
- Mobile first. Tables scroll horizontally in a container on small screens.
- Visible focus ring on every interactive element. Minimum target size for buttons and links.
- Every input has a `<label>`. Errors are linked with `aria-describedby` and announced with `aria-live="polite"`.
- Use semantic HTML (`<table>` with `<caption>`, `<th scope>`, `aria-sort`). Color is never the only signal.
- No inline event handlers and no inline style attributes. Only a small script file with a CSP nonce or a hashed external file.

## 4. Component library (tag helpers and partials)
Build once, use everywhere. Each has a test that renders it.
| Component | Purpose |
|---|---|
| `a2-button`, `a2-link-button` | Primary, secondary, danger, ghost; loading state; disabled |
| `a2-field`, `a2-input`, `a2-select`, `a2-checkbox`, `a2-textarea`, `a2-multiselect`, `a2-password` (show/hide) | Label, hint, error, required marker |
| `a2-alert`, `a2-toast` (from TempData) | Success, info, warning, error |
| `a2-card`, `a2-tabs`, `a2-breadcrumbs`, `a2-badge` | Layout and status labels |
| `a2-table`, `a2-sort-header`, `a2-pager`, `a2-page-size`, `a2-filter-bar`, `a2-search`, `a2-empty-state` | The standard list (section 7) |
| `a2-confirm` | Confirmation dialog (native `<dialog>`), with optional typed confirmation text |
| `a2-picker` | Searchable, paged picker for roles, permissions, users, groups, scopes |
| `a2-copy`, `a2-secret-once` | Copy button; one-time secret display |
| `a2-code-input` | One-time code input (length from settings) |
Behavior is server-rendered. Small vanilla JavaScript only for: dialogs, copy, show/hide password, row selection, auto-submit of page size and filters (also works without JavaScript through a submit button). No SPA, no HTMX in v1 (decision; record it).

## 5. Layouts
- `_LayoutAuth`: public pages (Login, Register, ...). Variants `centered`, `split`, `minimal`, chosen by UI settings. Contains the branding partial (logo, title, favicon, CSS variables), culture selector (if more than one culture), footer links.
- `_LayoutAccount`: signed-in `Manage/*` pages. Side menu: Overview, Profile, Password, Two-factor, Passkeys, External logins, Sessions.
- `_LayoutAdmin`: admin console. Top bar (project switcher, user menu), left navigation (section 9), breadcrumbs, flash messages, main area. GlobalAdmin sees a project switcher with search; a ProjectAdmin sees only their project(s). The current project is part of every admin URL.
- A `Error` page with a correlation id and no stack trace; a 404 page that looks the same for "does not exist" and "not yours".

## 6. Public pages in depth (settings-driven)

### 6.1 Resolving the project and application (all pages)
`Auth2PageModel` uses `IUiContextResolver` and builds `UiContext(ProjectId, ProjectKey, ApplicationId, Culture, EffectiveUiSettings, EffectiveBranding)`:
1. From `returnUrl` (validated as a local URL): parse the OpenIddict authorization request, take `client_id`, load the application, then its project.
2. External callbacks: the signed `state` carries project and application. SAML: route `/saml/{projectKey}/{providerKey}/acs|metadata|slo` (project key optional in single-project mode).
3. `Manage/*`: the signed-in session's project and application.
4. Missing or tampered context: generic default page, no project name, no project list. In multi-project mode a direct visit to `/Account/Login` shows "Start from your application" (except for the `_global` admin console).
5. Disabled project or application: `AccessDenied` with default branding and a generic message. The reason goes only to the audit log.
6. Application UI mode `Self`: redirect to the application's configured login URL (404 if none). UI mode `None`: 404.
The context is cached briefly per `(ProjectId, ApplicationId, culture)` and invalidated on save (Redis when present).

### 6.2 UI settings (stored under `ui` in project and application `SettingsJson`)
Effective value = application value, else project value, else Server default.
```json
{
  "ui": {
    "login": {
      "identifier": "EmailOrUsername",
      "identifierLabel": { "en": "Email or username" },
      "title": { "en": "Sign in to PaperTrade" },
      "subtitle": { "en": "Welcome back" },
      "methods": ["password", "passkey", "external:google", "saml:acme", "ldap:corp"],
      "showRememberMe": true,
      "showForgotPassword": true,
      "showRegisterLink": true,
      "layout": "split",
      "footerLinks": [{ "text": { "en": "Privacy" }, "url": "https://example.com/privacy" }]
    },
    "register": {
      "fields": [
        { "name": "email", "enabled": true, "required": true },
        { "name": "userName", "enabled": false, "required": false },
        { "name": "phone", "enabled": true, "required": false },
        { "name": "firstName", "enabled": true, "required": true },
        { "name": "lastName", "enabled": true, "required": true }
      ],
      "acceptTermsRequired": true,
      "termsUrl": "https://example.com/terms",
      "passwordHint": { "en": "At least 12 characters" }
    },
    "twoFactor": { "allowedFactors": ["totp", "recoveryCode", "passkey", "emailOtp"] },
    "texts": { "welcome": { "en": "..." }, "footer": { "en": "..." } }
  }
}
```
- `identifier`: `Email`, `UserName`, `Phone`, `EmailOrUsername`. It sets input type, label, `autocomplete`, and how the lookup works.
- `methods` is ORDERED. One partial per method (`_PasswordForm`, `_PasskeyButton`, `_ExternalButtons`, `_SamlButton`, `_LdapForm`). The list is filtered to methods that are configured, enabled for the project, and (for external ones) allowed for the application's provider subset. At least one method must stay enabled (a save that removes the last one is rejected).
- Registration fields come from a fixed catalog: email, userName, phone, firstName, lastName, culture. No free custom fields in v1.
- Labels and texts have one value per culture, falling back to the project's default culture.
- Password rules shown on the page come from project settings; the hint text is only text.

### 6.3 The server never trusts the page
- On POST, recompute allowed methods, fields and required flags from settings. A posted method or field that is not allowed is rejected or ignored.
- User lookup is limited to the resolved project.
- Error messages are generic ("The email or password is not correct") and do not reveal whether the account, project or application exists.
- Rate limits and lockout are per project, and per IP + identifier.
- Redirect targets (`returnUrl`, post-logout) are checked against the application's registered URIs.
- The login cookie name includes the project key, so a session of project A is never accepted for project B.
- Every POST uses antiforgery.

### 6.4 Page by page
For each page: route, what it shows, project/application settings that change it, states, and rules.

**Login** (`/Account/Login`)
- Shows: branding, title, identifier input, ordered methods, remember me, forgot password link, register link, external buttons, passkey button.
- States: default, validation error, lockout (redirect to `Lockout`), requires 2FA (redirect to `TwoFactor`), requires join (`JoinProject`), requires email confirmation (message with resend), project/application disabled (`AccessDenied`).
- Rules: order of checks is project enabled, application enabled, user in project, lockout, password/passkey/external, 2FA, consent. Register link only when the project's `JoinMode` and registration allow it. Passkey button script follows `ui-handover.md`.

**Register** (`/Account/Register`)
- Fields from `ui.register.fields`; terms checkbox when required; password with live rule hints; culture.
- Rules: blocked when `JoinMode` is `InviteOnly` or `Closed` (generic message). Email normalized and unique inside the project. Creates the identity and the `user_projects` row, then sends the verification email (template from the project). An existing email never says "already registered"; it sends a "you already have an account" email (no account enumeration).

**JoinProject** (`/Account/JoinProject`, renamed from JoinApplication)
- Appears only for a signed-in person with no `user_projects` row for the project of the client, and only when `JoinMode = AutoJoin`. Shows project name, terms (if required), a Join button. Other join modes show the generic denial.

**ForgotPassword / ResetPassword**
- Always the same response ("If the account exists, an email was sent"). The token is single-use, short-lived, project-bound. Reset page enforces password rules, revokes the user's sessions and refresh tokens in that project after success.

**VerifyOtp**
- Code input length from settings; attempts limited; resend with cooldown; shows masked destination; codes are single-use and stored hashed.

**TwoFactor**
- Offers only `allowedFactors` the user has set up: TOTP, recovery code, passkey, email OTP. "Remember this device" only if the project allows it and for a limited time.

**ConfirmEmail / ConfirmPhone**
- Landing pages for link or code; result states: success, expired, invalid (generic), already confirmed. Change of email requires a new confirmation before it becomes active.

**Consent**
- Shows application display name, requested scopes with descriptions (from the scope entity, per culture), approve/deny. Skipped when the application's consent type is implicit/system. Deny returns `access_denied` to the redirect URI.

**Lockout**
- Shows lockout message and remaining time from settings (no exact internal counters). Offers password reset.

**AccessDenied**
- Generic, default branding. Used for disabled project or application, closed join mode, and other denials. No details.

**Logout**
- Confirmation (GET shows page, POST signs out), revokes the server session, redirects to a validated post-logout URI, shows branding.

**External/Challenge, Callback, Confirm**
- Challenge starts the provider flow with a signed state (project, application, return URL, nonce). Callback validates state and provider, finds the existing link or creates one ONLY with a verified email and project rules; never links accounts by an unverified email. `Confirm` asks the user to confirm creating or linking an account when the rules need it. Provider errors are generic for the user and detailed in the audit log.

**Saml/Acs, Metadata, Slo**
- Acs: validate signature, audience, destination, time window, `InResponseTo`, and replay (replay table); map attributes using the provider's mapping. Metadata: public SP metadata per project and provider. Slo: handle logout requests and responses with signature checks. Unknown project or provider key returns 404.

**Error** and **404**: correlation id, no stack trace, same look for missing and forbidden.

### 6.5 Account area (`Manage/*`, signed-in person)
| Page | Content | Rules |
|---|---|---|
| `Index` | Overview: name, email, security status, recent sign-ins | Only own data |
| `Profile` (add) | Name, phone, culture | Phone change re-verifies |
| `ChangeEmail` (add) | New email, confirmation | Old email gets a notice; new one confirmed first |
| `ChangePassword` | Current, new, confirm | Revokes other sessions optionally |
| `TwoFactor/Enable` | QR/secret, verification code | Shows recovery codes once |
| `TwoFactor/RecoveryCodes` | Regenerate (needs current password or recent login) | Shown once |
| `Passkeys` (add) | List, rename, add, remove | Cannot remove the last sign-in method without a backup; script per `ui-handover.md` |
| `ExternalLogins` | Linked providers, link, unlink | Cannot unlink the last sign-in method |
| `Sessions` | Own sessions, revoke one or all others | Shows application, device, IP, last seen |
| `DeleteAccount` (optional) | Remove own membership from the project | Needs recent login; a project admin cannot remove the last `project-admin` |
`ManagePageModel` requires an authenticated person (not a machine token) and checks "recent login" for sensitive actions (password change, passkey removal, email change, 2FA changes): `auth_time` within the configured window (default 10 minutes) or step-up.

## 7. The standard list: pagination, sorting, filtering, search, bulk, export
EVERY admin list uses this one contract (pages and API). Build it once in `Admin/Lists`, then each entity only declares a `ListDefinition`.

### 7.1 Query contract
| Parameter | Meaning | Rules |
|---|---|---|
| `page` | 1-based page number | Default 1. Clamped to the last page. Reset to 1 when filters, search or page size change. |
| `pageSize` | Rows per page | Allowed 10, 25, 50, 100. Default 25. API maximum 200. |
| `sort` | Comma list of fields, `-` prefix for descending | Up to 2 fields. Only fields in the whitelist. Default per entity. A stable tiebreaker (`Id`) is always added. |
| `q` | Free-text search | Minimum 2 characters, trimmed, maximum 100. Searches the fields declared by the entity. |
| `f.<name>` | Filter value | Names from the whitelist. Repeat the key for multiple values (`f.status=a&f.status=b`). |
| `f.<name>From`, `f.<name>To` | Range (dates, numbers) | ISO 8601, UTC. `To` is inclusive for dates. |
Invalid values: the API returns 400 `ProblemDetails` (`validation_failed`) naming the parameter; the Razor page ignores the bad value, uses the default, and shows a notice. State lives in the URL, so every list view can be shared, bookmarked and used with the browser back button.

### 7.2 Response
```csharp
public sealed record PageQuery(int Page, int PageSize, IReadOnlyList<SortSpec> Sort, string? Q, IReadOnlyDictionary<string, string[]> Filters);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount, bool TotalIsCapped, int TotalPages, bool HasNext, bool HasPrevious);

public sealed class ListDefinition<TEntity, TRow>
{
    public required IReadOnlyDictionary<string, Expression<Func<TEntity, object?>>> Sortable { get; init; }
    public required IReadOnlyDictionary<string, FilterRule<TEntity>> Filters { get; init; }
    public required Func<string, Expression<Func<TEntity, bool>>> Search { get; init; }
    public required string DefaultSort { get; init; }
    public required Expression<Func<TEntity, TRow>> Select { get; init; }
}
```
The generic `ApplyList(query, definition)` applies filter, search, sort, tiebreaker and paging in SQL, and projects to a row DTO (never load entities for lists).

### 7.3 Rules
- **Whitelists.** Sort and filter names come only from the definition. A free string is never used as a column or an expression.
- **Search.** Case-insensitive with escaped wildcards (`%`, `_` and the escape character are escaped). Prefer prefix and trigram-backed search on indexed columns. Document which columns are indexed.
- **Counting.** Offset paging with an exact total by default. For large tables (audit, sessions) use keyset paging (cursor = sort value + Id) with "Next/Previous", and a capped count (for example "10,000+", `TotalIsCapped = true`).
- **Tenant.** Every list runs under the tenant filter on `ProjectId`. A list that runs without the filter fails a test.
- **Selection and bulk actions.** Checkboxes select rows of the current page only (no "select all pages" in v1). Bulk actions: maximum 100 ids; each id is checked against the route project; the result shows how many succeeded and failed with reasons; one audit event per affected row (and one summary event).
- **Export.** CSV of the current filter and sort, streamed, row limit configurable (default 50,000), audited with the filter used. Cells that start with `=`, `+`, `-` or `@` are prefixed to stop spreadsheet formula injection. UTF-8 with BOM for spreadsheet programs. No secrets or hashes in any export.
- **Performance.** Every sortable and filterable column has an index plan (documented per entity). A test checks the generated SQL uses `LIMIT/OFFSET` or the keyset condition, and that no list loads a full table.
- **Concurrency.** Row DTOs carry the version string for edit forms (`If-Match`).
- **States.** Loading (skeleton), empty ("No users match these filters" with a clear-filters button), error (retry), and no-permission (404 page).

### 7.4 Markup pattern (Tailwind)
Filter bar and table header use the shared components. Sketch:
```html
<form method="get" class="flex flex-wrap items-end gap-3 mb-4">
  <a2-search name="q" value="@Model.Q" />
  <a2-select name="f.status" values="@Model.StatusOptions" />
  <a2-page-size value="@Model.PageSize" />
  <button class="rounded px-3 py-2 bg-[var(--a2-color-primary)] text-[var(--a2-color-primary-text)] focus:outline-none focus:ring-2">Apply</button>
</form>
<div class="overflow-x-auto rounded border">
  <table class="min-w-full text-sm">
    <caption class="sr-only">Users</caption>
    <thead class="bg-gray-50 text-left">
      <tr>
        <th scope="col" aria-sort="ascending"><a2-sort-header field="email" label="Email" /></th>
        ...
```
`a2-sort-header` renders a link that toggles ascending, descending and default, keeps all other query values, and sets `aria-sort`. `a2-pager` shows "Showing 26-50 of 1,234", first, previous, pages around the current one (current +/- 2, with ellipsis), next, last, and a page size select.

## 8. Authorization, security and step-up for all admin pages
- Policies `GlobalAdmin` and `ProjectAdmin`. The route project must equal the caller's `project_id` claim (except GlobalAdmin). Handler-level check on every page and endpoint (not only the menu).
- Page handlers call the same application services as the API. Services take `ProjectId` from `IProjectContext`, never from the form.
- Ids from the route or form (role, group, application, user) are always loaded under the tenant filter; a wrong-project id gives 404.
- Antiforgery on every POST/PUT/DELETE. `frame-ancestors 'none'`. Strict CSP with nonce. `Cache-Control: no-store` for admin pages with personal data.
- Step-up (recent login or 2FA within 10 minutes) for: rotate secret, delete project, delete user, reset 2FA, change provider secrets, rotate signing key, disable project.
- Secrets (client secret, provider secrets, LDAP bind password) are write-only: shown once at creation, shown as "stored" afterwards, never in HTML source after creation, never in logs, never in audit before/after values.
- Admin session timeout shorter than user sessions (default 30 minutes idle).
- Rate limit admin writes and exports per admin and per project.
- PII: list pages show full email to project admins of that project only; logs and audit details mask phone numbers and tokens.
- Dangerous actions use `a2-confirm`; project delete needs the typed project key.
- Disabled project: its ProjectAdmins cannot use the admin console; GlobalAdmin can.

## 9. Admin navigation and route map
Navigation (left menu), under a project:
Dashboard, Users, Roles, Permissions, Groups, Applications, Scopes, Authorizations, Providers (external login), Sessions, Audit, Reports, Settings (Project settings, UI and login page, Branding, Email templates), then GlobalAdmin only: Projects, Signing keys, Maintenance jobs, Global settings.
Razor pages (`Pages/Admin/...`), mirrored by the API under `/api/admin/projects/{projectId}/...`:
| Page route | Purpose |
|---|---|
| `/admin` | Project chooser (GlobalAdmin) or redirect to the project's dashboard |
| `/admin/projects` (+ `/new`, `/{projectId}`) | Projects list, create, detail |
| `/admin/p/{projectKey}` | Dashboard |
| `/admin/p/{projectKey}/users` (+ `/new`, `/{id}`, `/{id}/...`) | Users |
| `/admin/p/{projectKey}/roles`, `/permissions`, `/groups` (+ new, detail) | Access model |
| `/admin/p/{projectKey}/applications` (+ new, detail, `/setup`, `/secret`) | Applications |
| `/admin/p/{projectKey}/scopes`, `/authorizations` | Scopes and authorizations |
| `/admin/p/{projectKey}/providers` (+ new, detail) | External providers |
| `/admin/p/{projectKey}/sessions`, `/audit`, `/reports` | Operations |
| `/admin/p/{projectKey}/settings`, `/ui`, `/branding`, `/email-templates` | Configuration |
| `/admin/keys`, `/admin/jobs`, `/admin/settings` | GlobalAdmin |
The URL uses the project key for readability; the server resolves it to `ProjectId` and checks it against the caller. Application routes use the application name or id.

## 10. Entity catalog (every table that needs an admin screen)
Each entity lists: columns, sort, filters, search, row and bulk actions, form fields and validation, detail tabs, delete rules, audit events, indexes. All lists follow section 7. All entities except Projects, Signing keys, Maintenance jobs and global scopes are project-scoped.

### 10.1 Projects (`projects`, GlobalAdmin)
- Columns: Name, Key, Status, Users, Applications, Created, Updated.
- Sort: name, key, createdAt, usersCount, status. Default: name.
- Filters: status (enabled/disabled), createdFrom/To. Search: name, key.
- Row actions: open, edit, enable/disable, delete. Bulk: none (projects are changed one at a time).
- Form: Name (3-100); Key (create only; `^[a-z0-9]+(-[a-z0-9]+)*$`, 2-40 characters, immutable, unique, reserved words rejected: `_global`, `admin`, `api`, `saml`, `branding`, `account`, `manage`); Description; JoinMode (AutoJoin, InviteOnly, Closed); default culture; time zone.
- Detail tabs: Overview, Applications, Users, Roles, Permissions, Groups, Providers, Settings, UI and branding, Email templates, Sessions, Audit, Danger zone.
- Rules: disable runs the shared procedure (revoke authorizations, refresh tokens and sessions in one transaction, audit with reason, keep data). Delete only when disabled, with typed key, in one transaction, and with step-up. `_global` cannot be disabled or deleted.
- Audit: `project.created|updated|enabled|disabled|deleted`. Index: unique on key.

### 10.2 Applications (`applications`, the extended OpenIddict table)
- Columns: Name, Client id, Type, Status, UI mode, Redirect URIs (count), Created.
- Sort: name, clientId, type, status, createdAt. Filters: status, type (confidential, public, machine), uiMode. Search: name, clientId.
- Row actions: open, edit, enable/disable, rotate secret (confidential only), setup snippet, delete.
- Form: Name (unique in project, `^[a-z0-9]+(-[a-z0-9]+)*$`); Type; Redirect URIs (exact match, `https` only, `http://localhost` and `http://127.0.0.1` only when the project allows development URIs; no wildcards, no fragments); Post-logout URIs; Allowed scopes (multi-select); Consent type; UI mode (Hosted, Self, None) with login URL when Self; Token lifetimes (within project limits); CORS origins; Providers subset; Cookie lifetime.
- Detail tabs: Overview, Credentials, URIs, Scopes, Settings, UI and branding, Setup, Sessions, Audit.
- Rules: create, update and delete only through `IOpenIddictApplicationManager`. The client id is `{projectkey}.{name}` unless an admin gives a globally unique value. The secret is shown once (`a2-secret-once`). Disabling revokes only this client's tokens. The `_global` application is read-only except URIs and secret.
- Audit: `application.created|updated|enabled|disabled|deleted|secret_rotated`. Index: unique (ProjectId, Name); unique client id.

### 10.3 Scopes (OpenIddict scopes with nullable `ProjectId`)
- Columns: Name, Display name, Owner (Global or this project), Applications using it, Created.
- Sort: name, createdAt, applicationsCount. Filters: owner (global/project). Search: name, display name.
- Form: Name (no spaces, double quotes or backslash; unique across the database); display name and description per culture; resources.
- Rules: global scopes (ProjectId null) are managed by GlobalAdmin only. Project scopes are managed by that project's admin; pushed scopes land there. A name that collides with a global scope or another project's scope is rejected. Delete is blocked while an application uses the scope (show which). (Decision to confirm.)

### 10.4 Authorizations (OpenIddict authorizations, project-scoped through the application)
- Columns: User, Application, Status, Scopes, Created. Sort: createdAt, status. Filters: application, status, user. Keyset paging.
- Actions: revoke one, revoke selected, revoke all of an application. No edit, no create. Tokens are NOT listed (reference values are secrets); revoking an authorization revokes its tokens.

### 10.5 Users (`users` plus `user_projects`)
- Columns: Email, User name, Name, Status (active, locked, disabled), Email confirmed, 2FA, Passkeys, Roles (count), Last login, Created.
- Sort: email, userName, name, createdAt, lastLoginAt, status. Default: createdAt descending.
- Filters: status, emailConfirmed, twoFactorEnabled, hasPasskey, role, group, permission (direct), provider (linked login), createdFrom/To, lastLoginFrom/To. Search: email, user name, first name, last name, phone (prefix match; index plan documented).
- Row actions: open, edit, lock, unlock, reset password (sends email), reset 2FA (step-up), revoke sessions, remove from project.
- Bulk actions (max 100): lock, unlock, revoke sessions, add role, remove role, add to group, remove from group, remove from project (step-up and typed count).
- Form: Email (valid, normalized, unique in project); User name (optional, rules from settings); Phone; First and last name; Culture; Enabled. Create: either send an invitation (default) or set a temporary password that must be changed at first login.
- Detail tabs: Profile; Roles (paged picker; assign and remove); Permissions (direct permissions; read-only "effective permissions" table that shows each permission's source: direct, role X, group Y); Groups; Security (2FA status, passkeys list and delete, linked external logins and unlink, lockout end, password changed date); Sessions; Audit (filtered to this user).
- Rules: an admin cannot lock or remove themselves; the last `project-admin` cannot be removed or locked; changing email re-verifies; password hashes, TOTP keys and recovery codes are never shown. Remove from project deletes the membership and project data (links, sessions, passkeys, external logins); the identity record is removed when no membership remains. Global delete of an identity is GlobalAdmin only.
- Audit: `user.created|updated|locked|unlocked|password_reset_requested|two_factor_reset|passkey_removed|login_unlinked|removed_from_project|sessions_revoked`, plus `user.role_added|role_removed|group_added|group_removed`. Indexes: (ProjectId, NormalizedEmail) unique; (ProjectId, CreatedAt); (ProjectId, LastLoginAt).

### 10.6 Roles (`roles`)
- Columns: Name, Description, System, Permissions (count), Users (count), Created.
- Sort: name, createdAt, usersCount, permissionsCount. Filters: isSystem, hasUsers. Search: name, description.
- Form: Name (2-64, unique in project, case-insensitive); Description; permission picker (searchable, grouped by category, paged).
- Detail tabs: Overview, Permissions (assign, remove, bulk), Users (paged members, add/remove), Audit.
- Rules: system roles (`project-admin`) are read-only and cannot be deleted. Deleting a role with users shows the count and needs confirmation; links are removed, users are not. A role cannot be edited so that the last `project-admin` loses admin rights.
- Audit: `role.created|updated|deleted|permission_added|permission_removed|user_added|user_removed`.

### 10.7 Permissions (`permissions`)
- Columns: Key, Name, Category, Source (manual or sync, and the application), Status (active or obsolete), Roles (count), Last seen.
- Sort: key, name, category, lastSeenAt, rolesCount. Filters: category, source, application, status. Search: key, name, description.
- Form: Key (create only, key rules from the catalog; immutable); Name; Description; Category.
- Rules: only text fields are editable. Synced permissions cannot be deleted while active; sync marks missing ones obsolete; obsolete and manual ones can be deleted (links removed after confirmation). A "who has this permission" tab lists roles and users (paged).
- Audit: `permission.created|updated|deleted|marked_obsolete`. Index: unique (ProjectId, Key).

### 10.8 Groups (`groups`)
- Columns: Name, Description, Roles (count), Users (count), Created. Sort: name, createdAt, usersCount. Search: name, description.
- Detail tabs: Overview, Roles (assign/remove), Members (paged, searchable, add/remove, bulk), Audit.
- Rules: deleting a group removes only its links.
- Audit: `group.created|updated|deleted|role_added|role_removed|member_added|member_removed`.

### 10.9 Link tables (`user_roles`, `user_permissions`, `role_permissions`, `group_roles`, `group_users`)
- No separate list pages. They are managed from the detail tabs above through `a2-picker` (search, paging inside the picker, select several, add or remove with one confirmation).
- Rules: both sides must belong to the route project (404 otherwise). Duplicate links are ignored. Each add or remove writes an audit event. The "effective permissions" view is computed (direct + roles + groups) and read-only.

### 10.10 External providers (`external_providers`)
- Columns: Name, Type (Social, OIDC, SAML, LDAP), Key, Status, Applications (count), Created. Sort: name, type, status, createdAt. Filters: type, status. Search: name, key.
- Form by type: Social (provider kind, client id, secret, scopes); OIDC (authority URL, client id, secret, scopes, claim mapping); SAML (entity id, metadata URL or XML, signing certificate, name id format, attribute mapping, signed requests); LDAP (server, port, TLS mode `ldaps` or `starttls` only, base DN, bind DN and password, user filter, attribute mapping, group mapping, timeouts).
- Rules: secrets and bind passwords are write-only. "Test connection" runs a safe check (OIDC discovery, SAML metadata fetch and parse, LDAP bind and one search) with rate limit, timeout and audit; it never logs secrets. Plain LDAP is refused. Account linking only with a verified email. Disabling a provider hides its button at once (cache invalidation). Which applications show the provider is set on the application.
- Audit: `provider.created|updated|enabled|disabled|deleted|tested` (never the secret values).

### 10.11 Sessions (`sessions`)
- Columns: User, Application, Method (amr/idp), IP, Device (parsed user agent), Created, Last seen, Expires, Status.
- Sort: createdAt, lastSeenAt, expiresAt. Default lastSeenAt descending. Filters: user, application, status (active, revoked, expired), method, ip, createdFrom/To. Keyset paging.
- Actions: revoke one, revoke selected, revoke all of a user, of an application, or of the project (step-up for the last). Revocation also revokes refresh tokens. Audit: `session.revoked` with scope.

### 10.12 Audit events (`audit_events`) - see section 12.

### 10.13 Branding assets (`branding_assets`) and email templates
- Branding page: name, logo (light/dark), favicon, background, colors with live contrast check, texts, layout, with a live preview of the Login page. Reset to defaults per field or all.
- Assets list (per project or application): kind, type, size, updated. Upload and delete only. Upload safety in section 13.
- Email templates: list (name, culture, custom or default, updated), edit (subject, body with the fixed placeholder set), preview with sample data, reset to default.
- Audit: `branding.updated|asset_uploaded|asset_deleted|reset`, `email_template.updated|reset`.

### 10.14 Settings pages
- Project settings grouped in sections: Sign-in (identifier, methods, remember me), Registration (JoinMode, fields, terms), Passwords (length, rules, history), Lockout, Two-factor, Sessions (single active session, idle and absolute lifetime), Tokens (defaults), Email and SMS sender, Retention (audit days).
- Application settings: only values that override the project. Each field shows "inherited from project" or "overridden".
- Effective settings page: every key, its value, and its source (Application, Project, Default).
- Rules: validate against the schema (unknown keys rejected, ranges checked); `If-Match` versions (412 on stale save); sensitive keys are write-only; every save writes a change summary to the audit log.

### 10.15 Signing keys (GlobalAdmin; key ring in PostgreSQL)
- Columns: Key id, Purpose (signing, encryption), Algorithm, Status (next, active, retired), Created, Activates, Expires. Actions: rotate now (step-up), retire early (step-up). Private material is never shown. The `key-removal` job deletes keys after their retention period.
- Audit: `key.rotated|retired`.

### 10.16 Maintenance jobs (GlobalAdmin)
- New table `job_runs` (Id, JobName, InstanceId, StartedAtUtc, FinishedAtUtc, Success, Error (short), RowsAffected). Written by `MaintenanceJobs` after each run.
- Page: per job last run, last success, last error, duration, instance; history list (paged, filter by job and success). "Run now" takes the same lock as the timer. The readiness health check turns unhealthy when a job has not succeeded for longer than a configured limit.

### 10.17 Tables with no admin screen
OTP codes, replay entries, Data Protection keys, Identity user tokens, and OpenIddict tokens have no list or edit screen (secrets or internal state). Their cleanup is in the maintenance jobs. Claim tables for users and roles are not in v1.

## 11. Dashboard (`/admin/p/{projectKey}`)
- Cards (last 24 hours and 7 days): successful logins, failed logins, lockouts, new registrations, active sessions, users total.
- Recent audit events (last 10, link to the full list), applications with their status, providers with their status, expiring items (signing keys for GlobalAdmin, provider certificates), and a job health badge (GlobalAdmin).
- Numbers are computed by indexed queries over a limited range and cached for a short time. A simple chart of logins per day uses server-rendered SVG or a small chart script with a CSP nonce; the table of numbers is always present for accessibility.

## 12. Audit list, search and reports
### 12.1 Audit event list (`/admin/p/{projectKey}/audit`)
- Columns: Time (UTC and project time zone), Actor, Action, Target (type and name), Application, Result, IP, Session.
- Sort: time (default, descending), actor, action. Keyset paging; capped count.
- Filters: date range (with presets: last hour, 24 hours, 7 days, 30 days, custom), actor, action (multi-select grouped by area), target type and id, application, result (success, failure), IP address, session id, correlation id.
- Search: actor email and target name (prefix and trigram on indexed columns; minimum 2 characters).
- Detail drawer or page: all fields, before/after summary with secrets masked, correlation id, link to the actor and to the target.
- Export: CSV of the current filters (section 7.3). The export is itself an audit event.
- Rules: audit rows are append-only; the only delete is the retention job, with a project retention setting that overrides the global default; no passwords, secrets, tokens or one-time codes in any audit field. GlobalAdmin has a cross-project audit list with a project filter.
- Indexes: (ProjectId, OccurredAtUtc), (ProjectId, ActorId, OccurredAtUtc), (ProjectId, Action, OccurredAtUtc), (ProjectId, TargetType, TargetId, OccurredAtUtc); trigram index on the searchable text.

### 12.2 Reports (`/admin/p/{projectKey}/reports`)
Fixed set in v1, each with a date range, an optional application filter, a table, a simple chart, and CSV export:
1. Logins per day by method (password, passkey, social, SAML, LDAP) and by application.
2. Failed logins and lockouts per day; top failing IP addresses; top failing identifiers (masked).
3. Registrations and removals per day.
4. Admin changes by actor and by entity type.
5. Session and token revocations by reason.
6. Dormant users (no login for N days) with a bulk action link.
7. Users without 2FA or passkey (for projects that want to push for it).
Rules: computed in SQL on indexed columns, with a row limit and a statement timeout; the date range maximum is configurable (default 366 days); results never cross projects; each report run and export is audited.

## 13. Branding and uploads (summary of v3 section 5, still valid)
- Branding levels: project default, application override. Items: display name, logo (light and dark), favicon, background, colors (design tokens), fonts from a safe list, texts per culture, layout, custom CSS (off by default; only GlobalAdmin enables it), email templates.
- Contrast: primary against primary-text and text against background at least 4.5:1; a save that fails names the pair.
- Storage: settings in `SettingsJson` under `branding`; assets in `branding_assets` (PostgreSQL), served at `/branding/{projectKey}/{applicationName}/{kind}?v={sha256}` with long immutable caching.
- Upload safety: check the real type from bytes (PNG, JPEG, WebP, sanitized SVG); size limits (logo 512 KB, favicon 64 KB, background 2 MB); re-encode raster images; SVG either rejected in v1 or strictly sanitized (decide in a spike and record it), served with `Content-Security-Policy: sandbox` and `X-Content-Type-Options: nosniff`; custom CSS parsed and rejected for `@import`, external `url()`, `expression`, `behavior`; text encoded on output, no HTML in v1; legal links `https` only.
- A page of project A never shows the name, logo or colors of project B.

## 14. API conventions (parity with the pages)
- Base `/api/admin/projects/{projectId}/...` and `/api/admin/projects` for GlobalAdmin. Lists use section 7; `GET` returns `PagedResult<T>`.
- DTOs are records: `Create...Request`, `Update...Request`, `...Row` for lists, `...Dto` for details. Never return entities. Never return hashes or secrets.
- `PUT` and `DELETE` need `If-Match` where a version exists (412 on mismatch). `POST` actions (`/lock`, `/enable`, `/secret/rotate`) are idempotent where possible.
- Errors are RFC 7807 `ProblemDetails` with stable `code`: `not_found`, `conflict`, `validation_failed`, `forbidden`, `step_up_required`, `rate_limited`.
- Every write returns the new version and writes an audit event.
- OpenAPI document lists every endpoint with its filter and sort whitelists.

## 15. Tests
### 15.1 Generic list tests (run for EVERY entity in section 10)
- Paging: first, middle, last page; page beyond the end is clamped; `pageSize` outside the allowed set falls back or returns 400; totals and `HasNext` are right.
- Sorting: each whitelisted field ascending and descending; unknown field rejected; two-field sort; the tiebreaker keeps order stable across pages (no duplicates and no gaps when paging through all rows).
- Filtering: each filter alone and combined; multiple values; ranges; invalid values; filter on another project's id returns nothing.
- Search: minimum length, case-insensitive, wildcard characters in the input are treated as text, SQL-like input is harmless.
- Tenant isolation: rows of project B never appear in project A (list, search, export, picker, bulk).
- Keyset paging (audit, sessions, authorizations): stable under inserts between pages.
- Export: respects filters, row limit, CSV formula-injection prefix, no secrets, audited.
- Bulk actions: maximum 100, wrong-project ids ignored or 404, partial failure report, one audit event per row.
- Generated SQL: uses LIMIT/OFFSET or keyset, never loads all rows (query-count and plan checks on a seeded table of at least 100,000 rows for users and audit).
### 15.2 Entity tests
- Per entity: create, edit, delete happy paths and validation failures; unique rules; `If-Match` 412; delete rules (system roles, active synced permissions, last project-admin, self-removal, scope in use, project must be disabled).
- Policy matrix per endpoint and page: anonymous, normal user, machine token, ProjectAdmin of the same project, ProjectAdmin of another project (404), GlobalAdmin.
- Applications through the OpenIddict manager keep `ProjectId`, `IsEnabled`, `Name`, `SettingsJson` on the same row; secret shown once; disabled application blocks only itself; disabled project blocks all login methods and the project's admins.
- Providers: secrets never returned; test connection is rate limited and audited; plain LDAP refused.
- Step-up required for the listed actions.
### 15.3 Page tests
- The same `Login.cshtml` renders different title, logo, colors, identifier input and ordered methods for two projects, and for two applications of one project (override works).
- Resolver: `client_id` in `returnUrl` finds the right project; tampered or missing context gives the generic page; an unknown client id looks like a missing one.
- A posted method or field that settings do not allow is rejected; required fields follow settings; lookup never finds a user of another project; generic error text.
- Register fields and required flags follow settings; `JoinMode` blocks or hides registration; account-enumeration safe responses on Register and ForgotPassword.
- Self UI redirects; None returns 404; disabled project or application shows `AccessDenied` with default branding.
- Every public and account page renders for the default and for a branded project and contains no other project's data.
- Cookie isolation between two projects on one host.
- SAML routes resolve the project from the route; unknown project key returns 404.
- Account area: cannot remove the last sign-in method; recent-login rule enforced.
### 15.4 UI quality
- Tailwind build runs in CI and the Docker build; build fails on error; the CSS file exists and contains the classes used by the components (smoke test).
- Accessibility checks on key pages (labels, focus, `aria-sort`, error association); keyboard-only run through Login, a user list, and a form.
- Rendered output encodes text (a script tag in a display name or user name is shown as text).
- CSP has no violations in the browser console on key pages (browser test with Playwright, if available).
- Snapshot or HTML assertion tests of the Login page for two branded projects.

## 16. Docs to write after the tests pass
- How to use: choose login methods and fields per project and per application; branding; email templates; the admin console, entity by entity; filters, sorting, export and bulk actions; reports and audit search.
- How to implement/extend: add a login method partial; add a registration field; add a design token; add a component; add a new admin entity (definition, row DTO, filters and sort whitelist, indexes, page, API route, policy, audit events, tests including the generic list tests); the tenant-filter rule; Tailwind build and `@source` folders; upload safety rules.
- Update `docs/decisions.md`: Tailwind v4 with CLI and the runtime-variable approach (spike S1), no HTMX or SPA in v1, SVG choice, scope ownership, remove-from-project behavior, 404 rule, `JoinApplication` renamed `JoinProject`, direct visit to Login in multi-project mode, step-up window, export limits.

## 17. Implementation phases (each phase: code, tests, run, fix, then docs)
1. Tailwind pipeline (S1 spike, build in MSBuild and Docker), layouts, branding partial, component library with render tests.
2. UI context resolver, `Auth2PageModel`, settings schema and effective settings service.
3. Public pages: Login, Register, JoinProject, ForgotPassword, ResetPassword, VerifyOtp, TwoFactor, Confirm pages, Consent, Lockout, AccessDenied, Logout, Error.
4. External and SAML pages (`External/*`, `Saml/*`) with the project-aware state and routes.
5. Account area pages, including the added Profile, ChangeEmail and Passkeys pages and the recent-login rule.
6. The generic list engine (`ListDefinition`, `PageQuery`, `PagedResult`, keyset paging, export, bulk) with the generic list test suite.
7. Admin: Projects, Applications, Scopes, Authorizations.
8. Admin: Users, Roles, Permissions, Groups and link pickers.
9. Admin: Providers, Sessions, Settings, UI settings, Branding, Email templates.
10. Audit list, Dashboard, Reports, Signing keys, Maintenance jobs (`job_runs`).
11. Full test run, accessibility and CSP checks, docs consistency pass, final report (built, tests run and passed, decisions, anything left undone).

## 18. Decisions to confirm (record the answer in docs/decisions.md)
1. Scope ownership: nullable `ProjectId` on scopes (section 10.3) versus global-admin-only scopes.
2. SVG logos: reject in v1 or sanitize.
3. Admin URLs use the project key (`/admin/p/{projectKey}/...`).
4. Offset paging for most lists and keyset paging for audit, sessions and authorizations.
5. New table `job_runs` for the maintenance jobs page.
6. No HTMX or SPA in v1.
