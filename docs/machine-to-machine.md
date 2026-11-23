# Machine to machine (client credentials)

## What this is

Tokens for **programs**, not people. Your application proves who it is with its client id and secret and gets an access token. It uses that token to call the Auth2 Server (handshake, register, sync) and, if you want, to call other APIs that trust the same server.
The OAuth name is *client credentials* grant. A "machine token" has no user in it.

## When to use it

- Your backend registers users or syncs permissions on the server.
- A background service calls another API.
- Never in a browser or a mobile app: the client secret must stay on a server.

## How the client handles tokens

`IAuth2MachineTokenProvider.GetTokenAsync(scopes)`:

- asks the server (`POST /connect/token`, `grant_type=client_credentials`);
- **caches** the token until `expires_in - 60 s` (at least 30 s). One cache entry per set of scopes; the order of the scopes does not matter;
- is **thread-safe and single flight**: 100 parallel calls for the same scopes cause **one** request;
- does not cache failures.

Errors (`Auth2Exception`):

| Cause | `IsTransient` | Code |
|---|---|---|
| Network error, timeout, 5xx, 429 | yes | `server_unavailable` or `rate_limited` |
| 400 / 401 (wrong secret, scope not allowed) | no | the server code, for example `invalid_client` |

The message never contains the secret.

## Scopes the client needs

| Scope | Used for |
|---|---|
| `auth2.client.handshake` | The handshake |
| `auth2.register` | Register, join |
| `auth2.login` | OTP request, password forgot / reset, confirmations |
| `auth2.permissions.sync` | Sync and read permissions |
| `auth2.roles.sync` | Sync and read roles |
| `auth2.groups.read` | Read groups |

These are in the default `AllowedScopes` of every application. `auth2.admin` and `auth2.admin.global` are **not**: give them only to trusted back-end clients.
The client asks for the right scope by itself for each call.

## Calling another API with a machine token

```csharp
builder.Services.AddHttpClient("orders", c => c.BaseAddress = new Uri("https://orders.example.com/"))
    .AddAuth2MachineToken("orders.read");

// somewhere in a service
var http = factory.CreateClient("orders");          // or IAuth2MachineService.CreateClient("orders")
var items = await http.GetFromJsonAsync<Item[]>("items");
```

`AddAuth2MachineToken(scopes)` adds a `MachineTokenHandler`: every request gets `Authorization: Bearer <token>`. If the other API answers `401`, the cached token is dropped and the next request gets a new one.
Scopes set with `.EnableMachineToMachine(o => o.Scopes = [...])` are used when you name none.

## Common problems

| Problem | Fix |
|---|---|
| `invalid_client` | `ClientId` or `ClientSecret` is wrong (was the secret rotated?) |
| `invalid_scope` | The application does not allow that scope. Add it to `Clients.AllowedScopes` |
| A `401` from the other API | It does not trust this server, or the scope is missing in the token |
| Many token requests | You use many different scope sets. Use one set per target API |

## Related pages

- [client-setup.md](client-setup.md)
- [authentication-flows.md](authentication-flows.md)
- [permission-sync.md](permission-sync.md)
