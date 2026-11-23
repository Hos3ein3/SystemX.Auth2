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
