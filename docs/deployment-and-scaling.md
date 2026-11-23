# Deployment and scaling

## What this is

How to run several instances of the Server, what they share, and what goes wrong when they do not.

## When to use it

You run more than one instance (for load or for rolling updates), or you deploy to Kubernetes.

## What instances must share

| Thing | Shared through | Notes |
|---|---|---|
| Database | PostgreSQL 15 | One database for all instances |
| Data Protection keys | `data_protection_keys` table | Fixed application name `SystemX.Auth2`. Cookies, anti-forgery and confirmation tokens work on every instance |
| Signing and encryption keys | key ring in the database (or the same certificate files) | A token from one instance is valid on the others |
| Rate-limit counters | **Redis** | Without Redis each instance counts alone |
| One-time codes | Redis when on, else the `otp_codes` table | Both are shared |
| Settings cache | memory per instance + Redis pub/sub | A change on one instance clears the others at once. Without Redis the cache expires in 10 minutes |
| Access cache | memory per instance | Correct on every instance, because the key contains the access version that is read from the database each time |
| Background jobs | database lock | One instance runs each job |

## External providers and several instances

Every instance keeps the enabled external providers in memory. A change made on one instance reaches the others through Redis (at once) or by polling the database every `Auth2:ExternalProviders:PollSeconds` (default 30). Without Redis a change can take up to that long to appear on other instances. SAML replay values are stored in the database table `replay_entries`, so all instances share them. See [external-identity.md](external-identity.md).

## Redis

Set `Auth2:Redis:ConnectionString` (for example `redis:6379,password=...`). Optional `Auth2:Redis:KeyPrefix` (default `auth2`) lets several servers share one Redis.
When Redis is on: rate-limit counters, one-time codes and settings invalidation use it, and `/health/ready` checks it. Data Protection keys and the key ring stay in the database on purpose: Redis can lose data, and without those keys cookies and tokens would stop working.
Redis down: the server still starts (`AbortOnConnectFail=false`), calls that need Redis fail, and `/health/ready` says not ready.

## Start-up order

1. Run **migrations as a separate step**: `dotnet SystemX.Auth2.Server.dll migrate` (a job or an init container). It also seeds and creates the first keys, under a database lock, so running it twice is safe.
2. Start the instances. In Production they do **not** migrate unless `Auth2:AutoMigrate=true`, and do not seed unless `Auth2:AutoSeed=true`.

## Health checks

| Route | Meaning |
|---|---|
| `GET /health/live` | The process runs. Use it as the liveness probe |
| `GET /health/ready` | The database (and Redis when on) answer. Use it as the readiness probe |

Both are anonymous and never rate limited. The Server stops gracefully: in-flight requests finish when it gets SIGTERM.

## Problem → fix

| Problem | Cause | Fix |
|---|---|---|
| Login works on one instance, "invalid token" on another | Different keys | Use the key ring (default) or the same certificate files on all instances. Check that every instance uses the same database |
| Users are logged out after every deploy | Data Protection keys were lost or per-instance | Keep the `data_protection_keys` table. Do not change the application name |
| Key ring cannot be read (log: "A key of the key ring could not be read") | Data Protection keys differ from those that protected the key ring | Restore the database table, or rotate keys on a clean instance |
| `429` from the first request | All clients share the proxy address | Turn on forwarded headers and list the proxy |
| Rate limits seem 3× too weak | Three instances, counters in memory | Turn on Redis |
| Settings change appears after 10 minutes on some instances | No Redis pub/sub | Turn on Redis, or accept the delay |
| A new key is not used by an instance | It checks every 30 s | Wait `Auth2:Keys:RefreshSeconds`, or restart it |
| Instances fight over migrations | Auto-migrate on in several instances | It is safe (database lock), but prefer the separate `migrate` step |
| Cookies not set behind the proxy | The server thinks the request is HTTP | `UseForwardedHeaders=true`, proxy sends `X-Forwarded-Proto` |
| Readiness fails after start | Database not reachable or not migrated | Check the connection string and run `migrate` |

## Kubernetes (example, text only)

```yaml
# Job: run once per release
command: ["dotnet", "SystemX.Auth2.Server.dll", "migrate"]
---
# Deployment: 3 replicas
env:
  - { name: ConnectionStrings__Auth2, valueFrom: { secretKeyRef: { name: auth2, key: db } } }
  - { name: Auth2__Issuer, value: "https://auth.example.com" }
  - { name: Auth2__Redis__ConnectionString, valueFrom: { secretKeyRef: { name: auth2, key: redis } } }
  - { name: Auth2__Security__UseForwardedHeaders, value: "true" }
  - { name: Auth2__Security__KnownProxies__0, value: "10.0.0.5" }
  - { name: OTEL_EXPORTER_OTLP_ENDPOINT, value: "http://otel-collector:4317" }
livenessProbe:  { httpGet: { path: /health/live,  port: 8080 } }
readinessProbe: { httpGet: { path: /health/ready, port: 8080 } }
```

Rotate keys with a `CronJob` that runs `rotate-keys`, or by hand.

## Related pages

- [security.md](security.md)
- [observability.md](observability.md)
- [server-setup.md](server-setup.md)
