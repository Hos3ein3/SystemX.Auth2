# Handshake and startup

## What this is

The check a client application makes when it starts: "am I talking to the right server, and is it set up the way I expect?".
This page describes the **Server** side. The Client's startup service comes in phase 11.

## When to use it

Call it once at startup (and again later if you want). It does not log anyone in. It needs a client-credentials token with scope `auth2.client.handshake`.

## The request

`POST /api/auth2/client/handshake`

```json
{ "projectKey": "blog", "applicationName": "Blog", "clientVersion": "1.0.0" }
```

The body is only a **check**. The application is the one of the token. Nothing in the body can select another application.

## The rules

| Server mode | `projectKey` in the request | Result |
|---|---|---|
| Multi-application | present and equal to the token's application | continue |
| Multi-application | missing | `409 server_mode_mismatch` |
| Multi-application | another application's key | `409 project_key_or_name_mismatch` |
| Single-application | missing | continue |
| Single-application | present | `409 server_mode_mismatch` |

Then, in both modes, `applicationName` must equal the application's name (ignoring case and outer spaces), otherwise `409 project_key_or_name_mismatch`.

Before these rules the normal API checks run: no token or a bad token is `401 invalid_token`; a user token or a missing scope is `403`; an unknown application is `404 application_not_found`; a disabled application is `403 application_disabled`.

## The answer

```json
{ "applicationId": "0198...", "projectKey": "blog", "applicationName": "Blog",
  "serverMode": "MultiApplication", "issuer": "https://auth.example.com",
  "settingsVersion": 3, "cookieLifetime": "01:30:00", "singleActiveSession": false }
```

- `serverMode` tells the client which mode the server uses. The client must **not guess** the mode.
- `projectKey` is `null` in single-application mode.
- `issuer` is `Auth2:Issuer`, or the address of the request when that is not set.
- `cookieLifetime` is `null` when the application did not set one. The client then uses its own default.
- `settingsVersion` goes up when the settings change.

## What the client does with the answer (phase 11)

| HTTP | Client status |
|---|---|
| 200 | `Ok` |
| timeout, network error, 5xx | `ServerUnavailable` |
| 404 | `NotFound` |
| 401, 403, 409 | `Rejected` |

Messages for the two mode mismatches: "The server is single-application. Remove ServerWithMultiApplication." and "The server is multi-application. Call ServerWithMultiApplication(projectKey)."
Sync (see [permission-sync.md](permission-sync.md)) starts only after a successful handshake.

## Common problems

| Problem | Cause and fix |
|---|---|
| `server_mode_mismatch` | Client and server disagree about the mode. Fix the client configuration, not the server |
| `project_key_or_name_mismatch` | The client secret belongs to another application, or the name differs. Compare `ProjectKey` and `Name` in the admin screen |
| `403 insufficient_scope` | Add `auth2.client.handshake` to the client's allowed scopes |

## Related pages

- [permission-sync.md](permission-sync.md)
- [concepts.md](concepts.md)
- [api-reference.md](api-reference.md)
