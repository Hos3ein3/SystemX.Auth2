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
