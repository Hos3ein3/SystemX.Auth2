# SystemX.Auth2 - Work Package: Server UI Pages, Branding and Admin CRUD (merged, v3)

Status: replaces `UiBranding-v2` and `AdminCrud-v2`. Nothing here has been compiled or run. The sketches show intended shape only.
How to use with an LLM: give the main specification first, then this file. This file overrides the main specification and earlier work packages where they differ.
The page list in section 3 comes from the screenshots of the current `SystemX.Auth2.Server/Pages` folder. Pages that were not visible in the screenshots are marked.

## 1. Terms (do not mix them)
- **Project**: the tenant (example: PaperTrade). Owns users, credentials, roles, permissions, groups, project settings, branding, audit and sessions.
- **Application**: a client of a project (papertrade-api, papertrade-mvc). It is the extended OpenIddict application table. It owns redirect URIs, allowed scopes, secrets, UI mode and client overrides.
- A user with a `user_projects` row can use every application of that project. There is no per-application access list.
- Every project data row has `ProjectId`. Audit rows and sessions also keep an optional `ApplicationId`.
- **One server page, many looks**: the server has ONE `Login.cshtml` (and one of every other page). What the user sees (title, logo, colors, texts, input fields, login methods, links) comes from the effective settings of the project and the calling application. Pages are never copied per project.

## 2. Working method (mandatory)
1. Change the code. 2. Add tests. 3. Build and run ALL tests. 4. Fix the code until everything passes. 5. Only then write the docs ("How to use" and "How to implement/extend").
Never claim a build or test result that you did not run. Record decisions in `docs/decisions.md`.

## 3. Page inventory (from the current folder)
Pages under `Pages/Account`:
| Page | Purpose | Project/application-driven parts |
|---|---|---|
| `Login` | Sign in | Everything in section 4 |
| `Register` | Create the account and the `user_projects` row | Fields, required flags, texts; hidden or blocked by `JoinMode` and registration settings |
| `JoinApplication` | **Rename to `JoinProject`.** An existing project user... see note below | Texts |
| `ForgotPassword`, `ResetPassword` | Reset flow | Branding, texts, email template |
| `VerifyOtp` | One-time code step (email/SMS) | Code length, texts |
| `TwoFactor` | Second factor (TOTP, recovery code, passkey) | Allowed factors from project settings |
| `ConfirmEmail`, `ConfirmPhone` | Verification landing pages | Branding, texts |
| `Consent` | OAuth consent | Application name, scopes, consent type of the application |
| `Lockout` | Locked account | Texts, lockout duration from settings |
| `AccessDenied` | Generic denial (also disabled project or application) | Default branding only, generic text |
| `Logout` | Sign-out confirmation and post-logout redirect | Branding, validated post-logout URI |
| `External/Challenge`, `External/Callback`, `External/Confirm` | Social, OIDC brokering and LDAP/SAML result handling | Provider list of the project and client; `Confirm` only for verified-email linking rules |

Pages under `Pages/Manage` (signed-in account area; project comes from the session): `Index`, `ChangePassword`, `ExternalLogins`, `Sessions`, `TwoFactor/Enable`, `TwoFactor/RecoveryCodes`, base class `ManagePageModel`.
Pages under `Pages/Saml`: `Acs`, `Metadata`, `Slo`. Route must carry the project and the provider: `/saml/{projectKey}/{providerKey}/acs|metadata|slo`. In single-project mode the project key part is optional.
Root: `_ViewImports`, `Auth2PageModel` (base class that loads the UI context), `Error`.

Missing from the screenshots (add them, or confirm they exist elsewhere):
- `Manage/Passkeys` (list, add, remove passkeys; the browser script follows `ui-handover.md`).
- `Manage/Profile` (name, phone, culture) and change email with re-verification.
- `Shared/_Layout` and a branding partial (`_Branding`) that writes the CSS variables, logo, title and favicon.
- Admin pages (section 7), for example `Pages/Admin/...`. Not visible in the screenshots.
- A passkey button and the script on `Login`/`TwoFactor` (script contract in `ui-handover.md`, not written here).

