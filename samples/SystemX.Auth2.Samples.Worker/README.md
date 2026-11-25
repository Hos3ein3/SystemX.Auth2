# SystemX.Auth2 Worker sample: a background job that works as a program

This sample is a **background job** (a .NET worker, no web server, no browser, no user). It proves who it is to the Auth2 Server with a **client id and a secret** and gets a **machine token**: this is **machine to machine (M2M)**, called the *client credentials* grant in OAuth. It then uses the token to:

1. call **another API** (the `SystemX.Auth2.Samples.MinimalApi` sample), which accepts only the right tokens;
2. call the **Auth2 Server itself** (it reads the permission catalog).

Use this pattern for jobs, services and integrations: nightly imports, report builders, queue consumers, anything that runs **without a person signing in**.

> A machine token has **no user** in it. It says "this program", not "this person". The program may only do what the **scopes** of its application allow.

## What happens, step by step

```
 Worker                                   Auth2 Server                         MinimalApi sample
   | POST /connect/token                        |                                       |
   |   grant_type=client_credentials            |                                       |
   |   client_id + client_secret                |                                       |
   |   scope=reports.read jobs.run  ---------->|                                       |
   |                                            | checks the secret and the allowed scopes
   |<-------- access token (machine) -----------|                                       |
   |    aud = [worker-sample, reports-api, minimal-api-sample]                          |
   | GET /api/reports   Authorization: Bearer <token> ---------------------------------->|
   |                                            |            checks signature, aud, scope|
   |<------------------------------------------------------------------ 200 + the data  |
   | (the token is kept until it nearly expires; the next run reuses it)                 |
```

- The token's **scopes** say what the job may use (`reports.read` for the reports API, `jobs.run` for jobs).
- The token's **audiences** (`aud`) say which APIs it is for. The scope `jobs.run` has the **resource** `minimal-api-sample`, so the MinimalApi sample, which expects its own client id as the audience, accepts the token ([docs/scopes.md](../../docs/scopes.md)).
- The Client package makes the token, **caches** it until shortly before it expires, and renews it. A hundred parallel calls cause one token request.

## Run it

You need the Server and the MinimalApi sample running. The easiest way is docker-compose from the repository root:

```bash
docker compose up --build
```

It starts PostgreSQL, Redis, the Server (which creates the sample project and the application `worker-sample` for this job), the MVC and MinimalApi samples, **and this worker**. Watch the job:

```bash
docker compose logs -f sample-worker
```

Without docker:

```bash
# 1. the Server (creates the database and the sample data in Development)
dotnet run --project src/SystemX.Auth2.Server
# 2. the API the job calls
dotnet run --project samples/SystemX.Auth2.Samples.MinimalApi
# 3. the job
dotnet run --project samples/SystemX.Auth2.Samples.Worker
```

What you should see every 30 seconds:

```
Machine token: client worker-sample, scopes [reports.read jobs.run], audiences [worker-sample, reports-api, minimal-api-sample], expires 2026-11-26 11:30:00Z
GET api/machine-only -> 200 {"message":"Authenticated with a Machine (M2M) token."}
GET api/reports -> 200 {"report":"Financial Summary", ...}
The Auth2 server has 60 permissions in this project.   (3 without the demo data, 60 with it)
```

## Configure it

Everything is in `appsettings.json` (and can be overridden by environment variables: `__` replaces `:`, for example `Auth2__ClientSecret`).

| Setting | Meaning | Sample value |
|---|---|---|
| `Auth2:ServerUrl` | Where the Auth2 Server is | `http://localhost:5100` |
| `Auth2:ProjectKey` | The project this application belongs to | `sample-project` |
| `Auth2:ApplicationName` | The **name** of the application on the Server (it must match) | `WorkerSample` |
| `Auth2:ClientId` | The client id of the application | `worker-sample` |
| `Auth2:ClientSecret` | The client secret. **Keep it out of the source in a real job** | `worker-sample-secret` |
| `Auth2:OnServerUnavailable` | `Continue`: start even when the Server is down and keep trying in the background | `Continue` |
| `Samples:MinimalApiUrl` | The API the job calls | `http://localhost:5102/` |
| `Job:IntervalSeconds` | Seconds between two runs | `30` |

In a real job keep the secret in a secret store: `dotnet user-secrets set "Auth2:ClientSecret" "..."` while you develop, an environment variable or the secret of your platform (Kubernetes `Secret`, Azure Key Vault) in production. Never put it in git and never put it in a browser or a mobile app.

### The code (it is short)

`Program.cs`:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject("sample-project")
    .EnableMachineToMachine(o => o.Scopes = ["reports.read", "jobs.run"])   // used when a call names no scope
    .EnableResilience();                                                      // retry + timeout + circuit breaker for safe calls

