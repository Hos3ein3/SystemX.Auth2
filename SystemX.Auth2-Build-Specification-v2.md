# SystemX.Auth2 — Full Build Specification (Prompt for the Implementing LLM)

> Audience: an LLM (or engineer) that will design and build the whole `SystemX.Auth2` solution from zero.
> Read this file fully before writing any code. Follow the rules in section 1 for every piece of work.
> This is the single, final version. It already includes all decisions made so far.

---

## 0. Project in one paragraph

`SystemX.Auth2` is an open-source, self-hosted **Authentication and Authorization server** plus a **client NuGet package** for .NET 10.
The Server is an ASP.NET Core app (Razor Pages + Minimal API) built on **ASP.NET Core Identity** and **OpenIddict**, with **PostgreSQL 15**.
One Server can serve many applications ("projects"). Each application has its own settings, roles, permissions, groups and (by default) its own credentials.
The Server can also run in **single-application mode** (`services.ServerWithSingleApplication()`), where `ProjectKey` and the `user_applications` logic are not used.
The Client package is installed in APIs, MVC, Razor Pages and Blazor apps. It handles login, tokens, cookies, permission checks, permission sync and machine-to-machine calls with very little configuration.
It is **not** a replacement for Keycloak or Duende. It is a simple tool for small and medium .NET systems. The README and docs must say this clearly.

---

## 1. Working rules (MUST follow)

1. **Work in small pieces.** For every piece of code, in this order:
   1. Write the code.
   2. Write its unit tests (and integration tests when the piece touches the database, Redis or HTTP).
   3. Run the tests. Fix until all pass.
   4. Write or update its documentation page in `/docs`.
   5. Only then go to the next piece.
