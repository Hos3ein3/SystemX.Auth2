# SystemX.Auth2 — Work Package: UI (Tailwind CDN), Branding, MVC Sample, Docker

> Give this file to the implementing LLM **after** `SystemX.Auth2-Build-Specification-v2.md` and `SystemX.Auth2-WorkPackage-ExternalProviders-v2.md`.
> This package **lifts the "no UI" rule** of the main spec for the Server. The PageModels, handlers and flags already exist. Now write the real Razor views, layouts, components, styles and the branding feature.
> It also adds a **per-application branding** feature (display name, logo, colors), an **MVC sample project**, and **Dockerfiles + docker-compose** so the owner can run everything locally.
> If this file and the other files disagree about UI, branding, samples or Docker, this file wins.

---

## 0. Working method for every step (MUST follow)

For every piece of work, in this order:
1. **Write the code** (views, partials, tag helpers, CSS, endpoints, Dockerfiles).
2. **Write the tests** (section 8).
3. **Run the tests until all pass.** If Docker is available, also run the compose smoke test (section 11.6). Do not write docs for failing code.
4. **Write the docs** (section 9). Each docs page has two parts: **How to use** and **How to implement / customize**.
5. Check docs against the code (class names, settings, routes, commands must exist; code blocks must compile or be marked illustrative).
6. Update `CHANGELOG.md` and `docs/feature-status.md`. Commit. Then go to the next step.

Rules that stay from the main spec: English only, docs in B1–B2 English, no secrets in logs or HTML, the PageModels keep using `IStringLocalizer` keys (no hard-coded user text in `.cshtml`).

If you could not run something (for example Docker is not installed in your environment), say so clearly in the final report and in the docs. Never claim you ran it.

---

## 1. Goal and scope

Deliver:
1. Good-looking, **responsive**, accessible Razor views for **all** Server pages (public, self-service, admin), styled with **Tailwind CSS from a CDN**.
2. **Branding per application:** each project can set its own display name, logo, favicon and colors. Users see the project's name and logo in the page title and on the page. Changing the branding needs no code change and no restart.
3. A new sample `samples/SystemX.Auth2.Samples.Mvc` (MVC app using the Client package as an **MVC / cookie / hosted UI** client).
4. `Dockerfile` for the Server and for the samples, and one `docker-compose.yml` so the owner can start PostgreSQL + Redis + Server + MVC sample(s) locally and test the whole flow in a browser.

Out of scope: a JavaScript SPA framework, a Node build chain, email template design beyond a simple branded HTML template.

## 2. Technology decisions (locked)

- **Tailwind CSS through a CDN script** (the user asked for the CDN). The Tailwind docs say the Play CDN is meant for development, not production, because the script is large and builds styles in the browser. Therefore:
  - Put **all** Tailwind loading in **one** partial: `Pages/Shared/_TailwindCdn.cshtml`. Use the Tailwind v4 browser build (`@tailwindcss/browser@4` from a CDN such as jsDelivr) and **check the current official URL in the Tailwind docs when you implement it**. Theme tokens go in a `<style type="text/tailwindcss">` block with `@theme` (v4 style).
  - Add the option `Auth2:Ui:TailwindMode = Cdn | Local` (default `Cdn`). `Local` serves a prebuilt file `wwwroot/css/site.css` instead. Implement the switch and the partial logic now. Do **not** add npm or Node to the build. Document in `docs/ui-guide.md` how to produce a local CSS file with the Tailwind CLI later and why it is better for production.
  - Add a startup log warning in `Production` when `TailwindMode = Cdn`: "Tailwind CDN is for development. Use Local mode in production."
- **No other CSS framework.** Small vanilla JavaScript only (progressive enhancement; every form must work without JavaScript, except the QR code image which is rendered server-side).
- No inline event handlers (`onclick=`) and no inline scripts without a **CSP nonce**.
- Icons: inline SVG (a small set in `Pages/Shared/Icons/`), including simple brand icons for Google, Microsoft, GitHub, Apple, Facebook.
- QR code for TOTP setup: add `QRCoder` (MIT) and render the QR as an **inline SVG on the server**. Never send the TOTP secret to a third-party service.
- Fonts: system font stack (no external font CDN).
- Content Security Policy (CSP) must allow exactly what is needed: the Tailwind CDN script (with nonce), `style-src 'self' 'unsafe-inline'` (the Tailwind browser build injects styles), `img-src 'self' https: data:`, `form-action 'self'` plus allowed redirect origins for the authorize flow, `frame-ancestors 'none'`. Make the CDN host configurable. Test the CSP header.

