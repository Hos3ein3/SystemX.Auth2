# Authentication flows

## What this is

How a user or an application gets a token from the SystemX.Auth2 Server.
The server uses **OpenIddict** (an OpenID Connect and OAuth 2.0 library). It is **not** a certified OpenID Connect provider.

Flows in this version:

| Flow | For | Status |
|---|---|---|
| Authorization code + PKCE (hosted UI) | Web apps and APIs with users | Implemented. Recommended |
| Client credentials (M2M) | Server to server, no user | Implemented |
| Refresh token | Long sessions | Implemented (rotation + reuse detection) |
| Password grant | Self UI, mobile apps | Implemented, **off by default** |
| Two-factor grant | Second step of the password grant | Implemented |
| OTP grant (`urn:systemx:auth2:grant:otp`) | Login with a one-time code | Phase 6 |
| External login, passkeys | Social login, passkeys | Phase 14 (experimental) |

*PKCE* (Proof Key for Code Exchange) protects the authorization code. *M2M* means machine to machine. *TOTP* is the six-digit code from an authenticator app.

## When to use it

Use the **hosted UI** unless you have a reason not to. The client app never sees the password.

## Endpoints

| Endpoint | Purpose |
|---|---|
| `/connect/authorize` | Starts the code flow. Sends the browser to the login page when needed |
| `/connect/token` | All token requests. **The only place that issues tokens** (there is no `/api/auth2/login`) |
| `/connect/endsession` | Sign out |
| `/connect/userinfo` | Claims of the signed-in user, by granted scope |
| `/connect/revoke`, `/connect/introspect` | Revoke or check a token |
| `/.well-known/openid-configuration`, `/.well-known/jwks` | Discovery and public keys |

The authorize, token, end-session and user-info endpoints run in **passthrough** mode: OpenIddict reads and checks the request, then our code (`ConnectEndpoints`, `ILoginService`) decides and signs in.

## One place decides: `ILoginService`

The order of the checks:

1. The application is enabled.
2. The login method is allowed (user name, email or phone). The method is guessed from the login text (`@` means email; a phone-number pattern means phone).
3. The user exists, is active, and belongs to the application (multi mode).
4. The account is not locked.
5. The password is right.
6. Email or phone is confirmed, if the application requires it.
7. Two-factor, if it is on for this user in this application.
8. Single active session.

Consent is checked by the authorize endpoint. The consent page and service come in phase 6.

**No user enumeration.** Unknown user, wrong password, inactive user and "not registered in this application" all return `invalid_credentials`. For an unknown user the server still spends the time of a password check. The real reason is written to the audit log (`login.failed`, field `code`).
The errors that are allowed to differ are the ones that do not tell if an account exists: `application_disabled`, `login_method_not_allowed`, `locked_out` (the account is real, and was locked by many failures), and the confirmation and two-factor-setup errors (shown only after the password was right).

Every login writes an audit event (`login.success`, `login.failed`, `login.locked_out`). Events never contain passwords, codes or login names.

## Step by step: hosted UI (code + PKCE)

1. Your app redirects the browser to `/connect/authorize` with `client_id`, `redirect_uri`, `response_type=code`, `scope`, `state`, `code_challenge` and `code_challenge_method=S256`.
   (`plain` is not accepted.)
2. If the user is not signed in, the server redirects to `/Account/Login?ReturnUrl=<the authorize URL>`.
3. The user signs in. The page sets the session cookie and redirects back to the `ReturnUrl`.
4. The authorize endpoint creates a one-time code and redirects to your `redirect_uri` with `code` and `state`.
5. Your server sends the code, the `code_verifier`, the `client_id` and the `client_secret` to `/connect/token`.
6. You get an access token, a refresh token (if you asked for `offline_access`) and an ID token (if you asked for `openid`).

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&code=...&redirect_uri=https://app.example.com/signin-oidc
&code_verifier=...&client_id=papertrade&client_secret=...
```

**Exact redirect URIs.** The redirect URIs and post-logout URIs of an application are the lists `Redirects.AllowedRedirectUris` and `AllowedPostLogoutRedirectUris` in its settings. They are matched exactly. No wildcards. When you change the settings, the OpenIddict client is updated in the same transaction.

**The session cookie belongs to one application.** The cookie `.SystemX.Auth2.Session` has a claim `app`. If a user signed in for application A and then comes to authorize for application B, they must sign in again for B, because the credentials may be different there.

`prompt=login` forces a new login. `prompt=none` returns `login_required` to your app when there is no session.

## Step by step: client credentials (M2M)

```http
POST /connect/token
grant_type=client_credentials&client_id=papertrade-api&client_secret=...&scope=auth2.client.handshake auth2.permissions.sync
```

The token has `sub` = the client id, and the claim `machine=true`. It has no user, no `sid` and no `amr`.
The scopes must be in `Clients.AllowedScopes` of the application. The scopes `auth2.admin` and `auth2.admin.global` are not allowed by default.

## Optional: the password grant

> **Risky. Off by default.** The OAuth 2.1 draft removes the password grant. With it, **your client app handles the user's password**, and every app you give this right becomes a place where passwords can leak. Use the hosted UI when you can.

It is on only when the application setting `Clients.AllowSelfUiLogin` is `true`. Then:

- The OpenIddict client gets the permission for `password` and for the two-factor grant. Without the setting the server answers `unauthorized_client`.
- The client is always **confidential**: it must send its secret. (Every client of this server has a secret.)
- Rate limiting is added in phase 10. Until then, the lockout rules are the only brake. Do not expose this on the internet before that.

```http
POST /connect/token
grant_type=password&username=sara&password=...&scope=openid offline_access&client_id=...&client_secret=...
```

Errors are standard OAuth errors plus a stable `error_code`:

```json
{ "error": "invalid_grant", "error_description": "invalid credentials", "error_code": "invalid_credentials" }
```

### Two-factor step

If the user has TOTP on, the first request fails with:

```json
{ "error": "two_factor_required", "error_code": "two_factor_required", "error_description": "...", "two_factor_token": "<short-lived token>" }
```

The token is valid for **5 minutes** and only for this application. Send the second step:

```http
POST /connect/token
grant_type=urn:systemx:auth2:grant:two_factor&two_factor_token=...&code=123456&client_id=...&client_secret=...
```

Use `recovery_code=...` instead of `code` to use a recovery code. A recovery code works once. Wrong codes count as failed attempts (lockout). The tokens then have `amr` = `pwd` and `otp`.

The hosted UI does the same in `/Account/TwoFactor`.

## Refresh tokens

- A refresh request gives a **new** refresh token and invalidates the old one (rotation).
- If an old refresh token is used again, the server treats it as stolen and revokes the whole chain, including the newest token. The client must sign in again.
- At refresh time the server checks again that the application is enabled, the user is active, still belongs to the application and is not locked out. The roles and the access version (`pv`) in the new access token are fresh. `amr` is kept.
- A refresh token of application A cannot be used with the client of application B.

## Single active session

With `Sessions.SingleActiveSession=true`, a successful login revokes all tokens and authorizations of that user **in this application**. Other applications are not touched. The server also changes the per-application security stamp (only with per-application credentials; a shared stamp would also affect other applications). Audit event: `session.revoked_others`.
Access tokens are checked against the stored entries, so the old access token stops working at `/connect/userinfo` at once. APIs that only check the signature accept it until it expires (15 minutes by default).
A cache of access data does not exist yet; when it arrives in phase 7 it will be cleared here too.

## Return URL and redirect rules

The app that wants a page remembers it (in `state` or its own `RedirectUri`). The server only has to go back to `/connect/authorize?...` using `ReturnUrl`.

After login the server goes to the first valid item of:

1. `ReturnUrl`, if it is a **local path** (starts with `/`, not `//` and not `/\`) or exactly matches `Redirects.AllowedRedirectUris`
2. `Redirects.RedirectAfterLogin` of the application (also checked)
3. `/`