2. Never leave a failing test, a `TODO` without a note in `docs/roadmap.md`, or an undocumented public API.
3. **No UI work.** For every page or screen, create only the **PageModel/action and an empty `.cshtml` file** (only the `@page` and `@model` lines and one Razor comment). Do **not** write HTML, CSS or JavaScript. Another LLM will do the UI later. See section 5.10.
4. Use English for code, comments, logs and docs. **Docs must use simple English (CEFR B1–B2):** short sentences, active voice, no slang, explain every acronym once.
5. Target framework is **net10.0** for every project. C# latest, nullable enabled, implicit usings enabled, warnings as errors.
6. Do not invent features outside this file. If something is unclear, choose the safest option, write the decision in `docs/decisions.md`, and continue.
7. Security first: never log passwords, OTP codes, tokens, client secrets, recovery codes or TOTP keys. Hash secrets. Encrypt personal secrets at rest.
8. Public API names in this file are the contract. Keep them. You may add more members, but do not rename.
9. Licence: **MIT** (add `LICENSE`). Add `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `CHANGELOG.md`.
10. Keep `docs/decisions.md` (short ADR list) and update it when you make a design choice.

---

## 2. Locked decisions

| Topic | Decision |
|---|---|
| Framework | net10.0 everywhere |
| Database | PostgreSQL 15+ only (jsonb, `NULLS NOT DISTINCT`) |
| ORM | EF Core + Npgsql. Migrations are kept in the repo |
| Cache | Redis optional. In-memory fallback for single instance |
| Logging / tracing | Serilog + OpenTelemetry (OTLP) |
| Rate limiting | ASP.NET Core built-in `AddRateLimiter`, Redis-backed counters when Redis is on |
| Tokens | OpenIddict (authorization code + PKCE, client credentials, refresh token) |
| Identity | ASP.NET Core Identity (the 6 standard tables) + custom stores |
| Default credential scope | `PerApplication` |
| Server mode API | `services.ServerWithSingleApplication()` — **Server layer only**. No such method on the Client |
| Client mode API | Only `.ServerWithMultiApplication(projectKey)`. No call = Client expects a single-application server. The handshake checks it |
| Localization | **resx files only.** No translations table |
| First admin | Hard-coded seed (`Auth2Seeder`). Docs explain how to move it to environment variables |
| Test stack | xUnit, Testcontainers (PostgreSQL, Redis), WebApplicationFactory, NSubstitute, Shouldly |
| Licence | MIT |
| Name prefix | `SystemX.` for all projects, namespaces and NuGet IDs |
| UI | Not now. Actions + empty pages only |
| Gateway | `SystemX.Auth2.Gateway` is an empty project + small README |
| Samples | One Minimal API sample only (no MVC sample, no docker-compose now; mention them in docs/roadmap) |
| Docs | Markdown only, in `/docs`. No DocFX, no site generator |
| Workflow rule | Code → tests → pass → docs. Must be written in `docs/contributing-workflow.md` |

### Feature support level

| Feature | Level in v1 |
|---|---|
| OIDC and OAuth 2.0 (code + PKCE, client credentials, refresh) | Fully implemented |
| Username / email / phone + password login | Fully implemented |
| Phone + OTP login | Fully implemented (SMS sender is an interface; the development implementation logs a masked code only in Development) |
| Account lockout | Fully implemented |
| TOTP 2FA + recovery codes | Fully implemented (server logic and endpoints; pages are empty) |
| Consent | Fully implemented (logic; page is empty) |
| Permissions, Roles, Groups, UserPermissions, RolePermissions | Fully implemented |
| Permission / Role / Group sync (Push / Pull / Both / None) | Fully implemented |
| SingleActiveSession | Fully implemented |
| Localization (resx) | Fully implemented |
| Admin JSON API (global + project admin) | Fully implemented (screens are empty pages) |
| Email / SMS sending | Interfaces + development implementations |
| Social login (Google, Microsoft, GitHub) and identity brokering (generic OIDC) | Implemented, marked **experimental** |
| Passkeys (.NET 10 Identity built-in) | Implemented, marked **experimental** |
| SAML | **Not in v1.** Extension point + docs only |
| LDAP / Active Directory | **Not in v1.** Extension point + docs only |
| Gateway package | **Empty project + README only** |

Every item above, including the "not in v1" ones, **must be explained in the docs** (what it is, status, why, and how to add it later).

---

## 3. Repository layout

```
SystemX.Auth2/
├─ README.md
├─ LICENSE  SECURITY.md  CONTRIBUTING.md  CODE_OF_CONDUCT.md  CHANGELOG.md
├─ Directory.Build.props
├─ Directory.Packages.props            (Central Package Management)
├─ global.json
├─ .editorconfig  .gitignore  .gitattributes
├─ SystemX.Auth2.slnx
├─ src/
│  ├─ SystemX.Auth2.Contracts/
│  ├─ SystemX.Auth2.Server/
│  ├─ SystemX.Auth2.Client/
│  └─ SystemX.Auth2.Gateway/           (empty + README.md)
├─ samples/
│  └─ SystemX.Auth2.Samples.MinimalApi/
├─ tests/
│  ├─ SystemX.Auth2.Contracts.Tests/
│  ├─ SystemX.Auth2.Server.Tests/             (unit)
│  ├─ SystemX.Auth2.Server.IntegrationTests/
│  ├─ SystemX.Auth2.Client.Tests/             (unit)
│  └─ SystemX.Auth2.Client.IntegrationTests/  (Client against a real Server in-memory host)
└─ docs/   (see section 10)
```

### 3.1 Build files

- `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=true`, `Deterministic=true`, `GenerateDocumentationFile=true` for `src/*`, SourceLink, package metadata (Authors, Company, Description, RepositoryUrl, PackageLicenseExpression=MIT, PackageReadmeFile).
- `Directory.Packages.props`: `ManagePackageVersionsCentrally=true`. List every package version here. Projects use `<PackageReference Include="X" />` without version.
- Packages (pick the latest stable that supports net10.0):
  - Server: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`, `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore` (if available), `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `StackExchange.Redis`, `Microsoft.AspNetCore.DataProtection.StackExchangeRedis`, `AspNetCore.HealthChecks.NpgSql`, `AspNetCore.HealthChecks.Redis`, `Microsoft.AspNetCore.Authentication.Google`, `Microsoft.AspNetCore.Authentication.MicrosoftAccount` (+ a GitHub provider package; experimental).
  - Client: `OpenIddict.Validation.AspNetCore` **or** `Microsoft.AspNetCore.Authentication.JwtBearer` (choose one and record it in `docs/decisions.md`), `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Extensions.Options.DataAnnotations`.
  - Tests: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Microsoft.AspNetCore.Mvc.Testing`, `NSubstitute`, `Shouldly`, `coverlet.collector`.
- Packaging: Client and Contracts are packable. Server is **not** a NuGet package (publish it as a normal app; docs explain how). Gateway is packable but empty for now.

---

## 4. SystemX.Auth2.Contracts

Only DTOs, enums and constants. **No dependency** on ASP.NET, EF or OpenIddict. Must be safe to share between Server and Client.

```
Contracts/
├─ Common/        Result.cs, ApiError.cs, PagedResult.cs
├─ Applications/  ApplicationDto.cs, CreateApplicationRequest.cs, UpdateApplicationRequest.cs,
│                 ApplicationSettingsDto.cs (see 5.3), ClientHandshakeRequest.cs, ClientHandshakeResponse.cs
├─ Auth/          LoginRequest.cs, LoginResponse.cs, RegisterRequest.cs, RegisterResponse.cs,
│                 TwoFactorRequest.cs, ForgotPasswordRequest.cs, ResetPasswordRequest.cs, ChangePasswordRequest.cs
├─ Users/         CurrentUserInfo.cs, UserDto.cs
├─ Access/        PermissionDefinition.cs, RoleDefinition.cs, GroupDefinition.cs,
│                 PermissionSyncRequest.cs, RoleSyncRequest.cs, GroupSyncRequest.cs, SyncResponse.cs
├─ Enums/         PermissionSyncPolicy, RoleSyncPolicy, GroupSyncPolicy, ScanningPolicy,
│                 UiMode, ClientType, ServerMode, CredentialScope, LoginMethod, StartupFailureBehavior,
│                 HandshakeStatus, ConsentMode, AccessLoadMode, CurrentUserSource
└─ Constants/     Auth2Scopes.cs, Auth2Claims.cs, Auth2Routes.cs, Auth2ErrorCodes.cs, Auth2Roles.cs
```

Key definitions:

```csharp
public enum PermissionSyncPolicy { None, Push, Pull, Both }
public enum RoleSyncPolicy       { None, Push, Pull, Both }
public enum GroupSyncPolicy      { None, Pull }          // groups are decided by admins
public enum ScanningPolicy       { Manual, AssemblyScan, Both }
public enum UiMode               { None, Hosted, Self }
public enum ClientType           { Api, Mvc, RazorPages, Blazor, Machine }
public enum ServerMode           { SingleApplication, MultiApplication }
public enum CredentialScope      { PerApplication, Shared }
public enum LoginMethod          { UserNamePassword, EmailPassword, PhonePassword, PhoneOtp, EmailOtp }
public enum StartupFailureBehavior { Throw, Continue }
public enum HandshakeStatus      { Ok, ServerUnavailable, NotFound, Rejected }
public enum ConsentMode          { Implicit, Explicit, Systematic }
public enum AccessLoadMode       { Lazy, OnSignIn, RolesInCookie, None }
public enum CurrentUserSource    { TokenOnly, TokenAndServer }

public sealed record PermissionDefinition(string Key, string? DisplayName = null, string? Description = null, string? Group = null);
public sealed record RoleDefinition(string Name, string? DisplayName = null, string? Description = null, IReadOnlyList<string>? Permissions = null);
public sealed record GroupDefinition(string Name, string? DisplayName = null);
public sealed record SyncResponse(int Added, int Updated, int Unchanged, IReadOnlyList<string> AddedKeys);

public sealed record CurrentUserInfo(Guid UserId, string? UserName, string? Email, string? PhoneNumber,
    string? FirstName, string? LastName, string? FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups,
    IReadOnlyList<string> Scopes, int AccessVersion);

// ProjectKey is null when the client runs against a single-application server.
public sealed record ClientHandshakeRequest(string? ProjectKey, string ApplicationName, string ClientVersion);

public sealed record ClientHandshakeResponse(Guid ApplicationId, string? ProjectKey, string ApplicationName,
    ServerMode ServerMode, string Issuer, int SettingsVersion, TimeSpan? CookieLifetime, bool SingleActiveSession);
```

Constants:
- Scopes: `auth2.register`, `auth2.login`, `auth2.me`, `auth2.client.handshake`, `auth2.permissions.sync`, `auth2.roles.sync`, `auth2.groups.read`, `auth2.admin`, `auth2.admin.global`.
- Claims: `sub`, `preferred_username`, `email`, `phone_number`, `given_name`, `family_name`, `name`, `role`, `scope`, `client_id`, `pv` (access version), `app` (application id), `projectkey` (omitted in single-application mode), `amr`, `sid`.
- Routes: all `/api/auth2/...` route strings in one static class so Client and Server never disagree.
- Error codes: `invalid_credentials`, `locked_out`, `two_factor_required`, `not_registered_in_application`, `application_not_found`, `project_key_or_name_mismatch`, `server_mode_mismatch`, `password_too_short`, ... (string constants used by API errors and localization keys).

Tests: serialization round-trip for every DTO (System.Text.Json), enum string conversion, constants uniqueness.

---
## 5. SystemX.Auth2.Server

ASP.NET Core Razor Pages app with Minimal API endpoints. **Folders, not class libraries**, in a Clean Architecture style.

```
Server/
├─ Program.cs
├─ Domain/            entities, value objects, enums, domain events (no EF, no ASP.NET)
├─ Application/       services, use cases, interfaces, validators, DTO mapping
├─ Persistence/       DbContext, EF configurations, migrations, stores, Seeding/
├─ Infrastructure/    DependencyInjection, Redis, email/SMS senders, rate limiting, logging, telemetry, OpenIddict setup, localization, key management, external providers
├─ Endpoints/         Minimal API route groups (one file per area)
├─ Pages/             Razor Pages (PageModel + EMPTY .cshtml only)
├─ Resources/         .resx files (Errors, Validation, Pages, Emails)
└─ appsettings*.json
```

Dependency rule: `Domain` ← `Application` ← (`Persistence`, `Infrastructure`) ← (`Endpoints`, `Pages`). Add an architecture test that fails if `Domain` references `Microsoft.AspNetCore` or `EntityFrameworkCore`.

### 5.1 Server modes

```csharp
// SystemX.Auth2.Server / Program.cs
builder.Services.AddSystemXAuth2Server(builder.Configuration, o => { ... });

builder.Services.ServerWithSingleApplication();   // optional. Without it the server is multi-application.
```

- Default is **multi-application**: uses `ProjectKey`, `applications`, `user_applications`.
- `ServerWithSingleApplication()` is an extension method on `IServiceCollection`, in `Infrastructure/DependencyInjection/`. It lives in the **Server layer only**. It sets `ServerMode.SingleApplication` in `Auth2ServerOptions` and registers `SingleApplicationContext` as `IApplicationContext`.
- In single-application mode:
  - `ProjectKey` is **ignored** everywhere (handshake, register, admin API). The `projectkey` token claim is omitted.
  - The `user_applications` table is **not read and not written**. The table and the `applications.ProjectKey` column still exist in migrations (so the owner can switch to multi mode later), but no business logic uses them.
  - Credentials live in the user row (`AspNetUsers`), so credential scope is effectively `Shared`.
  - Roles, permissions and groups use **one fixed application row** (created by the seeder; name from `Auth2:SingleApplication:Name`, default `Default`). `ApplicationId` stays filled with this row, so a later switch to multi mode needs no data change in those tables.
  - The `_global` admin project is still created. The GlobalAdmin works the same way.
- Use **one** abstraction and two strategies, never `if (single)` all over the code:
  - `IApplicationContext { Task<Auth2Application> GetRequiredAsync(ct); }`
  - `SingleApplicationContext` and `MultiApplicationContext` (resolved from token `client_id`, from the authorize request `client_id`, or from the admin's selected project).
- Commands `convert-to-multi` and `convert-to-single` copy credentials between the user row and `user_applications`. Document the data-loss risk when going to single.
- Tests: in single mode no row is ever written to `user_applications`; handshake and register work without `ProjectKey`; switching with the command moves credentials correctly.
- Docs: explain this in `server-setup.md`, `credential-scope.md`, `concepts.md`.

### 5.2 Domain model (PostgreSQL)

Use `Guid` keys (UUID v7 if available), `timestamptz` for dates, snake_case names (choose one way, document it).

**Identity tables (the standard 6, customized)**
- `AppUser : IdentityUser<Guid>` + `FirstName`, `LastName`, `DisplayName`, `PreferredCulture`, `IsActive`, `CreatedAtUtc`, `Profile` (jsonb, optional).
- `AppRole : IdentityRole<Guid>` + `ApplicationId` (nullable = global role), `Description`, `IsSystem`.
  - **Remove** the default unique index on `NormalizedName`. Add unique index `(ApplicationId, NormalizedName)` with `NULLS NOT DISTINCT` (PostgreSQL 15). Reason: role `writer` in App 1 (blog) and App 2 (book) are different roles.
- `IdentityUserRole<Guid>`, `IdentityUserClaim<Guid>`, `IdentityRoleClaim<Guid>`, `IdentityUserLogin<Guid>`, `IdentityUserToken<Guid>` standard. Add nullable `ApplicationId` to `IdentityUserLogin` and `IdentityUserToken` so external logins and tokens can be per application.
- User name / email uniqueness: global (one person = one `AppUser`). Test it.

**New tables**

| Table | Main columns | Notes |
|---|---|---|
| `applications` | Id, `ProjectKey` (unique), Name, Description, `OpenIddictApplicationId`, `IsEnabled`, `SettingsJson` (jsonb), `SettingsVersion`, `AccessVersion`, CreatedAtUtc | One row per project. `_global` is the built-in project for developers/global admins |
| `user_applications` | `UserId`, `ApplicationId` (composite PK), `JoinedAtUtc`, `Status`, `PasswordHash`, `SecurityStamp`, `TwoFactorEnabled`, `AuthenticatorKey` (encrypted), `AccessFailedCount`, `LockoutEnd`, `LockoutEnabled`, `UserAccessVersion`, `LastLoginUtc`, `ActiveSessionId` | Credentials per application (credential scope = PerApplication). Not used in single mode |
| `user_application_recovery_codes` | Id, `UserId`, `ApplicationId`, `CodeHash`, `UsedAtUtc` | Hashed |
| `permissions` | Id, `ApplicationId`, `Key` (lowercase), DisplayName, Description, `Group`, `Source` (manual/sync), `IsObsolete` | Unique `(ApplicationId, Key)` |
| `user_permissions` | `UserId`, `PermissionId`, `ApplicationId`, `GrantedAtUtc`, `GrantedBy` | Direct grants |
| `role_permissions` | `RoleId`, `PermissionId` | |
| `groups` | Id, `ApplicationId`, Name (unique per app), DisplayName, Description | |
| `user_groups` | `UserId`, `GroupId` | |
| `group_roles` | `GroupId`, `RoleId` | |
| `group_permissions` | `GroupId`, `PermissionId` | |
| `audit_events` | Id, `OccurredAtUtc`, `ApplicationId`, `ActorUserId`, `ActorClientId`, `Action`, `TargetType`, `TargetId`, `IpAddress`, `UserAgent`, `DataJson` | Never store secrets |
| `otp_codes` | `UserId`, `ApplicationId`, `Purpose`, `CodeHash`, `ExpiresAtUtc`, `Attempts`, `ConsumedAtUtc` | Use Redis when enabled, this table otherwise |
| `data_protection_keys` | Standard EF store | For scale-out |
| OpenIddict tables | Applications, Authorizations, Scopes, Tokens | Via `options.UseOpenIddict()`. Redirect URIs live here (exact match) |

There is **no translations table**. Localization uses resx files only (5.17).

Rules:
- Global EF **query filter** by current application for every tenant table (`roles`, `permissions`, `groups`, `user_applications`, `audit_events`, ...). Only the global developer context uses `IgnoreQueryFilters()`, and only through an explicit `IGlobalScope` service. Test this.
- `ProjectKey`: lowercase letters, digits, `-`, `_`; 3–64 chars; `_global` is reserved.
- `SettingsJson` is mapped to a typed `ApplicationSettings` object with `jsonb` (`OwnsOne(...).ToJson()` or a value converter). Keep a **schema version** inside the JSON.

### 5.3 ApplicationSettings (jsonb) — every field

All have safe defaults. All are editable by the developer (global admin). Mark each setting `GlobalOnly` or `ProjectAdminEditable` in code and docs.

**Identity rules**
- `AllowedLoginMethods` (list of `LoginMethod`), `AllowedRegisterMethods`
- UserName: `MinLength`, `MaxLength`, `AllowedCharacters` (regex), `RequireUnique`
- Password: `MinLength` (default 10), `MaxLength` (128), `RequireDigit`, `RequireLower`, `RequireUpper`, `RequireNonAlphanumeric`, `UniqueChars`, `BannedPasswords` / breached-list hook (hook only)
- Email: `RequireConfirmedEmail`, `AllowedDomains` / `BlockedDomains`
- Phone: `PhoneNumberRegex` (default E.164), `RequireConfirmedPhone`, `RegexTimeoutMs` (default 200, protects from ReDoS)
- `RegistrationEnabled`, `JoinMode` (`AutoJoin` | `InviteOnly` | `Closed`)
- `LoginRequiredAfterRegister`

**Lockout:** `LockoutEnabled`, `MaxFailedAttempts`, `LockoutMinutes`, `LockoutForNewUsers`

**Two-factor and OTP:** `TwoFactorMode` (`Off`, `Optional`, `Required`), `AllowTotp`, `AllowRecoveryCodes` (count default 10), `OtpLength` (6), `OtpLifetimeSeconds` (120), `OtpMaxAttempts` (5), `OtpResendSeconds` (60), `AllowPasskeys` (experimental)

**Sessions and tokens:** `AccessTokenMinutes` (15), `RefreshTokenDays`, `IdentityTokenMinutes`, `CookieLifetimeMinutes`, `CookieSliding`, `SingleActiveSession` (when a user signs in, revoke other sessions/tokens for **this application**), `RequireConsent` / `ConsentMode`

**Redirects:** `RedirectAfterLogin`, `RedirectAfterRegister`, `RedirectAfterLogout` (fallbacks only, see 5.9), `AllowedRedirectUris`, `AllowedPostLogoutRedirectUris` (exact match, no wildcards)

**Client behaviour:** `AllowedClientTypes`, `AllowedScopes`, `PermissionSyncAllowed`, `RoleSyncAllowed`, `AllowRemovePermissionsFromSync` (default false), default `UiMode`, `AllowSelfUiLogin` (enables the password grant for this application, default false)

**Localization and branding:** `DefaultCulture`, `SupportedCultures` (these only choose a language; they are not texts), `BrandName`, `SupportEmail`

**External logins (experimental):** `ExternalProviders[]`: `Name`, `Type` (Google/Microsoft/GitHub/GenericOidc), `ClientId`, `ClientSecretProtected`, `Authority`, `Scopes`, `Enabled`

**Credential scope:** `CredentialScope` (default `PerApplication`) — server option, plus per-application override only if the server allows it.

Validation: write `ApplicationSettingsValidator`. Reject unsafe values (`MaxFailedAttempts < 1`, `Password.MinLength < 6`, wildcard redirect URIs, invalid regex, regex without timeout). Cache settings per application (memory or Redis), invalidate on change, bump `SettingsVersion` on every change.

### 5.4 Multi-application identity design

- One real person = one `AppUser` row (identity data and confirmed flags).
- `user_applications` says "this person belongs to this application" and holds the **credential state** when `CredentialScope = PerApplication`: password hash, security stamp, TOTP key, recovery codes, lockout, failed count.
- Email and phone confirmation are **global** (same mailbox).
- If `CredentialScope = Shared` (or single-application mode), credentials are in `AspNetUsers` as usual.
- **Joining a second application** must not allow account pre-hijacking. Flow: user asks to join → server requires proof of ownership (confirmed email or phone OTP) → user sets a password for this application → `user_applications` row is created. Never "just add a row"; never let a caller set a password for an existing user without proof.
- Registration is idempotent: create `AppUser` only when it does not exist; always ensure the `user_applications` row; return `UserWasCreated`, `JoinedApplication`. Use a transaction; handle duplicate-key races.
- The calling application is **always taken from the authenticated client token**, never from the request body.
- Create the `user_applications` row **before** setting the password (same transaction).

### 5.5 Custom Identity stores and managers

- `Auth2UserStore : UserStore<AppUser, AppRole, Auth2DbContext, Guid>` with overrides for `IUserPasswordStore`, `IUserSecurityStampStore`, `IUserTwoFactorStore`, `IUserAuthenticatorKeyStore`, `IUserTwoFactorRecoveryCodeStore`, `IUserLockoutStore`. Use an `ICredentialState` abstraction with two strategies: `UserRowCredentialState` and `UserApplicationCredentialState`. The store picks one by `IApplicationContext` and `CredentialScope`.
- `Auth2RoleStore`: find by name **inside the current application** (plus global roles). The role validator must work with it.
- Override `AddToRoleAsync`, `IsInRoleAsync`, `RemoveFromRoleAsync`, `GetRolesAsync` to use the current application.
- `Auth2UserManager`, `Auth2RoleManager`, `Auth2SignInManager`: thin classes. Project-based behaviour comes from services, not from big overrides.
- `Auth2IdentityErrorDescriber : IdentityErrorDescriber` using `IStringLocalizer` (resx).
- Per-project Identity rules: do **not** change the global `IdentityOptions`. Use custom `IPasswordValidator<AppUser>`, `IUserValidator<AppUser>` and an `IAuthFlowResolver` that reads `ApplicationSettings` for the current application.
- `LoginService` order: application enabled → user belongs to application (multi mode) → login method allowed → lockout → password/OTP → 2FA → consent → single active session.

Required tests: same role name in two applications; same user in two applications with two passwords; App A password fails in App B; TOTP enabled in A not required in B; lockout in A does not lock B; joining a second application requires proof.

### 5.6 Application layer — services (interfaces in `Application/`)

`IApplicationContext`, `IApplicationService`, `IApplicationSettingsProvider`, `IRegisterService`, `ILoginService`, `ITokenIssuer`, `ITwoFactorService`, `IOtpService`, `IPasswordService` (forgot/reset/change), `IConsentService`, `IAccessService` (effective access, access version), `IPermissionSyncService`, `IRoleSyncService`, `IGroupService`, `IAdminUserService`, `IAuditService`, `ISessionService` (single active session, revoke), `IReturnUrlValidator`, `IEmailSender`, `ISmsSender`, `IClock`, `IGlobalScope`, `ISecretHasher`, `IKeyRing` (signing/encryption key rotation), `IExternalProviderService` (experimental), `IPasskeyService` (experimental).

**Effective permissions** = direct user permissions ∪ permissions of user roles ∪ permissions of roles of user groups ∪ permissions of user groups, filtered by the application. Cache by `(application, user, accessVersion)`. Increase `UserAccessVersion` / `Application.AccessVersion` on every change that affects access (role, group, permission, membership, lockout, password reset).

### 5.7 OpenIddict configuration (best practice)

- `UseOpenIddict()` on the same DbContext. Core + Server + Validation (the server validates its own admin API tokens).
- Endpoints: `/connect/authorize`, `/connect/token`, `/connect/endsession`, `/connect/userinfo`, `/connect/introspect` (optional), `/connect/revoke`, discovery and JWKS.
- Flows: **authorization code + PKCE (required)**, **client credentials**, **refresh token** (rotation on, reuse detection on).
- **Password grant is OFF by default.** OAuth 2.1 removes it. Enable it **per application** only when `AllowSelfUiLogin = true` and the client is confidential (see 5.8).
- Use **passthrough** mode for authorize and token endpoints: your handlers run the logic and call `SignIn`.
- Short access tokens (default 15 min), refresh rotation, one-time authorization codes, exact redirect URI match.
- Token format decision (signed + encrypted JWT with stored token entries so revocation works, or reference tokens) goes into `docs/decisions.md`.
- Keys: development certificates only in Development. In Production load keys from a configured source (file, PFX, Key Vault interface `IKeyRing`) and support **rotation** (add a new key, keep the old one until tokens expire). Document the steps.
- Claims in tokens: `sub`, `preferred_username`, `email`, `phone_number`, names, `role` (only roles of that application), `scope`, `app`, `projectkey` (multi mode only), `pv` (access version), `amr`, `sid`. **Do not** put the full permission list in the token.
- Scopes: seed the standard ones plus all `auth2.*` scopes. Each client may request only the scopes allowed in its application settings.
- Each `Application` creates/updates an OpenIddict application (client id, hashed secret, permissions, redirect URIs, consent type) in one transaction. Secrets are shown **once**.

### 5.8 Login and token issuing (best practice design)

There is **one** place that decides if a login is valid: `ILoginService`.

- **Hosted UI (default and recommended):** browser → `/connect/authorize` → login page (empty page) → `ILoginService` → back to authorize → code → client gets tokens. The client app never sees the password.
- **Self UI / mobile / SPA backends (optional):** the client collects credentials and calls the **token endpoint** (`/connect/token`) handled in the passthrough token action:
  - `grant_type=password` only when the application has `AllowSelfUiLogin = true`, the calling client is **confidential** and authenticated with its secret, and rate limiting is on.
  - 2FA step: custom grant type `urn:systemx:auth2:grant:two_factor` (inputs: `two_factor_token`, `code` or `recovery_code`). The first step returns error `two_factor_required` + a short-lived `two_factor_token`.
  - OTP login: custom grant type `urn:systemx:auth2:grant:otp` (OTP request is a separate endpoint `POST /api/auth2/otp/request`).
  - Refresh: standard `refresh_token`.
- Client `LoginAsync(...)` calls `/connect/token`. **The server has no separate `/api/auth2/login` endpoint** (one issuing path = fewer bugs).
- Errors use standard OAuth `error` plus `error_description` and a stable `error_code` from Contracts. Wrong user and wrong password return the **same** error.
- Always: rate limit, lockout, audit event (success and failure), constant-time compare, no user enumeration, `Cache-Control: no-store` on token responses.
- `SingleActiveSession`: on successful login (setting on) revoke all other tokens/authorizations of this user **in this application**, change the per-application security stamp, remove cached access data, write audit event `session.revoked_others`.
- `docs/security.md` must explain why the password grant is optional and risky, and recommend Hosted UI.

### 5.9 Return URL and redirect rules

- The client app (OIDC handler) stores the page the user wanted (page B). The server only has to return to `/connect/authorize?...` using `ReturnUrl`. Do not break this chain.
- Priority after login: **1)** valid `ReturnUrl` (local URL or allowed redirect) → **2)** application `RedirectAfterLogin` → **3)** server home.
- Same for register (`RedirectAfterRegister`) and logout (`post_logout_redirect_uri` if registered → `RedirectAfterLogout` → home).
- Carry `ReturnUrl` through login → register → confirm email → forgot password → 2FA → consent. Include it in confirmation links.
- `IReturnUrlValidator`: accept local URLs only when they start with `/` and not `//` or `/\`. Accept absolute URLs only if they exactly match the application's allowed list. Test: `https://evil.com`, `//evil.com`, `/\evil.com`, `javascript:`, encoded variants.

### 5.10 Pages and Actions (NO HTML/CSS)

For each page create the `PageModel` with working handlers (`OnGet`, `OnPost`), binding properties, validation attributes and service calls. Create the `.cshtml` with **only** `@page`, `@model` and `@* UI: to be done *@`. No markup, no styles, no scripts, no layout.

Public: `Account/Login`, `Account/Register`, `Account/Logout`, `Account/ForgotPassword`, `Account/ResetPassword`, `Account/ConfirmEmail`, `Account/ConfirmPhone`, `Account/VerifyOtp`, `Account/TwoFactor`, `Account/Lockout`, `Account/AccessDenied`, `Account/Consent`, `Account/JoinApplication`, `Account/External/Callback` (experimental), `Error`.

Self-service: `Manage/Index`, `Manage/ChangePassword`, `Manage/TwoFactor/Enable` (returns the `otpauth://` URI + shared key for the future UI), `Manage/TwoFactor/RecoveryCodes`, `Manage/Sessions`, `Manage/Passkeys` (experimental).

Admin (developer and project admin): `Admin/Index`, `Admin/Applications/{Index,Create,Edit,Details}`, `Admin/Users/{Index,Details,Edit}`, `Admin/Roles/*`, `Admin/Permissions/*`, `Admin/Groups/*`, `Admin/Settings/Edit`, `Admin/AuditLog/Index`, `Admin/Sessions/Index`.

Pages use the **same application services as the JSON admin API** (5.12). A PageModel exposes simple flags such as `ShowPhoneField` computed with plain `if/else` from the application settings, so the future UI only reads flags. Messages come from `IStringLocalizer` keys.

### 5.11 Public and client-facing endpoints (Minimal API)

Routes come from `Auth2Routes`. Group with `MapGroup` in `Endpoints/`. JSON only. Errors use `ApiError { code, message, details? }` (problem-details compatible), localized `message`.

| Method + route | Auth | Purpose |
|---|---|---|
| `POST /api/auth2/register` | M2M token, scope `auth2.register` | Create user if needed and ensure `user_applications` row (5.4). Application from token |
| `POST /api/auth2/otp/request` | M2M token, scope `auth2.login` | Send OTP (rate limited per user + IP) |
| `POST /api/auth2/password/forgot` | M2M token | Start reset (always same response) |
| `POST /api/auth2/password/reset` | M2M token | Finish reset with token |
| `POST /api/auth2/password/change` | User token | Change own password for the token's application |
| `POST /api/auth2/email/confirm`, `/phone/confirm` | M2M token | Confirm |
| `POST /api/auth2/2fa/setup`, `/2fa/enable`, `/2fa/disable`, `/2fa/recovery-codes` | User token | TOTP management |
| `GET /api/auth2/me` | User token, scope `auth2.me` | Returns `CurrentUserInfo` for the token's user and application |
| `POST /api/auth2/client/handshake` | M2M token, scope `auth2.client.handshake` | Check ProjectKey + ApplicationName + server mode (5.13) |
| `POST /api/auth2/permissions/sync` | M2M token, scope `auth2.permissions.sync` | Idempotent upsert, never deletes (5.14) |
| `POST /api/auth2/roles/sync` | M2M token, scope `auth2.roles.sync` | Same for roles |
| `GET /api/auth2/permissions`, `/roles`, `/groups` | M2M token | Pull |
| `GET /api/auth2/users/{id}/access` | M2M token with `auth2.admin` | Machine reads one user's access |
| `GET /health/live`, `/health/ready` | Anonymous | Health checks (Postgres, Redis if on) |

OpenIddict endpoints from 5.7 exist as well. `/me` reads the user from the token only; it must **not** accept a user id.

Example for docs:

```http
GET /api/auth2/me
200 { "userId":"...", "userName":"sara", "email":"sara@example.com", "roles":["Editor"],
      "permissions":["orders.read"], "groups":["Finance"], "scopes":["openid","auth2.me"], "accessVersion":7 }
401 { "code":"invalid_token" }
```

### 5.12 Admin JSON API (for the future UI)

Base route `/api/auth2/admin/...`. Needs a user token with scope `auth2.admin`.

- `GlobalAdmin`: user of the built-in `_global` project. Can see and manage everything: applications, settings, users, roles, permissions, groups, audit, keys, server options.
- `ProjectAdmin`: admin of one application (e.g. "PaperTrade"). Can see and manage **only** the records of that application.
- Enforce with **code**: policies `RequireGlobalAdmin`, `RequireProjectAdmin` + resource check + the EF query filter. Never trust the UI.
- Only `GlobalAdmin` may: create/delete applications, edit `GlobalOnly` settings, rotate keys, change server options, view all audit.
- Project admins cannot grant `GlobalAdmin`, cannot read other applications, cannot change their own client secret unless a setting allows it.

Endpoints (CRUD + paging/filter/sort, row version / `If-Match` for edits): `applications`, `applications/{id}/settings`, `applications/{id}/secret/rotate`, `applications/{id}/redirect-uris`, `users`, `users/{id}`, `users/{id}/roles|permissions|groups`, `users/{id}/sessions` (list/revoke), `users/{id}/lock|unlock|reset-2fa`, `roles`, `roles/{id}/permissions`, `permissions` (create, mark obsolete), `groups`, `groups/{id}/members|roles|permissions`, `audit-events`, `external-providers` (experimental).

Every write creates an `audit_events` row. Every list is paged (max page size 200).

### 5.13 Client handshake (server side)

`POST /api/auth2/client/handshake` with `ClientHandshakeRequest`.
- Find the application from the token's `client_id`. None → `404 application_not_found`.
- Disabled application → `403`.
- **Multi mode:** request `ProjectKey` is required and must match the token's application, and `ApplicationName` must match → otherwise `409 project_key_or_name_mismatch`. If `ProjectKey` is missing → `409 server_mode_mismatch`.
- **Single mode:** if the request has a `ProjectKey` → `409 server_mode_mismatch`. `ApplicationName` is still checked.
- Success → `200 ClientHandshakeResponse` (includes `ServerMode`, issuer, settings version, cookie lifetime, single-active-session flag).
- The body is only a **check**. It never selects the application.

### 5.14 Permission, role and group sync (server side)

- `PermissionSyncService.SyncAsync`: normalize keys (trim, lower-case, pattern like `orders.read`, max length 100), distinct, max 2000 items, upsert by `(ApplicationId, Key)`. Return counts and added keys. **Never delete.** Removal is a separate admin action (mark obsolete) or opt-in setting `AllowRemovePermissionsFromSync`.
- Roles: upsert by `(ApplicationId, NormalizedName)`; an optional permission list per role is **added**, never removed unless the setting allows.
- Groups are not pushed (policy only `None` or `Pull`).
- Idempotent and race-safe through unique indexes.
- Allowed only if `PermissionSyncAllowed` / `RoleSyncAllowed` is true.
- Audit every sync that changes something.

### 5.15 Security features (must implement)

- **Rate limiting** with named policies: `login`, `token`, `register`, `otp`, `password-reset`, `handshake`, `permission-sync`, `admin`. Partition by IP + (username or client id). Redis-backed counters when Redis is on. Return `429` + `Retry-After`.
- **OTP**: hashed codes, short life, max attempts, resend delay, one-time use, timing-safe compare.
- **Lockout** per application settings.
- **Audit log**: login success/fail, lockout, password change/reset, 2FA changes, role/permission/group changes, admin writes, sync changes, key rotation, session revoke, seed events.
- **Secrets**: client secrets hashed and shown once; TOTP keys and external provider secrets encrypted with Data Protection; recovery codes hashed.
- **Key rotation**: multiple signing/encryption keys. CLI `rotate-keys`. Docs with steps.
- **HTTP hardening**: HTTPS redirect, HSTS, security headers (`X-Content-Type-Options`, `Referrer-Policy`, frame-ancestors, CSP placeholder), anti-forgery on pages, cookies `Secure`, `HttpOnly`, `SameSite=Lax` (use `None` only when needed).
- **Forwarded headers** behind a proxy; explicit `Issuer` option.
- **No user enumeration** in login, register, forgot password.
- **ReDoS protection**: every user-supplied regex has a timeout.
- **CORS**: off by default; allowed origins from settings.
- **Production guard**: fail startup when development certificates or an HTTP-only issuer are used. (The default admin password only causes a **warning**, see 5.18.)

### 5.16 Redis, Serilog, OpenTelemetry

- Redis is optional (`Auth2:Redis:ConnectionString`). When on: distributed cache, rate-limit counters, OTP storage, data-protection keys, access cache, settings invalidation through pub/sub. When off: in-memory implementations behind the same interfaces. Both must pass the same test suite.
- Serilog: structured logs, console + OTLP sink, enrich with `ApplicationId`, `ProjectKey`, `TraceId`, `CorrelationId`. Redact sensitive properties. Request logging without bodies.
- OpenTelemetry: traces (ASP.NET Core, HttpClient, EF Core, Npgsql, Redis), metrics (login success/fail, tokens issued, lockouts, sync counts, rate-limit rejections, handshake results), OTLP exporter configured by `OTEL_*` variables.
- Health checks: live and ready.

### 5.17 Localization (resx only)

- Supported cultures per server (resx: `en` default; add `fa`, `nl`, `it` skeleton files with a few keys as examples) and per application (`SupportedCultures`).
- Culture order: OIDC `ui_locales` → user's saved culture → culture cookie → `Accept-Language` → application `DefaultCulture` → `en`.
- Resource files: `Errors.resx`, `Validation.resx`, `Pages.resx`, `Emails.resx`.
- Text lookup: **resx of the selected culture → resx `en` → the key itself.** There are no database overrides and no translations table.
- Developers who want their own texts edit or add resx files in the Server project. `localization.md` explains how to add a language (copy `Errors.resx` to `Errors.fa.resx`, translate, add the culture to `SupportedCultures`).
- `Auth2IdentityErrorDescriber` and all API errors use resx. API errors also return a stable `code`.
- Test: every error code has an English text; missing keys fall back to English, then to the key.
- `roadmap.md` lists "database translation overrides" as a possible future feature.

### 5.18 Seeding and startup

**Seeder.** Create `Persistence/Seeding/Auth2Seeder.cs`:

```csharp
public sealed class Auth2Seeder(
    Auth2DbContext db,
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    IOptions<Auth2ServerOptions> options,
    ILogger<Auth2Seeder> log)
{
    // Hard-coded first admin. Change before production (see docs/seeding.md).
    internal const string AdminUserName = "globaladmin";
    internal const string AdminEmail    = "globaladmin@systemx.local";
    internal const string AdminPassword = "Admin@12345!ChangeMe";

    public Task SeedAsync(CancellationToken ct = default);
}
```

`SeedAsync` must be **idempotent** and run in one transaction:
1. Create the `_global` application if missing (and its OpenIddict application record).
2. Create the standard OpenIddict scopes (all `auth2.*` and the standard OIDC scopes).
3. Create the `GlobalAdmin` role (system role, global, `ApplicationId = null`).
4. Create the user `globaladmin` with the hard-coded email and password **only if no GlobalAdmin user exists**. Mark the email as confirmed. Add to `GlobalAdmin`. In multi mode also create the `user_applications` row for `_global`.
5. In single-application mode, also create the one fixed application row (5.1).
6. Never overwrite or reset the password of an existing user.
7. After creating the admin, log a **warning**: "Default GlobalAdmin created with the default password. Change it now." Write audit event `seed.global_admin_created` (no password in it).
8. At every startup in `Production`, if `globaladmin` still has the default password (check with `PasswordHasher.VerifyHashedPassword`), log a warning. Do **not** stop the application.

When it runs: on the `seed` and `migrate` commands; at normal startup in `Development`; in other environments only when `Auth2:AutoSeed=true`.

**Docs file `docs/seeding.md`** (B1–B2 English) must explain step by step:
- What the seed does and when it runs.
- The hard-coded user name, email and password, with a clear warning: **never use the default password in production**.
- How to move the values to environment variables:
  1. Add `Auth2SeedOptions` with `AdminUserName`, `AdminEmail`, `AdminPassword`, bound to section `Auth2:Seed`.
  2. In `Auth2Seeder` read `IOptions<Auth2SeedOptions>`; use the constants only as a fallback in `Development`.
  3. Set environment variables (`__` replaces `:`): `Auth2__Seed__AdminUserName=admin`, `Auth2__Seed__AdminEmail=admin@example.com`, `Auth2__Seed__AdminPassword=<strong password>`.
  4. Kubernetes: put the password in a `Secret` and map it to the variable. Azure: Key Vault. Local: `dotnet user-secrets`.
  5. In `Production`, make the app refuse to seed when the password is missing or equals the old default.
  6. Remove the constants from the code and change the password after the first login.
- How to change the admin password after the first login; how to create more admins with the `create-admin` command.
- Troubleshooting: "I cannot log in as globaladmin", "the seed ran twice", "I forgot the admin password" (use `create-admin` to add a new GlobalAdmin).
- Link it from `getting-started.md`, `server-setup.md` and the `security.md` production checklist.

**Commands** (same executable): `migrate`, `seed`, `rotate-keys`, `convert-to-single`, `convert-to-multi`, `create-application`, `create-admin`.

**Other startup rules:** never auto-migrate in Production unless `Auth2:AutoMigrate=true`; use a database lock when several instances start. Background services (OpenIddict token pruning, expired OTP cleanup, audit retention) use a distributed lock so only one instance runs each job.

### 5.19 Scale-out readiness

- Data Protection keys in database or Redis, fixed application name.
- Shared signing/encryption keys.
- No in-memory-only state that breaks with several instances (OTP, rate limit, sessions).
- Cache invalidation across instances.
- Migrations as a separate step.
- Health/readiness endpoints, graceful shutdown.
- Document in `docs/deployment-and-scaling.md` with a problem → fix table and a Kubernetes example (text only now).

---

## 6. SystemX.Auth2.Client (NuGet package)

References `SystemX.Auth2.Contracts`. **Never** references Server.

### 6.1 Folder layout

```
Client/
├─ DependencyInjection/   AddAuth2Client, builder + feature extensions
├─ Options/               Auth2ClientOptions, Auth2CookieOptions, Auth2ApiOptions, Auth2PermissionOptions, Auth2RoleOptions, Auth2GroupOptions, Auth2CurrentUserOptions, Auth2MachineOptions, validators
├─ Http/                  Auth2ApiClientService, MachineTokenHandler, resilience, error mapping
├─ Handshake/             HandshakeHostedService, Auth2ClientState, Auth2StartupException
├─ Sync/                  CatalogSyncService<T>, PermissionScanner, RoleScanner, hosted services
├─ Authorization/         attributes, policy provider, handlers, requirements, endpoint extensions
├─ Authentication/        Api (JWT/validation), Cookie+OIDC, Blazor helpers, Self-UI helpers
├─ CurrentUser/           ICurrentUser, store abstractions, memory and distributed stores
├─ Machine/               M2M token provider, named machine clients
├─ HealthChecks/          Auth2ClientHealthCheck
└─ Services/              IAuth2ApiClientService, IAuth2WebService, IAuth2BlazorService, IAuth2MachineService
```

### 6.2 Registration API (fluent builder). Keep these names.

```csharp
builder.Services
    .AddAuth2Client("https://auth.example.com", o =>
    {
        o.ApplicationName = "PaperTrade.Api";
        o.ClientId = "papertrade-api";
        o.ClientSecret = builder.Configuration["Auth2:Secret"]!;
        o.OnServerUnavailable   = StartupFailureBehavior.Continue;
        o.OnApplicationNotFound = StartupFailureBehavior.Throw;
        o.HandshakeOnStartup = true;
        o.HandshakeTimeout = TimeSpan.FromSeconds(5);
    })
    .ServerWithMultiApplication("PaperTrade")   // ONLY when the server is multi-application. Omit it for a single-application server.
    .EnablePermissions(o => { o.SyncPolicy = PermissionSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Both; o.PermissionList = [...]; })
    .EnableRoles(o => { o.SyncPolicy = RoleSyncPolicy.Push; o.ScanningPolicy = ScanningPolicy.Manual; o.RoleList = [...]; })
    .EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Pull)
    .UseApiAuthentication(o => o.Audience = "papertrade-api")     // API
    // .UseCookieAuthentication(o => {...})                        // MVC / Razor
    // .UseBlazor()                                                // Blazor
    // .UseHostedUi() / .UseSelfUi() / (no UI call = UiMode.None)
    .EnableMachineToMachine(o => o.Scopes = ["orders.read"])
    .EnableCurrentUser(o => { o.Source = CurrentUserSource.TokenAndServer; o.CacheTtl = TimeSpan.FromMinutes(10); })
    .UseDistributedCurrentUserStore()                              // or memory (default)
    .EnableTokenForwarding()
    .EnableResilience()
    .EnableHealthCheck();

app.UseAuth2();          // UseAuthentication + UseAuthorization + Auth2 endpoints
```

Rules:
- **There is no `ServerWithSingleApplication()` on the Client.** That method exists only on the Server (5.1). The Client has one mode method: `ServerWithMultiApplication(projectKey)`.
- No `ServerWithMultiApplication` call → the Client sends no `ProjectKey` and expects a single-application server. The Client never guesses; **the handshake decides** (6.3).
- Methods can be called in any order (use `Configure<T>`; do the real work at runtime). Statements inside lambdas end with `;`.
- Options are validated at startup with `IValidateOptions` and clear messages. `ProjectKey` is optional; if present it must match the server pattern (lowercase letters, digits, `-`, `_`; 3–64 chars; `_global` not allowed).
- Extension methods live in namespace `Microsoft.Extensions.DependencyInjection`.
- Also support `AddAuth2Client(builder.Configuration.GetSection("Auth2"))`.
- `UseApiAuthentication` and `UseCookieAuthentication` can be combined: use a policy scheme that chooses bearer when an `Authorization` header exists, else the cookie.
- `client-setup.md` shows two Program.cs examples: one for a multi-application server, one for a single-application server.

### 6.3 Handshake at startup

`HandshakeHostedService` (`IHostedService`):
- Sends `ClientHandshakeRequest(ProjectKey?, ApplicationName, version)` with the machine token.
- Outcomes → `HandshakeStatus`: `Ok`, `ServerUnavailable` (timeout, network, 5xx), `NotFound` (404), `Rejected` (401/403/409: wrong secret, name/key mismatch, server mode mismatch).
- Server mode check using `ClientHandshakeResponse.ServerMode`:
  - Client has a `ProjectKey` but the server is single-application → `Rejected`, message: "The server is single-application. Remove ServerWithMultiApplication."
  - Client has no `ProjectKey` but the server is multi-application → `Rejected`, message: "The server is multi-application. Call ServerWithMultiApplication(projectKey)."
- `OnServerUnavailable` and `OnApplicationNotFound` (`Throw` or `Continue`) decide what happens. `Rejected` follows `OnApplicationNotFound`. `Throw` raises `Auth2StartupException` and the app does not start.
- `Continue` + `ServerUnavailable`: warn, then **retry in the background** with exponential backoff (2s → max 60s, default 6 tries). If a configuration error appears later (`NotFound`/`Rejected`), log an error and stop retrying, but do not kill the app.
- `Auth2ClientState` (singleton) exposes `Status` and the last response. `Auth2ClientHealthCheck` returns `Healthy` for `Ok`, `Degraded` otherwise.
- Sync services wait until the handshake is `Ok`.
- The cookie lifetime from the handshake response is used when `Auth2CookieOptions.ExpireTimeSpan` is null.
- Never log secrets or tokens.

### 6.4 Machine token handling

- `IAuth2MachineTokenProvider.GetTokenAsync(scopes, ct)`: client credentials through `/connect/token`, **cached** until shortly before expiry (`expires_in - 60s`, minimum 30s), thread-safe (single flight).
- `MachineTokenHandler : DelegatingHandler` adds the bearer token to server calls.
- Named machine clients: `IAuth2MachineService.CreateClient(name)` returns an `HttpClient` that adds a token for the given scopes (for background services calling other APIs).
- Token endpoint 400/401 → `Rejected`; network errors → `ServerUnavailable`.

### 6.5 Authorization on the client

Use ASP.NET Core authorization (requirements, handlers, policies, `IAuthorizationPolicyProvider`). Do not build a new engine.

- **Minimal API extensions** (chainable; chaining means AND; `Any`/`All` overloads where noted):
  - `.WithAuth2()` — requires login
  - `.WithAnonymous()` — allow anyone
  - `.WithRole(params string[] roles)` — any of the roles (`WithAllRoles` = all)
  - `.WithPermission(params string[] permissions)` — any of (`WithAllPermissions` = all)
  - `.WithOnlyRolePermission()` — permission must come from a role (or the role of a group), not a direct grant
  - `.WithOnlyUserPermission()` — permission must be a direct grant
  - `.WithScope(params string[] scopes)`
  - `.WithMachineOnly()` / `.WithUserOnly()` (client-credentials token vs user token)
  - `.WithGroup(params string[] groups)`
  - `.WithAuth2Policy(string name)`
  - `.WithAuth2Audit(string action)` (optional hook that raises an event)
  - All add endpoint **metadata** (`IAuth2PermissionMetadata`, `IAuth2RoleMetadata`, ...) so scanners can read it.
- **Attributes** for controllers, MVC, Razor Page models, Blazor components: `[Auth2Authorize]`, `[Auth2Anonymous]`, `[Auth2Role("...")]`, `[Auth2Permission("...")]`, `[Auth2Scope("...")]`, `[Auth2Group("...")]`, `[Auth2MachineOnly]`, `[Auth2UserOnly]`.
- Razor Pages: attributes on the PageModel + `Auth2AuthorizeFolder` / `Auth2AuthorizePage` conventions. Blazor: `<AuthorizeView Policy="...">`, `[Authorize(Policy="...")]`, plus `IAuth2BlazorService` helpers (`HasPermissionAsync`).
- Browser apps: on failure redirect to login/denied pages. API controllers and Minimal API: return `401`/`403` (no redirect). Detect with `[ApiController]`, the `Accept` header or endpoint metadata.
- Permission checks read the cached effective permissions from `ICurrentUser`. Scope and role checks read claims.
- Document a simple static-class pattern for permission constants (no source generator now).

### 6.6 CurrentUser and access data

- `ICurrentUser` has claim-based sync properties (`IsAuthenticated`, `UserId`, `UserName`, `Email`, `PhoneNumber`, `FirstName`, `LastName`, `FullName`, `Scopes`, `IsMachine`) and **async** access lists (`GetRolesAsync`, `GetPermissionsAsync`, `GetGroupsAsync`, `HasPermissionAsync`, `HasRoleAsync`). Async on purpose: it may call the server.
- `IAuth2CurrentUserService.FetchAsync(ClaimsPrincipal?, ct)` only fetches from `/api/auth2/me`. It does not cache. A developer can call it after login and keep the result anywhere (Redis, memory, session, cookie).
- `ICurrentUserStore` (`GetAsync/SetAsync/RemoveAsync`). Provide `MemoryCurrentUserStore` (default) and `DistributedCurrentUserStore` (`IDistributedCache`, so Redis works). Cookie storage only for roles/small data, documented as risky (4 KB limit).
- Version check: if cached `AccessVersion < pv` claim, refetch. Also refetch when TTL ends. One load per request (request-level cache).
- Machine tokens: `IsMachine = true`, no user id, empty access lists, no exceptions.
- Cookie wrapper `UseCookieAuthentication` options: `CookieName`, `ExpireTimeSpan` (null = use the server value), `SlidingExpiration`, `SameSite`, `Secure`, `LoginPath`, `AccessDeniedPath`, `SaveTokens`, `AccessMode` (`AccessLoadMode`). The OIDC `OnTokenValidated` hook is implemented **inside the package** (loads access data, saves it to the store or adds role claims per `AccessMode`). Developers do not write that hook. Docs must explain in simple words what `OnTokenValidated` is.

### 6.7 Client services

- `IAuth2ApiClientService`: `RegisterAsync`, `LoginAsync` (calls `/connect/token`, supports 2FA and OTP steps), `RequestOtpAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync`, `ChangePasswordAsync`, `RefreshAsync`, `LogoutAsync` (revoke), `GetMeAsync`, `HandshakeAsync`, `SyncPermissionsAsync`, `SyncRolesAsync`, `GetPermissionsAsync`, `GetRolesAsync`, `GetGroupsAsync`.
- `IAuth2WebService` (cookie apps): `LoginAsync` (Self UI: calls the server, then `HttpContext.SignInAsync` with the claims; cookie lifetime from the server setting unless overridden), `LogoutAsync`, `ChallengeAsync(returnUrl)`.
- `IAuth2BlazorService`, `IAuth2MachineService`.
- Use either `Result<T>` results or `Auth2Exception(statusCode, errorCode, message)`. Choose one, document it. Never leak secrets in exceptions.
- Resilience: `Microsoft.Extensions.Http.Resilience` (retry with jitter for idempotent calls, timeout, circuit breaker). Do not retry non-idempotent login/register calls.
- Cache the OIDC discovery document and JWKS; on an unknown key id refresh once (rotation friendly).

### 6.8 Permission, role and group sync (client side)

- `PermissionScanner` / `RoleScanner`:
  - `Manual`: only `PermissionList` / `RoleList`.
  - `AssemblyScan`: read all `IAuth2PermissionMetadata` from `EndpointDataSource` (Minimal API, controllers, Razor Pages) **and** reflect over `AssembliesToScan` for `[Auth2Permission]` / `[Auth2Role]` on types and methods (Blazor components, services).
  - `Both`: union. Manual entries win (they have names and descriptions).
  - Validate keys with the same pattern as the server.
- `CatalogSyncService<TItem>` (generic) runs from a hosted service **after `ApplicationStarted`** (endpoints are mapped) and after a successful handshake. Hash the sorted list and skip if unchanged since the last success. Retry with backoff. Default on failure: log and continue (`FailOnSyncError = false`).
- `Push`: send. `Pull`: read the catalog/assignments from the server at startup and on cache expiry. `Both`: push first, then pull. `None`: nothing.
- Groups: `Pull` only.

### 6.9 UI modes on the client

- `UseHostedUi()`: OIDC code + PKCE, redirect to the server; routes `/signin-oidc`, `/signout-callback-oidc`. Keep page B through `AuthenticationProperties.RedirectUri`.
- `UseSelfUi()`: the app shows its own forms and uses `IAuth2WebService` / `IAuth2ApiClientService`. Docs warn: the app handles passwords; the server must allow `AllowSelfUiLogin`.
- No UI call = `UiMode.None` (APIs): 401/403 only.
- `UseBlazor()`: authentication state provider, `AuthorizeView` support, return URL through `NavigationManager`.

---

## 7. SystemX.Auth2.Gateway

Create only:
- `SystemX.Auth2.Gateway.csproj` (net10.0, packable, no code besides what the compiler needs).
- `README.md` that explains the plan: a YARP-based helper for gateway projects that (1) routes `/connect/*` and auth pages to many Server replicas with health checks and forwarded headers, (2) validates the JWT once, (3) applies route metadata rules (`Auth2:Permission`, `Role`, `Anonymous`), (4) forwards the original token, (5) strips untrusted identity headers. State clearly: **not implemented in v1**, clients do not need it, and the Server must already work behind any load balancer.
- No tests, no sample for it now.

---

## 8. Sample: SystemX.Auth2.Samples.MinimalApi

A small Minimal API app (net10.0):
- `AddAuth2Client` with API authentication, permissions push + `ScanningPolicy.Both`, roles manual, groups pull, current user, M2M.
- Endpoints: `POST /signup` (`RegisterAsync`), `POST /login` (`LoginAsync`), `GET /me`, `GET /orders` (`.WithPermission("orders.read")`), `POST /orders` (`.WithPermission("orders.write")`), `GET /admin` (`.WithRole("Admin")`), `GET /public` (`.WithAnonymous()`), `GET /reports` (`.WithScope("reports.read").WithMachineOnly()`).
- A background service that calls another API with a machine token.
- `appsettings.json` with placeholders and a `README.md` with exact steps to run it against a local Server, with example `curl` commands and example responses.
- Shows handshake logs for `Continue` and `Throw`.

---
## 9. Testing requirements

General:
- Every public service and endpoint has tests. Name tests `Method_Scenario_ExpectedResult`.
- Unit tests: no network, no database (fakes/NSubstitute). Integration tests: Testcontainers PostgreSQL 15 (and Redis where relevant) + `WebApplicationFactory<Program>` (make `Program` accessible).
- A shared `Auth2ServerFactory` fixture starts one container per test collection, applies migrations, runs the seeder and creates two sample applications (`appA`, `appB`).
- Target 80% line coverage for `Application`, `Client`, `Contracts` (coverlet). Do not chase coverage with meaningless tests.
- Tests are deterministic: inject `IClock`/`TimeProvider`, no `Thread.Sleep`.

Mandatory tests (minimum):

**Contracts:** serialization, enum strings, constants.

**Server unit:** settings validator (all rules, bad regex, wildcard URI), return URL validator (all evil cases), effective permission calculation (user + role + group, application filter), access version increment, credential state strategies, login service branches (disabled app, not in application, method not allowed, lockout, wrong password, 2FA required, success, single active session), OTP service (expiry, attempts, resend delay, one-time use), sync services (idempotent, never deletes, max items, invalid keys), localization fallback (culture resx → `en` → key), architecture rule (Domain has no ASP.NET/EF reference), seeder (see below).

**Server integration:**
- Register in App A then App B → 1 user row, 2 `user_applications` rows, different passwords work independently.
- App A password fails in App B. TOTP enabled in A is not required in B. Lockout in A does not lock B.
- Same role name `writer` in A and B → both exist, assignments do not mix.
- Token from App A cannot create permissions for App B. Body project key mismatch → 409. Unknown client → 404.
- Project admin cannot read another application's users, roles or audit (query filter + policy). Global admin can.
- Authorization code + PKCE end to end through the empty pages' handlers using a test login helper (post to the PageModel handler; no HTML needed).
- Return URL: page B preserved through the authorize chain; direct login uses `RedirectAfterLogin`; evil URLs ignored.
- Client credentials token and scope enforcement on every secured endpoint (missing scope → 403).
- Password grant only when `AllowSelfUiLogin` and only for confidential clients; refresh rotation and reuse detection; `SingleActiveSession` revokes other sessions of the same application only.
- Rate limiting returns 429 for login/otp/token; counters shared through Redis across two instances.
- Two server instances (shared DB + Redis): cookie/Data Protection works across instances; a token from one validates on the other.
- Health endpoints, migration command, seed command, key rotation (old tokens valid until expiry, new tokens use the new key).
- Audit events written for all listed actions, with no secrets.
- Localization: `ui_locales`, cookie, `Accept-Language`, default culture.
- **Single-application mode:** no row is ever written to `user_applications`; handshake and register work without `ProjectKey`; `convert-to-multi` / `convert-to-single` move credentials correctly.
- **Seeder:** first run creates `_global`, the `GlobalAdmin` role, the admin user and scopes; second run changes nothing; an admin whose password was changed keeps the new password; single mode creates the fixed application row and no `user_applications` rows; the default password never appears in logs or audit data.
- Passkeys and social login: basic experimental tests only (provider registration, settings validation).

**Client unit:** options validation (`ProjectKey` optional, pattern check), builder order independence, scanner (manual/assembly/both, manual wins, invalid keys), sync hash skip, handshake state machine for all outcomes and both failure behaviors, machine token cache (single flight, expiry), current user (token-only, store, version refresh, machine token), attribute/extension metadata, policy provider, error mapping.

**Client integration (against an in-memory Server):** handshake ok / 404 / 409 / server down (`Continue` and `Throw`); **server mode mismatch in both directions** (client with `ProjectKey` vs single server, client without `ProjectKey` vs multi server) and a client without `ProjectKey` against a single server works; permission push idempotent; role push; groups pull; `WithPermission` returns 200/401/403; `WithOnlyRolePermission`; `WithOnlyUserPermission`; `WithMachineOnly`; `WithScope`; login through the token endpoint with the 2FA step; cookie lifetime taken from the server; hosted OIDC flow keeps the original page; current user cache refreshed by access version.

---

## 10. Documentation requirements (`/docs`)

Write for developers who are not security experts. **Simple English (B1–B2).** Short sentences. Active voice. Explain each acronym once (OIDC, PKCE, TOTP, M2M, jsonb...). Every page has: *What this is*, *When to use it*, *Step by step*, *Example*, *Common problems*, *Related pages*. Code blocks must really compile (copy from the sample/tests when possible). Update docs in the same piece of work that changes behaviour.

```
docs/
├─ index.md                         (map of all docs)
├─ getting-started.md               (run server, create app, call from an API in 10 steps)
├─ concepts.md                      (application/project, ProjectKey, single vs multi application, user vs UserApplication, scopes vs permissions vs roles vs groups, tokens, hosted vs self UI, M2M)
├─ architecture.md                  (solution layout, layers, dependency rule, mermaid/text diagrams)
├─ server-setup.md                  (Program.cs, ServerWithSingleApplication, options, seeding, commands, configuration reference)
├─ seeding.md                       (seeder, hard-coded admin, how to move to environment variables, change password, create-admin)
├─ server-settings.md               (EVERY ApplicationSettings field: meaning, default, who can edit, example, risks)
├─ credential-scope.md              (PerApplication vs Shared, single mode, switching modes, joining a second application)
├─ authentication-flows.md          (hosted code+PKCE, client credentials, refresh, optional password grant, OTP, 2FA, consent, external login, passkeys, return URL rules, SingleActiveSession)
├─ authorization.md                 (permissions, roles, groups, effective permissions, access version, caching)
├─ client-setup.md                  (all builder methods, options tables, API / MVC / Razor / Blazor / Machine setups; examples for multi and single servers)
├─ client-authorization.md          (all Minimal API extensions, attributes, chaining, examples)
├─ client-current-user.md           (ICurrentUser, stores, cookie wrapper, OnTokenValidated explained simply)
├─ permission-sync.md               (Push/Pull/Both/None, scanning policies, rules, never-delete rule)
├─ handshake-and-startup.md         (handshake, server mode check, failure behaviours, retry, health check)
├─ machine-to-machine.md
├─ admin-api.md                     (global admin vs project admin, every endpoint, examples)
├─ api-reference.md                 (every public endpoint: request, response, errors, scopes)
├─ error-codes.md
├─ localization.md                  (resx only, culture order, how to add a language, IdentityErrorDescriber)
├─ security.md                      (threat model, checklist, rate limiting, audit, secrets, key rotation, open redirect, user enumeration, password grant risk, production checklist incl. "default admin password changed")
├─ deployment-and-scaling.md        (many instances: Data Protection, keys, Redis, rate limit, OTP, migrations, jobs, health, proxy; Kubernetes text example; Docker Compose "planned")
├─ observability.md                 (Serilog, OpenTelemetry, metrics list, log redaction)
├─ gateway.md                       (what the gateway package will do, status: not implemented)
├─ comparison-and-limits.md         (not a replacement for Keycloak/Duende; feature table; NOT supported: SAML, LDAP/AD, user federation, certified OIDC; what is experimental)
├─ feature-status.md                (every feature, level, notes, test names)
├─ testing.md                       (how to run tests, containers, coverage)
├─ contributing-workflow.md         (code → tests → pass → docs rule; PR checklist; coding style)
├─ decisions.md                     (ADR list)
├─ troubleshooting.md               (symptom → cause → fix: 401/403, invalid issuer, redirect loop, cookie not set behind proxy, key mismatch across instances, handshake errors incl. server mode mismatch, rate limit 429, OTP not arriving, Redis down, cannot log in as globaladmin)
├─ roadmap.md                       (UI pages for server and admin, MVC sample, docker-compose, Gateway package, SAML, LDAP, passkeys stable, permission-constant source generator, Helm chart, webhook cache invalidation, database translation overrides; DocFX not planned)
└─ ui-handover.md                   (every empty page, its PageModel properties/handlers, which settings control which flags, expected form fields and validation rules — so the UI LLM can build the HTML later)
```

Also write the root `README.md` (what it is, **not a replacement for Keycloak/Duende**, quick start, feature list, status table, links) with a short "Why this project" section.

---

## 11. Build order (phases with gates)

Do not start a phase before the previous one has: all tests green, docs written, `CHANGELOG.md` updated. Commit after each gate.

1. **Skeleton:** solution, `Directory.Build.props`, `Directory.Packages.props`, all projects, test projects, CI file (GitHub Actions: restore, build, test with Testcontainers, pack), Gateway empty project + README. Docs: `architecture.md`, `contributing-workflow.md`, `testing.md`, `decisions.md`.
2. **Contracts:** all DTOs/enums/constants. Tests + `api-reference.md` skeleton, `error-codes.md`.
3. **Server core data:** Domain, DbContext, EF configs, migrations, query filters, application settings (jsonb), validator, `IApplicationContext` (single + multi), `ServerWithSingleApplication()`, **`Auth2Seeder`** and commands `migrate`/`seed`. Docs: `server-setup.md`, `seeding.md`, `server-settings.md`, `concepts.md`.
4. **Identity customization:** stores, role index change, credential scope strategies, validators, error describer, **resx localization**. Docs: `credential-scope.md`, `localization.md`.
5. **OpenIddict + login:** configuration, scopes, application ↔ OpenIddict sync, authorize/token passthrough, `ILoginService`, lockout, optional password grant, refresh rotation, SingleActiveSession, return URL validator, empty pages with working handlers. Docs: `authentication-flows.md`.
6. **Register, OTP, password reset, 2FA, consent, join-application, sessions:** services + endpoints + PageModels. Docs updates.
7. **Access model:** permissions, roles, groups, effective access, access version, cache, `/me`. Docs: `authorization.md`.
8. **Sync + handshake endpoints (with server mode check).** Docs: `permission-sync.md`, `handshake-and-startup.md`.
9. **Admin JSON API + audit + global/project admin policies.** Docs: `admin-api.md`.
10. **Security and operations:** rate limiting, Redis implementations, Serilog, OpenTelemetry, health checks, key rotation, security headers, background jobs with locks, scale-out tests with two instances. Docs: `security.md`, `deployment-and-scaling.md`, `observability.md`.
11. **Client core:** options, builder, validation, machine token provider, API client service, handshake incl. mode check, health check. Docs: `client-setup.md`, `machine-to-machine.md`.
12. **Client authorization:** metadata, attributes, Minimal API extensions, policy provider/handlers, current user + stores, API authentication. Docs: `client-authorization.md`, `client-current-user.md`.
13. **Client cookie/OIDC, Blazor helpers, Self UI service, scanners and sync services.** Docs updates.
14. **Experimental features:** social login/brokering, passkeys (marked experimental in code and docs). SAML/LDAP: extension interfaces + docs only.
15. **Sample Minimal API + final docs pass:** `getting-started.md`, `troubleshooting.md`, `comparison-and-limits.md`, `feature-status.md`, `ui-handover.md`, `roadmap.md`, root README. Run the full test suite, then a manual run of the sample against a local Server.

---

## 12. Definition of done (per piece)

- Code compiles with zero warnings.
- Unit and integration tests for the piece pass locally and in CI.
- No secret appears in logs (test or log-capture check where logins are involved).
- Public API has XML docs.
- Docs page written or updated, in B1–B2 English, with a working example.
- `decisions.md` updated if a design choice was made.
- `CHANGELOG.md` updated.

---

## 13. Feature coverage checklist (nothing may be missed)

Tick each when code + tests + docs exist.

**Solution:** CPM · Directory.Build.props · 4 src projects · sample · tests · docs · licence/policy files · CI.
**Server design:** folder-based clean architecture · Identity + OpenIddict · PostgreSQL jsonb settings · `ProjectKey` · `_global` project · `services.ServerWithSingleApplication()` (Server only) · `UserApplications` · `Permissions`, `UserPermissions`, `RolePermissions` · Groups tables · roles per application (unique index changed) · per-application credentials (`CredentialScope`) · custom stores/managers/describer.
**Settings (editable by developers; by project admins only where allowed):** login/register methods · username/password/phone rules · lockout · OTP · 2FA · consent · token and cookie lifetimes · redirects (login/register/logout) · `LoginRequiredAfterRegister` · `SingleActiveSession` · `PhoneNumberRegex` · cultures · external providers · sync permissions.
**Server features:** login / register / forgot password / OTP / 2FA / recovery codes / lockout / consent / join application / sessions · return URL handling · localization (resx only) · admin JSON API with global/project scopes · audit log · rate limiting · Redis · Serilog · OpenTelemetry · health checks · key rotation · scale-out readiness · **`Auth2Seeder` + `docs/seeding.md`** · commands.
**Standards:** OIDC · OAuth 2.0 (code+PKCE, client credentials, refresh) · optional password grant · custom OTP/2FA grants · userinfo · revoke · end session.
**Experimental:** social login · identity brokering · passkeys.
**Not in v1 (docs only):** SAML · LDAP/AD · user federation.
**Client:** `AddAuth2Client` fluent builder · `ServerWithMultiApplication` (only mode method) · `EnablePermissions` / `EnableRoles` / `EnableGroups` · `UseApiAuthentication` / `UseCookieAuthentication` / `UseBlazor` / `UseHostedUi` / `UseSelfUi` / none · `EnableMachineToMachine` · `EnableCurrentUser` + stores (memory, distributed) · cookie wrapper · token forwarding · resilience · health check · `UseAuth2()` · handshake with `Throw`/`Continue` and server mode check · scanning (`Manual`/`AssemblyScan`/`Both`) · sync (`None`/`Push`/`Pull`/`Both`) · attributes · Minimal API extensions (`WithAuth2`, `WithAnonymous`, `WithRole`, `WithPermission`, `WithOnlyRolePermission`, `WithOnlyUserPermission`, `WithScope`, `WithGroup`, `WithMachineOnly`, `WithUserOnly`, ...) · services (`IAuth2ApiClientService`, `IAuth2WebService`, `IAuth2BlazorService`, `IAuth2MachineService`) · `ICurrentUser`.
**Gateway:** empty project + README.
**Sample:** Minimal API.
**Docs:** every file in section 10.

---

## 14. Known pitfalls to handle on purpose

1. Identity looks roles up by name only → override store methods or roles will mix between applications.
2. Setting the password before the `user_applications` row exists → create the row first (same transaction).
3. Global `IdentityOptions` cannot change per request → use custom validators and the flow resolver.
4. Joining a second application without proof of ownership allows account pre-hijacking → require proof.
5. `ReturnUrl` lost between login/register/confirm pages → carry it everywhere and test.
6. Open redirect on `ReturnUrl`, `redirect_uri`, `post_logout_redirect_uri` → exact match and local-only rules.
7. Different Data Protection or signing keys per instance → shared keys, tested with two instances.
8. Scanning endpoints before the app is built → run after `ApplicationStarted`.
9. Permissions in tokens make tokens huge → use `pv` + `/me` + cache.
10. Retrying login/register calls can double-submit → do not retry non-idempotent calls.
11. Cookie too large (>4 KB) → keep permissions out of the cookie by default.
12. Regex from settings can hang the server → always use a match timeout.
13. "Single sign-in" is confusing (SSO) → the public setting is `SingleActiveSession`.
14. The handshake body must never choose the application → the application comes from the token.
15. Never log secrets, tokens, OTPs, recovery codes, TOTP keys, or the default admin password.
16. The Client must not guess the server mode → the handshake response says which mode the server uses.
17. `ServerWithSingleApplication()` is a Server method. Do not add it to the Client.
18. The hard-coded admin password is public in the repository → warn loudly in logs and docs (`seeding.md`, `security.md`).

---

## 15. Final deliverables

- The full repository as in section 3 with all tests passing.
- All docs in section 10.
- `docs/feature-status.md` listing each feature with its level (implemented / experimental / not in v1) and the test names that prove it.
- `docs/ui-handover.md` so the next LLM can build the HTML and CSS without reading the code.

When something in this file conflicts with a library limit, choose the safest working option, write it in `docs/decisions.md`, and tell the user in the final report.