## 3. Branding per application

### 3.1 Settings (replaces `BrandName` and `SupportEmail` of the main spec, section 5.3)

Add `ApplicationSettings.Branding` (jsonb, `ProjectAdminEditable`):

| Field | Meaning | Rules |
|---|---|---|
| `DisplayName` | Name users see (page title, header, emails). Defaults to the application `Name` | 1–60 chars, no HTML |
| `Tagline` | Short line under the name | max 120 chars |
| `LogoAssetId` | Uploaded logo (see 3.2) | nullable |
| `LogoUrl` | Alternative: HTTPS URL of a logo | HTTPS only; used if no upload |
| `LogoDarkAssetId` / `LogoDarkUrl` | Optional logo for dark mode | same rules |
| `FaviconAssetId` | Optional favicon | PNG/ICO |
| `PrimaryColor` | Hex like `#2563eb` | validated `#RRGGBB`; server checks contrast against white/dark text and picks the text color; reject colors that fail minimum contrast (WCAG AA for buttons) |
| `AccentColor` | Optional | same |
| `BackgroundStyle` | `Plain` \| `Soft` \| `Gradient` | enum |
| `FooterText` | Short footer text | max 200 chars, no HTML |
| `PrivacyUrl`, `TermsUrl`, `SupportUrl` | Links in the footer | HTTPS or relative |
| `SupportEmail` | Contact email | valid email |
| `ShowPoweredBy` | Show "Powered by SystemX.Auth2" | default true |

Rules:
- All text is HTML-encoded when rendered. **No HTML allowed in any branding field.**
- If a field is empty, fall back to the server-level default branding (`Auth2:Ui:DefaultBranding` in configuration), then to built-in defaults.
- The `_global` project has its own branding (used by the admin console).
- Page title format: `{Page title} · {DisplayName}`. Example: `Sign in · PaperTrade`. The login header shows the logo (if any) and `DisplayName`; if no logo, show a round monogram with the first letter in `PrimaryColor`.
- Colors are exposed as CSS variables (`--brand`, `--brand-contrast`, `--accent`) set in a nonce'd `<style>` block in the layout and used in Tailwind with arbitrary values or the `@theme` tokens, so a new color needs no rebuild.
- Set `<meta name="theme-color">` and the favicon from branding.

### 3.2 Logo and favicon storage

New table `application_assets`: `Id` (GUID, random, not guessable), `ApplicationId`, `Kind` (`Logo`, `LogoDark`, `Favicon`), `ContentType`, `Data` (`bytea`), `Sha256`, `SizeBytes`, `CreatedAtUtc`, `UpdatedAtUtc`. Unique `(ApplicationId, Kind)`. Add the migration.

Upload rules (all tested):
- Allowed types: **PNG, JPEG, WebP** (favicon also ICO). **SVG is not allowed in v1** (script risk). Check the **file signature (magic bytes)**, not only the extension or the `Content-Type` header.
- Max size: logo 512 KB, favicon 128 KB. Max dimensions: 1024×1024 (read the image header; do not decode huge images into memory).
- Strip nothing silently: reject files that fail validation with a clear error code (`asset_too_large`, `asset_type_not_allowed`, `asset_invalid`).
- Serve from `GET /branding/{projectSlug}/{kind}` (anonymous, because the login page is public). Response headers: correct `Content-Type`, `X-Content-Type-Options: nosniff`, `Cache-Control: public, max-age=3600`, `ETag` = SHA-256, support `If-None-Match` (304). In `Development` use a short cache.
- `projectSlug` is the `ProjectKey` (multi mode) or `default` (single mode). Unknown project or missing asset → 404 (no hint whether the project exists beyond what the login page already shows).
- Only an admin of that application (or the global admin) can upload or delete. Every change writes an audit event (`branding.updated`, `branding.asset.uploaded`, `branding.asset.deleted`). Increase `SettingsVersion` so caches refresh on all instances.

### 3.3 Admin API for branding

| Route | Purpose |
|---|---|
| `GET applications/{id}/branding` | Read branding + asset info (no binary) |
| `PUT applications/{id}/branding` | Update the text/color fields (validated) |
| `PUT applications/{id}/branding/assets/{kind}` | Upload (multipart) |
| `DELETE applications/{id}/branding/assets/{kind}` | Remove |
| `GET applications/{id}/branding/preview` | Returns resolved branding (after fallbacks) so a future UI or tests can check it |

