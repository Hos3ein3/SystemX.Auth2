# SystemX.Auth2.Server

The server of **SystemX.Auth2**: an ASP.NET Core application (Razor Pages + Minimal API) built on ASP.NET Core Identity, OpenIddict and PostgreSQL. One server serves many **projects** (tenants) and, inside each project, many **applications** (clients). It issues OpenID Connect / OAuth 2.0 tokens, hosts the sign-in pages and the admin console, and keeps the roles, permissions, scopes and groups that your applications publish with the [client package](../SystemX.Auth2.Client/README.md).

It is **not** a replacement for Keycloak or Duende and it is not a certified OpenID Connect provider ([what is and is not supported](../../docs/comparison-and-limits.md)).

## What it does

| Area | Details |
|---|---|
| Protocols | Authorization code with PKCE, client credentials, refresh tokens (rotation with reuse detection), optional password and two-factor grants, userinfo, introspection, revocation, end session |
| Accounts | Register, join a second project with proof of ownership, email and phone confirmation, one-time codes, password reset, authenticator-app two-factor with recovery codes, lockout, consent, sessions |
| Access model | Permissions, roles and groups per project, the access version `pv`, `/me`, push and pull sync from your applications |
| Registration defaults | Roles, groups and permissions every new member gets (`Registration.AssignedRoles`, `AssignedGroups`, `AssignedPermissions`) |
| External sign-in | Google, Microsoft, GitHub, Apple, Facebook and any OpenID Connect provider (OpenIddict.Client + WebIntegration), **SAML 2.0** (as a service provider), **LDAP / Active Directory**, **passkeys**, brokering with claim and group mapping |
| Email and SMS | A provider per project and per application: SMTP, Google (Gmail), or your own |
| Admin | A JSON API for everything, and an admin console at `/admin` (projects, applications, users, roles, permissions, groups, scopes, providers, sessions, audit, settings, branding, email templates, keys, jobs) |
| Operations | Rate limiting, optional Redis, several instances, key ring and rotation, Serilog, OpenTelemetry, health checks (`/health/live`, `/health/ready`), background jobs |

## Requirements

.NET 10 SDK, PostgreSQL 15 or newer, Node.js (the build compiles the Tailwind CSS), and optionally Redis.

## Run it

```bash
docker compose up -d postgres redis                 # from the repository root
dotnet run --project src/SystemX.Auth2.Server       # http://localhost:5100
```

In `Development` the server migrates the database and seeds it by itself (`appsettings.Development.json`: `AutoMigrate`, `AutoSeed`). The seed creates the `_global` project, the admin user `globaladmin` (password `Admin@12345!ChangeMe`: **change it**, [seeding](../../docs/seeding.md)), and the project `sample-project` with the applications `mvc-sample` and `minimal-api-sample`. Sign in at `http://localhost:5100/admin`.

`docker compose up --build` starts the Server, PostgreSQL, Redis, pgAdmin and both samples. Step by step with the samples: [getting started](../../docs/getting-started.md).

## Configuration

