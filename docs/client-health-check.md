# Health check: one-way and two-way

## What this is

`EnableHealthCheck` gives your application a health check named `auth2`. It has two modes:

| Mode | Who asks whom | What you get |
|---|---|---|
| **One-way** (default) | Only the **client** checks the **server**. | A health check `auth2` in your application: `Healthy` after a good handshake, `Degraded` otherwise. The Server never calls your application. |
| **Two-way** | The client checks the server, **and the server checks the client**. | The same, plus: your application answers the Server's question "are you healthy and ready?" at `/auth2/health`, and the **Server asks every minute** and shows the answer on the **dashboard of the project** in the admin console (`/admin/p/{project-key}`), beside the badges `Jobs: Healthy` and `Status: Active`. |

```csharp
// one-way (the default)
builder.Services.AddAuth2Client(section).EnableHealthCheck();

// two-way
builder.Services.AddAuth2Client(section).EnableHealthCheck(o =>
{
    o.Mode = Auth2HealthCheckMode.TwoWay;
    o.PublicUrl = "https://shop.example.com";    // this application, as the Auth2 server reaches it
});
```

## When to use it

- **One-way** is enough when your application's own probes (Kubernetes, a load balancer) watch it, and you only want its health to show that the Auth2 server is reachable.
- **Two-way** when the people who run the Auth2 server want to **see, in one place, whether the applications that depend on it are up**: the dashboard shows it per project, with no extra monitoring system. It also tells them, when sign-in fails for an application, that the application itself is down.

## Enable two-way: step by step

1. Choose the address of your application **as the Auth2 server reaches it**: `https://shop.example.com` on the internet, `http://sample-mvc:5001` inside docker-compose, a cluster address in Kubernetes. It is **not** what the browser uses if that is different.
2. Set it with `PublicUrl` (here from configuration, as both samples do) and switch the mode on:

```csharp
builder.Services.AddAuth2Client(section)
    .ServerWithMultiProject("sample-project")
    .EnableHealthCheck(o =>
    {
        o.Mode = Auth2HealthCheckMode.TwoWay;
        o.PublicUrl = builder.Configuration["Auth2:PublicUrl"] ?? "http://localhost:5101";
    });
```

3. Call `app.UseAuth2()` as usual: it maps the health route.
4. Start the application. The handshake with the Server tells the Server the address (`PublicUrl` + `Path`). Within a minute (the first round starts about 15 seconds after the Server starts) the dashboard shows `Clients: Healthy`.

The options (`Auth2HealthCheckOptions`):

| Option | Default | Meaning |
|---|---|---|
| `Mode` | `OneWay` | `OneWay` or `TwoWay` |
| `PublicUrl` | none | Address of this application as the Server reaches it. **Required for two-way**: an absolute `http` or `https` address, with no user name, query or fragment (the application does not start with a wrong one) |
| `Path` | `/auth2/health` | Where the application answers. Must start with `/` |

## What the client answers

`GET {PublicUrl}{Path}` runs **all** the health checks of your application (your database, your queue, ... and the `auth2` check) and answers:

| Result of your checks | HTTP status | Body |
|---|---|---|
| Healthy | `200` | `{"status":"Healthy","applicationName":"...","clientVersion":"..."}` |
| Degraded | `200` | `{"status":"Degraded", ...}` |
| Unhealthy | `503` | `{"status":"Unhealthy", ...}` |

"Healthy and ready" therefore means whatever **you** registered with `AddHealthChecks()`. The answer is public and has **no details** (not the names or descriptions of the failing checks, no client id): it says one word. If you do not want it public, put the route behind your network rules; the Server calls it without a token.

## What the Server does

- **Registers** the address from the handshake. A handshake without an address (one-way) **removes** the registration, so switching back to one-way stops the calls.
- **Asks every minute** (`Auth2:Jobs:ClientHealthIntervalSeconds`, default 60, at least 10). With several Server instances only **one** asks (a database lock). A round asks at most eight clients at the same time, each with a 5 second limit.
- **Sends a plain `GET`**: no token, no cookie, no body, no redirects, and it reads only a few kilobytes. It keeps the **state**, the time of the check, the last time the client was healthy and a short message.
- **Accepts only plain http or https addresses**. An address with a user name or password, another scheme (`ftp:`), a fragment or no host is ignored (the handshake still works, and the log says so).

