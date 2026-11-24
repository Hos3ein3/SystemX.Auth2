# SystemX.Auth2

SystemX.Auth2 is an open-source, self-hosted **authentication and authorization server** and a **client NuGet package** for .NET 10.
The server uses ASP.NET Core Identity, OpenIddict and PostgreSQL. One server can serve many applications.

> **Not a replacement for Keycloak or Duende.**
> It is a simple tool for small and medium .NET systems.
> It is not a certified OpenID Connect provider. It can **use** SAML, LDAP / Active Directory, social providers and passkeys to sign users in, but it is not a SAML identity provider ([limits](docs/comparison-and-limits.md)).

## Why this project

Many small .NET teams need login, roles and permissions, but do not want to run a large identity product.
SystemX.Auth2 gives them one server and one client package with very little configuration.

## Status

Early development. See [docs/feature-status.md](docs/feature-status.md) and [CHANGELOG.md](CHANGELOG.md).

| Part | Status |
|---|---|
| Contracts | Done (phase 2) |
| Server | Data model, identity, login and tokens, accounts (register, join, OTP, reset, 2FA, consent), access model (permissions, roles, groups, `/me`), sync and handshake, admin API, security and operations done (phases 3–10). External identity: social login, generic OIDC, brokering, SAML 2.0 service provider, LDAP / Active Directory, passkeys, admin API for providers |
| Client | Core, authorization, API / cookie / hosted UI / Self UI / Blazor, scanners and sync, external sign-in info (phases 11–13 and external identity) |
| Gateway | Empty project, not implemented in v1 |

## Quick start

```bash
dotnet build
dotnet test
```

## Features

- OpenID Connect / OAuth 2.0 server on ASP.NET Core Identity and OpenIddict, PostgreSQL, many applications.
- Permissions, roles and groups per application, synchronised from your code.
- Sign in with Google, Microsoft, GitHub, Apple, Facebook, any OpenID Connect provider, SAML 2.0, LDAP / Active Directory and passkeys. See [docs/external-identity.md](docs/external-identity.md).
- Admin JSON API, rate limiting, Redis, key rotation, observability.
- Client package for APIs, cookie apps, Blazor and Self UI.

## Documentation

Start at [docs/index.md](docs/index.md).

## Licence

MIT. See [LICENSE](LICENSE).