Everything is read from the `Auth2` section (and `ConnectionStrings:Auth2`). `__` replaces `:` in environment variables.

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Auth2` | required | PostgreSQL connection string. Keep it in a secret store |
| `Auth2:Mode` | `MultiProject` | `MultiProject`, or call `ServerWithSingleProject()` for one project |
| `Auth2:CredentialScope` | `PerProject` | Where passwords and two-factor live: [credential-scope](../../docs/credential-scope.md) |
| `Auth2:Issuer` | none | The public `https` address of the server. **Required in Production** |
| `Auth2:Keys:SigningCertificates`, `EncryptionCertificates` | none | PFX files. **Required in Production**; Development makes temporary keys |
| `Auth2:AutoMigrate`, `Auth2:AutoSeed` | `false` | Migrate / seed at start outside Development (better: run the `migrate` command as a separate step) |
| `Auth2:Redis:ConnectionString` | none | Shared rate-limit counters, one-time codes and cache invalidation |
| `Auth2:RateLimiting:*` | on | Named policies with a limit and a window |
| `Auth2:Security:UseForwardedHeaders` | `false` | **Turn on behind a proxy** |
| `Auth2:Messaging:Email` | `logging` | Server default email provider (`smtp`, `google`, ...). Projects and applications can choose their own: [email and SMS](../../docs/email-and-sms.md) |
| `Auth2:Passkeys:ServerDomain` | none | Required for passkeys in Production |
| `Auth2:ExternalProviders:PollSeconds` | `30` | How often instances look for changed provider settings |
| `Auth2:Jobs:*` | on | Background jobs (audit retention, ...) |

The full list with every default: [server setup](../../docs/server-setup.md). Per-project and per-application settings (password rules, lockout, two-factor, registration, tokens, ...) are stored in the database and edited in the admin console or with the admin API: [server settings](../../docs/server-settings.md).

## Commands

```bash
dotnet run --project src/SystemX.Auth2.Server -- <command>
```

| Command | What it does |
|---|---|
| `migrate` | Applies the migrations, then seeds |
| `seed` | Seeds only |
| `create-project <projectKey> <name>` | Creates a project |
| `create-application <projectKey> <name> [clientId]` | Creates an application and prints its secret **once** |
| `rotate-client-secret <clientId>` | New client secret, printed once |
| `rotate-keys` | New signing and encryption keys (the old ones keep checking old tokens until they are removed) |
| `create-admin <userName> <email>` | Creates a global admin. The password is read from the `AUTH2_ADMIN_PASSWORD` environment variable, never from the command line |

Exit codes: `0` success, `1` failed, `2` wrong usage.

## Folders

```
Domain/          entities and settings classes (no EF, no ASP.NET)
Application/     the interfaces and rules your code and the pages use (IEmailSender, IRegistrationDefaults, EffectiveSettings, ...)
Persistence/     DbContext, migrations, stores, seeding
Infrastructure/  OpenIddict setup, login and token services, external identity (OIDC, SAML, LDAP, passkeys), messaging, Redis, rate limiting, commands
Endpoints/       the JSON routes: /connect/*, /api/auth2/*, /api/auth2/admin/*
Pages/           Razor Pages: Account/*, Manage/*, Admin/*, Saml/*
Resources/       texts (resx) per language
Styles/, wwwroot/ Tailwind CSS source and the built assets
```

Layers and the rule between them: [architecture](../../docs/architecture.md).

## Extend it

| You want to | Do this |
|---|---|
| Send email another way | Register your own `IEmailSender` (replaces all), or add an `IEmailTransport` with a name (an admin then selects it) |
| Add an SMS provider | Add an `ISmsTransport` |
| Give new members rights | Set `Registration.AssignedRoles`, `AssignedGroups`, `AssignedPermissions` |
| Add a login provider type | [How to add a new OIDC provider type](../../docs/external-identity.md#how-to-add-a-new-oidc-provider-type-worked-example) |
| Read or change settings in code | `ProjectSettings`, `ClientApplicationSettings`, `EffectiveSettings` ([server reference](../../docs/server-reference.md)) |

## Security notes

Tokens are signed JWTs; the pages use a strict content security policy with a nonce; secrets (client secrets, provider secrets, SMTP password, SAML keys) are encrypted with Data Protection and never returned or logged; every sign-in path goes through one gate for disabled projects and applications. See [security](../../docs/security.md) for the threat model, rate limits and the production checklist.

## Tests

```bash
dotnet test tests/SystemX.Auth2.Server.Tests               # fast unit tests
dotnet test tests/SystemX.Auth2.Server.IntegrationTests    # PostgreSQL (and OpenLDAP for the LDAP tests) in containers, so Docker is needed; fake SMTP, OpenID Connect and SAML servers run in process
```

## Documentation

[Documentation index](../../docs/index.md) · [Concepts](../../docs/concepts.md) · [Authentication flows](../../docs/authentication-flows.md) · [Admin API](../../docs/admin-api.md) · [External identity](../../docs/external-identity.md) · [Deployment and scaling](../../docs/deployment-and-scaling.md) · [Troubleshooting](../../docs/troubleshooting.md)

## License

MIT.
