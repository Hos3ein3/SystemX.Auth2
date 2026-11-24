# Changelog

All notable changes are listed here. The format follows "Keep a Changelog".

## [Unreleased]

### Changed
- **Social login and generic OpenID Connect now use OpenIddict.Client** (`OpenIddict.Client.AspNetCore`, `.SystemNetHttp`, `.WebIntegration`) instead of a hand-written client. Registrations are built from the provider list at run time (no restart); see ADR-096 for the spike result and the findings. Behaviour changes: a specific Microsoft tenant refuses another tenant at the issuer check; Apple, Google and GitHub follow the library (RFC 9207 `iss`); the Facebook Graph version is applied to the library's endpoints. Upstream tokens are never stored in the Server's token table.
- The HTTP clients of OpenIddict.Client get the SSRF guard (`SsrfGuardHandler`). The answer of a provider (`/callback/login/...`) is read by Auth2 itself; the OpenIddict middleware does not handle it.

### Changed (breaking, pre-1.0)
- **Project vs Application.** The old "application" (tenant + client) is split. **Project** = the tenant (table `projects`, `ProjectKey` never changes, project settings). **Application** = a client of a project and **is** the OpenIddict application table (`applications`, custom OpenIddict entities with `ProjectId`, `IsEnabled`, `Name` unique inside the project, `SettingsJson`), created, changed and deleted only through `IOpenIddictApplicationManager`.
  `user_applications` is now `user_projects`; every tenant table uses `ProjectId` (the query filter too). Audit events, sessions and synced catalog items keep an **optional** `ApplicationId`. One data-preserving migration (`ProjectsAndApplications`, hand-written, upgrade test compares the result with a fresh schema). OpenIddict major upgrades need a migration review.
  Access rule: a member of a project can use every application of it. Joining is a project setting.
  Enabled/disabled: one shared gate (`AccessGate`) for every sign-in path and token (OpenIddict event handlers for validation); generic public answer `access_denied`, the reason only in the audit log; disabling a project revokes tokens, authorizations and sessions in one transaction; a short cached status (`Auth2:ProjectStatusCacheSeconds`).
  `ServerWithSingleApplication()` is now `ServerWithSingleProject()`, `IApplicationContext` is `IProjectContext`, new `IClientApplicationContext`; claims are `project_id`, `projectkey`, `client_id`, `application_id` (the `app` claim is gone); the login cookie is per project (`.SystemX.Auth2.Session.{projectkey}`).
  Two-level settings with one resolver (`EffectiveSettings`): project default, overridden by the application value. Handshake request: `ProjectKey` (multi mode only) + `ApplicationName`; the answer returns `ProjectId` and `ApplicationId`; a key sent to a single-project server is ignored with a warning.
  Admin API: `/projects`, `/projects/{projectId}/users|roles|permissions|groups|settings|audit-events|external-*`, `/projects/{projectId}/applications/{applicationId}/...` (client setup page data, secret shown once). Commands: `create-project`, `create-application`, `rotate-client-secret <clientId>`.
- **`ICurrentClient` and `ICurrentUser` are split.** `ICurrentClient`: token, client id, application id, project id and key, scopes, `HasScope`, machine flag, session id, `Idp`, `Amr` (synchronous, from claims). `ICurrentUser`: the person only (`IsAuthenticated` is false for machine tokens).
- **Catalog sources.** One generic `CatalogOptions<T>` for permissions, roles, scopes and groups with `Add`, `AddFromClass`, `AddFromEnum`, `AddFromString`, `AddFromListString`, `AddProvider`, `AddFactory`, `AddFromConfiguration`; merge by key, `DuplicatePolicy`, startup validation that names the source; `[Auth2Key]`, `[Auth2Category]`, attributes usable on fields.

### Added
- External identity step 10: Client `ICurrentUser.IdentityProvider` and `AuthenticationMethods` (`idp`, `amr` claims), `IAuth2ApiClientService.GetExternalProvidersAsync()` and the server route `GET /api/auth2/external-providers`. Docs: Self UI limits in `client-setup`, `client-current-user`, `external-identity`.
- External identity step 9: admin API for external providers (`IAdminExternalService`): types, list, create, update with row version, rotate secret, delete, test, LDAP user lookup, SAML metadata refresh and SP certificate, mappings, email domain routes, user external logins and passkeys, blocked AAGUIDs (`server_values` table).
  Secrets are never returned or audited; project admins cannot turn on trust flags; the generic settings route keeps the provider list and hides secrets. Migration `Phase14ServerValues`.
- External identity step 8: passkeys (`IPasskeyService`) on the ASP.NET Core Identity passkey support with a per-application store: registration and sign-in routes (`/account/passkey/*`), management routes and page (`/account/passkeys`, `Manage/Passkeys`),
  limits, user verification rule, backup rule when unlinking, blocked AAGUIDs and allow list, `Auth2:Passkeys:ServerDomain` (required in Production), `amr` `swk`/`hwk` + `mfa`, local two-factor skipped for a user-verified passkey, rate limit policy `passkey`.
  Docs: passkeys guide, JSON contract in `ui-handover`.
