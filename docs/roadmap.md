# Roadmap

## What this is

What is not built yet, and the limits you should know. Everything that works today is in [feature-status.md](feature-status.md). What the product does and does not do is in [comparison-and-limits.md](comparison-and-limits.md).

## Future work

- **Admin and sign-in pages.** The admin console (`/admin/...`) covers projects, applications, users, roles, permissions, groups, assignments, scopes, authorizations, providers, sessions, audit, reports, settings, branding, messaging (email and SMS, with test-send) and email templates. The sign-in page has the provider and passkey buttons, and the join page works. What is left is polish, not missing pages: the descriptions of the pages for a custom UI in [ui-handover.md](ui-handover.md) still say that these pages are not drawn, and are to be brought up to date.
- **Real SMS provider.** Only the extension point (`ISmsTransport`) and the `logging` provider exist ([email-and-sms.md](email-and-sms.md)).
- **Gateway package** (YARP): the project is empty ([gateway.md](gateway.md)).
- **Commands** `convert-to-single` and `convert-to-multi` (move credentials between the two places described in [credential-scope.md](credential-scope.md)). The other commands exist ([server-setup.md](server-setup.md)).
- SAML **identity provider** role (issuing assertions to other applications): not planned, applications use OpenID Connect with this Server.
- Background directory sync and user federation (LDAP is read at login only).
- Upstream single logout for OpenID Connect providers; SP-initiated SAML single logout.
- Passkey attestation validation (the AAGUID is read as the authenticator reports it).
- Tests against the real services (Google, Microsoft, GitHub, Apple, Facebook); today the tests use fake providers.
- Source generator for permission constants.
- Helm chart.
- Webhook cache invalidation.
- Database translation overrides.

DocFX is not planned.

## Known limits

Each limit below is a design choice or is stated in the page named next to it.

- A role given to a user shows in a **new** token only. `/me` is always current and the `pv` claim tells a client when to ask again ([authorization.md](authorization.md), [client-current-user.md](client-current-user.md)).
- An API that validates the JWT locally accepts a revoked access token until it expires (15 minutes by default). The Server itself refuses it at once ([decisions.md](decisions.md)).
- In a cookie session the access token is not renewed. Prefer `AccessMode = OnSignIn` for long sessions ([client-setup.md](client-setup.md)).
- Blazor WebAssembly (browser side) is not covered: the Client package runs on the server side.
- Rate limiting uses fixed windows, so a burst at the window edge is possible ([security.md](security.md)).
- `Auth2:Security:UseForwardedHeaders` is off by default. Behind a proxy the operator must turn it on ([security.md](security.md), [deployment-and-scaling.md](deployment-and-scaling.md)).
- Serilog sinks other than console and OTLP are configured by the operator through the `Serilog` section; extra sink packages are not included.
- Two-factor has no "remember this device".
- A fresh authenticator code (step-up, `WithTotp()` / `[Auth2Totp]`) is asked by the **hosted UI** only. A Self UI application must send the person to its own page and check `step_up_at` itself, and there is one window for all of them: `TwoFactor.StepUpMinutes` ([two-factor.md](two-factor.md), [client-endpoint-metadata.md](client-endpoint-metadata.md)).
- Recovery codes are not "something you have" for step-up: a recovery code signs in or turns two-factor off, but it is not a fresh authenticator code for `VerifyTotp`.
- Self UI applications cannot use redirect-based external sign-in or passkeys ([external-identity.md](external-identity.md#limits)).

## Rule for open items

Every `TODO` in code must be listed on this page.

## Related pages

- [Changelog](../CHANGELOG.md)
- [Documentation index](index.md)
