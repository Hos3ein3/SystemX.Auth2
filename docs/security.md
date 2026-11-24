# Security

## What this is

What the Server does to keep accounts and tokens safe, what you must do before production, and the limits.

## When to use it

Read it before you go live. The **production checklist** at the end is the short version.

## What the Server does

| Area | Behaviour |
|---|---|
| Passwords | Hashed by ASP.NET Core Identity. Rules per project (length, characters). Never logged |
| Client secrets | Hashed; shown **once** when created or rotated |
| One-time codes (OTP) | Hashed, short life, limited attempts, resend delay, work once, compared in constant time. Stored in the database, or in Redis when Redis is on |
| Two-factor | The authenticator key is encrypted (Data Protection). Recovery codes are hashed |
| Lockout | Per project, from the settings |
| Project and application status | A disabled **project** blocks every sign-in path and every token of the project; a disabled **application** blocks only itself. One shared check (`AccessGate`) is used by every path, and OpenIddict event handlers refuse tokens issued before. The public answer is the generic `access_denied`, the real reason is only in the audit log. A cached status (`Auth2:ProjectStatusCacheSeconds`, default 10 s) means a token of a project that was just disabled stops working within that window (at once on the instance that made the change; other instances are told through Redis when it is on) |
| Login cookie | The hosted-UI cookie has the project key in its name (`.SystemX.Auth2.Session.{projectkey}`), so a login in one project never counts in another project on the same domain. Applications of one project share it |
| Project key | An identifier, not a secret, never hashed, never proof of anything. The server finds the project from the client id; the client id and secret (confidential), PKCE and exact redirect URIs are the real protection |
| User enumeration | Login, register, forgot password and resend answer the same for known and unknown people |
| Account hijacking | A second project is joined only with proof of ownership (a code to a **confirmed** email or phone). An admin never sets a password for an existing person |
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
- **CORS** is off. An origin is allowed when it is listed in `AllowedCorsOrigins` of an enabled application of an enabled project (exact match). Credentials are never allowed. The list is read again every minute.

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

## External identity (social, OIDC, SAML, LDAP, passkeys)

What the Server does so that a wrong or hostile provider cannot take over an account. Details and tests are in [external-identity.md](external-identity.md).

| Risk | Defence |
|---|---|
| Account takeover by email (someone makes a provider account with your email) | Linking by email only when the provider **proves** the email (`email_verified`, per-type defaults, off for unknown providers). Otherwise `external_email_exists_not_verified`. Only a global admin can trust a provider's email |
| Login CSRF and replay | OIDC `state`, `nonce` and PKCE; the state is sealed, expires in 10 minutes, is bound to one provider and one project (and remembers the application that started it) and is **single use** (shared `replay_entries`) |
| Mix-up between providers or projects | Callback URLs are unique per project and provider; a state made for another provider or project is refused |
| Forged SAML | XXE-safe XML, size limit, exactly one assertion, no duplicate ids, pinned certificates, issuer, destination, audience, `InResponseTo`, time window, single-use assertion ids, IdP-initiated off by default, `RelayState` never trusted for redirects |
| LDAP injection and downgrade | Filters are escaped (RFC 4515); TLS required with chain and name check; no anonymous bind, no empty passwords; the unique id (not the DN) is the link key; same answer for unknown user and wrong password |
| SSRF through provider addresses | Every call to a provider uses a client that refuses private, loopback, link-local and metadata addresses, no redirects, https only |
| Secret leaks | Client secrets, Apple keys, SAML private keys and bind passwords are encrypted (Data Protection), never returned by the API, never logged or audited |
| Privilege escalation by group mapping | Mappings only add things of the same project; `GlobalAdmin` and system roles can never be mapped; only mapped targets are removed |
| Passkey theft or reuse | Per-project store, single-use challenges, signature counter, origin and domain checks, user verification rule, AAGUID block list, limits |
| Locking yourself out | The last way to sign in cannot be removed; one passkey needs a backup |

## Limits (what this is not)

- The Server is **not a SAML identity provider**: it does not issue SAML assertions to other applications.
- Directory data (LDAP) and groups are read **at login**, not synchronised in the background. A user who is disabled in the directory keeps a session until the access token and refresh token end (revoke sessions in the admin API to cut it earlier).

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
- [ ] Passkeys: `Auth2:Passkeys:ServerDomain` is set (and never changed afterwards).
- [ ] External providers: only `External`, `Ldap` and `Passkey` login methods that you use are in `AllowedLoginMethods`; provider secrets are rotated when they expire; SAML certificates are watched ([saml-certificate-rotation.md](external-identity/saml-certificate-rotation.md)).

## Related pages

- [deployment-and-scaling.md](deployment-and-scaling.md)
- [observability.md](observability.md)
- [seeding.md](seeding.md)
- [authentication-flows.md](authentication-flows.md)
