# Architecture

## What this is

This page shows how the solution is built.

## When to use it

Read it before you change code. It tells you where each piece belongs.

## Solution layout

```
src/SystemX.Auth2.Contracts   DTOs, enums, constants (no ASP.NET, no EF)
src/SystemX.Auth2.Server      ASP.NET Core app (Razor Pages + Minimal API)
src/SystemX.Auth2.Client      NuGet package for your apps
src/SystemX.Auth2.Gateway     Empty project (planned)
samples/                      Two samples: Mvc (hosted sign-in) and MinimalApi (JWT bearer)
tests/                        Unit and integration tests
docs/                         These pages
```

## Layers inside the Server

The Server uses folders, not class libraries.

```
Domain  <-  Application  <-  (Persistence, Infrastructure)  <-  (Endpoints, Pages)
```

- **Domain**: entities and rules. No EF, no ASP.NET.
- **Application**: services and interfaces.
- **Persistence**: `DbContext`, migrations, stores, seeding.
- **Infrastructure**: Redis, email, SMS, rate limiting, logging, OpenIddict setup.
- **Endpoints / Pages**: the HTTP surface.

An architecture test fails when `Domain` references `Microsoft.AspNetCore` or `EntityFrameworkCore`.

## Contracts

`Contracts` is shared by the Server and the Client. It has no dependency on ASP.NET, EF or OpenIddict.
The Client never references the Server.

## Build files

- `Directory.Build.props`: net10.0, nullable, warnings as errors.
- `Directory.Packages.props`: all package versions in one place (central package management).

## Common problems

- *Build fails with a warning:* warnings are errors. Fix the warning, for example add the missing XML comment.

## Related pages

- [contributing-workflow.md](contributing-workflow.md)
- [decisions.md](decisions.md)
- [Concepts](concepts.md)
- [Database tables](database-tables.md)
- [Credential scope](credential-scope.md)
- [Comparison and limits](comparison-and-limits.md)
- [The key ring and its snapshot](key-ring.md)
- [Security](security.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Server settings](server-settings.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Deployment and scaling](deployment-and-scaling.md)
- [Email and SMS](email-and-sms.md)
- [Testing](testing.md)
- [Getting started](getting-started.md)
- [Observability](observability.md)
- [Documentation index](index.md)
- [Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)](identity-stores.md)
- [Authentication flows](authentication-flows.md)
- [Current user and current client](client-current-user.md)
