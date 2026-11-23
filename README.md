# SystemX.Auth2

SystemX.Auth2 is an open-source, self-hosted **authentication and authorization server** and a **client NuGet package** for .NET 10.
The server uses ASP.NET Core Identity, OpenIddict and PostgreSQL. One server can serve many applications.

> **Not a replacement for Keycloak or Duende.**
> It is a simple tool for small and medium .NET systems.
> It is not a certified OpenID Connect provider. It does not support SAML or LDAP in v1.

## Why this project

Many small .NET teams need login, roles and permissions, but do not want to run a large identity product.
SystemX.Auth2 gives them one server and one client package with very little configuration.

## Status

Early development. See [docs/feature-status.md](docs/feature-status.md) (written in the last phase) and [CHANGELOG.md](CHANGELOG.md).

| Part | Status |
|---|---|
| Contracts | Done (phase 2) |
| Server | Data model, settings, seeding and commands done (phase 3). Login and tokens come next |
| Client | Not started |
| Gateway | Empty project, not implemented in v1 |

## Quick start

```bash
dotnet build
dotnet test
```

## Documentation

Start at [docs/index.md](docs/index.md).

## Licence

MIT. See [LICENSE](LICENSE).
