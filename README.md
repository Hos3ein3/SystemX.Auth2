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
| Contracts | Done |
| Server | Data model, identity, login and tokens, accounts (register, join, OTP, reset, 2FA, consent), access model (permissions, roles, groups, `/me`), sync and handshake, admin API, security and operations. External identity: social login, generic OIDC, brokering, SAML 2.0 service provider, LDAP / Active Directory, passkeys, admin API for providers |
| Client | Core, authorization, API / cookie / hosted UI / Self UI / Blazor, scanners and sync, external sign-in info |
| Gateway | Empty project, not implemented in v1 |

## Quick start

```bash
dotnet build
dotnet test
```

## Features

- OpenID Connect / OAuth 2.0 server on ASP.NET Core Identity and OpenIddict, PostgreSQL, many applications.
- Permissions, roles and groups per project, synchronised from your code. A project has many applications (clients); see [docs/concepts.md](docs/concepts.md).
- Sign in with Google, Microsoft, GitHub, Apple, Facebook, any OpenID Connect provider, SAML 2.0, LDAP / Active Directory and passkeys. See [docs/external-identity.md](docs/external-identity.md).
- Admin JSON API, rate limiting, Redis, key rotation, observability.
- Client package for APIs, cookie apps, Blazor and Self UI.

## Documentation

Start at [docs/index.md](docs/index.md) (the map of every page).

| I want to | Read |
|---|---|
| Try it in an hour | [docs/getting-started.md](docs/getting-started.md) |
| Understand the words (project, application, membership) | [docs/concepts.md](docs/concepts.md) |
| Run and configure the Server | [Server README](src/SystemX.Auth2.Server/README.md), [docs/server-setup.md](docs/server-setup.md), [docs/server-settings.md](docs/server-settings.md) |
| Add the Client package to my app | [Client README](src/SystemX.Auth2.Client/README.md), [docs/client-setup.md](docs/client-setup.md), [docs/client-reference.md](docs/client-reference.md) |
| Sign in with Google, SAML, LDAP or a passkey | [docs/external-identity.md](docs/external-identity.md) |
| Know what each database table is | [docs/database-tables.md](docs/database-tables.md), [docs/key-ring.md](docs/key-ring.md) |
| See what works and what does not | [docs/feature-status.md](docs/feature-status.md), [docs/roadmap.md](docs/roadmap.md), [docs/comparison-and-limits.md](docs/comparison-and-limits.md) |

## Licence

MIT. See [LICENSE](LICENSE).