- External identity step 7: LDAP and Active Directory (`LdapDirectory`): LDAPS or StartTLS with chain and name checks, service-account search with RFC 4515 escaping, user bind (no empty passwords), unique id as link key (`entryUUID`, `objectGUID`), groups with optional nesting,
  disabled-account handling, modes `LdapOnly`, `LocalThenLdap`, `LdapThenLocal` (`LdapAwareLoginService`), rate limit policy `ldap`, failed passwords count toward the lockout, test lookup and connection test services, `ldap_unavailable`. Docs: OpenLDAP and Active Directory guides, troubleshooting.
- External identity step 6: SAML 2.0 service provider (`SamlIdentitySource`): AuthnRequest (Redirect or POST binding, optional signing), ACS with XXE-safe parsing, size limit, signature-wrapping defences, pinned certificates, issuer, destination, audience, `InResponseTo`, time window, single-use request and assertion ids,
  IdP-initiated login (off by default), encrypted assertions, SP metadata, IdP metadata import with cache and last-good fallback, SP certificate generation, optional IdP-initiated single logout. Pages `Saml/{Acs,Metadata,Slo}`.
  Docs: five setup guides, certificate rotation, how the flow is validated.
- External identity step 5: `IExternalLoginService` (decision table: link, link by verified email, register, email confirmation step), claim and group mapping (`IExternalMappingService`: add, remove only mapped, never GlobalAdmin or system roles, same application only),
  `IExternalLinkService` (list, link, unlink with `cannot_remove_last_login_method`), `ILoginService.ExternalLoginAsync` (same pipeline as password login), `idp` and `amr` token claims, home-realm discovery,
  `GET /connect/external/providers`, `POST /connect/external/challenge`, pages `Account/External/{Challenge,Callback,Confirm}` and `Manage/ExternalLogins`, rate limit policies `external`, `ldap`, `saml-acs`, `passkey`.
  Docs: `authentication-flows`, `ui-handover`, `error-codes`, brokering and group mapping guides.
- External identity step 4: OpenID Connect / OAuth 2.0 engine (`OidcIdentitySource`) for Google, Microsoft, GitHub, Apple, Facebook and generic OIDC (code flow, PKCE, state, nonce, single-use state through `IReplayCache`,
  provider rules: Google `HostedDomain`, Microsoft `AllowedTenants`, GitHub organizations and verified email, Apple signed client secret, generic `AllowedIssuers`), endpoint discovery with cache, SSRF guard for all provider calls,
  `ExternalIdentity` and source abstractions. Docs: setup guide per provider, engine description, how to add a provider type.
- External identity step 3: `IExternalProviderRegistry` (enabled providers of all applications in memory), provider URLs per application and provider (`ExternalProviderUrls`), reload on every instance (Redis pub/sub plus polling,
  option `Auth2:ExternalProviders:PollSeconds`). Docs: `external-identity` (URLs, runtime changes), `deployment-and-scaling`.
- External identity step 2 (data model): `AppUserLogin` is per application (key `application_id, login_provider, provider_key`), passkey store per application (`user_passkeys`, `AppUserPasskey`),
  `external_group_mappings`, `replay_entries`, polymorphic provider settings (`OidcProviderSettings`, `SamlProviderSettings`, `LdapProviderSettings`) and `PasskeySettings`, `ExternalProviderSettingsValidator`,
  provider descriptors, `IExternalSecretProtector` (encrypt and redact), migration `Phase14ExternalIdentity`, English texts for the new error codes. Docs: `external-identity` (settings reference, data model), `server-settings`, `credential-scope`, `feature-status`.
  Changed: the old experimental `ExternalProviderSettings` (single class) is replaced; the settings JSON reader accepts out-of-order `kind` (jsonb reorders keys).
- Scope and group sync: `ScopeScanner`, `GroupScanner`, `EnableScopes`, `ScopeSyncPolicy`; `GroupSyncPolicy.Push` and `Both`; server routes `POST /groups/sync`, `POST /scopes/sync`, `GET /scopes`;
  settings `Clients.ScopeSyncAllowed` and `Clients.GroupSyncAllowed`; scopes `auth2.groups.sync` and `auth2.scopes.sync`; error code `invalid_scope_name`.
- Phase 13: Client cookie authentication, hosted UI (OpenID Connect, code + PKCE) and Self UI (`IAuth2WebService`), `/auth2/login` and `/auth2/logout` routes, `OnTokenValidated` access loading (`AccessMode`),
  cookie lifetime from the server, API-versus-browser answers (401 / 403 or redirect), Blazor helpers (`UseBlazor`, `IAuth2BlazorService`), permission and role scanners, generic catalog sync service,
  sync hosted service (push, pull, both), `IAuth2Catalog`. Docs: `client-setup` (UI modes and sync), updates to `permission-sync` and `client-current-user`.
