# Troubleshooting

Symptom, cause, fix. For external sign-in see also [external-identity/troubleshooting.md](external-identity/troubleshooting.md).

## What this is

A short list of the problems people meet first. Every server error has a stable code ([error-codes.md](error-codes.md)); the audit log (`/api/auth2/admin/audit-events`) has the reason.

## When to use it

When something does not work and you do not know where to look.

## Problems

| Symptom | Cause | Fix |
|---|---|---|
| API answers 401 | Token missing, expired, wrong audience or wrong issuer | Check `Auth2:Issuer` of the Server equals the address your API uses for `AddAuth2Client`; the token audience is the **client id** of the application |
| API answers 403 | Missing permission, role or scope | Call `/api/auth2/me`; compare with the attribute; remember the `pv` claim and cache time |
| `invalid issuer` | The Server is reached under another address than `Auth2:Issuer` (proxy, http versus https) | Set `Auth2:Issuer` to the public `https://` address; use forwarded headers |
| Redirect loop after login | The redirect URI is not registered, or the cookie is not kept | Add the exact URI in `Redirects.AllowedRedirectUris`; check cookie domain and `SameSite` |
| Cookie not set behind a proxy | The Server thinks the request is `http` | `UseForwardedHeaders` and `KnownProxies` ([deployment-and-scaling.md](deployment-and-scaling.md)) |
| Tokens refused after a restart or on a second instance | Different signing keys | Keys live in the database (`key_ring`); instances must share one database; see `rotate-keys` |
| Handshake errors at start | Wrong client id or secret, application disabled, **server mode mismatch** | Read the message: `ServerWithMultiProject` is for a multi-project Server only ([handshake-and-startup.md](handshake-and-startup.md)) |
| 429 `rate_limited` | A rate limit policy was hit | Wait; change `Auth2:RateLimiting` if needed; with several instances use Redis |
| One-time code does not arrive | The default email and SMS senders only log | Register your own `IEmailSender` and `ISmsSender` |
| Redis is down | Redis is optional | Counters fall back per instance; check `/health/ready`; see [deployment-and-scaling.md](deployment-and-scaling.md) |
| Cannot sign in as `globaladmin` | The password was changed, or you use the wrong project | The global admin signs in at the `_global` project; reset with the `create-admin` command ([seeding.md](seeding.md)) |
| `login_method_not_allowed` | The method is not in `Methods.AllowedLoginMethods` | Add `External`, `Ldap` or `Passkey` for external sign-in |
| A setting is refused with 403 | It is `GlobalOnly` | Only a global admin changes it ([server-settings.md](server-settings.md)) |

## Related pages

- [error-codes.md](error-codes.md)
- [observability.md](observability.md)
- [security.md](security.md)