// every request of this HttpClient carries "Authorization: Bearer <machine token>"
builder.Services.AddHttpClient("minimal-api", c => c.BaseAddress = new Uri("http://localhost:5102/"))
    .AddAuth2MachineToken("reports.read", "jobs.run");

builder.Services.AddHostedService<ReportJob>();
await builder.Build().RunAsync();
```

`ReportJob.cs` (a `BackgroundService`):

```csharp
var client = http.CreateClient("minimal-api");          // IHttpClientFactory
var response = await client.GetAsync("api/reports");   // the token is added, cached and renewed for you

var permissions = await auth2.GetPermissionsAsync();   // IAuth2ApiClientService: calls the Auth2 Server as a program
var token = await tokens.GetTokenAsync(["reports.read"]); // IAuth2MachineTokenProvider: the token itself, when you need it
```

Three ways to use a machine token, from the simplest:

| You want | Use |
|---|---|
| To call **another API** | `services.AddHttpClient("name").AddAuth2MachineToken("scope")`, then `IHttpClientFactory` (or `IAuth2MachineService.CreateClient("name")`) |
| To call the **Auth2 Server** (register people, sync permissions, read catalogs) | `IAuth2ApiClientService`: it asks for the scope each call needs |
| The **token itself** (a library that wants a string) | `IAuth2MachineTokenProvider.GetTokenAsync(["scope"])` |

## Register your own job

The sample's application, `worker-sample`, is created by the Server's seed (when the sample project is seeded). For **your** job make a new application:

1. In the admin console open your project → **Applications** → **Create**. Give it a name and a client id. A job needs **no redirect addresses**.
2. Copy the **client secret**: it is shown **once**.
3. In **Allowed Scopes** list only what the job needs: the `auth2.*` scopes of the Server calls it makes (for example `auth2.client.handshake auth2.permissions.sync`) and the **custom scopes** of the APIs it calls (`reports.read`). Do **not** add `auth2.admin` or `auth2.admin.global` to a job.
4. If the API the job calls checks the **audience**, create the scope with that API's client id as a **resource** (Scopes → Create → *Associated Resources*), or the API will refuse the token ([docs/scopes.md](../../docs/scopes.md#resources-of-a-scope)).
5. Put `ClientId`, `ClientSecret`, `ApplicationName` and `ProjectKey` in the job's settings.

The application name in the settings must match the one on the Server exactly, or the start-up handshake says `project_key_or_name_mismatch`.

## Make the API accept the job

The API checks the token with the Client package. Both rules below are in the MinimalApi sample:

```csharp
builder.Services.AddAuth2Client(...).UseApiAuthentication();                // accepts tokens whose audience is this API's client id

app.MapGet("/api/machine-only", ...).WithMachineOnly();                      // only programs (no person)
app.MapGet("/api/reports", ...).WithScope("reports.read");                   // only tokens that have this scope
```

A machine token has **no roles, permissions or groups**, so a rule such as `WithPermission(...)` always refuses it. Give a program access with **scopes**, or with `WithMachineOnly()`.

## Common problems

| Log message or result | Cause and fix |
|---|---|
| `invalid_client` (not transient) | The client id or secret is wrong, or the application is disabled. Check `Auth2:ClientId` and `Auth2:ClientSecret` |
| `invalid_scope` (not transient) | The application does not allow a scope the job asks for. Add it to the application's **Allowed Scopes**, and check that the scope exists on the Server |
| `server_unavailable` (transient) | The Server is down or too slow. The job logs it and tries again at the next run; with `EnableResilience()` safe calls are also retried |
| `GET api/... -> 401` | The API refused the token: the audience is not the API's. Make the scope list the API's client id as a **resource** (the `jobs.run` scope of this sample does that for `minimal-api-sample`) |
| `GET api/... -> 403` | The token is fine but lacks the scope the endpoint needs, or the endpoint wants a person (`WithUserOnly`) |
| The start-up handshake says `project_key_or_name_mismatch` | `ApplicationName` or `ProjectKey` does not match the application on the Server |
| The job starts but nothing is logged | `Job:IntervalSeconds` is large, or the log level hides `Information` |

## Files

| File | What it is |
|---|---|
| `Program.cs` | The configuration: Auth2 client, machine scopes, the `HttpClient` with the token, the job |
| `ReportJob.cs` | The `BackgroundService` that runs every `Job:IntervalSeconds` |
| `appsettings.json` | The settings above |
| `Dockerfile` | Builds the job for docker-compose (`sample-worker`) |

More: [docs/machine-to-machine.md](../../docs/machine-to-machine.md), [docs/scopes.md](../../docs/scopes.md), [docs/client-builder.md](../../docs/client-builder.md).
