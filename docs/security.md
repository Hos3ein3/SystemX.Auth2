# Security

## What this is

What the Server does to keep accounts and tokens safe, what you must do before production, and the limits.

## When to use it

Read it before you go live. The **production checklist** at the end is the short version.

## What the Server does

| Area | Behaviour |
|---|---|
| Passwords | Hashed by ASP.NET Core Identity. Rules per application (length, characters). Never logged |
| Client secrets | Hashed; shown **once** when created or rotated |
| One-time codes (OTP) | Hashed, short life, limited attempts, resend delay, work once, compared in constant time. Stored in the database, or in Redis when Redis is on |
| Two-factor | The authenticator key is encrypted (Data Protection). Recovery codes are hashed |
| Lockout | Per application, from the settings |
| User enumeration | Login, register, forgot password and resend answer the same for known and unknown people |
| Account hijacking | A second application is joined only with proof of ownership (a code to a **confirmed** email or phone). An admin never sets a password for an existing person |
| Open redirect | Return URLs, `redirect_uri` and `post_logout_redirect_uri` use exact match or local paths only |
| PKCE | Only `S256` |
| Tokens | Signed JWTs. Refresh tokens rotate, and reuse of an old one revokes the chain |
| Regular expressions | Every regex that comes from settings has a time limit |
| Audit | Logins, lockouts, password and 2FA changes, role / permission / group changes, admin writes, sync, key rotation, session revoke, seed events. Never secrets |
| Logs | Passwords, tokens, codes, secrets and keys are not logged. A redaction step replaces properties with such names by `***` as a last safety net |

### Rate limiting

Every request that can be abused is counted in a fixed window. Policies:

| Policy | Covers | Default (per window of 60 s) |
|---|---|---|
| `login` | password, OTP and two-factor grants; login and two-factor pages | 10 |
| `token` | other token requests (client credentials, refresh, code) | 60 |
| `register` | register, join | 10 |
| `otp` | OTP request, email and phone confirm, OTP page | 10 |
| `password-reset` | forgot, reset, resend | 5 |
| `handshake` | client handshake | 30 |
| `permission-sync` | permission and role sync | 30 |
| `admin` | every admin API route | 120 |

Each request is counted twice: for **address + login (or client id)** with the limit above, and for the **address alone** with five times the limit. So guessing one account is slowed, and so is trying many accounts from one address.
Over the limit the answer is `429` with `Retry-After` (seconds) and, for API and token routes, `{ "code": "rate_limited" }`.

Settings (`Auth2:RateLimiting`):

```json
{ "Auth2": { "RateLimiting": { "Enabled": true, "Policies": { "login": { "PermitLimit": 5, "WindowSeconds": 60 } } } } }
```

Counters are in memory by default (right for **one** instance). With **Redis** they are shared by all instances. Behind a proxy turn on forwarded headers, or every client looks like the proxy (see below).

### HTTP hardening

- HTTPS redirect and HSTS (one year) outside Development and Testing. Setting: `Auth2:Security:RequireHttps`.
- Headers on every answer: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `Content-Security-Policy` (a strict placeholder, set `Auth2:Security:ContentSecurityPolicy` for your UI), `Permissions-Policy`. `/api` and `/connect` answers also get `Cache-Control: no-store`.
- Cookies: `HttpOnly`, `SameSite=Lax`, `Secure` outside Development.
- Anti-forgery on all pages that post.
- **Forwarded headers**: set `Auth2:Security:UseForwardedHeaders=true` behind a reverse proxy, and list the proxies in `KnownProxies` (or `TrustAllProxies=true` only when nobody else can reach the server). Set `Auth2:Issuer` to the public HTTPS address.
- **CORS** is off. An origin is allowed when it is listed in `Clients.AllowedCorsOrigins` of an enabled application (exact match). Credentials are never allowed. The list is read again every minute.

### Keys and rotation

Tokens are signed and encrypted with keys. Two sources:

1. **Certificate files** (`Auth2:Keys:SigningCertificates` and `EncryptionCertificates`). You manage them. Several are allowed: the one valid the longest signs, all of them check.
2. **The key ring** (default when no files are set). The Server makes a signing key and an encryption key on first start, stores them in the database (protected with Data Protection) and all instances share them.

Rotate the key ring with the command (or `POST /api/auth2/admin/keys/rotate`, global admin):

```
dotnet SystemX.Auth2.Server.dll rotate-keys
```

What happens:

1. A new pair is created. New tokens use it at once on the instance that rotated, and within `Auth2:Keys:RefreshSeconds` (30) on the others.
2. The old pair is **retired**: it no longer signs, but it still checks and decrypts, so **old tokens keep working**. Both public keys are in `/.well-known/jwks`.
3. After `Auth2:Keys:RetiredKeyDays` (default 35, longer than the 30-day refresh token) a background job deletes the old pair. Tokens signed with it stop working then.

Rotate regularly (for example every 6–12 months) and at once if you think a key leaked.
**Important:** the key ring is protected by Data Protection. If you lose the Data Protection keys (the `data_protection_keys` table), the key ring cannot be read. Back up the database. For stronger protection add a certificate to protect the Data Protection keys in your own startup code (see the Microsoft documentation, `ProtectKeysWithCertificate`).

### Background jobs

Every `Auth2:Jobs:IntervalMinutes` (60) one instance runs, under a database lock: delete old one-time codes, delete audit events older than `AuditRetentionDays` (365; 0 keeps them), prune old OpenIddict tokens and authorizations, and remove retired keys. Other instances skip a job that is running.

## The default admin password is public

The first admin (`globaladmin`, password `Admin@12345!ChangeMe`) is in the source code. **Change it before production.** See [seeding.md](seeding.md). The Server logs a warning at every start while the default password is still set.

## Limits (what this is not)

- Not a certified OpenID Connect provider.
- The password grant is for your own trusted clients. It is off by default.
- Fixed-window rate limiting allows a short burst at a window edge.
- Memory counters are per instance. Use Redis with more than one instance.
- Data Protection keys sit in the database unencrypted unless you add a certificate.

## Production checklist

- [ ] `Auth2:Issuer` is the public `https://` address.
- [ ] HTTPS everywhere; `RequireHttps` on; HSTS understood.
- [ ] Behind a proxy: `UseForwardedHeaders` and `KnownProxies` set.
- [ ] **Default admin password changed** (or seed values come from secrets).
- [ ] Database backups include `data_protection_keys` and `key_ring`.
- [ ] Redis on if you run more than one instance.
- [ ] The `auth2.admin` and `auth2.admin.global` scopes are only on clients that need them.
- [ ] Email and SMS senders replaced (the defaults only log).
- [ ] Rate limits checked against your traffic.
- [ ] Logs and traces go to a store with access control.
- [ ] `rotate-keys` is in your calendar.

## Related pages

- [deployment-and-scaling.md](deployment-and-scaling.md)
- [observability.md](observability.md)
- [seeding.md](seeding.md)
- [authentication-flows.md](authentication-flows.md)