Note on `JoinApplication`: with the Project model, a user who is in the project can use every application. The page is needed only when a person who has no `user_projects` row signs in through an application of a project with `JoinMode = AutoJoin`. Rename it `JoinProject`. In `InviteOnly` or `Closed` projects it shows the generic denial.

## 4. One Login page, many looks

### 4.1 How the page knows the project and application
Order of resolution in `Auth2PageModel` (through a service `IUiContextResolver`):
1. For `Login`, `Register`, `Consent`, `Logout` and others reached from an authorization request: read `returnUrl`, parse the OpenIddict authorization request, take `client_id`, load the application, take its `ProjectId`. Validate `returnUrl` as a local URL.
2. For external callbacks and SAML endpoints: the project and application are stored in the signed `state` (or in the route for SAML).
3. For `Manage/*`: the signed-in session's project and application.
4. Unknown, missing or tampered context: show the generic default page. Never show a project list, never show any project's name. In multi-project mode the page `Login` opened directly shows an error "Start from your application", except for the `_global` admin console.
5. If the project or application is disabled: `AccessDenied` with default branding and the generic message. The real reason goes only to the audit log.
6. If the application UI mode is `Self` (the developer builds the screens): redirect to the application's configured login URL, or return 404 if none is set. If UI mode is `None`: 404.

The result is `UiContext(ProjectId, ProjectKey, ApplicationId, Culture, EffectiveUiSettings, EffectiveBranding)`. It is cached for a short time per `(ProjectId, ApplicationId, culture)`, invalidated on save (Redis when present, short expiry otherwise).

### 4.2 What the project settings control on Login
Stored in `SettingsJson` under `ui.login` (project default) with optional application override. Effective = application value, else project value, else Server default.
```json
{
  "ui": {
    "login": {
      "identifier": "EmailOrUsername",
      "identifierLabel": { "en": "Email or username", "tr": "E-posta veya kullanici adi" },
      "title": { "en": "Sign in to PaperTrade" },
      "subtitle": { "en": "Welcome back" },
      "methods": ["password", "passkey", "external:google", "external:microsoft", "saml:acme", "ldap:corp"],
      "showRememberMe": true,
      "showForgotPassword": true,
      "showRegisterLink": true,
      "layout": "split",
      "footerLinks": [{ "text": { "en": "Privacy" }, "url": "https://example.com/privacy" }]
    },
    "register": {
      "fields": [
        { "name": "email", "required": true },
        { "name": "userName", "required": false, "enabled": false },
        { "name": "phone", "required": false },
        { "name": "firstName", "required": true },
        { "name": "lastName", "required": true }
      ],
      "acceptTermsRequired": true,
      "termsUrl": "https://example.com/terms"
    }
  }
}
```
Rules:
- `identifier`: `Email`, `UserName`, `Phone` or `EmailOrUsername`. It decides the input type, label, autocomplete attribute, and how the server looks the user up.
- `methods` is an ORDERED list. The page renders one partial per method (`_PasswordForm`, `_PasskeyButton`, `_ExternalButtons`, `_SamlButton`, `_LdapForm`). The list is filtered to methods that are really configured and enabled for the project, and (for external ones) allowed for that application's provider subset.
- Registration fields are a fixed catalog of known names (email, userName, phone, firstName, lastName, culture). No free-form custom fields in v1. Each has `enabled` and `required`.
- Labels, titles and texts have one value per culture, with the project's default culture as fallback.
- `layout`: `centered`, `split`, `minimal`.

### 4.3 The server never trusts the page
- On POST the server recomputes the allowed methods and fields from settings. A posted method that is not allowed (for example a password POST when only SAML is allowed) is rejected, even if the form had the field.
- Required fields come from settings, not from the posted form. Unknown posted fields are ignored.
- Lookup by identifier is limited to the resolved project. A user of another project with the same email is never found.
- The login error text is generic and does not reveal whether the account, project or application exists. Rate limits and lockout apply per project.
- `returnUrl` and post-logout URIs are validated against the application's registered URIs.
- The per-project login cookie rule applies, so a session in project A is never accepted in project B.

