# Server setup

## What this is

How to start the SystemX.Auth2 Server: configuration, database, seeding and commands.
This page covers phase 3. Later phases add more options (Redis, keys, logging).

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

// builder.Services.ServerWithSingleApplication();   // optional. Without it the server is multi-application.
```

## Configuration reference

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Auth2` | none (required) | PostgreSQL connection string |
| `Auth2:Mode` | `MultiApplication` | Use `ServerWithSingleApplication()` for single mode |
| `Auth2:CredentialScope` | `PerApplication` | `PerApplication` or `Shared`. Single mode forces `Shared` |
| `Auth2:AllowCredentialScopeOverride` | `false` | Let an application override the credential scope |
| `Auth2:Issuer` | none | Public URL of the server (needed in Production) |
| `Auth2:AutoSeed` | `false` | Seed at startup outside Development |
| `Auth2:AutoMigrate` | `false` | Migrate at startup in Production |
| `Auth2:SingleApplication:Name` | `Default` | Name of the one application in single mode |

## Single-application mode

Call `builder.Services.ServerWithSingleApplication();` after `AddSystemXAuth2Server`.
It switches the server to single mode and registers `SingleApplicationContext`. See [concepts.md](concepts.md).

`convert-to-single` and `convert-to-multi` commands arrive in a later phase.
They copy credentials between the user row and `user_applications`. Going to single can lose data, because a person with different passwords in several applications can keep only one.

## What `AddSystemXAuth2Server` registers (so far)

- `Auth2DbContext` (PostgreSQL), Data Protection keys in the database.
- ASP.NET Core Identity core with our own `Auth2UserStore`, `Auth2RoleStore`, `Auth2UserManager`, `Auth2RoleManager`, `Auth2SignInManager`, validators and `Auth2IdentityErrorDescriber`.
- OpenIddict core (stores only; endpoints come in phase 5).
- `IApplicationContext`, `IAuthFlowResolver`, credential state strategies, localization (`IAuth2Localizer`).

Add `app.UseAuth2Localization();` to the pipeline for request cultures. See [localization.md](localization.md).

## Commands

The server executable has commands. Run them with `dotnet run --project src/SystemX.Auth2.Server -- <command>`.

| Command | What it does |
|---|---|
| `migrate` | Applies migrations, then seeds |
| `seed` | Seeds only (see [seeding.md](seeding.md)) |
| `create-application <projectKey> <name>` | Creates an application and its OpenIddict client |
| `create-admin`, `rotate-keys`, `convert-to-single`, `convert-to-multi` | Planned (later phases) |

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
OpenIddict tables are `openiddict_applications`, `openiddict_authorizations`, `openiddict_scopes`, `openiddict_tokens`.
Dates are `timestamptz`. Keys are UUID version 7.

Roles are unique by `(application_id, normalized_name)` with `NULLS NOT DISTINCT`. This means `writer` in two applications is fine, but two global roles with the same name are not.

## Create a migration (for contributors)

```bash
dotnet ef migrations add <Name> -p src/SystemX.Auth2.Server -s src/SystemX.Auth2.Server -c Auth2DbContext -o Persistence/Migrations
```

## Common problems

- *`Connection string 'Auth2' is missing`:* set `ConnectionStrings:Auth2`.
- *`The single application 'Default' does not exist`:* run the `seed` command.
- *Role or user lookups return nothing in your own code:* tenant tables need a selected application. Call `IApplicationContext.GetRequiredAsync()` first, or use `IGlobalScope.Begin()` for developer work.

## Related pages

- [seeding.md](seeding.md)
- [server-settings.md](server-settings.md)
- [concepts.md](concepts.md)