| State | Meaning |
|---|---|
| `Healthy` | The client answered `200` and `Healthy` |
| `Degraded` | `200` and `Degraded` |
| `Unhealthy` | `503`, `200` with `Unhealthy`, or any other HTTP status (for example `404`: wrong `Path`) |
| `Unreachable` | No answer: refused, no route, or slower than `ClientHealthTimeoutSeconds` (5) |
| `Unknown` | Registered but not asked yet, **or the last answer is older than three rounds** (the checker is not running), so the console never shows a state that may be wrong |

### On the dashboard

The header of `/admin/p/{project-key}` shows one badge for the project's applications that use two-way, next to the job badge:

| Badge | Meaning |
|---|---|
| `Clients: Healthy` (green) | Every two-way client is healthy |
| `Clients: 1 of 2 not healthy` (red) | The number of clients that are `Degraded`, `Unhealthy` or `Unreachable` |
| `Clients: Unknown` (grey) | Not asked yet, or the checker is off |

Hover the badge to see each application with its state, the time of the check and the message. With no two-way client the badge is not shown. Project admins see it for their project; global admins for every project.

## Server settings

In the `Auth2:Jobs` section of the Server:

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | The master switch of all background jobs (also this one) |
| `ClientHealthEnabled` | `true` | Ask the two-way clients. `false` turns the questions off; the dashboard then shows `Unknown` after a few minutes |
| `ClientHealthIntervalSeconds` | `60` | Seconds between two rounds (at least 10) |
| `ClientHealthTimeoutSeconds` | `5` | Seconds to wait for one client (1 to 60) |

The answers are kept in the table `server_values` (key `client-health:{application id}`), so no migration is needed ([database-tables.md](database-tables.md#server_values)).

## Docker and networks

- The **Server** must be able to open a connection to `PublicUrl`. In docker-compose use the service name (`http://sample-mvc:5001`), not `localhost`: inside the Server's container `localhost` is the Server itself. The compose file of this repository sets `Auth2__PublicUrl` for both samples.
- The client reaches the Server for the handshake; two-way adds the opposite direction. A firewall that allows only the first makes the state `Unreachable`.

## Security notes

- The address comes from a client that proved who it is with its client secret, but a project admin can set up any client, so the Server treats the address as input: only http and https, no credentials, no redirects, a small read and a short time. It sends nothing secret.
- The Server only **reads the status word**. It never uses the body of the answer for anything else.
- The route of the client has **no authentication** on purpose (it holds nothing secret). It does run your health checks on every call, so keep them cheap, or protect the route with your network rules.

## Common problems

| Problem | Cause |
|---|---|
| The application does not start: "a two-way health check needs PublicUrl" | `Mode` is `TwoWay` and `PublicUrl` is empty or not an absolute http or https address |
| No `Clients:` badge | No application of the project uses two-way, or the handshake has not run yet (look at the `auth2` health check), or the address was refused (see the Server log) |
| `Clients: Unknown` | The first round has not run yet (about 15 seconds after the Server starts, then every minute), or `ClientHealthEnabled` is `false` |
| `Unreachable` | The Server cannot reach `PublicUrl` (docker service name, firewall, wrong port), or the client is down |
| `Unhealthy` with message "answered 404" | `Path` differs between what the client maps and what it told the Server: restart the client so a new handshake sends the right address, or check `UseAuth2()` is called |
| `Unhealthy` while the site works | One of **your** health checks fails: open `/health` of the application |

## How to implement or extend

- Your own "ready" rules: register them with `AddHealthChecks()`; the answer includes them.
- The answer is always the result of **all** the registered checks. Choosing a subset (for example only checks with a tag) is not configurable yet; keep a check out of the answer by not registering it with `AddHealthChecks()`.
- Reading the states in code: `IClientHealthService.GetProjectAsync(projectId)` on the Server returns `ClientHealthInfo` (state, times, message).

## Related pages

- [Client setup](client-setup.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Handshake and startup](handshake-and-startup.md)
- [Observability](observability.md)
- [Deployment and scaling](deployment-and-scaling.md)
- [Security](security.md)
- [Documentation index](index.md)
