# Server setup

## What this is

How to start the SystemX.Auth2 Server: configuration, database, seeding and commands.
Redis, keys and logging have their own pages: [deployment-and-scaling.md](deployment-and-scaling.md), [security.md](security.md), [observability.md](observability.md).

## When to use it

When you run the server for the first time, or when you deploy it.

## Requirements

- .NET 10 SDK
- PostgreSQL 15 or newer (the server uses `jsonb` and `NULLS NOT DISTINCT`)

## Step by step

1. Start PostgreSQL and create an empty database, for example `auth2`.
2. Set the connection string. Use user secrets or an environment variable, not a file in git:

   ```bash
   export ConnectionStrings__Auth2="Host=localhost;Database=auth2;Username=postgres;Password=postgres"
   ```

3. Create the tables and the first data:

   ```bash
   dotnet run --project src/SystemX.Auth2.Server -- migrate
   ```

4. Run the server:

   ```bash
   dotnet run --project src/SystemX.Auth2.Server
   ```

   In `Development` the server migrates and seeds by itself at startup.

## Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSystemXAuth2Server(builder.Configuration, o =>
{
    // optional code options; configuration section "Auth2" is also read
});

// builder.Services.ServerWithSingleProject();   // optional. Without it the server is multi-project.
```

## Configuration reference

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Auth2` | none (required) | PostgreSQL connection string |
| `Auth2:Mode` | `MultiProject` | Use `ServerWithSingleProject()` for single mode |
| `Auth2:CredentialScope` | `PerProject` | `PerProject` or `Shared`. Single mode forces `Shared` |
| `Auth2:AllowCredentialScopeOverride` | `false` | Let a project override the credential scope |
| `Auth2:Issuer` | none | Public URL of the server (needed in Production) |
| `Auth2:AutoSeed` | `false` | Seed at startup outside Development |
| `Auth2:AutoMigrate` | `false` | Migrate at startup in Production |
| `Auth2:SingleProject:Name` | `Default` | Name of the one project in single mode (its application is named `default`) |
| `Auth2:Development:LogMessageBodies` | `false` | Development only: log full emails and SMS (secrets!) |
| `Auth2:Keys:SigningCertificates` | none | List of `{ "Path": "sign.pfx", "Password": "..." }`. The first signs; all validate |
| `Auth2:Keys:EncryptionCertificates` | none | Same, for encrypting code and refresh tokens |

## Keys, issuer and the production guard

Tokens are signed and encrypted with keys. Two sources:

- **The key ring** (default). No setup: the first start makes a signing key and an encryption key, keeps them in the database (encrypted with Data Protection) and every instance shares them. Rotation without a restart: [key-ring.md](key-ring.md).
- **Certificate files** (`Auth2:Keys:SigningCertificates` and `EncryptionCertificates`, PFX). You make and rotate them. When at least one of each is set the key ring is not used.

Outside `Development` and `Testing` the server **refuses to start** unless `Auth2:Issuer` is an `https://` URL. Keys are not part of that check: the key ring is accepted in Production.

```json
{
  "Auth2": {
    "Issuer": "https://auth.example.com",
    "Keys": {
      "SigningCertificates":    [ { "Path": "/secrets/sign.pfx", "Password": "..." } ],
      "EncryptionCertificates": [ { "Path": "/secrets/enc.pfx",  "Password": "..." } ]
    }
  }
}
```

(The `Keys` part is only needed when you want your own certificates.) Keep the passwords in environment variables or a secret store (`Auth2__Keys__SigningCertificates__0__Password`). The session cookie is `Secure` outside Development and Testing.

## Single-project mode

Call `builder.Services.ServerWithSingleProject();` after `AddSystemXAuth2Server`.
It switches the server to single mode and registers `SingleProjectContext`. See [concepts.md](concepts.md).

The commands `convert-to-single` and `convert-to-multi` do not exist yet ([roadmap.md](roadmap.md)).
They copy credentials between the user row and `user_projects`. Going to single can lose data, because a person with different passwords in several projects can keep only one.

## What `AddSystemXAuth2Server` registers

- `Auth2DbContext` (PostgreSQL), Data Protection keys in the database.
- ASP.NET Core Identity core with our own `Auth2UserStore`, `Auth2RoleStore`, `Auth2UserManager`, `Auth2RoleManager`, `Auth2SignInManager`, validators and `Auth2IdentityErrorDescriber`.
- OpenIddict server and validation: `/connect/authorize`, `/connect/token`, `/connect/endsession`, `/connect/userinfo`, `/connect/revoke`, `/connect/introspect`, discovery and keys. See [authentication-flows.md](authentication-flows.md).
- `ILoginService`, `ITokenIssuer`, `ISessionService`, `IReturnUrlValidator`, and the hosted-UI cookie `.SystemX.Auth2.Session`.
- Razor Pages: the account pages (`Account/*`), the profile pages (`Manage/*`), the admin console (`Admin/*`), the SAML pages (`Saml/*`) and `Error`. See [ui-handover.md](ui-handover.md).
- `IProjectContext`, `IAuthFlowResolver`, credential state strategies, localization (`IAuth2Localizer`).