### 4.4 Page model sketch
```csharp
public sealed record LoginPageViewModel(
    string Title, string? Subtitle, IdentifierKind Identifier, string IdentifierLabel,
    IReadOnlyList<LoginMethodView> Methods, bool ShowRememberMe, bool ShowForgotPassword,
    bool ShowRegisterLink, string Layout);

public abstract class Auth2PageModel(IUiContextResolver ui) : PageModel
{
    protected UiContext Ui { get; private set; } = default!;
    public override async Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext c) { /* resolve, or redirect to AccessDenied/Self UI */ }
}
```
`Login.cshtml.cs` builds `LoginPageViewModel` from `Ui.EffectiveUiSettings`; `Login.cshtml` only renders it. The same pattern applies to every page in section 3.

## 5. Branding

### 5.1 What can be branded
| Item | Level | Notes |
|---|---|---|
| Display name | project, application | Header and page titles |
| Logo (light, dark), favicon, background image | project, application | PNG, JPEG, WebP, or sanitized SVG |
| Colors | project, application | Design tokens (5.2) |
| Fonts | project | Safe list only, no remote font URLs in v1 |
| Texts | project, application | Titles, welcome, footer, legal links, per culture |
| Layout | project | `centered`, `split`, `minimal` |
| Custom CSS | project | Off by default; only GlobalAdmin can enable it |
| Email templates | project | Verification, reset, OTP, security alert, per culture |
Not in v1: custom Razor views, custom JavaScript, remote assets.

### 5.2 Design tokens
Pages use CSS variables only. Branding changes values, never markup:
`--a2-color-primary`, `--a2-color-primary-text`, `--a2-color-background`, `--a2-color-surface`, `--a2-color-text`, `--a2-color-muted`, `--a2-color-error`, `--a2-color-success`, `--a2-radius`, `--a2-font-family`.
Colors are `#RRGGBB`. The Server rejects a save when contrast is below 4.5:1 for primary/primary-text and text/background, naming the pair.

### 5.3 Storage and delivery
- Settings in `SettingsJson` under `branding` (typed `BrandingSettings`, with a schema version).
- Assets in table `branding_assets`: Id, ProjectId, ApplicationId (nullable), Kind, ContentType, Bytes, Sha256, Size, UpdatedAtUtc. Limits: logo 512 KB, favicon 64 KB, background 2 MB (configurable).
- Served at `/branding/{projectKey}/{applicationName}/{kind}?v={sha256}` with long immutable caching. Unknown project or application returns 404.
- `_Branding` writes the CSS variables in a `<style>` block with a CSP nonce.

### 5.4 Upload safety
- Check the real file type from the bytes, not the name or content type. Allow only PNG, JPEG, WebP, and sanitized SVG.
- SVG is the highest risk: either reject it in v1 or sanitize strictly (no scripts, event attributes, `foreignObject`, external references, `data:` URIs). Decide in a spike, record it. Serve SVG with `Content-Security-Policy: sandbox` and `X-Content-Type-Options: nosniff`.
- Limit dimensions and re-encode raster images to remove metadata.
- Custom CSS (when enabled): size limit, parse it, reject `@import`, external `url()`, `expression`, `behavior`.
- Texts are encoded on output; no HTML in v1. Legal links must be `https`.
- Email templates allow a fixed placeholder set (`{{UserName}}`, `{{ProjectName}}`, `{{Link}}`, `{{Code}}`, `{{ExpiresIn}}`); unknown placeholders fail validation.

## 6. Roles and policies
| Policy | Who | Scope |
|---|---|---|
| `GlobalAdmin` | role `global-admin` in the `_global` project | Everything, including projects, global scopes and enabling custom CSS |
| `ProjectAdmin` | role `project-admin` in the project named in the route | Everything inside the project, including its applications, users, UI settings and branding |
The project in the route is checked against the caller's `project_id` claim on every request. A ProjectAdmin of A gets 404 (not 403) for anything of project B. Only `GlobalAdmin` endpoints may use `IGlobalScope`. No ApplicationAdmin in v1.

