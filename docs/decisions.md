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
`Result` / `Result<T>` live in Contracts for server-side use. The Client package uses exceptions for faults and `Result<T>` for answers it can handle (see [client-setup.md](client-setup.md)).

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
`IApplicationContext` sets `IApplicationScope` when it resolves the application. The DbContext reads `IApplicationScope`, not `IApplicationContext`, so there is no circular dependency. Multi mode resolves from the admin's selected project key, otherwise from `IClientIdAccessor.ClientId`. The token and authorize endpoints fill the client id from the token or the authorize request.

## ADR-014: Naming
Tables and columns are `snake_case`. Identity tables are renamed `users`, `roles`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`. OpenIddict tables are `openiddict_*`. We use our own small snake_case function, not an extra package.

## ADR-015: Email uniqueness
The default Identity email index is replaced by a unique index on `NormalizedEmail` (nulls allowed), and `RequireUniqueEmail = true`. One person = one `AppUser`.

## ADR-016: Seeder and credentials
In multi mode with `CredentialScope.PerApplication` the seeder stores the admin's password hash and security stamp in `user_applications` and leaves the `users` row without a hash. In single mode or `Shared` scope the hash stays in `users`. The user stores follow the same rule.
The OpenIddict client made by the seeder and by `create-application` is a basic public client (authorization code + PKCE, refresh token). Redirect URIs, secrets and permissions are reconciled when the application is saved.

## ADR-017: Single-application mode switch
`ServerWithSingleApplication()` sets `Mode = SingleApplication`, forces `CredentialScope = Shared` and replaces `IApplicationContext`. `AddSystemXAuth2Server` registers a runtime-switching context, so the call order does not matter. In single mode `ApplicationService.CreateAsync` throws: there is exactly one application.

## ADR-018: Data Protection keys in the database
`AddDataProtection().SetApplicationName("SystemX.Auth2").PersistKeysToDbContext<Auth2DbContext>()`. Needed by Identity token providers and by scale-out later.

## ADR-019: IdentityUserLogin / IdentityUserToken keys
`AppUserLogin` and `AppUserToken` add a nullable `ApplicationId` column only. The primary keys are unchanged. If the same external account must be linked twice for different applications, the keys must change; this was left open until external identity needed it and is handled there.

## ADR-020: Test host
Integration tests use one PostgreSQL 15 container per test run and one new database per test. `WebApplicationFactory<Program>` runs with environment `Testing` (no automatic migration). Tests call `DatabaseInitializer`.

## ADR-021: Credential state strategies
`ICredentialState` has two implementations (`UserRowCredentialState`, `UserApplicationCredentialState`). `Auth2UserStore` overrides every credential member of Identity (password, stamp, 2FA, lockout, TOTP key, recovery codes) and asks `ICredentialStateProvider`. The effective scope is `Shared` in single mode, the application override when the server allows it, else the server option.

## ADR-022: Row-first rule in the store
A new user's `user_applications` row is held in memory and saved together with the user (`BeforeUserCreated`), so there is never a password without a row and a failed validation leaves no rows. For an existing person without a row, the store throws instead of creating one: joining needs proof of ownership (see [authentication-flows.md](authentication-flows.md)). Reading with no row returns "no credentials".

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

## ADR-029: Token format
Access tokens are signed JWTs, **not encrypted** (`DisableAccessTokenEncryption`), so remote APIs can validate them with the public keys and no shared secret. Token entries are stored, so revocation works at the server (userinfo, introspection, refresh). APIs that only check the signature accept a revoked access token until it expires (15 minutes by default). Authorization codes and refresh tokens stay encrypted. Reference tokens were not chosen: they need a server call for every request.

## ADR-030: Every client is confidential
Each application has one OpenIddict client with a secret (shown once, stored hashed). There are no public clients, so the password grant can never be used by a public client. Browser-only apps (SPA without a backend) are out of scope for v1. The secret of the seeded `_global` and single-application clients is generated and not shown; `rotate-client-secret` makes a known one.

## ADR-031: Settings drive the OpenIddict client
`IClientProvisioner` copies redirect URIs, scope permissions, the password and two-factor grant permissions and the consent type from `ApplicationSettings` into the client. It is called on create and on every settings update, in the same transaction. Updates copy the stored descriptor (with the hashed secret), so the secret is never changed or double hashed.

## ADR-032: Passthrough endpoints, one issuing path
`/connect/authorize`, `/token`, `/endsession`, `/userinfo` are Minimal API handlers in passthrough mode. There is no `/api/auth2/login`. Error bodies on the token endpoint are `error` + `error_description` + `error_code`; `two_factor_required` also carries `two_factor_token`.

## ADR-033: Hosted session cookie is per application
The cookie `.SystemX.Auth2.Session` carries the claim `app`. The authorize endpoint uses it only for the same application. A user who signed in for application A signs in again for B, because credentials can differ. One cookie holds one login at a time (the last one).

## ADR-034: No user enumeration in login
Unknown user, inactive user, wrong password and "not in this application" return `invalid_credentials`; the audit log keeps the real reason. A dummy password hash is verified for unknown users to even out timing. `login_method_not_allowed` and `application_disabled` are config answers and are returned as they are.

## ADR-035: Two-factor token
The `two_factor_token` is a Data Protection time-limited token (5 minutes) that holds the user id and the application id. It is stateless; replay inside 5 minutes still needs the second factor, and wrong codes count as failed attempts (lockout).

## ADR-036: pv claim
`pv` = application `AccessVersion` + the user's `UserAccessVersion` in that application (single mode: application only). Both only grow, so the sum only grows. Role, permission and group changes raise them.

## ADR-037: SingleActiveSession and the security stamp
Revoking tokens and authorizations is per OpenIddict client, so it is per application. The security stamp is changed only with per-application credentials. With shared credentials the stamp is global, and changing it would sign the user out of other applications too. `ActiveSessionId` in `user_applications` is not used: we revoke all earlier sessions at login, so no session id has to be remembered (and single mode has no such table).

## ADR-038: Keys (superseded by ADR-067)
In Development and Testing the server created temporary keys (RSA signing key and a symmetric encryption key). We do not use `AddDevelopmentSigningCertificate`, because it writes to the machine's certificate store. Other environments need PFX certificates in `Auth2:Keys` and an HTTPS issuer, or the startup guard stops the app. Rotation is done by `IKeyRing` and the `rotate-keys` command.

## ADR-039: Only S256
`plain` is removed from `CodeChallengeMethods`. PKCE is required for the authorization code flow.

## ADR-040: Consent and the OTP grant belong to the accounts work
The consent service and the OTP service are part of the accounts work. The sign-in work only maps the `ConsentMode` setting to the OpenIddict consent type; it has no consent page. The OTP grant constant exists (`Auth2GrantTypes.Otp`) but is not registered yet.

## ADR-041: Email uniqueness moved into our validator
`IdentityOptions.User.RequireUniqueEmail` is now **false**: with `true`, Identity demands an email for every user, and phone-only users (the spec supports them) could not exist. `Auth2UserValidator` reports `DuplicateEmail`, and the unique index on `NormalizedEmail` stays as the last guard (nulls are allowed). This replaces the setting in ADR-015.

## ADR-042: Register never changes an existing person
`IRegisterService` creates a person only when no user matches the user name, email or phone. If one matches, nothing is changed: a member gets `JoinedApplication=true`; a non-member gets a proof code (`IJoinService`). If the identifiers match two different people, nothing happens and the answer looks the same. A duplicate-key race (parallel calls) is handled by catching the unique violation or the `DuplicateEmail` / `DuplicateUserName` error and treating the call as "the person exists". `UserWasCreated` and `JoinedApplication` are for the trusted client backend; the hosted register page hides them.

## ADR-043: Join needs a code to an address that was confirmed before
The proof channel is the confirmed email, else the confirmed phone. An address that is not confirmed is never used (the audit event `join.no_proof_channel` is written). The new password is checked against the rules **before** the code is used. The membership row and the password are saved in one transaction. With shared credentials only the row is added.

## ADR-044: OTP in a table, one open code per user, purpose and application
`otp_codes` stores a salted hash (ISecretHasher). Issuing a new code closes the older open one. "Works once" is one conditional UPDATE, so parallel requests cannot both succeed. Reads use `AsNoTracking`, because `ExecuteUpdate` does not refresh tracked entities. A Redis implementation sits behind `IOtpService`. The creation time is derived from the expiry minus the lifetime in the settings at that time.

## ADR-045: Notification senders are interfaces; the defaults only log a masked line
`IEmailSender` / `ISmsSender` have logging implementations that never write a body. `Auth2:Development:LogMessageBodies` works only in `Development`. Messages are built by `IUserNotifier` from `Emails.resx` in the user's saved culture. `EmailMessage.Data` carries the raw token or code for tests and must not be logged.

## ADR-046: API authentication by an endpoint filter
Account endpoints use `Auth2ApiFilter(scope, TokenKind)`: it authenticates with the OpenIddict validation scheme, checks the token kind (`machine` claim) and the scope, sets the application from the token audience, and answers with `ApiError` JSON. `invalid_token` is 401 only from this filter; for a bad confirmation or reset token it is 400. Users of user endpoints are loaded and checked with `IsSubjectValidAsync` on every call.

## ADR-047: Password change and reset end sessions
Reset ends all sessions of the application and clears the lockout. Change ends all except the session of the caller (kept by the authorization id of the access token). Both raise the user access version and write audit events. A wrong current password counts as a failed attempt.

## ADR-048: Consent
The effective mode is `ConsentPolicy.Effective`: `RequireConsent` off is implicit; on, `Implicit` means explicit. The consent page returns to the authorize URL with a `consent` token (Data Protection, 10 minutes) bound to the user, the application and the exact scope set. Explicit mode saves a permanent OpenIddict authorization and links the tokens to it (so it shows as a session). Systematic mode asks every time. `prompt=none` returns `consent_required`. Deny redirects with `access_denied` only to a registered redirect URI.

## ADR-049: Session cookie is the default authenticate scheme
`HttpContext.User` is the hosted-UI session on every page, so anti-forgery tokens are bound to the same user everywhere. API endpoints and `/connect/userinfo` choose their own scheme explicitly. The Manage pages need the cookie (policy `Auth2.Session`); the application comes from the cookie claim `client_id`, never from the query.

## ADR-050: Access version service
`IAccessVersionService` raises `user_applications.user_access_version` (or the application version in single mode). Reset, change and two-factor changes call it. `IAccessService` and `IGroupService` call it (see ADR-052 to ADR-056).

## ADR-051: Phone and OTP details
Phone login and OTP request look the user up by exact `PhoneNumber`; two users with one number are treated as unknown. A right login code marks the email or phone confirmed. TOTP is not asked after an OTP login.

## ADR-052: Effective access definition
Effective permissions are the union of direct grants, permissions of the user's roles, permissions of the roles of the user's groups, and permissions of the user's groups. Effective roles are own roles plus roles of the user's groups; the token and `/me` use these. In multi mode a non-member has no access. Global roles count only in the `_global` project and in single mode (same rule as `GetRolesAsync`). Obsolete permissions stay effective: "obsolete" is a hint for admins, not a switch.

## ADR-053: Access version is a sum and is read from the database
`accessVersion = Application.AccessVersion + UserAccessVersion`. Both only grow, so each sum belongs to one state. The version is read from the database on each call so other instances see changes at once; only the (heavier) access lists are cached in memory, keyed `(application, user, version)`. User-level changes raise the user version; changes that can reach many users raise the application version. A no-op change raises nothing. A Redis/distributed cache is not needed because the key contains the version.

## ADR-054: Access services work on the current application only
`IAccessService` and `IGroupService` take no application id. They use `IAuthFlowResolver`, so the global query filters apply and nothing can touch another application. The admin API sets the current application from its route or token before it calls them. Roles of other applications are "not found". Granting to a non-member is "not found".

## ADR-055: Keys are checked without regular expressions
`AccessKeys.NormalizePermissionKey` is a small loop (lowercase, 1–100 chars, `a-z0-9._:-`, start and end with a letter or digit). No regex means no timeout problem (pitfall 12). Group names use the same rules. Sync uses the same method.

## ADR-056: `/me` reads only the token
`GET /api/auth2/me` has no id parameter; extra query values are ignored. It needs a user token (`auth2.me`); a machine token gets 403 `forbidden`. The answer is computed now, so it is correct even when the token is older than a role change.


## ADR-057: The user store raises the access version for role changes
`Auth2UserStore` remembers that `AddToRoleAsync` / `RemoveFromRoleAsync` ran and raises the access version right after `UpdateAsync` saves. So a role change made with a plain `UserManager` (seeder, scripts, other code) is as safe as one made with `IAccessService`. `IAccessService.AssignRoleAsync` / `RemoveRoleAsync` rely on this and do not bump twice.

## ADR-058: Sync is serialized per application by an advisory lock
Parallel syncs of one application (many instances starting together) deadlocked on inserts of roles and links. Each sync now runs in a transaction that first takes `pg_advisory_xact_lock` for the application, so syncs queue up. Unique indexes stay as a second guard. The work runs inside the EF execution strategy, because user transactions need it.

## ADR-059: Sync rules
Validate everything first, then write (all-or-nothing validation). Permissions: admin-made text is never overwritten; a re-sent obsolete permission is restored; `AllowRemovePermissionsFromSync` marks missing sync permissions obsolete and never deletes. Roles: matched only inside the application; `GlobalAdmin` and `ProjectAdmin` are reserved names; permission links are only added; unknown permission keys in roles are created as sync permissions when permission sync is allowed; new links raise the application access version. `displayName` of a role is not stored (the role entity has none).

## ADR-060: Handshake compares, it does not select
The handshake takes the application from the token and compares the body with it. Mode mismatch and name/key mismatch are `409`. Name comparison ignores case and outer spaces; the project key is compared ignoring case too (keys are lowercase anyway). `issuer` comes from `Auth2:Issuer` or the request address. `GET /users/{id}/access` needs the `auth2.admin` scope, which is not allowed to clients by default, and returns `404` for non-members.

## ADR-061: Admin guard is an endpoint filter, roles are read on every call
Policies `RequireGlobalAdmin` and `RequireProjectAdmin` are implemented by `AdminApiFilter(AdminLevel)`, like the other API filters (ADR-046). It finds the caller in a **separate DI scope** for the home application (the token audience); the request scope then switches to the target application. `GlobalAdmin` counts only for tokens of the `_global` project or the single application (it is a global role). Roles and user state are read on every call, so a revoked admin loses access at once.

## ADR-062: Choosing the application
A global admin picks the application with `X-Auth2-Application: {projectKey}` (default: the home application). A project admin who names another application gets 403. Objects of other applications are 404 (not 403) so their existence is not shown. Everything below the filter uses the normal services and the EF query filters, so the application rule is enforced twice.

## ADR-063: Admin never sets a password for an existing person
`POST /admin/users` for an existing person only adds the membership row. A password in the request is ignored. This keeps the rule against account pre-hijacking (5.4): only the person, with proof, sets a password. Admins also cannot lock, remove or de-admin themselves, and only a global admin handles `GlobalAdmin`.

## ADR-064: Settings editing and secrets
A project admin may change only settings marked `ProjectAdminEditable`; `SettingsEditability` compares old and new values and lists changed `GlobalOnly` paths (403 with the paths). Settings use `ETag: "v<SettingsVersion>"` and optional `If-Match`; users use the Identity concurrency stamp. Client secret rotation is global-only unless `Clients.AllowProjectAdminRotateSecret` is on. Secrets are returned once, never in later reads, never in audit data.

## ADR-065: Audit enrichment and project role creation
`AuditService` adds the admin id (from `HttpContext.Items`), the IP address and the user agent (cut to 256 characters) to every event written during a web request. The `ProjectAdmin` role is created lazily per application the first time someone is made project admin; deleting an application deletes it with the rest (cascade).

## ADR-066: Rate limiting is our own middleware with a small store interface
ASP.NET's built-in limiter cannot count in Redis, so `RateLimitMiddleware` maps a request to a named policy (`login`, `token`, `register`, `otp`, `password-reset`, `handshake`, `permission-sync`, `admin`) and counts with `IRateLimiter` (memory or Redis, fixed window). Each request is counted for "address + client id + login" and for the address alone (5x the limit). The identity part is the client id and the login together (a client serves many users). Bearer tokens are hashed, never stored. Over the limit: 429, `Retry-After`, `rate_limited`.

## ADR-067: Keys live in a key ring in the database; no temporary keys any more
How it works and how to use it: [key-ring.md](key-ring.md).
When no certificate files are configured, the Server creates a signing and an encryption certificate on first start, stores them protected by Data Protection (`key_ring` table) and every instance uses them. Rotation adds a pair and retires the old one (kept `RetiredKeyDays`=35 for checking and decrypting, then removed by a job). OpenIddict prefers the key with the latest "not after" date, so a new key always gets a strictly later date. Each instance re-reads the table every 30 s; a change fires an options change token so OpenIddict rebuilds its options. The **validation** options copy the server keys when they are built, so a post-configure step refreshes them too (found by a test: new tokens were refused after a rotation). If the table does not exist yet (not migrated), temporary keys keep the server answering until the real keys can be read. The production guard now only requires an HTTPS issuer.

## ADR-068: Redis scope
With Redis on: rate-limit counters, one-time codes and settings invalidation (pub/sub). Data Protection keys and the key ring stay in the database, because Redis may lose data and without those keys cookies, tokens and TOTP keys become unreadable. The access cache stays in memory: its key contains the access version, which is read from the database each time, so it is correct on every instance. Redis down does not stop the start-up (`AbortOnConnectFail=false`); `/health/ready` reports it.

## ADR-069: Jobs use a PostgreSQL advisory lock on a dedicated connection
One lock per job name (`pg_try_advisory_lock`), so with several instances only one runs a job; others skip it. Jobs: old one-time codes, audit retention (default 365 days), OpenIddict pruning, retired key removal. No Redis needed.

## ADR-070: Logging and telemetry choices
Serilog is added as one logging provider (not replacing the others), so tests and other providers keep working. Request logging has no body and no query string. A redaction enricher masks properties with secret-like names. OTLP export is on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. EF Core and Redis instrumentation are pre-release packages. Metrics carry no personal data. CORS is decided per origin from the settings of all enabled applications (cache 60 s), never with credentials. Security headers are added to every answer; `/api` and `/connect` are `no-store`.

## ADR-071: Client returns Result, building blocks throw
`IAuth2ApiClientService` returns `Result` / `Result<T>` (code and message from `ApiError`), so a wrong password or a server that is down is a normal answer. Low-level pieces (`IAuth2MachineTokenProvider`, `MachineTokenHandler`) throw `Auth2Exception(statusCode, errorCode, message, isTransient)` because a `DelegatingHandler` cannot return a result. Exceptions and results never hold secrets. A server that cannot be reached is the client code `server_unavailable`.

## ADR-072: Two named HTTP clients, resilience only where it is safe
`SystemX.Auth2` (never repeated by itself: register, login, password calls, refresh, because a repeated login counts twice towards lockout and a refresh token works once) and `SystemX.Auth2.Resilient` (machine token, handshake, sync, reads; gets retry, timeout and circuit breaker with `EnableResilience()`). A machine token that gets a 401 is dropped and renewed once, for calls that are safe to repeat.

## ADR-073: The handshake decides the server mode
The client never guesses the mode. It sends the project key (or none) and also checks `ServerMode` of a 200 answer. A `409 server_mode_mismatch` becomes a message for the client's side ("Remove ServerWithMultiApplication" or "Call ServerWithMultiApplication(projectKey)"). `Throw` / `Continue` decide start-up; `Continue` + unreachable server retries in the background with a doubling delay (2 s to 60 s, 6 tries). A wrong configuration (not found, rejected) is never retried. The delay uses `TimeProvider`, so tests control time.

## ADR-074: Machine token cache
One cache entry per sorted scope set, kept until `expires_in - 60 s` (at least 30 s), one request at a time per scope set (single flight), failures not cached. The default login scope of the client is `openid offline_access auth2.me`, so `GetMeAsync` works with the tokens it gets.

## ADR-075: Client authorization is policy names plus one requirement type
Attributes derive from `AuthorizeAttribute` and set a policy name such as `auth2:perm:any:orders.read|orders.write`. `Auth2PolicyProvider` builds the policy (authenticated user + one `Auth2Requirement`) and sends every other name to the default provider, so user policies keep working. Several attributes on one endpoint are several policies and ASP.NET combines them with AND. The Minimal API extensions only add these attributes (or plain metadata) to the endpoint, so scanners can read them. `WithOnlyRolePermission` / `WithOnlyUserPermission` are metadata that the permission check reads from the endpoint.

## ADR-076: /me tells where permissions come from
`CurrentUserInfo` got two optional lists, `DirectPermissions` and `RolePermissions` (roles of the user and roles of the user's groups). Permissions given directly to a group are in `Permissions` only. A missing list (older server) makes source checks fail closed.

## ADR-077: Roles, scopes and token kind from claims; permissions and groups from the server
Tokens carry roles and scopes but not permissions (size, see ADR-052 to ADR-056). The cache key is client id + user id; the entry is read again when its version is older than the token's `pv`, or when the time is over. Same-token changes stay hidden until then (documented). When the server cannot be asked: an old cached answer is used, otherwise the check fails closed (403).

## ADR-078: API authentication uses the JWT bearer handler
The standard handler gives discovery, a cached key set and a reload on an unknown key id. Claims are not remapped (`sub`, `role`, `scope`). The audience is the client id, so tokens of other applications are refused. Revocation is not checked (no introspection): tokens are short-lived. After a rotation the first token with an unknown key id triggers a read of the keys and a wait of up to 2 seconds (a signing-key resolver, rate limited by `KeyRefreshInterval`), so no request is refused.

## ADR-079: One default scheme "Auth2" chooses bearer or cookie
When the cookie is used, the default scheme is a policy scheme: a request with `Authorization: Bearer` goes to the JWT handler, every other request to the cookie. A policy scheme can only forward to one scheme per direction, so the difference between "redirect" and "401" is made in the cookie events: `OnRedirectToLogin` / `OnRedirectToAccessDenied` answer 401 / 403 for API-like requests (bearer, JSON `Accept`, AJAX, `[ApiController]`, `AsAuth2Api()`) and redirect otherwise. The hosted UI's `LoginPath` is the package's own `/auth2/login` route, which starts the OpenID Connect challenge.

## ADR-080: Hosted UI = OpenID Connect handler; the hook lives in the package
`UseHostedUi` configures the standard OIDC handler: code + PKCE with `response_mode=query` (the handler's `form_post` default needs cross-site POST cookies with `SameSite=None; Secure`), `SaveTokens`, no inbound claim mapping. Its `OnTokenValidated` event keeps the `pv` claim (read from the access token) and loads access data by `AccessMode` (store, role claims, or nothing). `returnUrl` goes through `AuthenticationProperties.RedirectUri` and only local paths are accepted.

## ADR-081: Cookie lifetime comes from the server
`Auth2CookieOptions.ExpireTimeSpan = null` means "use `CookieLifetime` from the handshake" (set when the cookie is signed in), 8 hours when the server has none. Setting the option wins.

## ADR-082: Sync waits for start and handshake, and never blocks start-up
`Auth2SyncHostedService` is a `BackgroundService`: it waits for `ApplicationStarted` (endpoints exist, so scanning sees them) and for `Auth2ClientState` to be `Ok`, then pushes. Lists are hashed and only sent when changed. Failures are retried (transient only) and then logged; `FailOnSyncError` turns a final failure into a host failure. Pull results go to `IAuth2Catalog`.

## ADR-083: Roles for cookie users
Role checks read role claims. A signed-in principal with no role claim (cookie, `Lazy` mode) falls back to the access data from the server, so `WithRole` works without putting roles in the cookie. The per-request memo of the access provider lives only 3 seconds, so a Blazor circuit never keeps old data for long.

## ADR-084: Group push and scope sync (decision of the project owner)
The first specification said groups are pull-only (`GroupSyncPolicy` = `None`, `Pull`). The owner asked for `Push` and `Both` too. They are added (numbers of `None` and `Pull` unchanged) with safeguards: the server setting `Clients.GroupSyncAllowed` is **off by default**, sync only **adds missing groups**, display names and everything else stay under admin control, nothing is deleted. Groups use the same key rules as permission keys.
Custom scopes: `POST /scopes/sync` registers a scope in OpenIddict (shared registry, first text stays) and adds it to the application's `AllowedScopes` so the client can use it at once. Reserved names (`auth2.*`, standard OIDC scopes) are refused (`invalid_scope_name`). A per-application advisory lock (`RunExclusiveAsync`) makes parallel syncs safe, and the OpenIddict client update is retried on a concurrency conflict. The default `AllowedScopes` now include `auth2.groups.sync` and `auth2.scopes.sync`; applications created earlier need them added by a global admin before their clients can use the new routes.

The settings change that allows the scopes runs in its **own DI scope** (own `DbContext`). The request's context may already track an old copy of the OpenIddict client (loaded while the token was validated); an update through it then fails with a concurrency error, also after a retry. A fresh scope reads the current row (found by the parallel scope sync test).

## ADR-085: External identity — OIDC providers use our own thin code + PKCE client (spike 1) — SUPERSEDED by ADR-096
Question: can providers be added or changed in the database and used without a restart, with two applications using the same provider type at once?
Facts: OpenIddict documents no dynamic client registration (registrations are read when its options are built; the maintainer suggests a change-token source or a derived service). The social web integration also fixes its redirect endpoints in the options.
Decision: the Server uses **its own small OpenID Connect / OAuth 2.0 client** built on `HttpClient`, `Microsoft.IdentityModel` (token and key validation) and the Server's own tables, with **descriptors** for the provider differences (Google, Microsoft, GitHub, Apple, Facebook, generic). Reasons: providers are plain database rows read per request (no option rebuilds, live reload and several instances see changes at once), one callback URL per provider and application, our own SSRF guard, single-use server-side state (the PKCE verifier never travels in the browser), and nothing depends on internals of another library.
Honest note: OpenIddict.Client was **not prototyped in code** when this was written. That was a mistake: the work package asks for the spike (approach (a), then (b), then the fallback) and says the technology is locked unless the spike says otherwise. The spike was done later and showed that approach (a) works; see ADR-096, which replaces this decision.

## ADR-086: SAML — ITfoxtec.Identity.Saml2 (spike 2)
`ITfoxtec.Identity.Saml2` 4.22.3 is used for the SP side. It builds and checks signed responses with plain configuration objects (`Saml2Configuration`), so every provider row becomes one configuration at request time (no authentication scheme per provider, unlike `Sustainsys.Saml2`, whose configuration is centred on ASP.NET authentication schemes).
Proven by `SamlSpikeTests`: a signed response is accepted (claims, name id and assertion id are readable); a response signed with another key is refused; a wrong audience is refused; two trusted certificates work at once (rotation). The test identity provider is built with the same library (`SamlSpikeKit`).
What the library does not do and we add: replay cache (assertion, response and request ids, shared database table), `InResponseTo` binding to a stored request, `Destination` and issuer checks, XXE-safe pre-check of the XML, message size limit, single-use state.

## ADR-087: LDAP — Novell.Directory.Ldap.NETStandard 4.1.0 (spike 3)
Pure managed library, no native packages, works on Linux and macOS. Proven by `LdapSpikeTests` against an OpenLDAP container (`osixia/openldap`, arm64 host): LDAPS and StartTLS with the container's own CA (the server certificate chain **and** the host name are checked in our validation callback), service-account bind, search, re-bind as the found user, attributes (`entryUUID`), group search, wrong password refused, plain LDAP connects only without TLS (the application refuses it), an untrusted certificate fails the handshake.
`System.DirectoryServices.Protocols` needs native LDAP libraries on Linux, so it was not chosen. Lessons: the image's own CA had expired, so the test makes its own CA and server certificate; an empty password can produce an anonymous "success" on some servers, so the application never sends an empty password.

## ADR-088: Passkeys — Identity's passkey API with a per-application store (spike 4)
.NET 10 Identity has `IUserPasskeyStore<TUser>` and an EF model (`IdentityUserPasskey<TKey>` with `IdentityPasskeyData`, table `AspNetUserPasskeys`; here `user_passkeys`). `UserStore` (10 type parameters) has virtual `CreateUserPasskey`, `FindUserPasskeyAsync` and `FindUserPasskeyByIdAsync`, and `IdentityDbContext` has a variant with the passkey type. The Server uses a derived entity `AppUserPasskey` with `ApplicationId` in the key and overrides those methods so every lookup is filtered by the current application. The test of step 2 (`PasskeyStoreTests`) proves that a passkey of application A is "not found" in application B.


## ADR-089: Project and Application are two things (work package "fixing issues")
The old `applications` table was both the tenant and the client. It is split: `projects` (tenant) and `applications` (the OpenIddict application table itself, extended). The application is **not** a second table next to OpenIddict's: `Auth2Application : OpenIddictEntityFrameworkCoreApplication<Guid, ...>` adds `ProjectId`, `IsEnabled`, `Name` and settings. (The property is called `ClientSettings` in code because the base class already has a `Settings` string; the column is `settings_json`.) The type is named `Auth2Application`, not `Application`, because the namespace `SystemX.Auth2.Server.Application` would make the short name ambiguous. All four OpenIddict entities (application, authorization, scope, token) are custom with Guid keys and registered with `ReplaceDefaultEntities` and `UseOpenIddict<...>`; they live in the Infrastructure folder, the rest of the code sees `ClientApplicationInfo`.
Writes go only through `IOpenIddictApplicationManager` (`OpenIddictClientProvisioner`) so validation and caching stay right. Client ids are `{projectkey}.{name}` unless the admin gives one; the seeder uses the project key as client id. `ProjectKey` never changes after creation and is not a secret.
Migration: one hand-written, data-preserving migration (EF would have dropped and re-created the old table). A test upgrades a database made by the last old migration and compares its schema with a fresh one. Pre-1.0, so no down migration (restore a backup).

## ADR-090: One membership per project, one gate for enabled and disabled
A `user_projects` row gives access to every application of the project. No per-application list, no `RestrictUsersToApplications`. `JoinMode` is a project setting.
A project that is disabled blocks all sign-in paths in all applications; a disabled application blocks only itself. One method (`AccessGate.Check`, reached through `IAuthFlowResolver.CheckGateAsync`) decides, in the order project, application, user, lockout, credentials, two-factor, consent. The public answer is `access_denied` (OpenIddict `access_denied` at the token endpoint); the real reason is in the audit log (`project_disabled`, `application_disabled`).
Tokens issued earlier are refused by two OpenIddict event handlers (server `ProcessAuthenticationContext` for code, refresh, userinfo, introspection; validation for the server's own APIs) that ask `IProjectStatusCache`. The cache window is `Auth2:ProjectStatusCacheSeconds` (default 10; tests use 0); a local change clears it at once and Redis pub/sub tells the other instances. Disabling a project revokes authorizations and tokens of all its applications and clears active session ids in one transaction. APIs hosted elsewhere that validate the JWT locally cannot know: their tokens live until they expire (access tokens are short).

## ADR-091: The session cookie is per project, chosen before authentication
The cookie name is `.SystemX.Auth2.Session.{projectkey}` (custom `ICookieManager`). Authentication runs before the page knows its project, so `ProjectCookieMiddleware` sets `IProjectCookieKey` first: from the client id of the request (query, `ReturnUrl`; a cached lookup client id to project key), else from the only session cookie the browser sent (pages like `/Manage` have no client id), else nothing. With several session cookies and no client id nothing is read: a login in one project never counts in another. The cookie claim `project_id` is still checked. External callbacks keep the starting application in a sealed cookie, because the provider URL only names the project.

## ADR-092: Two levels of settings and one resolver
Project settings keep the rules of the tenant. Application settings are the OpenIddict fields plus a small json (UI mode, cookie lifetime, token lifetimes, CORS origins, Self UI flag, shown providers (a subset only), consent mode). `EffectiveSettings` is the only place that says "application value wins where set". Redirect URIs, scopes and consent type are not duplicated in json. Settings that moved were removed from `ProjectSettings` (`Redirects.Allowed*`, `Clients.AllowedScopes`, `AllowSelfUiLogin`, `AllowedCorsOrigins`).

## ADR-093: Admin routes carry the project
Project data lives under `/projects/{projectId}/...` and applications under `/projects/{projectId}/applications/{applicationId}/...`; the old header `X-Auth2-Application` is gone. The filter reads `projectId` from the route: a global admin may use any project, a project admin only the own project (another one is `404`). Creating, changing and deleting applications and projects stays global-only; only a global admin may disable a project. There is no application admin in v1.

## ADR-094: ICurrentClient and ICurrentUser, and the catalog sources
A scope is what a client may do, a permission is what a user may do. `ICurrentClient` (token, client, project, scopes, machine flag, `Idp`, `Amr`, all synchronous) and `ICurrentUser` (the person; `IsAuthenticated` is false for machine tokens) read the same principal. Tokens now carry `application_id` and `client_id` so `ICurrentClient` needs no call.
Catalog sources: one generic `CatalogOptions<TDefinition>` (four catalogs) with eager checks for static sources and a host-start check for providers and factories (`Auth2CatalogStartupService`). Test change: `new Auth2PermissionAttribute()` without keys used to throw; it is allowed now because on a field the attribute only carries metadata (the value is the key). Test change: a ProjectKey sent to a single-project server used to be rejected (`server_mode_mismatch`); it is ignored with a warning, as the package says. Merge rules: ordinal, case-sensitive, same data ignored, different data fails unless `FirstWins`. Explicit sources count when the scanning policy is `Manual` or `Both`.

## ADR-095: Parallel work in one working tree
While this package was implemented, another process changed the same working tree (admin CRUD backend and Tailwind UI pages) and kept breaking builds. The Project/Application work was therefore done in a separate git worktree (branch `fixing-issues`) and merged afterwards. Entities and interfaces that the other process had added (`JobRun`, `BrandingAsset`, `EmailTemplate`, admin service interfaces, `KeyRing.RetireAsync`) were kept; their tables are created by the same migration.

## ADR-096: Social login and generic OIDC use OpenIddict.Client (spike 1, done; replaces ADR-085)
**Question (work package, section 6, spike 1):** can OpenIddict.Client take its provider registrations from the database, with no restart, with two providers of the same kind for two projects at once and a different redirect URI for each?
**Answer: yes, with approach (a).** `IConfigureOptions<OpenIddictClientOptions>` + `IOptionsChangeTokenSource<OpenIddictClientOptions>` (`ExternalClientRegistrations`). The change token fires when `IExternalProviderRegistry` reports a change (settings version poll, Redis message, or the admin call on this instance), so every instance sees a change within the poll time. Approach (b) (a derived `OpenIddictClientService`) and the fallback ("a change may need a restart") were not needed. The proof is in `OpenIddictClientSpikeTests` (generic registration at run time, change, removal, two projects, full code flow with PKCE, a mixed-up callback and a replayed callback are refused, a WebIntegration provider from a row) and in `ExternalClientIntegrationTests`.
**What the Server uses:** `OpenIddict.Client.AspNetCore`, `OpenIddict.Client.SystemNetHttp` and `OpenIddict.Client.WebIntegration` (7.9.0). Google, Microsoft, GitHub, Apple and Facebook come from the WebIntegration package; every other OpenID Connect provider is a generic registration (discovery from the authority). OpenIddict does the protocol (discovery, PKCE, `state`, `nonce`, the token request, the checks of the identity token). Auth2 keeps what is its own: one registration per provider and project, the single-use state, the rules per provider (Microsoft tenant, Google hosted domain, GitHub verified primary email and organizations, Facebook never verified, allowed issuers, claim mappings, groups, MFA), and the mapping to `ExternalIdentity`. The old hand-written client (`OidcIdentitySource`) is removed.
**Findings that shaped the code (all proven by tests):**
1. OpenIddict cannot build its options without at least one redirection endpoint, even with no provider. A static placeholder endpoint is registered.
2. OpenIddict accepts only an `HttpClientHandler` as the primary handler of its HTTP clients. The SSRF guard therefore cannot use a connect callback for these clients: `SsrfGuardHandler` (a delegating handler, added by `ExternalClientHttpFilter` to every client named `OpenIddict.Client...`) checks https, user info and that every resolved address is public **just before the request**. The gap between that check and the connection is the price of using the library. The Server's own client (SAML metadata, GitHub calls) still checks at connect time.
3. `DisableTokenStorage()`: upstream tokens never go into the Server's own token table (a test counts the rows).
4. The type of a WebIntegration provider is an internal id, so a registration cannot be written by hand. It is made by the library's own public builder in a small container and copied. That container keeps being used (discovery and keys), so it lives for two rebuilds; the HTTP filters of the main container (guard, test fake) are copied into it. A registration that cannot be built is skipped and logged by the type of the problem only (never its message, which may name a secret), because OpenIddict throws for **every** provider when one registration is bad.
5. OpenIddict's ASP.NET Core handler needs an `HttpContext`. `OpenIddictIdentitySource` runs it on a private context (same address, no real response, `IHttpContextAccessor` pointed at it for the call) and keeps the OpenIddict cookies inside the sealed state that `ExternalFlow` already stores in its own short-lived cookie. The real callback request must not be handled by the OpenIddict middleware (it would look for its own cookie and answer 400): `UseAuthentication` is not run for `/callback/login/...` (the explicit `AuthenticateAsync` calls still work), and the marker that stops WebApplication from adding its own is set by hand.
6. A provider that sends the RFC 9207 `iss` parameter (Google, GitHub) makes OpenIddict require it.
7. Differences from the old client, all accepted: OpenIddict does not check the lifetime of an identity token that comes over the back channel, so Auth2 checks `exp` (two minutes of clock difference); for a specific Microsoft tenant a token of another tenant now fails at the issuer check (`issuer_invalid`) before the allow-list; the Facebook Graph version is applied to the library's endpoints (`ApiVersion`); state and nonce are checked by OpenIddict, the single use of a state is still enforced by the replay table.
8. Multi-instance: the state token is protected with the Server's shared key ring credentials (copied into the client options), so any instance can finish a sign-in that another one started. After a key rotation the client options use the new keys at the next provider reload.
**Not changed:** SAML (ITfoxtec, ADR-086), LDAP, passkeys, the provider model, the admin API, the callback URLs.

## ADR-097: Email and SMS go through factories (provider per project and application)
**Decision.** `IEmailSender` and `ISmsSender` stay the contract the server code uses. Their default implementations ask `IEmailFactory` / `ISmsFactory`, which read `EffectiveSettings` (application, else project) and fall back to `Auth2:Messaging`, then to logging. A provider is an `IEmailTransport` / `ISmsTransport` selected **by name**, so a developer adds one with one registration and an admin selects it without a deploy. Shipped: `logging`, `smtp` (MailKit), `google` (the SMTP transport with fixed host, port and STARTTLS).
**Why names and not an enum.** A closed enum would make every new provider a change of the settings schema and of the validator. A name checked against the registered transports keeps the schema stable.
**Secrets.** The SMTP password is stored encrypted (`IExternalSecretProtector`, the same Data Protection keys as provider secrets), never returned (`passwordIsSet`), never written to the audit event, and not changed by the generic settings routes (they copy the stored value back). The server-level password in configuration is plain by nature and the docs say to keep it in a secret store.
**Who may change it.** Global admin only: the setting decides where addresses, links and codes are sent and holds a password. A project-admin-editable variant was rejected.
**Package.** MailKit 4.18.2 (4.13.0 was refused by the build: known advisories). `System.Net.Mail.SmtpClient` was not used (Microsoft marks it obsolete for new code).
**Not done.** No admin console page yet (the routes exist), no "send a test email" route, no real SMS provider (only the extension point and `logging`).

## ADR-098: Registration can hand out roles, groups and permissions (not scopes)
**Decision.** `Registration.AssignedRoles`, `AssignedGroups` and `AssignedPermissions` list what every new member of a project gets. One service (`IRegistrationDefaults`) does it and is called from the three places that create a member by themselves: register, join with proof of ownership, and the first external sign-in. Admin-created users are not touched (the admin chooses).
**Rules.** The settings are `GlobalOnly` (they decide what rights a stranger gets). The validator refuses `GlobalAdmin`, `Developer` and `ProjectAdmin`, bad names, duplicates and more than 50 entries; the service refuses the admin roles again at run time. A name that does not exist is skipped, logged by count and written to the audit event `register.defaults_applied` with an `assigned` and a `skipped` list; it never fails a registration.
**Scopes are left out on purpose.** A scope is what a **client** may ask for (it lives on the application), and a person has no scopes, so there is nothing to assign at registration. Which scopes an application may use is the `allowedScopes` of the application.

## Related pages

- [Concepts](concepts.md)
- [Architecture](architecture.md)
- [Database tables](database-tables.md)
- [Credential scope](credential-scope.md)
- [Comparison and limits](comparison-and-limits.md)
- [Roadmap](roadmap.md)
- [Feature status](feature-status.md)
- [Gateway](gateway.md)
- [Contributing workflow](contributing-workflow.md)
- [Email and SMS](email-and-sms.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Testing](testing.md)
- [Documentation index](index.md)