Project admins can edit only their own application. The global admin can edit all and the server default branding.

### 3.4 How a page knows which application it belongs to (important)

The public pages (login, register, forgot password, consent, 2FA, external login) must show the **right project's branding**. Resolve the application in this order and put the logic in **one service**, `IPageApplicationResolver`:
1. If `ReturnUrl` is an OpenIddict authorize request, read its `client_id` (OpenIddict request parsing) and map it to the application.
2. Else a query value `?project={ProjectKey}` (multi mode only).
3. Else, if a valid application was stored in the encrypted "current application" cookie from an earlier step of the same flow, use it (set when step 1 or 2 succeeded; short life; `HttpOnly`).
4. Else single mode: the fixed application. Multi mode: the server default branding and **no login form for a specific project** (show a neutral page: "Open the sign-in link from your application").
The resolved application also drives which login methods, providers and languages the page shows. Test every branch, including an unknown `client_id` (neutral page, no stack trace, no project enumeration).

## 4. Layouts, components and pages

### 4.1 Layouts and shared parts (`Pages/Shared/`)
- `_Layout.cshtml` (base: doctype, `lang` and `dir` from culture, meta, title, favicon, theme color, `_TailwindCdn`, CSS variables, nonce).
- `_AuthLayout.cshtml`: centered card on a soft background, brand header (logo + `DisplayName` + tagline), content, footer (links, language switcher, "Powered by"). Works from 320 px width up.
- `_ManageLayout.cshtml`: top bar + left nav that becomes a drawer on mobile.
- `_AdminLayout.cshtml`: sidebar (collapsible), top bar with application switcher (global admin) or application name (project admin), breadcrumbs, toast area.
- Partials / tag helpers (all take simple parameters, all use resx keys): `Input` (label, hint, error, show/hide password toggle, `autocomplete` values), `OtpInput` (6 boxes or one field, `inputmode="numeric"`, `autocomplete="one-time-code"`), `Button` (primary, secondary, danger, loading state), `Alert` (info, success, warning, error), `Card`, `FormSection`, `Table` (sortable headers, empty state), `Pagination`, `ConfirmDialog` (uses `<dialog>`), `Toast`, `Badge`, `Tabs`, `Avatar`, `LanguageSwitcher`, `ProviderButton` (icon + display name), `PasswordRules` (live checklist from the application's password settings), `Breadcrumbs`, `EmptyState`.
- A `BrandingViewModel` available to every layout through a view component `BrandingViewComponent`, filled by `IPageApplicationResolver`.

### 4.2 Page inventory and requirements

Use the PageModels and flags that already exist (`ShowPhoneField`, `AllowedLoginMethods`, providers, `RegistrationEnabled`, and so on). Do not move logic into views. Show or hide parts only with the existing flags.

**Public (`_AuthLayout`)**
- `Account/Login`: fields depend on `AllowedLoginMethods` (user name / email / phone + password; phone or email + OTP request). Tabs or a selector if several methods are allowed. "Show password" toggle. Remember-me only if the application allows it. Links to register and forgot password (only if enabled). **External provider buttons** (from the provider list) under an "or continue with" divider. Error summary at top, field errors under fields, no user enumeration in messages. Keeps `ReturnUrl` in a hidden field and in all links.
- `Account/Register`: fields from the settings (user name / email / phone, first and last name if configured), password with the live `PasswordRules` checklist, password confirmation, terms checkbox when `TermsUrl` is set. Different text when `JoinMode` is `InviteOnly` or `Closed`.
- `Account/JoinApplication`: explains "You already have an account. Confirm it is you to join {DisplayName}", with the ownership proof step (email/phone code), then set a password for this application.
- `Account/VerifyOtp`, `Account/ConfirmEmail`, `Account/ConfirmPhone`: code entry, resend button with a visible countdown (`OtpResendSeconds`), attempts hint, clear expiry message.
- `Account/TwoFactor`: authenticator code or recovery code (toggle), "trust this device" only if the setting exists.
- `Account/ForgotPassword`, `Account/ResetPassword`: same neutral message whether the account exists or not.
- `Account/Consent`: application name + logo, list of requested scopes in plain words (scope descriptions from resx), Allow / Deny buttons, "Remember my choice".
- `Account/Lockout`, `Account/AccessDenied`, `Error`: friendly, branded, with a correlation id the user can send to support. Never show exception details outside Development.
- `Account/Logout`: confirmation page (when no valid `post_logout_redirect_uri`), then redirect.
- `Account/External/Confirm`, `Account/External/Callback` error states: clear messages for the error codes of the external provider work package.

**Self-service (`_ManageLayout`)**
- `Manage/Index` (profile: names, email, phone with confirm status), `Manage/ChangePassword`, `Manage/TwoFactor/Enable` (QR as inline SVG + manual key with copy button + code check), `Manage/TwoFactor/RecoveryCodes` (show once, copy and download as `.txt` via a server handler, warning text), `Manage/Sessions` (list, revoke), `Manage/ExternalLogins` (link, unlink, "last login method" rule), `Manage/Passkeys` (experimental, with an "Experimental" badge).

**Admin (`_AdminLayout`)** — see section 7.

### 4.3 Email templates
- Branded HTML + plain-text templates for confirm email, reset password, OTP, security alerts: inline CSS (email clients), table layout, logo by absolute URL (`Auth2:PublicBaseUrl` + `/branding/...`), `DisplayName`, support email. Localized through resx. Tests render each template and check that no raw HTML from branding fields is injected.

## 5. Design system and quality bar

- **Mobile first.** Check 320, 375, 768, 1024, 1440 px widths. No horizontal scroll. Tap targets at least 44×44 px. Forms single column on mobile; admin tables become stacked cards or horizontally scrollable areas with a sticky first column on small screens.
- **Look:** clean, calm, modern. Rounded corners (`rounded-xl`), soft shadows, generous spacing, one primary color from branding, neutral grays, clear focus rings, subtle transitions (respect `prefers-reduced-motion`). The auth card max width about 28 rem; background from `BackgroundStyle`.
- **Dark mode:** follow `prefers-color-scheme` with a manual toggle (stored in `localStorage`, applied before first paint to avoid flash, using a small nonce'd script). Check contrast in both modes.
- **Accessibility (WCAG 2.2 AA target):** every input has a `<label>`; errors linked with `aria-describedby` and announced (`role="alert"`); logical heading order; visible focus; keyboard-only use works for the whole flow including dialogs; color is never the only signal; the page `lang` and `dir` follow the culture. **RTL support** for Persian/Arabic cultures (use logical properties like `ps-`/`pe-`, `ms-`/`me-`, `text-start`).
- **Password manager friendly:** correct `name`, `autocomplete="username"`, `current-password`, `new-password`, `one-time-code`, `email`, `tel`.
- **Performance:** no layout shift from the logo (reserve space; width/height attributes), `loading="lazy"` for non-critical images, one small JS file `wwwroot/js/auth2-ui.js` (show/hide password, OTP behavior, countdown, theme toggle, copy buttons, dialogs, form double-submit guard).
- **Language switcher** for `SupportedCultures` of the application; stores the culture cookie.
- All user-facing text comes from resx keys. Add the new keys to `Pages.resx` and `Emails.resx` (and to the example `fa`, `nl`, `it` files where already present). A test must fail when a key used in a view is missing from `en`.

## 6. UI security rules (all tested)

1. Anti-forgery token in every POST form. `POST` for logout, challenge, revoke and delete actions (never `GET`).
2. No sensitive data in query strings (codes, tokens, passwords). `ReturnUrl` always validated by `IReturnUrlValidator` before use, also when rendered in a hidden field.
3. All output encoded; no `Html.Raw` on user or branding data (add an analyzer rule or a test that scans `.cshtml` files for `Html.Raw` and fails unless whitelisted with a comment).
4. Clickjacking protection: `frame-ancestors 'none'` and `X-Frame-Options: DENY` on all pages.
5. Cache headers: `Cache-Control: no-store` on every page that shows or accepts credentials, codes or recovery codes.
6. Recovery codes and TOTP secret are shown **once** and never logged.
7. No user enumeration: same wording and similar timing for existing and unknown accounts in login, forgot password, register and OTP request.
8. Branding uploads: section 3.2 rules.
9. The Tailwind CDN script uses a CSP **nonce**; if a Subresource Integrity (SRI) hash is possible for the pinned version, add it.
10. Error pages show a correlation id, not stack traces.

## 7. Admin console UI

Same data and authorization as the admin JSON API (call the same application services; do not duplicate rules). Project admins see only their application; the global admin sees an application switcher.

Screens: Dashboard (counts, recent audit events, health), Applications (list, create, details with tabs: General, **Branding** with live preview and upload, Login & Register methods, Password & Lockout, Tokens & Sessions, Redirect URIs, **Providers** (forms drawn from provider descriptors, redirect URL with copy button, test button), Permissions & Roles, Clients and secrets (secret shown once)), Users (search, filters, details with tabs: Profile, Applications, Roles, Permissions, Groups, Sessions, External logins, Audit; lock/unlock, reset 2FA, revoke sessions), Roles, Permissions (mark obsolete), Groups (members, roles, permissions), Audit log (filters by date, actor, action; export CSV), Server settings (global admin only).

Quality bar: server-side paging, sorting and filtering through query strings; empty states; confirm dialogs for destructive actions (type the name to confirm for delete); optimistic concurrency message when a row changed; success and error toasts after redirect (TempData); every form shows validation errors from the server; settings forms are generated from field metadata (`ProviderField`, settings descriptors) instead of hand-written HTML where possible; secrets shown as "Secret is set" with a Rotate button.

## 8. Tests (minimum)

Use `WebApplicationFactory` and parse HTML with **AngleSharp** (add to `Directory.Packages.props`). No internet needed (the CDN script is not loaded in tests).

- Every page returns 200 (or the expected redirect) and has: one `<h1>`, a `<title>`, `lang` and `dir`, anti-forgery token in each POST form, every input with a label, no `Html.Raw` use, CSP header with a nonce, `Cache-Control: no-store` on credential pages.
- **Branding:** two applications with different `DisplayName`, color and logo → the login page for App A (resolved from `client_id` in `ReturnUrl`) shows A's name in `<title>` and header and A's logo URL; App B shows B's; neutral page for unknown `client_id`; single mode shows the fixed application; empty branding falls back to defaults; HTML in `DisplayName` is encoded; invalid colors are rejected; low-contrast colors are rejected.
- **Assets:** upload valid PNG/JPEG/WebP; reject SVG, wrong magic bytes, too large, too big dimensions; ETag + 304; project admin cannot upload for another application; delete falls back to the monogram; audit written; `SettingsVersion` bumped; second instance sees the change.
- Login page shows only allowed methods and enabled providers for the application (flags from settings), and no register link when registration is disabled.
- Return URL and `ReturnUrl` hidden fields are preserved across login ↔ register ↔ forgot password links, and evil values are dropped.
- Localization: missing-key test; `fa` culture sets `dir="rtl"`; culture switcher sets the cookie.
- No user enumeration: HTML of "forgot password" is the same for known and unknown users (compare normalized body).
- TOTP page renders an inline SVG QR and never an external image URL; recovery codes page is `no-store`.
- Email templates render for each culture without raw HTML injection and contain the branded display name.
- Admin pages: authorization tests (project admin cannot open another application's pages; anonymous gets a redirect/401); paging and filter query strings work; destructive actions require POST and anti-forgery.
- CSP test: header contains the configured CDN host and nonce, `frame-ancestors 'none'`.
- Optional UI tests with **Playwright for .NET** (`[Trait("Category","UI")]`, excluded from the default run, documented how to run): screenshots of login, register, consent and admin dashboard at 375 / 768 / 1280 px, in light and dark mode. If browsers are not available, say so; do not fake screenshots.

## 9. Documentation (B1–B2 English, per step, after tests are green)

Each page has **How to use** and **How to implement / customize**.
- `docs/ui-guide.md`: overview of layouts, components and pages; how to change a text (resx), add a language (including RTL), change a view, add a component; Tailwind `Cdn` vs `Local` mode and how to build a local CSS with the Tailwind CLI later; dark mode; accessibility checklist; CSP settings.
- `docs/branding.md`: all branding fields with examples, how to upload a logo (admin API example with `curl`), size/type limits and why SVG is blocked, how the page finds the application (3.4), fallbacks, email branding, troubleshooting ("my logo does not show", "title shows the default name").
- Update: `ui-handover.md` → rename to `ui-implementation.md` (map of every page → PageModel → view → partials → flags), `server-settings.md` (Branding), `admin-api.md`, `api-reference.md`, `error-codes.md` (`asset_*`), `security.md` (UI rules, CSP), `localization.md`, `observability.md` (no change unless needed), `troubleshooting.md`, `feature-status.md`, `roadmap.md` (compiled CSS pipeline, SVG logos with sanitizing, per-application custom CSS, theme editor, screenshots in docs), `index.md`.
- If Playwright screenshots were produced, store them in `docs/images/` and reference them; otherwise do not mention screenshots.

## 10. Sample: SystemX.Auth2.Samples.Mvc

Path: `samples/SystemX.Auth2.Samples.Mvc` (net10.0, ASP.NET Core MVC). It uses the **Client package** as an **MVC cookie app with hosted UI**. It is a small but real app, styled with the same Tailwind CDN approach (simple layout, no heavy design).

Configuration (all from environment or `appsettings`, so Docker can set them):

```csharp
builder.Services
    .AddAuth2Client(builder.Configuration["Auth2:ServerUrl"]!, o =>
    {
        o.ApplicationName = builder.Configuration["Auth2:ApplicationName"]!;
        o.ClientId = builder.Configuration["Auth2:ClientId"]!;
        o.ClientSecret = builder.Configuration["Auth2:ClientSecret"]!;
        o.OnServerUnavailable = StartupFailureBehavior.Continue;
        o.OnApplicationNotFound = StartupFailureBehavior.Throw;
    })
    .ServerWithMultiApplication(builder.Configuration["Auth2:ProjectKey"]!)
    .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; o.PermissionList = Permissions.All; })
    .EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Manual; o.RoleList = Roles.All; })
    .UseHostedUi()
    .UseCookieAuthentication(o => { o.CookieName = ".Auth2.MvcSample"; o.AccessMode = AccessLoadMode.OnSignIn; })
    .EnableCurrentUser(o => o.CacheTtl = TimeSpan.FromMinutes(5))
    .EnableHealthCheck();
```

Pages and behavior:
- `GET /` public home: shows project name from config, login state, and links.
- `GET /Account/Login?returnUrl=` → challenge to the Server (hosted UI). `POST /Account/Logout` → sign out of cookie and server. `GET /Account/Denied`.
- `GET /Account/Me` (login required): shows `ICurrentUser` data: user name, email, roles, permissions, groups, scopes, access version, token expiry; plus a "Refresh access data" button.
- `GET /Orders` requires `[Auth2Permission("orders.read")]`; `POST /Orders/Create` requires `orders.write`; `GET /Admin` requires `[Auth2Role("Admin")]`; `GET /Reports` requires scope + permission example. Show 403 page branded simply.
- A "Try it" page that explains which demo user can open which page.
- `GET /health` using the Client health check (shows handshake state).
- Test the original-page behavior: open `/Orders` while logged out → login on the Server → land on `/Orders` (not on the home page).
- The project runs **two ways**: locally against any Server URL, and in Docker (section 11).
- `README.md` in the sample folder: steps, demo users, expected results per page, and how to change the project key and client id.

Tests: `tests/SystemX.Auth2.Samples.Mvc.Tests` using `WebApplicationFactory` with the in-memory Server: anonymous `/Orders` redirects to the Server authorize endpoint with the right `client_id`; after a simulated login, `/Orders` is 200 for a user with the permission and 403 without; `/Account/Me` shows roles and permissions; logout clears the cookie; handshake `Throw` vs `Continue` behavior.

## 11. Docker and docker-compose (run everything locally)

### 11.1 Files
```
/docker-compose.yml
/docker-compose.override.yml.example     (optional local tweaks)
/.dockerignore
/.env.example                            (ports, passwords for local use; real .env is git-ignored)
/src/SystemX.Auth2.Server/Dockerfile
/samples/SystemX.Auth2.Samples.Mvc/Dockerfile
/samples/SystemX.Auth2.Samples.MinimalApi/Dockerfile   (optional profile "api")
/scripts/smoke-test.sh  and  /scripts/smoke-test.ps1
/docs/docker-local.md
```
Build context is the **repository root** (the projects use Central Package Management, `Directory.Build.props` and project references).

### 11.2 Dockerfiles
- Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` to restore, build and publish; `mcr.microsoft.com/dotnet/aspnet:10.0` for runtime. Copy the `.csproj`, `Directory.Build.props`, `Directory.Packages.props` first and restore (layer caching), then copy the rest.
- Publish with `-c Release /p:UseAppHost=false`. Run as a **non-root** user (`USER $APP_UID`). Expose the port with `ASPNETCORE_HTTP_PORTS`. Add a `HEALTHCHECK` that calls `/health/ready` (Server) or `/health` (MVC sample); if `curl` is missing in the image, use a tiny `dotnet`-based check or install curl in the stage; explain the choice in a comment.
- The Server image must include the `migrate` and `seed` commands (same entrypoint: `dotnet SystemX.Auth2.Server.dll [command]`).

### 11.3 Services in `docker-compose.yml`

| Service | Image / build | Host port | Notes |
|---|---|---|---|
| `postgres` | `postgres:15` | 5432 (optional) | volume `pgdata`, healthcheck `pg_isready`, env from `.env` |
| `redis` | `redis:7` (use a current stable tag) | none | `--appendonly yes` + volume (keeps Data Protection-related and short-lived data safe enough for local use), healthcheck |
| `auth-migrate` | Server image, command `migrate` then `seed` (including demo data, 11.5) | none | `depends_on: postgres: condition: service_healthy`; `restart: "no"` |
| `auth-server` | Server image | **5100** | `depends_on: auth-migrate: condition: service_completed_successfully`; Redis on; healthcheck; `extra_hosts: ["auth.localhost:host-gateway"]` |
| `mvc-papertrade` | MVC sample image | **5200** | project key `papertrade`, `extra_hosts` as above |
| `mvc-bookshop` | same image, other env | **5201** | project key `bookshop`, different client id, shows different branding |
| `api-sample` (profile `api`) | MinimalApi sample | 5300 | optional |

### 11.4 The one hard problem: the same Server URL for the browser and for containers
The browser and the containers must use the **same issuer URL**. `localhost` inside a container means the container itself, so it cannot be used. Solution (implement exactly, test, document):
- The public Server URL is **`http://auth.localhost:5100`**. Modern browsers resolve `*.localhost` to 127.0.0.1. The Server container listens on **5100 inside the container too**, and publishes `5100:5100`.
- Every container that talks to the Server gets `extra_hosts: ["auth.localhost:host-gateway"]`, so `auth.localhost:5100` from inside a container reaches the published host port.
- The Server sets `Auth2__Issuer=http://auth.localhost:5100` and `Auth2__PublicBaseUrl=http://auth.localhost:5100`.
- MVC samples run at `http://localhost:5200` and `:5201`; their redirect URIs (`/signin-oidc`, `/signout-callback-oidc`) are registered by the demo seed.
- Document the fallback if `auth.localhost` does not resolve in a browser (add `127.0.0.1 auth.localhost` to the hosts file) and the Linux note for `host-gateway` (needs a recent Docker Engine).
- Plain HTTP is used **only for this local setup**: add `Auth2__AllowInsecureHttp=true`, which is honored **only when `ASPNETCORE_ENVIRONMENT=Development`** (the Server must refuse to start with it in any other environment). It relaxes OpenIddict's HTTPS requirement and the `Secure` cookie flag, and uses development signing keys. Print a loud warning in the logs when active.

### 11.5 Demo seed (Development only)
Add `seed --demo` (also run by `auth-migrate` in compose when `Auth2__Seed__Demo=true`). It creates, idempotently:
- Application `papertrade` (display name "PaperTrade", blue brand color, a generated logo PNG asset) and `bookshop` (display name "BookShop", green brand color, other logo). Create the logos with a small generator in code (simple colored monogram PNG) so no binary files are needed in the repo.
- OpenIddict clients: `mvc-papertrade` (secret `mvc-papertrade-secret`) and `mvc-bookshop` (secret `mvc-bookshop-secret`), with redirect URIs for ports 5200/5201 and the sample scopes. These secrets are **demo values**; document that they are public and for local use only.
- Permissions `orders.read`, `orders.write`; roles `Admin`, `Editor`, `Viewer` (role `Editor` exists in both applications with different meaning, to show per-application roles); a group `Finance` in papertrade.
- Demo users (printed in `docs/docker-local.md`): `sara` (papertrade, Admin), `john` (papertrade, Viewer: only `orders.read`), `maria` (both applications, with **different passwords per application** to show per-application credentials), `lock.me` (to test lockout). Passwords are demo values.
- The seeder must **refuse to run `--demo` outside Development**.

### 11.6 Smoke test
`scripts/smoke-test.(sh|ps1)` (after `docker compose up -d --build --wait`): checks `http://auth.localhost:5100/health/ready`, the discovery document and issuer value, the branding logo URL returns an image, `http://localhost:5200/health` shows handshake `Ok`, `GET /Orders` redirects to the authorize endpoint, and the login page `<title>` contains "PaperTrade" (and "BookShop" for the second app). Exit code non-zero on failure. Run it if Docker is available; otherwise state clearly that it was not run. At minimum run `docker compose config` to validate the file and say so.

### 11.7 Docs: `docs/docker-local.md`
How to use: prerequisites, `cp .env.example .env`, `docker compose up --build`, URLs and ports, demo users and what to try (login, original page after login, two apps with different branding and different passwords for `maria`, lockout, permission 403, logout), how to see logs, how to reset (`docker compose down -v`), how to run only some services, how to run the API sample (`--profile api`). How it works: the `auth.localhost` explanation, the migrate/seed step, health checks, why HTTP is allowed here and why it must never be used in production. Troubleshooting: port in use, `auth.localhost` not resolving, `host-gateway` on Linux, redirect URI mismatch, cookies not set (different host names), invalid issuer, containers start order, Windows line endings in shell scripts. Link it from the README and `getting-started.md`.

## 12. Build steps — each step = code → tests → green → docs

| # | Step | Tests first | Docs after green |
|---|---|---|---|
| 1 | UI foundation: `_TailwindCdn`, `TailwindMode`, layouts, CSP + nonce, `auth2-ui.js`, components, icons, resx keys, `IPageApplicationResolver` skeleton | Layout and CSP tests, missing-key test, component render tests | `ui-guide.md` (foundation, Tailwind modes, CSP) |
| 2 | Branding: settings, `application_assets`, upload/serve endpoints, admin API, resolver (3.4), view component, title/favicon/colors | All branding and asset tests | `branding.md`, `admin-api.md`, `server-settings.md`, `error-codes.md` |
| 3 | Public pages (login, register, join, OTP/confirm, 2FA, forgot/reset, consent, lockout, denied, error, logout, external provider buttons and states) | Page tests, enumeration test, return URL test, a11y checks | `ui-guide.md` (pages), `ui-implementation.md` |
| 4 | Self-service pages (profile, password, TOTP with SVG QR, recovery codes, sessions, external logins, passkeys badge) | Page and security tests | `ui-implementation.md`, `security.md` |
| 5 | Email templates | Template tests | `branding.md` (email part) |
| 6 | Admin console | Authorization, paging, destructive-action tests | `admin-api.md` (UI mapping), `ui-implementation.md` |
| 7 | MVC sample + its tests | Sample tests | Sample `README.md`, `client-setup.md` (MVC section), `getting-started.md` |
| 8 | Dockerfiles, compose, demo seed, smoke scripts | Seeder tests (demo is Development-only, idempotent), `docker compose config`, smoke test if Docker is available | `docker-local.md`, `deployment-and-scaling.md` link, README |
| 9 | Final pass: optional Playwright screenshots, accessibility review, docs check, `feature-status.md`, `roadmap.md`, `CHANGELOG.md` | Full suite green | Docs index |

## 13. Definition of done

- All pages have real views; none is empty. All text comes from resx. All forms work without JavaScript (except the progressive features listed).
- Branding works end to end for at least two applications and appears in the page title, header, favicon, theme color and emails.
- Tests in section 8 pass without internet. No secret appears in HTML, logs or API responses.
- The MVC sample works against the in-memory Server in tests and against the compose stack (if Docker is available; otherwise clearly reported as not run).
- `docker compose up --build` is documented and, if Docker was available, proven by the smoke test.
- Docs (How to use + How to implement / customize) exist for every step, code blocks compile or are marked illustrative.
- `CHANGELOG.md` and `feature-status.md` updated.

## 14. Pitfalls

1. The Tailwind CDN script is for development; keep the `Local` switch and the production warning.
2. CSP blocks the CDN script or injected styles if the policy is too strict → test the real header in a browser or with a CSP test.
3. The login page cannot know the project unless the application is resolved from `client_id` in `ReturnUrl` (3.4) → build and test the resolver first.
4. SVG logos can carry scripts → not allowed in v1.
5. `localhost` inside a container is the container itself → use the `auth.localhost` + `host-gateway` design (11.4), and keep the issuer identical for browser and containers.
6. Cookies are per host: `localhost:5200` and `auth.localhost:5100` are different sites; test the full redirect loop, not only single pages.
7. Plain HTTP and development keys are allowed only in Development; the Server must refuse the insecure flag elsewhere.
8. Demo secrets and demo users are public; never reuse them.
9. Hidden `ReturnUrl` fields can be edited by the user → always validate on the server.
10. Do not hard-code user-facing text in views; a missing resx key must fail a test.
11. Logos change often: use `ETag` and a short cache, and bump `SettingsVersion` so all instances refresh.
12. Do not claim screenshots, Docker runs or browser tests that were not executed.
