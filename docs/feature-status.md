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
| Admin JSON API | Done | [admin-api.md](admin-api.md) |
| Admin console pages (`/admin/...`) | Done for projects, applications, users, roles, permissions, groups, assignments, scopes, authorizations, providers, sessions, audit, reports, settings, branding, email templates and the global pages | [ui-handover.md](ui-handover.md) |
| Security and operations (rate limits, key ring, Redis, health, logs, traces) | Done | [security.md](security.md), [deployment-and-scaling.md](deployment-and-scaling.md), [observability.md](observability.md) |
| Project and Application split, one membership, per-project cookie | Done | [concepts.md](concepts.md) |
| Enabled / disabled rules (project, application, shared gate, token refusal) | Done | [concepts.md](concepts.md) |
| Two-level settings and the effective-value resolver | Done | [server-settings.md](server-settings.md) |
| Email and SMS factories (provider per project and application), SMTP and Google providers, admin routes | Done (no admin page, no test-send route) | [email-and-sms.md](email-and-sms.md), [ADR-097](decisions.md) |
| External identity: provider model, registry, run-time reload, secrets | Done | [external-identity.md](external-identity.md) |
| Social login and generic OIDC (OpenIddict.Client + WebIntegration, SSRF guard, setup guides) | Done | [social-webintegration.md](external-identity/social-webintegration.md), [ADR-096](decisions.md) |
| SAML 2.0 service provider (ACS, metadata, single logout, rotation) | Done | [saml.md](external-identity/saml.md) |
| LDAP and Active Directory | Done | [ldap.md](external-identity/ldap.md) |
| Passkeys (per project, limits, backup rule, AAGUID block list) | Done | [passkeys.md](external-identity/passkeys.md) |
| Login service, mapping, brokering, unlink rules, external pages | Done (the sign-in page has no provider or passkey buttons yet) | [brokering.md](external-identity/brokering.md), [ui-handover.md](ui-handover.md) |
| Admin API for providers, mappings, domain routes, support routes | Done | [admin-api.md](admin-api.md#external-identity-routes) |
| Client package: current user and client split, catalog sources, scanning and sync | Done | [client-setup.md](client-setup.md), [client-current-user.md](client-current-user.md), [permission-sync.md](permission-sync.md) |
| Client package: API (JWT), cookie, hosted UI, Self UI, Blazor | Done | [client-setup.md](client-setup.md), [client-authorization.md](client-authorization.md) |
| Client changes for external identity (`IdentityProvider`, `AuthenticationMethods`, `GetExternalProvidersAsync`) | Done | [client-current-user.md](client-current-user.md) |
| Samples (`Mvc`, `MinimalApi`) and `docker-compose.yml` | Done | [getting-started.md](getting-started.md) |
| Gateway | Not built | [gateway.md](gateway.md) |

This page is updated whenever a feature is finished.

## Related pages

- [Comparison and limits](comparison-and-limits.md)
- [Changelog](../CHANGELOG.md)
- [Documentation index](index.md)
