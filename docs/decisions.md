# Decisions (ADR list)

Short records of design choices. Newest at the bottom.

## ADR-001: Central package management
All package versions live in `Directory.Packages.props`.

## ADR-002: xUnit v2 for tests
The spec names xUnit with `xunit.runner.visualstudio`. We use xUnit 2.9.3 with runner 3.x, which is stable with `Microsoft.NET.Test.Sdk`.

## ADR-003: Test package for ASP.NET
`Microsoft.AspNetCore.Mvc.Testing` is pinned to the 10.0.x line to match net10.0 (the 11.x line targets a newer framework).

## ADR-004: Enums serialize as strings
Every Contracts enum has `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`, so JSON is readable and stable.

## ADR-005: Result type
`Result` / `Result<T>` live in Contracts for server-side use. The Client will choose between `Result<T>` and exceptions in phase 11 and record it here.

## ADR-006: Gateway project is a class library
The Gateway is an empty class library with one internal marker type so it can be packed.

## ADR-007: Contracts additions
The spec lists DTO file names but not all fields. Fields for `LoginRequest`, `LoginResponse`, `RegisterRequest`, `RegisterResponse`, `TwoFactorRequest`, `ForgotPasswordRequest`, `ResetPasswordRequest`, `ChangePasswordRequest`, `ApplicationDto`, `CreateApplicationRequest`, `UpdateApplicationRequest`, `UserDto` were chosen by us. We added a few error codes (`application_disabled`, `invalid_token`, and others). Names from the spec were not changed.

## ADR-008: Application layer holds interfaces, DB code lives in Persistence
`Application/` has interfaces, options and pure logic (validators). Services that need `Auth2DbContext` (`ApplicationService`, `AuditService`, the seeder) live in `Persistence/`. This keeps the rule `Domain <- Application <- (Persistence, Infrastructure)` without a repository layer.

## ADR-009: TimeProvider instead of IClock
The spec allows `IClock` or `TimeProvider`. We use the built-in `TimeProvider`. No custom `IClock`.

## ADR-010: Settings are stored as a jsonb value converter
`applications.settings_json` is a `jsonb` column with a System.Text.Json value converter (not `OwnsOne().ToJson()`). It is simpler, keeps a `schemaVersion` in the JSON, and enums are strings. A `ValueComparer` compares the JSON text.

## ADR-011: Settings are grouped in sections
The spec lists settings in groups. In code each group is a class (`Password`, `Lockout`, `Sessions`, ...) inside `ApplicationSettings`. Each leaf field has `[SettingEditability]` (GlobalOnly or ProjectAdminEditable). A test fails if a field has no attribute. We added `Clients.AllowedCorsOrigins` because the spec says CORS origins come from settings.

## ADR-012: Query filter design
Tenant tables use `FiltersBypassed || ApplicationId == CurrentApplicationId`. With no selected application nothing is visible (safe default). Roles also show global roles (`ApplicationId` null). Join tables (`role_permissions`, `user_groups`, ...) have no filter; they are reached through filtered tables. EF warning `PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning` is ignored on purpose.

## ADR-013: Application resolution
`IApplicationContext` sets `IApplicationScope` when it resolves the application. The DbContext reads `IApplicationScope`, not `IApplicationContext`, so there is no circular dependency. Multi mode resolves from the admin's selected project key, otherwise from `IClientIdAccessor.ClientId`. Phase 5 fills the client id from the token or the authorize request.

