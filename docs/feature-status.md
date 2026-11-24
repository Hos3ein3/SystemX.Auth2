# Feature status

What works today. "Done" means: code, tests, docs and a CHANGELOG entry exist.

| Area | Status | Notes |
|---|---|---|
| Server: identity, tokens, sessions, hosted pages | Done | Phases 1 to 10 |
| Server: access model, sync, admin API | Done | Phases 7 to 9 |
| Client package (auth, authorization, sync, UI modes) | Done | Phases 11 to 13, scope and group sync |
| External identity: data model, settings, descriptors, secrets | Done | Work package step 2 |
| External identity: provider registry and reload | Done | Step 3 |
| Social login and generic OIDC (OpenIddict.Client + WebIntegration, run-time registrations, SSRF guard, setup guides) | Done | Step 4, rebuilt on OpenIddict.Client after the spike ([ADR-096](decisions.md)) |
| Email and SMS factories (provider per project and application), SMTP and Google providers, admin routes | Done (no admin page, no test-send route) | [ADR-097](decisions.md) |
| Login service, mapping, brokering, unlink rules, pages | Done | Step 5 |
| SAML 2.0 service provider (ACS, metadata, SLO, rotation) | Done | Step 6 |
| LDAP and Active Directory | Done | Step 7 |
| Passkeys (per project, limits, backup rule, AAGUID block list) | Done | Step 8 |
| Admin API for providers, mappings, domain routes, support routes | Done | Step 9 |
| Client changes for external identity (`IdentityProvider`, `AuthenticationMethods`, `GetExternalProvidersAsync`) | Done | Step 10 |
| Samples and final docs | Planned | |

This page is updated at every step of the external identity work package.
| Project vs Application (split, one OpenIddict table, one membership, per-project cookie) | Done | Work package "fixing issues", part 1 |
| Enabled / disabled rules (project, application, shared gate, token refusal) | Done | Part 1 |
| Two-level settings and the effective-value resolver | Done | Part 1 |
| `ICurrentClient` / `ICurrentUser` split | Done | Part 2 |
| Catalog sources (class, enum, string, list, provider, factory, configuration) | Done | Part 3 |
