# Feature status

## What this is

What works today. "Done" means: code, tests, docs and a CHANGELOG entry exist. What is missing is on [roadmap.md](roadmap.md).

## Status

| Area | Status | Where to read |
|---|---|---|
| Server: identity, login, tokens, sessions, hosted pages | Done | [authentication-flows.md](authentication-flows.md) |
| Server: accounts (register, join, confirm, OTP, reset, two-factor, consent) | Done | [authentication-flows.md](authentication-flows.md) |
| Registration defaults: assigned roles, groups and permissions; confirmation email and SMS switches | Done | [server-settings.md](server-settings.md#registration), [ADR-098](decisions.md) |
| Server: access model (permissions, roles, groups, `/me`), sync and handshake | Done | [authorization.md](authorization.md), [permission-sync.md](permission-sync.md), [handshake-and-startup.md](handshake-and-startup.md) |
| Password history (old password hashes, no reuse) and password expiry | Done | [password-history.md](password-history.md), [ADR-099](decisions.md) |
| Admin JSON API | Done | [admin-api.md](admin-api.md) |
| Admin console pages (`/admin/...`) | Done for projects, applications, users, roles, permissions, groups, assignments, scopes, authorizations, providers, sessions, audit, reports, settings, branding, email templates and the global pages | [ui-handover.md](ui-handover.md) |
| Security and operations (rate limits, key ring, Redis, health, logs, traces) | Done | [redis.md](redis.md), [security.md](security.md), [deployment-and-scaling.md](deployment-and-scaling.md), [observability.md](observability.md) |
| Project and Application split, one membership, per-project cookie | Done | [concepts.md](concepts.md) |
| Enabled / disabled rules (project, application, shared gate, token refusal) | Done | [concepts.md](concepts.md) |
| Two-level settings and the effective-value resolver | Done | [server-settings.md](server-settings.md) |
| Email and SMS factories (provider per project and application), SMTP and Google providers, admin routes | Done (admin page `/admin/p/{key}/messaging` and test-send routes) | [email-and-sms.md](email-and-sms.md), [ADR-097](decisions.md) |
| External identity: provider model, registry, run-time reload, secrets | Done | [external-identity.md](external-identity.md) |
| Social login and generic OIDC (OpenIddict.Client + WebIntegration, SSRF guard, setup guides) | Done | [social-webintegration.md](external-identity/social-webintegration.md), [ADR-096](decisions.md) |
| SAML 2.0 service provider (ACS, metadata, single logout, rotation) | Done | [saml.md](external-identity/saml.md) |
| LDAP and Active Directory | Done | [ldap.md](external-identity/ldap.md) |
| Passkeys (per project, limits, backup rule, AAGUID block list) | Done | [passkeys.md](external-identity/passkeys.md) |
| Login service, mapping, brokering, unlink rules, external pages | Done (the sign-in page shows the provider and passkey buttons) | [brokering.md](external-identity/brokering.md), [ui-handover.md](ui-handover.md) |
| Admin API for providers, mappings, domain routes, support routes | Done | [admin-api.md](admin-api.md#external-identity-routes) |
| Client package: current user and client split, catalog sources, scanning and sync | Done | [client-setup.md](client-setup.md), [client-current-user.md](client-current-user.md), [permission-sync.md](permission-sync.md) |
| Client package: API (JWT), cookie, hosted UI, Self UI, Blazor | Done | [client-setup.md](client-setup.md), [client-authorization.md](client-authorization.md) |
| Client changes for external identity (`IdentityProvider`, `AuthenticationMethods`, `GetExternalProvidersAsync`) | Done | [client-current-user.md](client-current-user.md) |
| Samples (`Mvc`, `MinimalApi`) and `docker-compose.yml` | Done | [getting-started.md](getting-started.md) |
| Sign-in methods: user name, email or phone with a password; email or phone one-time code; external provider; LDAP; passkey | Done | [features.md](features.md), [authentication-flows.md](authentication-flows.md) |
| Two-factor: authenticator app, recovery codes (sign in and turn off), admin reset | Done | [two-factor.md](two-factor.md) |
| Sudo mode (a fresh code before a restricted Server page) and `/Account/VerifyTotp` | Done | [two-factor.md](two-factor.md) |
| Step-up for applications: `acr_values`, `step_up_at` / `step_up_until` claims, one window `TwoFactor.StepUpMinutes` | Done (hosted UI only) | [client-endpoint-metadata.md](client-endpoint-metadata.md), [sample-mvc.md](sample-mvc.md) |
| Client: `WithTotp()` / `[Auth2Totp]` ask the Server for the code in the hosted UI; samples in MVC and MinimalApi | Done | [client-endpoint-metadata.md](client-endpoint-metadata.md) |
| Client: manage people, roles, permissions, groups, scopes and their links (`Admin` of `IAuth2ApiClientService` and `IAuth2WebClientService`) | Done | [client-api-service.md](client-api-service.md) |
| Client: `ICurrentUser` (`HasGroupAsync`, confirmed email and phone) | Done | [client-current-user.md](client-current-user.md) |
| Hybrid cache (memory and Redis) for settings, project and application; settings kept one day | Done | [redis.md](redis.md), [server-settings.md](server-settings.md) |
| Digits-only code boxes; one error message per page; settings, branding and messaging pages save only their own part | Done | [security.md](security.md), [server-settings.md](server-settings.md) |
| Gateway | Not built | [gateway.md](gateway.md) |

This page is updated whenever a feature is finished.

## Related pages

- [Features](features.md)
- [Comparison and limits](comparison-and-limits.md)
- [Changelog](../CHANGELOG.md)
- [Documentation index](index.md)