## ADR-014: Naming
Tables and columns are `snake_case`. Identity tables are renamed `users`, `roles`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`. OpenIddict tables are `openiddict_*`. We use our own small snake_case function, not an extra package.

## ADR-015: Email uniqueness
The default Identity email index is replaced by a unique index on `NormalizedEmail` (nulls allowed), and `RequireUniqueEmail = true`. One person = one `AppUser`.

## ADR-016: Seeder and credentials
In multi mode with `CredentialScope.PerApplication` the seeder stores the admin's password hash and security stamp in `user_applications` and leaves the `users` row without a hash. In single mode or `Shared` scope the hash stays in `users`. Phase 4 stores follow the same rule.
The OpenIddict client made by the seeder and by `create-application` is a basic public client (authorization code + PKCE, refresh token). Phase 5 reconciles redirect URIs, secrets and permissions.

## ADR-017: Single-application mode switch
`ServerWithSingleApplication()` sets `Mode = SingleApplication`, forces `CredentialScope = Shared` and replaces `IApplicationContext`. `AddSystemXAuth2Server` registers a runtime-switching context, so the call order does not matter. In single mode `ApplicationService.CreateAsync` throws: there is exactly one application.

## ADR-018: Data Protection keys in the database
`AddDataProtection().SetApplicationName("SystemX.Auth2").PersistKeysToDbContext<Auth2DbContext>()`. Needed by Identity token providers and by scale-out later.

## ADR-019: IdentityUserLogin / IdentityUserToken keys
`AppUserLogin` and `AppUserToken` add a nullable `ApplicationId` column only. The primary keys are unchanged. If the same external account must be linked twice for different applications, the keys must change; this is left for phase 14 (experimental).

## ADR-020: Test host
Integration tests use one PostgreSQL 15 container per test run and one new database per test. `WebApplicationFactory<Program>` runs with environment `Testing` (no automatic migration). Tests call `DatabaseInitializer`.

## ADR-021: Credential state strategies
`ICredentialState` has two implementations (`UserRowCredentialState`, `UserApplicationCredentialState`). `Auth2UserStore` overrides every credential member of Identity (password, stamp, 2FA, lockout, TOTP key, recovery codes) and asks `ICredentialStateProvider`. The effective scope is `Shared` in single mode, the application override when the server allows it, else the server option.

## ADR-022: Row-first rule in the store
A new user's `user_applications` row is held in memory and saved together with the user (`BeforeUserCreated`), so there is never a password without a row and a failed validation leaves no rows. For an existing person without a row, the store throws instead of creating one: joining needs proof of ownership (phase 6). Reading with no row returns "no credentials".

## ADR-023: Per-application lockout in the manager
`IdentityOptions.Lockout` is global. `Auth2UserManager` overrides `CreateAsync`, `GetLockoutEnabledAsync` and `AccessFailedAsync` to use `Lockout` settings of the current application. `IdentityOptions.Lockout.AllowedForNewUsers` is false. The standard `UserValidator` still checks uniqueness and email format; `AllowedUserNameCharacters` is empty so our validator owns the character rule.

## ADR-024: TOTP key and recovery codes
The TOTP key is encrypted with Data Protection (purpose `SystemX.Auth2.AuthenticatorKey`) in both strategies. Recovery codes are stored as salted SHA-256 hashes (`ISecretHasher`, format `v1.salt.hash`). Identity's default stores both in plain text. Passwords still use the Identity password hasher. SHA-256 is acceptable here because codes are random and single-use; it is not used for passwords.

## ADR-025: Roles in the store
Role lookup by name prefers the role of the current application over a global role with the same name. The store makes sure the application is resolved first. `GetRolesAsync` returns roles of the current application only; global roles are included only in the `_global` project and in single-application mode. `Auth2RoleValidator` checks duplicates inside one application (`ApplicationId` + normalized name), including global roles among themselves. Roles created with no `ApplicationId` are global, so services must set it.

## ADR-026: Settings provider is scoped
`IApplicationSettingsProvider` reads through the scoped DbContext, so it sees uncommitted data (the seeder runs in a transaction). The cache is the shared `IMemoryCache`.

## ADR-027: Localization
Resx only, marker classes `Errors`, `Validation`, `Pages`, `Emails` in `SystemX.Auth2.Server.Resources`. English is the neutral resx. `fa`, `nl`, `it` are skeletons. Culture order is a pure function (`Auth2CultureResolver`) plus a request provider. A culture needs both a server resx and the application's `SupportedCultures`. The saved user culture is read from the `locale` claim.

## ADR-028: Added error codes
Added to Contracts: `password_too_long`, `password_requires_unique_chars`, `password_banned`, `invalid_user_name`, `invalid_email`, `email_domain_not_allowed`, `invalid_phone_number`, `user_not_in_application`.
