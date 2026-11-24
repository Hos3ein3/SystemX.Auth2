# Comparison and limits

## What this is

An honest list of what SystemX.Auth2 does and does not do. It is **not a replacement for Keycloak or Duende IdentityServer**. It is one server and one client package for small and medium .NET systems.

## When to use it

Use it when you want login, roles, permissions and groups for several .NET applications, with PostgreSQL, and little configuration. Use a larger product when you need the things in "Not supported".

## What is supported

| Area | Supported |
|---|---|
| Standards | OpenID Connect and OAuth 2.0: code flow with PKCE, client credentials, refresh tokens, optional password grant, userinfo, revocation, end session. Not certified |
| Accounts | User name, email or phone with password; one-time codes (email, SMS); authenticator-app two-factor with recovery codes; lockout; password reset; consent |
| Applications | Many applications on one server, or one application; per-application settings, roles, permissions, groups; shared or per-application credentials |
| External sign-in | Google, Microsoft, GitHub, Apple, Facebook, generic OpenID Connect; **SAML 2.0 as a service provider**; **LDAP and Active Directory** (password check at login); **passkeys**; brokering with claim and group mapping; home-realm discovery by email domain |
| Admin | JSON admin API for everything, and admin console pages under `/admin` |
| Client package | API (JWT) and cookie authentication, hosted UI, Self UI, Blazor helpers, attributes and Minimal API extensions, current user, machine tokens, scanning and sync |
| Operations | Rate limiting, Redis (optional), several instances, key rotation, Serilog, OpenTelemetry, health checks |

## Not supported (v1)

| Not supported | Why and what to do |
|---|---|
| **SAML identity provider role** (issuing SAML assertions to other applications) | Applications use OpenID Connect with this Server. The Server *consumes* SAML from company identity providers, it does not *issue* it |
| User federation / background directory sync | LDAP data is read at login only. There is no import job and no write-back to the directory |
| Certified OpenID Connect, FAPI, device flow, token exchange, dynamic client registration | Not planned for v1 |
| Provider and passkey buttons on the Server's sign-in page, the join page | Not drawn yet; your own page can call the routes ([ui-handover.md](ui-handover.md)) |
| Single logout for OpenID Connect providers | Logout is local. SAML single logout (started by the identity provider) is optional |
| Attestation validation for passkeys | The authenticator model (AAGUID) is read as reported; a block list and an allow list exist |
| Gateway | Empty project ([gateway.md](gateway.md)) |

## Experimental

Nothing in the first release is marked experimental in code. The external sign-in features have their own test suites (fake OpenID Connect provider, fake SAML identity provider, OpenLDAP container, software passkey authenticator) but were not tested against every real provider; see the setup guides and [external-identity/troubleshooting.md](external-identity/troubleshooting.md).

## Related pages

- [feature-status.md](feature-status.md)
- [roadmap.md](roadmap.md)
- [security.md](security.md)
