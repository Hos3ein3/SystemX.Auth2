# Getting started

## What this is

The fastest way to see SystemX.Auth2 work: run the Server, sign in as the seeded admin, and run the two sample applications that ship in this repository.

| Part | Folder | Address (default) |
|---|---|---|
| Server | `src/SystemX.Auth2.Server` | `http://localhost:5100` |
| MVC sample (hosted sign-in, cookie, roles and permissions) | `samples/SystemX.Auth2.Samples.Mvc` | `http://localhost:5101` |
| Minimal API sample (JWT bearer, permission and scope endpoints) | `samples/SystemX.Auth2.Samples.MinimalApi` | `http://localhost:5102` |
| PostgreSQL, Redis, pgAdmin | `docker-compose.yml` | `5432`, `6379`, `5050` |

## When to use it

Your first hour. After that, read [concepts.md](concepts.md), then [server-setup.md](server-setup.md) and [client-setup.md](client-setup.md).

## Requirements

.NET 10 SDK, Docker (for PostgreSQL), Node.js (the Server builds its CSS with Tailwind when it builds).

## Steps

1. **Start the database (and Redis).**

   ```bash
   docker compose up -d postgres redis
   ```

   The compose file creates the database `auth2` (user and password `postgres`). The Server's `appsettings.Development.json` sets `AutoMigrate` and `AutoSeed`, so the tables and the sample data are created at the first start. `docker compose up --build` starts everything, samples included.

2. **Run the Server.**

   ```bash
   dotnet run --project src/SystemX.Auth2.Server
   ```

   It listens on `http://localhost:5100`. The seeder created: the `_global` project, the user `globaladmin` (password `Admin@12345!ChangeMe`, **change it**: [seeding.md](seeding.md)), and the project `sample-project` with the applications `mvc-sample` and `minimal-api-sample`.

3. **Run a sample** (a second terminal). Both read `Auth2:ServerUrl`, `ClientId`, `ClientSecret` and `ProjectKey` from their `appsettings.json`, which already match the seeded data.

   ```bash
   dotnet run --project samples/SystemX.Auth2.Samples.Mvc          # http://localhost:5101
   dotnet run --project samples/SystemX.Auth2.Samples.MinimalApi   # http://localhost:5102
   ```

4. **Look at what the samples did at start.** Each one checked in with the Server (the handshake, [handshake-and-startup.md](handshake-and-startup.md)) and pushed its permissions, roles, scopes and groups ([permission-sync.md](permission-sync.md)). Open `http://localhost:5100/admin/p/sample-project/roles` and `.../permissions` after signing in as `globaladmin`: you see the entries from the samples' `Program.cs`.

5. **Sign in to the MVC sample.** Click *Sign In* on `http://localhost:5101`. The sample uses the **hosted UI** (`.UseHostedUi()`): the browser goes to the Server's sign-in page and comes back with tokens. Create a user first with *Create an account* on that page. `[Auth2Role("Admin")]` and `[Auth2Permission("orders.read")]` on the actions of `HomeController` decide what the user may open; assign roles and permissions in the admin pages (`/admin/p/sample-project/users`).

6. **Call the Minimal API sample with a token.** The sample has a login helper: `POST http://localhost:5102/api/auth/login` with `{ "login": "sara", "password": "..." }` returns the tokens (this needs `AllowSelfUiLogin` on the application, which the seeder turns on). Then call an endpoint with `Authorization: Bearer <access_token>`: `GET /api/me`, `GET /api/orders` (needs the permission `orders.read`), `GET /api/admin` (needs the role `Admin`), `GET /api/reports` (needs the scope `reports.read`), `GET /api/finance` (needs the group `finance`). Machine-to-machine: [machine-to-machine.md](machine-to-machine.md).

## What the sample code shows

| File | Shows |
|---|---|
| `samples/SystemX.Auth2.Samples.Mvc/Program.cs` | The whole builder: `AddAuth2Client`, `ServerWithMultiProject`, `UseHostedUi`, `UseCookieAuthentication`, `UseApiAuthentication`, `EnableCurrentUser`, `EnablePermissions`, `EnableRoles`, `EnableScopes`, `EnableGroups`, `EnableResilience`, `EnableHealthCheck`, `UseAuth2` |
| `samples/SystemX.Auth2.Samples.Mvc/Controllers/HomeController.cs` | `[Auth2Role]`, `[Auth2Permission]`, `ICurrentUser` |
| `samples/SystemX.Auth2.Samples.MinimalApi/Program.cs` | The same builder for an API, and the Minimal API extensions: `WithAnonymous`, `WithAuth2`, `WithRole`, `WithAllRoles`, `WithPermission`, `WithScope`, `WithGroup`, `WithMachineOnly`, `WithUserOnly`, `WithOnlyRolePermission`, `WithOnlyUserPermission`, `WithAuth2Audit` |
| [client-setup.md](client-setup.md) | Every builder method with its overloads |

To use your own sign-in form instead of the hosted pages, replace `.UseHostedUi()` with `.UseSelfUi()` and set `LoginPath` ([client-setup.md](client-setup.md#self-ui)).

## Common problems

See [troubleshooting.md](troubleshooting.md). The usual first ones: PostgreSQL is not running (the Server stops at start), the port `5100` is taken, or the sample started before the Server (it logs the handshake failure and, with `OnServerUnavailable: Continue`, keeps running).

## Related pages

- [server-setup.md](server-setup.md)
- [client-setup.md](client-setup.md)
- [concepts.md](concepts.md)
- [Client package reference](client-reference.md)
- [Client authorization](client-authorization.md)
- [Current user and current client](client-current-user.md)
- [Contracts reference](contracts-reference.md)
- [Testing](testing.md)
- [Architecture](architecture.md)
- [Observability](observability.md)
- [Error codes](error-codes.md)
- [Troubleshooting external sign-in](external-identity/troubleshooting.md)
- [Repository README](../README.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Server settings](server-settings.md)
- [The MVC sample: one action for every kind of rule](sample-mvc.md)
- [Minimal API methods and attributes: when to use which](client-endpoint-metadata.md)
- [Scope, permission, role, group: which is which](access-concepts.md)
- [Scopes and their resources](scopes.md)