## 7. Admin CRUD

### 7.1 Matrix
| Resource | Level | List | Get | Create | Update | Delete | Notes |
|---|---|---|---|---|---|---|---|
| Projects | Global | yes | yes | yes | yes | yes | Key immutable. Delete only when disabled, typed key as confirmation. `_global` never disabled or deleted. |
| Applications | Project | yes | yes | yes | yes | yes | Only through `IOpenIddictApplicationManager`. Secret rotation. Enable/disable. |
| Users | Project | yes | yes | yes | limited | remove from project | Global delete is GlobalAdmin only. |
| Roles | Project | yes | yes | yes | yes | yes | System roles cannot be deleted. |
| Permissions | Project | yes | yes | yes | text only | obsolete or manual only | Key immutable. |
| Groups | Project | yes | yes | yes | yes | yes | |
| Scopes | Global or project | yes | yes | see rule | see rule | see rule | See 7.3. |
| Links | Project | yes | - | assign | - | unassign | user-role, user-permission, role-permission, group-role, group-user |
| Settings | Project and application | - | yes | - | yes | - | Optimistic concurrency; effective view read-only |
| UI settings (login, register) | Project and application | - | yes | - | yes | reset | Section 4.2, validated against a schema |
| Branding and assets | Project and application | - | yes | upload | yes | reset | Section 5 |
| Email templates | Project | yes | yes | - | yes | reset | Preview endpoint |
| Sessions | Project | yes | - | - | - | revoke | One, all of a user, all of an application, all of a project |
| Audit | Project (global for GlobalAdmin) | yes | yes | - | - | - | Read-only, CSV export |
Not in v1: user claim tables and role claim tables.

### 7.2 Routes
Project: `/api/admin/projects/{projectId}`
- `GET/POST /api/admin/projects` (GlobalAdmin); `GET/PUT/DELETE .../{projectId}`; `POST .../enable|disable`
- `.../users`, `.../users/{userId}` (+ `/lock`, `/unlock`, `/reset-password`, `/reset-2fa`, `/passkeys`, `/sessions`, `/roles`, `/permissions`, `/groups`); `DELETE` removes from project
- `.../roles`, `.../roles/{roleId}/permissions`; `.../permissions`; `.../groups`, `.../groups/{groupId}/roles|users`
- `.../settings`, `.../settings/effective`; `.../ui` (GET/PUT/DELETE); `.../branding` (+ `/assets/{kind}`, `/effective`); `.../email-templates/{name}` (+ `/preview`)
- `.../sessions`, `.../audit`, `.../audit/export`
Application: `/api/admin/projects/{projectId}/applications/{applicationId}`
- `GET/PUT/DELETE`; `POST .../enable|disable`; `POST .../secret/rotate` (shows the new secret once)
- `.../redirect-uris`, `.../post-logout-uris`, `.../scopes` (assign existing scopes)
- `.../settings`; `.../ui`; `.../branding` (+ assets); `.../setup` (config snippet); `.../sessions`
Razor admin pages (for example `Pages/Admin/...`) mirror these routes and call the same application services, not the HTTP API. The UI settings and branding pages show a live preview of Login using the chosen values.

