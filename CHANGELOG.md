# Changelog

All notable changes are listed here. The format follows "Keep a Changelog".

## [Unreleased]

### Added
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
