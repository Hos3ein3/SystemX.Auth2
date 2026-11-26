# Redis: what it does, how to enable it, and the Client's cache

## What this is

Redis is **optional**. Without it everything works on one Server instance. With it, several Server instances share a few small things that must be shared: **rate-limit counters**, **one-time codes**, and the **"settings changed" signal**. Your applications (the Client package) can also keep their per-user access data in Redis instead of memory.

| Side | What Redis holds | Without Redis |
|---|---|---|
| Server | Rate-limit counters (`{prefix}:rl:...`) | Each instance counts alone (`MemoryRateLimiter`) |
| Server | One-time codes of email and SMS (`{prefix}:otp:...`) | They are rows of the table `otp_codes` |
| Server | The **settings of every project**: a version number and the value, shared by all instances ([the hybrid cache](#the-hybrid-cache-settings-without-asking-the-database)) | Each instance keeps them in its own memory for 1 day |
| Server | The channel `{prefix}:settings-changed` (publish / subscribe) | Each instance learns from the database: a cache that expires, and a poll every `Auth2:ExternalProviders:PollSeconds` for providers |
| Client | The access data of signed-in users (roles, permissions, groups) | In the memory of each application instance |

What Redis does **not** hold, on purpose ([ADR-068](decisions.md)): the Data Protection keys and the key ring. Redis may lose data, and without those keys nobody could sign in. They stay in PostgreSQL ([key-ring.md](key-ring.md)). No plain password, token or secret is stored in Redis; one-time codes are stored **hashed**, and the settings cache holds provider secrets only **encrypted** ([the hybrid cache](#the-hybrid-cache-settings-without-asking-the-database)).

## When to use it

| Situation | Use Redis? |
|---|---|
| One Server instance, development, tests | No. Memory is simpler and exact |
| More than one Server instance (load, rolling updates, Kubernetes) | **Yes**. Otherwise each instance counts rate limits alone (an attacker gets N times the limit) and a settings change takes up to the cache time to reach the others |
| You want the one-time codes out of the main database | Yes |
| More than one instance of **your application** that uses `ICurrentUser` | Yes, for the Client's access-data cache (below) |

## Benefits for the Server

- **Rate limits that really limit.** With Redis the counters are shared: 10 sign-in attempts per minute means 10 per minute in total, not per instance. The count is one atomic Lua script (`INCR` plus `PEXPIRE`), so parallel requests cannot slip through.
- **One-time codes with the right rules at any scale.** A code is a Redis hash with an expiry that Redis itself enforces; "use" is `DEL`, so two parallel requests cannot both succeed. Resend delay and wrong-attempt limits work the same as in the table version.
- **Instant settings changes everywhere.** When an admin changes a project's settings, the instance that saved the change publishes the project id; every other instance drops its cached settings and project status at once and reloads its external providers. Without Redis the other instances wait for their cache to expire (1 day, because settings change rarely) or for the provider poll (30 seconds), so **run more than one instance only with Redis**.
- **Less load on PostgreSQL.** Short-lived, high-volume data (counters, codes) is not written to the database.
- **A readiness check.** `/health/ready` also pings Redis when it is configured ([observability.md](observability.md)).

## How to enable it

### 1. Run Redis

Locally, the repository's compose file has it:

```bash
docker compose up -d redis        # redis:7-alpine on port 6379
```

In production use a managed Redis or your own with a **password**, **TLS** and a **private network** only; the Server needs no persistence for these keys (a lost counter or code only costs a retry).

### 2. Give the Server the connection string

```json
{ "Auth2": { "Redis": { "ConnectionString": "localhost:6379", "KeyPrefix": "auth2" } } }
```

or the environment (as in `docker-compose.yml`): `Auth2__Redis__ConnectionString=redis:6379`.

The connection string is the StackExchange.Redis format. Examples:

| Case | Value |
|---|---|
| Local | `localhost:6379` |
| Password and TLS | `redis.example.com:6380,password=<secret>,ssl=true` |
| Several nodes / Sentinel | see the StackExchange.Redis documentation (`host1:6379,host2:6379,...`) |

Keep the password in a secret store or an environment variable, not in `appsettings.json` in the repository.

| Option | Default | Meaning |
|---|---|---|
| `Auth2:Redis:ConnectionString` | none | **Turns Redis on** when it is not empty |
| `Auth2:Redis:KeyPrefix` | `auth2` | Prefix of every key and channel. Give each environment its own (`auth2-prod`, `auth2-test`) when they share one Redis |

That is all: the switch is only the connection string. At start the Server registers the Redis rate limiter, the Redis one-time-code service and the settings publisher and listener (`AddOperations` in `ServiceCollectionExtensions.cs`).

### 3. Check that it works

- `GET /health/ready` is healthy only when Redis answers.
- In `redis-cli`: `SCAN 0 MATCH auth2:* COUNT 100` shows keys such as `auth2:rl:...` after a few sign-in attempts and `auth2:otp:...` after a code was requested. Use `SCAN`, never `KEYS`, on a big Redis.
- `SUBSCRIBE auth2:settings-changed` shows a message each time a project's settings are saved.

### What happens when Redis is down

The Server **still starts** (`AbortOnConnectFail = false`), reconnects by itself, and `/health/ready` says not ready. But the rate limiter **does not fail open**: it does not catch a Redis error, so every request that is rate limited (the sign-in pages, `/connect/token`, register, one-time codes, password reset, external sign-in, passkeys, the admin API, the handshake and sync routes) fails with a server error while Redis is unreachable, and so does requesting or checking a one-time code. Routes that are not limited keep working. So treat Redis as a **hard dependency** once you turn it on: run it highly available, watch `/health/ready`, and if you cannot, do not enable it.

## The hybrid cache: settings without asking the database

`IHybridCacheService` (`HybridCacheService`, registered by `AddAuth2Caching()` in the `DependencyInjection` folder) is a two-level cache for values that are read on every request and change rarely. The settings of a project use it: before this, every instance asked PostgreSQL for the settings again every 10 minutes and after every change.

| Level | Where | What it holds |
|---|---|---|
| 1 | The memory of the instance | The value and the **version** it was read under |
| 2 | Redis (only when `Auth2:Redis:ConnectionString` is set) | A small **version number** per key, and the value (JSON) with the version it belongs to |

**A read asks Redis only for the version number** (one `GET` of a few bytes):

1. The version equals the one of the copy in memory: the copy is returned. No database, no value over the network.
2. The version differs (an instance changed the value): the value is read from Redis (one `HMGET`).
3. Redis has no value for the current version: the database is read **once** and the value is written to Redis for every other instance.

**A change** (`IProjectSettingsProvider.Invalidate`, called when an admin saves settings) raises the version in Redis. Every instance sees the new number on its next read and loads the new value: no message to each instance, and no waiting for a cache to expire. The new value is stored **only if the version is still the one it was read under** (one Lua script), so a value that was read from the database before a change can never overwrite the newer one.

| Redis key | Content | Lifetime |
|---|---|---|
| `{prefix}:hc:{key}:ver` | The version (an integer, raised on every change) | none (a few bytes per key) |
| `{prefix}:hc:{key}:data` | A hash: `ver` and `json` (the value, written like the settings in the database) | `HybridCacheEntryOptions.Expiration` (1 day for settings) |

What uses it (every request starts with these three, so none of them reads PostgreSQL on every request any more):

| Key | Holds | Dropped (version raised) when |
|---|---|---|
| `settings:{projectId}` | The settings of the project | the settings are saved, the project is deleted, the Server seeds |
| `project:{projectId}` | The project row (key, name, enabled flag, settings version) | the settings are saved, the project is renamed, enabled or disabled, deleted |
| `client:{clientId}` | The calling application (`ClientApplicationInfo`: enabled flag, scopes, redirect URIs, settings, web server address, type) | the application is created, changed, deleted, its settings change, a handshake changes its address or type, the project settings change (the consent type follows the project) |
| `project-ref:key:{key}`, `project-ref:client:{clientId}`, `project-ref:single:{name}` | Only the id of the project that a key, a client id or the single-project name belongs to | the project or application is deleted, or the single project is renamed |

`Effective` settings (the project settings with the overrides of the application) are not stored: they are put together in memory from the first two for each request.

**Where it is used in the code.** `Auth2PageModel` (and every page, endpoint and service) gets the project, the settings and the application through `IAuthFlowResolver`: `GetProjectAsync`, `GetSettingsAsync`, `GetClientAsync`. The resolver is thin; the reading is in `IProjectContext` (`SingleProjectContext`, `MultiProjectContext`), `IClientApplicationContext` (`ClientApplicationContext`) and `IProjectSettingsProvider`, which all read through `IHybridCacheService`. So these three calls come from Redis when it is on, and from the memory of the instance when it is not. The key names are in `AuthFlowCache` (`Infrastructure/Caching/AuthFlowCache.cs`).

### Check that it works

1. Start the Server with `Auth2:Redis:ConnectionString` set (without it the cache is memory only) and open a sign-in page once.
2. In `redis-cli`: `SCAN 0 MATCH auth2:hc:* COUNT 100` (use your `KeyPrefix`; never `KEYS` on a big Redis). You should see, for a client `mvc-sample`:
   - `auth2:hc:project:{projectId}:ver` and `...:data`
   - `auth2:hc:settings:{projectId}:ver` and `...:data`
   - `auth2:hc:client:mvc-sample:ver` and `...:data`
   - `auth2:hc:project-ref:client:mvc-sample:ver` and `...:data`
3. `HGETALL auth2:hc:client:mvc-sample:data` shows `ver` (the version the value belongs to) and `json` (the value). `GET auth2:hc:client:mvc-sample:ver` is the number every request compares.
4. Change something in the admin console (for example a project setting): the `...:ver` number goes up and the next request reloads the value. `MONITOR` on a test Redis shows a request doing only `GET ...:ver` while nothing changed.

The test `AuthFlowCacheRedisTests` does the same against a real Redis: the keys exist, a row changed directly in PostgreSQL is **not** seen until the entry is invalidated, and saving the settings or changing an application is seen by the next request.

| Problem | Likely cause |
|---|---|
| No `hc:` keys in Redis | `Auth2:Redis:ConnectionString` is not set, or the Server was not restarted after setting it. The hybrid cache then uses memory only |
| A change made directly in PostgreSQL does not appear | By design: the cache is dropped by the Server's own write paths (admin console, API, handshake, seed). Entries also expire after 1 day. Change data through the Server, or drop the keys of the project (`SCAN` and `DEL` its `hc:` keys) |
| A change appears on one instance only | The instances use different `Auth2:Redis:KeyPrefix` values, or one of them has no Redis |
| Warnings "Redis did not answer the version of the cache key" in the log | Redis is down or too slow. The cache keeps working from memory and the database (slower); fix Redis |

**What this means for Redis.** The settings JSON holds the external-provider secrets and the SMTP password **in the form they have in the database: encrypted** with the Data Protection keys (fields ending in `Protected`). They are not readable without those keys, which stay in PostgreSQL (see the start of this page). Still, Redis now holds data that is more sensitive than counters, so protect it as the page says: a password, TLS and a private network. `project.AccessVersion` of the cached project row is **not** kept fresh (nothing reads it from there; the access version is read from the database where it matters).

**Without Redis** the cache is the memory of the instance only, as before (a change on another instance is seen when the copy expires). **When Redis fails** the cache does not: it logs a warning and works from memory and the database (a Redis outage costs speed, not availability, unlike the rate limiter above).

To use it for something else: inject `IHybridCacheService` and call `GetOrCreateAsync(key, factory)`; call `Invalidate(key)` when the value changes.

## What is in Redis (keys)

| Key or channel | Content | Lifetime |
|---|---|---|
| `{prefix}:rl:{policy key}` | A counter for one rate-limit window | The window of the policy (default 60 s) |
| `{prefix}:otp:{projectId}:{userId}:{purpose}` | A hash: `h` (hash of the code), `a` (wrong attempts), `c` (created), `e` (expires) | `TwoFactor.OtpLifetimeSeconds` (default 120 s) |
| `{prefix}:hc:{key}:ver`, `{prefix}:hc:{key}:data` | The version and the value of a hybrid cache entry (the settings of a project) | `ver`: none, `data`: 1 day |
| channel `{prefix}:settings-changed` | The project id (a message, not stored). Still used to drop the project status cache of the other instances | none |

The rate-limit key names the address, the client id and the login, never a raw token (a bearer token is hashed).

## The Client package: where the responses are kept

The Client calls the Server for the person's access data (`GET /api/auth2/me`) and keeps the **answer** so that a request does not wait for the Server each time ([client-current-user.md](client-current-user.md)). You choose where:

| Store | Use | How |
|---|---|---|
| Memory (default) | One instance of your application | Nothing to do (`MemoryCurrentUserStore`) |
| Redis (or any `IDistributedCache`) | Several instances of your application | `AddStackExchangeRedisCache` + `UseDistributedCurrentUserStore()` |

```csharp
builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = "redis:6379";
    o.InstanceName = "papertrade:";          // your application's own prefix in a shared Redis
});

builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))
    .ServerWithMultiProject("papertrade")
    .UseApiAuthentication()
    .EnableCurrentUser(o => o.CacheTtl = TimeSpan.FromMinutes(5))   // how long an answer is kept
    .UseDistributedCurrentUserStore();
```

- **Key and content.** One entry per client and person: `auth2:access:{clientId}:{userId}` (the cache's `InstanceName` is put in front). The value is the JSON of `Auth2Access` (roles, permissions, groups, the access version). Nothing secret: no token, no password.
- **Lifetime.** `CacheTtl` (default 10 minutes), as an absolute expiry. An entry whose access version is older than the `pv` claim of the person's token is read again at once, so a new token shows a change immediately.
- **Order of the calls does not matter.** `UseDistributedCurrentUserStore()` registers the in-memory distributed cache as a default and your Redis registration replaces it.
- **Why Redis here.** With memory, each instance of your application asks the Server separately and each instance can show different data for a while after an admin change. With Redis one answer serves all instances.
- **If Redis is down.** A read that fails is an error of the cache, not of the person: check your logs. Keep Redis highly available or use the memory store.

Other things the Client keeps, and where (all **in memory of the process**, not shared):

| What | Where | Notes |
|---|---|---|
| The Server's public signing keys | The JWT bearer handler | Read again on an unknown key id after a rotation ([key-ring.md](key-ring.md)) |
| Machine tokens (client credentials) | `IAuth2MachineTokenProvider` | One per set of scopes until it expires |
| The permission, role, scope and group lists that a `Pull` read | `IAuth2Catalog` | Replaced every `CatalogRefreshInterval` |

## Examples

**Server and two instances in compose** (`docker-compose.yml` already sets this for the `server` service):

```yaml
server:
  environment:
    Auth2__Redis__ConnectionString: redis:6379
```

**Prove the limit is shared** (sign-in limit 10 per minute): send 6 wrong sign-ins to instance 1 and 6 to instance 2 within a minute. With Redis the second batch is refused after 4 requests (`429 rate_limited`); without Redis each instance would allow all 6.

**A shared Redis for several environments:** `Auth2:Redis:KeyPrefix = auth2-staging` on one and `auth2-prod` on the other; the Client's `InstanceName` per application.

## Common problems

| Problem | Cause and fix |
|---|---|
| Sign-in returns a server error and the log names Redis | Redis is down or unreachable and the rate limiter does not fail open (see above). Restore Redis, or remove `Auth2:Redis:ConnectionString` and restart to go back to memory counters |
| `/health/ready` is not ready after enabling Redis | The connection string is wrong or Redis is not reachable from the Server (network, password, TLS: `ssl=true`) |
| Rate limits still count per instance | The connection string is empty on that instance, or the instance did not restart (the switch is read at start) |
| One-time codes "disappeared" after a restart | They were in memory/the table before; with Redis they live in Redis. Codes only last 2 minutes anyway |
| Two environments disturb each other | They share a Redis with the same `KeyPrefix`. Give each its own |
| The Client shows old roles after an admin change | `CacheTtl` and the age of the token (new token = immediate). Lower the TTL or sign in again |

## How to implement or extend

- `RedisConnection` (`Infrastructure/Operations/Redis.cs`): one shared multiplexer, `Key(name)` adds the prefix. `RedisRateLimiter` (the Lua counter, behind `IRateLimiter`), `RedisOtpService` (behind `IOtpService`), `RedisSettingsInvalidationBus` and `SettingsInvalidationListener` (behind `ISettingsInvalidationBus`), `RedisHealthCheck` (`Hardening.cs`). The memory versions are `MemoryRateLimiter`, the table version of the codes, and `NoOpSettingsInvalidationBus`.
- To put something else in Redis, inject `RedisConnection` (only registered when Redis is on, so use `GetService`) and build keys with `Key(...)`. Keep values small and give them an expiry.
- Another cache for the Client: implement `ICurrentUserStore` and register it before `AddAuth2Client` ([client-builder.md](client-builder.md)).
- Tests: `RedisOtpTests` (`OperationsTests.cs`), `ExternalRegistryRedisTests` (two instances with Redis), `ScaleOutTests` (rotation seen by a second instance), all with a Redis container.

## Related pages

- [deployment-and-scaling.md](deployment-and-scaling.md)
- [security.md](security.md)
- [observability.md](observability.md)
- [client-current-user.md](client-current-user.md)
- [client-builder.md](client-builder.md)
- [server-setup.md](server-setup.md)
- [Documentation index](index.md)
- [Server settings](server-settings.md)
- [Client setup](client-setup.md)
- [Troubleshooting](troubleshooting.md)