Logout: `post_logout_redirect_uri` (checked by OpenIddict against the registered list) → `Redirects.RedirectAfterLogout` → `/`.

`IReturnUrlValidator` also rejects spaces, control characters and backslashes, and it checks the URL again after decoding it (up to three times), so `/%2Fevil.com` and `/%5Cevil.com` do not pass.
`ReturnUrl` is carried through login, two-factor and logout. Register, confirm-email, forgot-password and consent will carry it in phase 6.

## What is in the tokens

Access tokens are **signed JWTs** (not encrypted), so APIs can validate them with the public keys from `/.well-known/jwks`. The server also stores token entries, so tokens can be revoked. Code and refresh tokens are encrypted.

| Claim | Meaning |
|---|---|
| `sub` | User id (or client id for machine tokens) |
| `preferred_username`, `email`, `phone_number`, `given_name`, `family_name`, `name`, `locale` | Profile data |
| `role` | Roles **of this application** only |
| `scope`, `aud` | Granted scopes; the audience is the client id of the application |
| `app` | Application id |
| `projectkey` | Project key. **Omitted in single-application mode** |
| `pv` | Access version: application version plus the user's version in the application. Both only grow |
| `amr` | `pwd`, `otp` |
| `sid` | Session id (user tokens only) |
| `machine` | `true` on client-credentials tokens only |

**The permission list is not in the token** (it would make tokens huge). Clients read it from `/api/auth2/me` (phase 7) and cache it by `pv`.
Default lifetimes: access token 15 minutes, refresh token 14 days, identity token 10 minutes. They come from `Sessions.*` in the settings.

## Keys

| Environment | Keys |
|---|---|
| Development, Testing | Temporary keys are made at startup. Tokens stop working when the app restarts. Two instances do not share them |
| Any other | You must set certificates in `Auth2:Keys` (see [server-setup.md](server-setup.md)). The server **refuses to start** without them, or without an `https://` `Auth2:Issuer` |

Key rotation steps are written in `security.md` (phase 10).

## Client secrets

Every application has an OpenIddict client with a **secret**. OpenIddict stores only a hash. The secret is shown **once**:

- `create-application <projectKey> <name>` prints it to the console.
- `rotate-client-secret <projectKey>` makes a new one and invalidates the old one.
- The seeded `_global` and single-application clients get a secret that nobody sees. Run `rotate-client-secret` for them when you need one.

## Common problems

- *`unauthorized_client` on the password grant:* `Clients.AllowSelfUiLogin` is false for the application.
- *`invalid_client`:* wrong client id or secret. A secret from application A never works for B.
- *Redirect loop between authorize and login:* the cookie is not sent. Check `SameSite`, `Secure` and that you use HTTPS (outside Development the cookie is `Secure`).
- *`invalid_request` on authorize:* a PKCE parameter is missing, the method is not `S256`, or the `redirect_uri` is not in the registered list.
- *The login page shows 404:* the application is unknown. In multi-application mode the page needs `client_id` (directly, or inside `ReturnUrl`).
- *`invalid_scope` or `invalid_request` for a scope:* add it to `Clients.AllowedScopes` of the application. Custom scopes must also exist in OpenIddict.
- *Tokens stop working after a restart (Development):* temporary keys. Set certificates.

## Related pages

- [server-settings.md](server-settings.md)
- [credential-scope.md](credential-scope.md)
- [server-setup.md](server-setup.md)
- [error-codes.md](error-codes.md)
