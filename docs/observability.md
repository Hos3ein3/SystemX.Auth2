# Observability

## What this is

Logs, traces, metrics and health checks of the Server.

## When to use it

You run the Server and want to see what it does, and to be alerted when something is wrong.

## Logs (Serilog)

Logs are structured. Console output is on. Every event has:

| Property | Meaning |
|---|---|
| `ApplicationId`, `ProjectKey` | The application of the request (when it is known; `ProjectKey` only in multi mode) |
| `TraceId` | The OpenTelemetry trace id |
| `CorrelationId` | From the request header `X-Correlation-Id` (letters, digits, `. _ -`, up to 64), else the request id. It is sent back in the response header |

Request logging writes one line per request (method, path, status, time). **No bodies, no query string** (links can hold tokens). Health checks are not logged at Information level.

Configure levels and sinks in the `Serilog` section (`MinimumLevel`, `Override`, `WriteTo`). When `OTEL_EXPORTER_OTLP_ENDPOINT` is set, logs also go to OTLP.

**Secrets:** the code never logs passwords, tokens, codes, secrets or keys. As a safety net, a log property whose name contains `password`, `secret`, `token`, `authorization`, `otp`, `code`, `cookie`, `recoverycode`, `authenticatorkey` or `apikey` is replaced by `***`. Name your own properties with this in mind.

## Traces and metrics (OpenTelemetry)

Traces: ASP.NET Core, HttpClient, Entity Framework Core, Npgsql and (with Redis) StackExchange.Redis. SQL text is not recorded. Query strings are redacted by the instrumentation.

Metrics (meter `SystemX.Auth2`, plus ASP.NET Core and HttpClient metrics):

| Metric | Tags | Counts |
|---|---|---|
| `auth2.logins` | `result` = `success` / `failure` | Login attempts |
| `auth2.tokens.issued` | `grant_type` | Tokens issued |
| `auth2.lockouts` | | Accounts locked by too many failures |
| `auth2.sync.changes` | `kind` = `permissions` / `roles` | Items added or updated by sync |
| `auth2.ratelimit.rejections` | `policy` | Requests refused with 429 |
| `auth2.handshakes` | `result` = `ok` or an error code | Client handshakes |

Tags never hold user names, emails or other personal data.

Export is **off until** you set the standard variables, for example:

```
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
OTEL_SERVICE_NAME=systemx-auth2
```

## Health checks

`/health/live` and `/health/ready`. See [deployment-and-scaling.md](deployment-and-scaling.md).

The server has two health checks, both tagged `ready`: **`database`** (can the server open a connection to PostgreSQL) and **`redis`** (does Redis answer a ping; only when `Auth2:Redis:ConnectionString` is set).

**On the dashboard.** A **global admin** sees both on the dashboard of every project (`/admin/p/{project-key}`), as badges beside `Jobs` and `Status`:

| Badge | Meaning |
|---|---|
| `PostgreSQL: Healthy` (green) | The database answers. `Degraded` is yellow, `Unhealthy` red (the tooltip says how long the check took, and the reason) |
| `Redis: Healthy` | Redis answers |
| `Redis: Off` (grey) | Redis is not configured: the server keeps one-time codes and rate-limit counters in the database instead ([redis.md](redis.md)) |

Each badge has a tiny refresh arrow: the one of `PostgreSQL` and `Redis` reloads the page (the checks run when the page opens); the one of `Clients` asks the two-way clients of the project at once. The checks run when the page opens, with a limit of 3 seconds, so a dead service shows `Unhealthy` and cannot hang the page. Project admins do not see these two badges (they say something about the server, not about one project). The same two rows (`PostgreSQL Store`, `Redis Cache`) are in **Subsystem & Job Status** on the home page of the console (`/`). The applications that use the two-way health check have their own badge, `Clients` ([client-health-check.md](client-health-check.md)).

## Alerts to start with

- `auth2.ratelimit.rejections` rising: an attack, or a limit that is too low.
- `auth2.logins` with `result=failure` far above normal.
- `/health/ready` failing for more than a minute.
- Log events at Error level, especially "A key of the key ring could not be read".

## Common problems

| Problem | Fix |
|---|---|
| No metrics in my backend | Set `OTEL_EXPORTER_OTLP_ENDPOINT`. Check the collector accepts the protocol |
| A property shows `***` and I need it | Rename the property (avoid the words above) |
| Logs have no `ApplicationId` | The event happened before the application was known (for example a 404 route) |

## Related pages

- [security.md](security.md)
- [deployment-and-scaling.md](deployment-and-scaling.md)
- [Server setup](server-setup.md)
- [Server settings](server-settings.md)
- [Seeding (first admin)](seeding.md)
- [Email and SMS](email-and-sms.md)
- [Server reference](server-reference.md)
- [The key ring and its snapshot](key-ring.md)
- [Client authorization](client-authorization.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Gateway](gateway.md)
- [Testing](testing.md)
- [Architecture](architecture.md)
- [Getting started](getting-started.md)
- [Troubleshooting](troubleshooting.md)
- [Error codes](error-codes.md)
- [Troubleshooting external sign-in](external-identity/troubleshooting.md)
- [Documentation index](index.md)
- [Redis: what it does, how to enable it, and the Client's cache](redis.md)
- [Client setup](client-setup.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Health check: one-way and two-way](client-health-check.md)
- [Handshake and startup](handshake-and-startup.md)
- [Database tables](database-tables.md)