### 7.3 Resource rules
- **Projects**: create sets the key (lowercase letters, digits, hyphens; unique; immutable). Disable runs the shared procedure (revoke authorizations, refresh tokens, sessions in one transaction; audit with reason; keep data). Delete removes the project and all its data in one transaction, only if disabled.
- **Applications**: client id generated as `{projectkey}.{name}` unless a globally unique value is given. `Name` unique inside the project. Rotation returns the secret once. Disabling revokes only that client's tokens. The setup endpoint returns ServerUrl, ProjectKey (multi mode only), ApplicationName and client id, never the secret after creation.
- **Users**: create makes a `user_projects` row (and the identity record if the email is new in the project). Update is limited to profile, email (re-verified) and enabled state. Lock, unlock, password reset, 2FA reset and passkey removal write audit events. "Remove from project" deletes the membership and project data; the identity record goes when no membership remains. An admin cannot remove or lock themselves; the last `project-admin` cannot be removed.
- **Permissions and roles**: keys immutable; synced items cannot be deleted while active (sync marks missing items obsolete).
- **Scopes**: names are globally unique (OpenIddict rule). The scope entity has a nullable `ProjectId`: null is a global scope (GlobalAdmin only); set is a project scope (that project's ProjectAdmin, and where pushed scopes land). Name collisions with another project or a global scope are rejected. Decision to confirm; record it.
- **UI settings**: validate against the schema in 4.2: known method ids only, known field names only, texts length limit, `https` URLs, at least one login method must stay enabled (reject a save that would leave none).
- **Settings** and **UI settings** use `If-Match` (412 on a stale version).

### 7.4 Patterns
- One application service per resource; endpoints and Razor pages are thin. Services take `ProjectId` from `IProjectContext`, never from a request body.
- Paging `?page=1&pageSize=50` (max 200), `sort`, `q`. Response `PagedResult<T>`.
- DTOs are records. Never return entities. Never return password hashes, secrets, TOTP keys or provider secrets.
- Errors are RFC 7807 `ProblemDetails` with a stable `code`.
- Every write creates an audit event (admin, project, application, action, target, before/after summary without secrets).

## 8. Tests
Pages and login:
- The same `Login.cshtml` renders different title, logo, colors, identifier input, labels and methods for two projects, and for two applications of one project (override works).
- Resolver: `client_id` from `returnUrl` finds the right project; tampered or missing context shows the generic page with default branding; an unknown `client_id` gives the same page as a missing one.
- Posting a method or field that settings do not allow is rejected; required fields follow settings, not the form.
- Identifier lookup never finds a user of another project.
- Registration fields and required flags follow settings; `JoinMode` hides or blocks registration; `JoinProject` appears only for `AutoJoin`.
- UI mode `Self` redirects; `None` returns 404.
- Disabled project or application shows `AccessDenied` with default branding and the generic text.
- Every page in section 3 renders with the default and with a project branding and contains no other project's name, logo or colors.
- Culture fallback to the project's default culture.
- SAML routes resolve the project from the route and reject an unknown project key.
Branding:
- Resolver precedence for every field (application, project, default); contrast rejection; upload rejects wrong real type, oversize files, scripts in SVG, HTML renamed to `.png`; asset URLs serve only the right project's file; cache invalidation across two instances; `<script>` in a display name is shown as text; unknown email placeholder fails; custom CSS refused unless GlobalAdmin enabled it, external `url()` rejected.
Admin:
- Happy path and validation failure per endpoint; policy matrix (anonymous, normal user, ProjectAdmin of the same project, ProjectAdmin of another project, GlobalAdmin).
- ProjectAdmin A cannot read or change project B (404); ids from project B used in project A links return 404; application ids are checked against the route project.
- Last-admin and self-removal protections; `_global` cannot be disabled or deleted by a ProjectAdmin.
- Stale `If-Match` returns 412; paging limits.
- Application create/update/delete goes through the OpenIddict manager and keeps `ProjectId`, `IsEnabled`, `Name` and `SettingsJson` on the same row.
- Project disable then enable restores login; delete needs disabled state and the typed key.
- Secret shown once only; every write audited with no secret in it; scope name collision rejected; a UI save that would remove the last login method is rejected.

## 9. Docs to write after the tests pass
- How to use: choose login methods and fields for a project and for one application, set name/logo/colors/texts, email templates, custom CSS (if enabled), use the admin pages and routes.
- How to implement/extend: add a login method partial, add a registration field name, add a design token, add a page that uses `Auth2PageModel` and `UiContext`, add an admin resource (service, DTO, endpoint, page, policy, audit event, tests), the tenant filter rule, and the upload safety rules.
- Update `docs/decisions.md`: SVG choice, size limits, CSS policy, scope ownership, remove-from-project behavior, 404 rule, `JoinApplication` renamed `JoinProject`, the root-level login behavior in multi-project mode.
