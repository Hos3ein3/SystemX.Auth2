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
| OTP grant (`urn:systemx:auth2:grant:otp`) | Login with a one-time code (email or SMS) | Implemented, same rules as the password grant |
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

Consent is checked by the authorize endpoint (see "Consent" below).

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
`ReturnUrl` is carried through login, register, confirm email, forgot password, reset password, two-factor, consent and logout. In emails it is added to the link **only if it passes the same checks** as every redirect. Otherwise it is left out.

## Accounts: register, join, confirm, reset, OTP, two-factor

These flows have JSON endpoints (for your backend) and pages (for the hosted UI). Both call the same services. Every endpoint is listed in [api-reference.md](api-reference.md).

### Register

`POST /api/auth2/register` with a client-credentials token that has the scope `auth2.register`. **The application is the one in the token.** The body never chooses it.

- Which identifiers are accepted (`UserName`, `Email`, `PhoneNumber`) depends on `Methods.AllowedRegisterMethods`.
- A new person is created together with the membership row in one transaction. The password follows the [credential scope](credential-scope.md).
- A confirmation email (link) and, for a phone number, a confirmation code (SMS) are sent. They are sent even when the application does not force confirmation.
- It is **idempotent**: calling it again for the same person creates nothing and **never changes the password**. Many parallel calls create one person.
- The answer says `userWasCreated`, `joinedApplication` and `confirmationRequired`. These are for your **trusted backend**. Do not show them to the end user. That would tell a stranger whether an account exists. The hosted `Account/Register` page gives every visitor the same answer.
- `Registration.RegistrationEnabled=false` closes registration (`registration_disabled`).

### Joining a second application (proof of ownership)

One person is one account on the whole server. When that person registers in another application, the server does **not** add them and does **not** take the password from the request. Otherwise a stranger could register an existing email in a new application, set their own password, and take over that application's login for the real owner (*account pre-hijacking*).

What happens instead:

1. The register call finds the existing person. The answer is `joinedApplication=false`, `confirmationRequired=true`.
2. The server sends a one-time **proof code** to the person's **confirmed** email (or, if none, confirmed phone). An unconfirmed address proves nothing, so nothing is sent (audit event `join.no_proof_channel`).
3. The person sends the code and a **new password for this application**: `POST /api/auth2/join/complete` (or the page `Account/JoinApplication`).
4. The server checks the password rules first (so a weak password does not use up the code), checks the code, then creates the membership row and sets the password in one transaction.

With shared credentials, no new password is asked: only the membership is added. `Registration.JoinMode` must be `AutoJoin`. In single-application mode there is nothing to join.

### One-time codes (OTP)

- A code has the length `TwoFactor.OtpLength`, lives `OtpLifetimeSeconds`, allows `OtpMaxAttempts` wrong tries, and can be asked again after `OtpResendSeconds`.
- It is stored **hashed**, compared in constant time, and works **once** (even with parallel requests). A code is only good for its **purpose** (`login`, `confirm_phone`, `join`, `password_reset`) and its **application**.
- Asking for a code looks the same for known and unknown users. Asking too soon looks the same too.

**Login with a code.** Allowed methods: `EmailOtp`, `PhoneOtp` in `Methods.AllowedLoginMethods`. A user name cannot get a code.

```http
POST /api/auth2/otp/request        (client token, scope auth2.login)
{ "login": "sara@example.com", "purpose": "login" }      ->  202 Accepted

POST /connect/token                (needs AllowSelfUiLogin, like the password grant)
grant_type=urn:systemx:auth2:grant:otp&username=sara@example.com&code=123456&client_id=...&client_secret=...
```

Every failure is `invalid_otp`, and wrong codes count as failed attempts, so too many lead to a lockout. A right code also proves the address, so the email or phone is marked confirmed. The tokens have `amr` = `otp`. The hosted page is `Account/VerifyOtp`. TOTP is not asked after an OTP login (the code already proves the mailbox or phone).

### Confirm email and phone

- Email: the mail has a link to `Account/ConfirmEmail?userId=...&token=...&ClientId=...&ReturnUrl=...`. The API is `POST /api/auth2/email/confirm`. A resend (`POST /api/auth2/email/confirm/resend`) looks the same for everybody.
- Phone: a code by SMS. Ask for a new one with `otp/request` and `purpose=confirm_phone`. Confirm with `POST /api/auth2/phone/confirm`.
- `Email.RequireConfirmedEmail` / `Phone.RequireConfirmedPhone` block the login with `email_not_confirmed` / `phone_not_confirmed`. The error is shown only after the password was right.

### Forgot, reset and change password

- **Forgot** (`/password/forgot`, page `Account/ForgotPassword`) always answers `202`. Only **members** of the application get a message: a link by email, or a code by SMS for a person with a confirmed phone and no email. A person who is not a member must use the join flow.
- **Reset** (`/password/reset`, page `Account/ResetPassword`) takes the token (or the SMS code) and the new password. A weak password does not use up the token. The token works once. A token made in one application does not work in another when credentials are per application (the security stamp is per application).
  After a reset the server clears the lockout, ends all sessions of that application and raises the access version.
- **Change** (`/password/change`, a **user** token with the scope `auth2.me`, page `Manage/ChangePassword`) needs the current password. A wrong current password counts as a failed attempt. All other sessions of this application end; the session that made the call stays.

### Two-factor (authenticator app)

Endpoints for a signed-in user (token scope `auth2.me`), or the pages `Manage/TwoFactor/Enable` and `Manage/TwoFactor/RecoveryCodes`:

1. `POST /api/auth2/2fa/setup` makes a key and returns `sharedKey` (to type) and `authenticatorUri` (an `otpauth://` URI for a QR code). Calling it again before turning it on makes a new key.
2. `POST /api/auth2/2fa/enable` with the first code. It returns the **recovery codes once** (`TwoFactor.RecoveryCodeCount`). From now on a login needs the second step ([above](#two-factor-step)).
3. `POST /api/auth2/2fa/recovery-codes` with a current code makes new recovery codes. The old ones stop working.
4. `POST /api/auth2/2fa/disable` needs the **password**. It is refused (`forbidden`) when `TwoFactor.Mode=Required`.

The key, the on/off state and the recovery codes belong to the application when credentials are per application. The key is encrypted and the recovery codes are hashed.

### Consent

`Sessions.RequireConsent` and `Sessions.ConsentMode` decide:

| `RequireConsent` | `ConsentMode` | Effect |
|---|---|---|
| off | any | No consent page |
| on | `Implicit` or `Explicit` | The user agrees **once** for a set of scopes. The server saves a permanent authorization. More scopes later: ask again |
| on | `Systematic` | The user agrees **every time** |

The authorize endpoint sends the browser to `Account/Consent` with the authorize URL as `ReturnUrl`. **Accept** goes back to that URL with a short-lived `consent` token that is bound to the **user, the application and the exact scopes**. **Deny** returns `error=access_denied` (and the `state`) to the client, but only to a registered redirect URI. `prompt=none` returns `consent_required` to the client. A saved authorization is also what shows up in `Manage/Sessions`. A new login with `SingleActiveSession` revokes it, so the user is asked again.

### Sessions

`Manage/Sessions` lists the sign-ins (OpenIddict authorizations) of the user in this application and ends one of them. Ending a session revokes its tokens. A session of another user, or of another application, is answered like "not found".

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