- Phase 12: Client authorization: attributes and Minimal API extensions (`WithAuth2`, `WithAnonymous`, `WithRole`, `WithAllRoles`, `WithPermission`, `WithAllPermissions`, `WithOnlyRolePermission`, `WithOnlyUserPermission`, `WithScope`,
  `WithMachineOnly`, `WithUserOnly`, `WithGroup`, `WithAuth2Policy`, `WithAuth2Audit`), policy provider and handlers, audit sink, `ICurrentUser`, `IAuth2CurrentUserService`, current user stores (memory, distributed),
  `UseApiAuthentication` (JWT bearer with key refresh), `UseDistributedCurrentUserStore`. Server: `/me` returns `DirectPermissions` and `RolePermissions`. Docs: `client-authorization`, `client-current-user`.
- Phase 11: Client core (`SystemX.Auth2.Client`): `AddAuth2Client` (URL or configuration section), builder methods (`ServerWithMultiApplication`, `EnablePermissions`, `EnableRoles`, `EnableGroups`,
  `EnableMachineToMachine`, `EnableCurrentUser`, `EnableResilience`, `EnableHealthCheck`), validated options, machine token provider and handler, handshake with the server-mode check, start-up behaviour and
  background retries, `Auth2ClientState`, health check, `IAuth2ApiClientService`. Docs: `client-setup`, `machine-to-machine`.
- Phase 10: rate limiting (named policies, memory or Redis), optional Redis (counters, one-time codes, settings pub/sub), key ring in the database with `rotate-keys` command and admin route,
  `create-admin` command, background jobs under a database lock, health checks (`/health/live`, `/health/ready`), HTTP hardening (headers, HSTS, forwarded headers, CORS from settings),
  Serilog with redaction and correlation ids, OpenTelemetry traces and metrics. Docs: `security`, `deployment-and-scaling`, `observability`.
  Changed: temporary development keys are gone (the key ring is used); the production guard needs only an HTTPS issuer.
- Phase 9: admin JSON API under `/api/auth2/admin` (applications, settings with ETag, redirect URIs, secret rotation, users incl. lock / unlock / reset-2fa / sessions / roles / permissions / groups,
  roles, permissions, groups, audit events), global and project admin guards, project selection header, audit events carry actor, IP and user agent. Docs: `admin-api`.
  Fixed: a malformed session id no longer throws when revoking a session.
- Phase 8: client handshake with server-mode check, permission and role sync (idempotent, never deletes, serialized per application), pull routes for permissions, roles and groups,
  `GET /users/{id}/access`. Docs: `permission-sync`, `handshake-and-startup`.
- Phase 7: access model: `IAccessService` (effective access, access version, cached by version, permissions, roles, role permissions, direct grants) and `IGroupService`
  (groups, members, roles, permissions), `GET /api/auth2/me`, effective roles in tokens, audit events for every change, `PermissionDto` / `RoleDto` / `GroupDto` in Contracts.
  Docs: `authorization`.
- Phase 1: solution skeleton, central package management, CI workflow, docs skeleton.
- Phase 6: register (idempotent, race-safe), join a second application with proof of ownership, one-time codes (`IOtpService`) and OTP login grant,
  email and phone confirmation, forgot / reset / change password, authenticator-app two-factor with recovery codes, consent (explicit and systematic),
  sessions (list, revoke), email and SMS sender interfaces with safe development implementations, access version service, JSON endpoints under `/api/auth2`,
  pages `Register`, `ForgotPassword`, `ResetPassword`, `ConfirmEmail`, `ConfirmPhone`, `VerifyOtp`, `JoinApplication`, `Consent`, `Manage/*`.
  Fixed: phone-only users (email is no longer required), stale OTP attempt counts, localized texts with placeholders.
- Phase 5: OpenIddict server (authorization code + PKCE, client credentials, refresh rotation with reuse detection, optional password and two-factor grants),
  `ILoginService`, `ITokenIssuer`, `ISessionService` (single active session), `IReturnUrlValidator`, the hosted-UI cookie, pages `Account/Login`, `TwoFactor`, `Logout`, `Lockout`, `AccessDenied`, `Error`,
  client provisioning from settings, `rotate-client-secret` command, production guard for keys and issuer. Docs: `authentication-flows`.
- Phase 4: Identity customization: `Auth2UserStore` / `Auth2RoleStore`, credential scope strategies (`PerApplication`, `Shared`), per-application password / user / lockout rules,
  `Auth2IdentityErrorDescriber`, role rules per application, resx localization (`en` + `fa`, `nl`, `it` skeletons), culture order. Docs: `credential-scope`, `localization`.
- Phase 3: Server data model (Domain, `Auth2DbContext`, `InitialCreate` migration, query filters, `ApplicationSettings` jsonb + validator),
  `IApplicationContext` (single and multi), `ServerWithSingleApplication()`, `Auth2Seeder`, commands `migrate`, `seed`, `create-application`.
  Docs: `server-setup`, `seeding`, `server-settings`, `concepts`.
- Phase 2: `SystemX.Auth2.Contracts` with DTOs, enums and constants, plus tests.
