# Handshake and startup

## What this is

The check a client application makes when it starts: "am I talking to the right server, and is it set up the way I expect?".
This page describes the **Server** side. The Client's startup service is described in [client-setup.md](client-setup.md).

## When to use it

Call it once at startup (and again later if you want). It does not log anyone in. It needs a client-credentials token with scope `auth2.client.handshake`.

## How to use

### The request

`POST /api/auth2/client/handshake`

```json
{ "projectKey": "papertrade", "applicationName": "papertrade-api", "clientVersion": "1.0.0" }
```

- `projectKey` is the key of the **project**. Send it only to a multi-project server (the client does this with `.ServerWithMultiProject("papertrade")`).
- `applicationName` is the `Name` of the **application** (the client) inside the project.

The body is only a **check**. The server finds the project from the application of the token (the client id). Nothing in the body can select another project.
The key is an identifier, not a secret and not proof of anything.

### The rules

| Server mode | `projectKey` in the request | Result |
|---|---|---|
| Multi-project | present and equal to the project of the token's application | continue |
| Multi-project | missing | `409 server_mode_mismatch` (the client stops at start with a clear message) |
| Multi-project | the key of another project | `409 project_key_or_name_mismatch` |
| Single-project | missing | continue |
| Single-project | present | **continue**. The server ignores the key and writes a warning to its log; the client logs a warning too |

Then, in both modes, `applicationName` must equal the `Name` of the calling application (ignoring case and outer spaces), otherwise `409 project_key_or_name_mismatch`.
So the server verifies two things: the calling client belongs to the project with that key, and it has that name.

Before these rules the normal API checks run: no token or a bad token is `401 invalid_token`; a user token or a missing scope is `403`;
an unknown application is `404 project_not_found`; a disabled **project or application** is refused with the generic `403 access_denied`
(or `401` when the token itself is not valid any more). The audit log tells which one it was.

### The answer

```json
{ "projectId": "0198...", "applicationId": "0199...", "projectKey": "papertrade", "applicationName": "papertrade-api",
  "serverMode": "MultiProject", "issuer": "https://auth.example.com",
  "settingsVersion": 3, "cookieLifetime": "01:30:00", "singleActiveSession": false }
```

- `serverMode` tells the client which mode the server uses. The client must **not guess** the mode.
- `projectId` and `applicationId` are the ids of the project and of the calling application.
- `projectKey` is `null` in single-project mode.
- `issuer` is `Auth2:Issuer`, or the address of the request when that is not set.
- `cookieLifetime` is the **effective** value (the application value, else the project value, else `null`). The client then uses its own default.
- `settingsVersion` goes up when the project settings change.

### What the client does with the answer

| HTTP | Client status |
|---|---|
| 200 | `Ok` (with a warning message when a key was sent to a single-project server) |
| timeout, network error, 5xx | `ServerUnavailable` |
| 404 | `NotFound` |
| 401, 403, 409 | `Rejected` |

Messages: "The server has a single project. The ProjectKey is ignored. Remove ServerWithMultiProject." (warning, the client still works)
and "The server serves many projects. Call ServerWithMultiProject(projectKey) with the key of the project of this application." (the client stops).
Sync (see [permission-sync.md](permission-sync.md)) starts only after a successful handshake.

## How to implement / extend

- The server side is `ClientHandshakeService`. It reads the project (`IAuthFlowResolver.GetProjectAsync`) and the calling application (`GetClientAsync`) and compares them with the body.
- The client side is `HandshakeRunner` (one attempt) and `HandshakeHostedService` (start-up, retries). The client never sends a project key unless `ServerWithMultiProject` was called.
- To add a field to the answer, add it to `ClientHandshakeResponse` in Contracts (at the end of the record, so older clients keep working) and fill it in the service.

## Common problems

| Problem | Cause and fix |
|---|---|
| `server_mode_mismatch` | The client has no `ProjectKey` but the server serves many projects. Call `ServerWithMultiProject("key")` on the client |
| `project_key_or_name_mismatch` | The client secret belongs to an application of another project, or the application name differs. Compare `ProjectKey` and the application `Name` in the admin screen |
| `403 insufficient_scope` | Add `auth2.client.handshake` to the allowed scopes of the application |
| `access_denied` | The project or the application is disabled. Ask an admin; the audit log has the reason |

## Related pages

- [permission-sync.md](permission-sync.md)
- [concepts.md](concepts.md)
- [api-reference.md](api-reference.md)