Add `app.UseAuth2Localization();` to the pipeline for request cultures. See [localization.md](localization.md).

## Sending email and SMS

The server sends confirmation links, reset links and one-time codes through two interfaces, `IEmailSender` and `ISmsSender`. By default they ask a **factory** that chooses the provider for the current project and application: the application's own setting, else the project's, else `Auth2:Messaging`, else `logging`. Providers that ship: `logging` (writes one log line with a masked recipient and sends nothing), `smtp` and `google` (Gmail) for email.

The development default (`logging`) never logs the body, a link, a token or a code. For local work you can see the full messages: set `Auth2:Development:LogMessageBodies=true`. It works **only in the `Development` environment**, and it writes links and codes to the log, so never use it elsewhere.

To send real email, configure a provider ([email-and-sms.md](email-and-sms.md)) or register your own after `AddSystemXAuth2Server`:

```csharp
builder.Services.AddSystemXAuth2Server(builder.Configuration);
builder.Services.AddScoped<IEmailTransport, MyTransport>();     // one more provider, selected by name
builder.Services.AddScoped<IEmailSender, MySmtpEmailSender>();  // or replace the whole mechanism
```

`EmailMessage.Data` / `SmsMessage.Data` hold the raw token or code (for tests and custom templates). Do not log them.

Message texts are in `Resources/Emails.resx` (see [localization.md](localization.md)). A message is written in the language the user saved, when the server has it. Links use `Auth2:Issuer` (or the address of the current request).

## Commands

The server executable has commands. Run them with `dotnet run --project src/SystemX.Auth2.Server -- <command>`.

| Command | What it does |
|---|---|
| `migrate` | Applies migrations, then seeds |
| `seed` | Seeds only (see [seeding.md](seeding.md)) |
| `create-project <projectKey> <name>` | Creates a project (no application yet) |
| `create-application <projectKey> <name> [clientId]` | Creates an application in that project (an OpenIddict application). The client id is `{projectkey}.{name}` unless given. Prints the client secret **once** |
| `rotate-client-secret <clientId>` | Makes a new client secret and prints it once. The old one stops working |
| `rotate-keys` | Makes a new signing and encryption key pair. The old keys keep checking old tokens until they are removed ([security.md](security.md)) |
| `create-admin <userName> <email>` | Creates a global admin. The password is read from the environment variable `AUTH2_ADMIN_PASSWORD`, never from the command line |
| `convert-to-single`, `convert-to-multi` | Not built yet ([roadmap.md](roadmap.md)) |

Exit codes: `0` success, `1` the command failed, `2` wrong usage.

## When the server migrates and seeds

| Environment | Migrate | Seed |
|---|---|---|
| Development | at startup | at startup |
| Other | only if `Auth2:AutoMigrate=true` | only if `Auth2:AutoSeed=true` |
| Any | by the `migrate` / `seed` commands | by the `migrate` / `seed` commands |

Never auto-migrate in Production unless you really want it. Prefer running `migrate` as a separate deployment step.
A PostgreSQL advisory lock makes sure that only one instance runs migration and seeding at the same time.

## Database naming

Tables and columns use `snake_case`. Identity tables have short names:
`users`, `roles`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`.
The table `applications` **is** the OpenIddict application table (with the extra columns `project_id`, `is_enabled`, `name`, `settings_json`, timestamps). The other OpenIddict tables are `openiddict_authorizations`, `openiddict_scopes`, `openiddict_tokens`. Every major OpenIddict upgrade needs an EF migration review.
Dates are `timestamptz`. Keys are UUID version 7.

Roles are unique by `(project_id, normalized_name)` with `NULLS NOT DISTINCT`. This means `writer` in two projects is fine, but two global roles with the same name are not.

## Create a migration (for contributors)

```bash
dotnet ef migrations add <Name> -p src/SystemX.Auth2.Server -s src/SystemX.Auth2.Server -c Auth2DbContext -o Persistence/Migrations
```

## Common problems

- *`Connection string 'Auth2' is missing`:* set `ConnectionStrings:Auth2`.
- *`The single project 'Default' does not exist`:* run the `seed` command.
- *Role or user lookups return nothing in your own code:* tenant tables need a selected project. Call `IProjectContext.GetRequiredAsync()` first, or use `IGlobalScope.Begin()` for developer work.

## Related pages

- [seeding.md](seeding.md)
- [server-settings.md](server-settings.md)
- [concepts.md](concepts.md)
- [Getting started](getting-started.md)
- [Client setup](client-setup.md)
- [Troubleshooting](troubleshooting.md)
- [Server reference](server-reference.md)
- [Client authorization](client-authorization.md)
- [Gateway](gateway.md)
- [Handshake and startup](handshake-and-startup.md)
- [Permission, role and group sync](permission-sync.md)
- [Server README](../src/SystemX.Auth2.Server/README.md)
- [Documentation index](index.md)
- [Redis: what it does, how to enable it, and the Client's cache](redis.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
