# Two-factor authentication and `TwoFactorService`

## What this is

Two-factor authentication (2FA) adds a **second step** to a password sign-in: after the right password the person must also give a six-digit code from an **authenticator app** (Google Authenticator, Microsoft Authenticator, Authy, 1Password and similar). The code changes every 30 seconds and is made from a secret key that only the person's phone and the Server know (TOTP, RFC 6238). If the phone is lost, a **recovery code** (one-time, made when 2FA was turned on) can be used instead.

The parts:

| Part | Where | Job |
|---|---|---|
| `ITwoFactorService` / `TwoFactorService` | `Infrastructure/Auth/TwoFactorConsentAccess.cs` | **Turn 2FA on and off, make the key, make recovery codes.** The enrolment side |
| `ILoginService.CompleteTwoFactorAsync` | `Infrastructure/Auth/LoginService.cs` | **Check the code at sign-in.** The sign-in side |
| The sign-in page `Account/TwoFactor` | `Pages/Account/TwoFactor.cshtml(.cs)` | The second step of the hosted sign-in |
| The profile pages `Manage/TwoFactor/Enable`, `Manage/TwoFactor/RecoveryCodes` | `Pages/Manage/TwoFactor/` | Turn on and off, show and renew recovery codes |
| Four JSON routes `/api/auth2/2fa/...` | `Endpoints/AccountEndpoints.cs` | The same as the pages, for your own UI |
| The token-endpoint grant `urn:systemx:auth2:grant:two_factor` | `Endpoints/ConnectEndpoints.cs` | The second step for the Self UI |
| `POST /users/{id}/reset-2fa` | admin API | An admin turns 2FA off for someone who lost the phone and the codes |
| `ICredentialState` | `Persistence/Identity/CredentialStates.cs` | Where the key, the flag and the recovery codes are stored ([identity-stores.md](identity-stores.md)) |

**What 2FA is not.** The one-time codes sent by email or SMS (`otp/request`, the `EmailOtp` and `PhoneOtp` login methods) are a *way to sign in*, not a second factor, and signing in with them does not ask for the authenticator code. A passkey that was unlocked by the person already counts as several factors. See [Other ways to sign in](#other-ways-to-sign-in).

## The pages, and what each one is for

Two-factor appears in **three different places**. They use the same authenticator app but do different jobs:

| # | When | Page or rule | What it does |
|---|---|---|---|
| 1 | **Signing in** (after the username and password) | `/Account/TwoFactor` | The **second step of the sign-in**. The first step (`/Account/Login`, or the token endpoint of a Self UI) answered "two-factor required"; this page takes the code of the authenticator app or a recovery code and completes the sign-in (`ILoginService.CompleteTwoFactorAsync`). Applications that use the hosted sign-in get it automatically |
| 2 | **A restricted action**, after the person is signed in | `/Account/VerifyTotp`, asked for by the restricted pages | Like GitHub's "sudo mode": deleting the account or turning two-factor off asks for a code **again**, unless the person gave one within `TwoFactor.StepUpMinutes` (default 15). See the next section |
| 3 | **A restricted endpoint of your own application** | `WithTotp()` / `[Auth2Totp]` in the Client | The request carries a fresh code and the Server checks it, every time ([client-endpoint-metadata.md](client-endpoint-metadata.md#ask-for-a-code-of-the-authenticator-app-totp)) |

Do not confuse them with **`/Account/VerifyOtp`**: that page is **not** two-factor. It signs a person in with **one** code sent by **email or SMS** (the one-time-code login, `Methods.AllowedLoginMethods` with `EmailOtp` or `PhoneOtp`), instead of a password. A "TOTP" is the code of an authenticator app (changes every 30 seconds, no message is sent); an "OTP" here is the code the Server sends.

And the pages where a person manages two-factor: `/Manage/TwoFactor/Enable` (turn it on with the first code, show the key and QR code, turn it off) and `/Manage/TwoFactor/RecoveryCodes`.

## Sudo mode: a fresh code before a restricted page

When a person has two-factor on, these Server pages ask for a code again if their last code in this session is older than `TwoFactor.StepUpMinutes`:

- `/Manage/DeleteAccount` (the page and the form)
- turning two-factor off (`/Manage/TwoFactor/Enable`, handler `Disable`)

How it goes: the restricted page redirects to `/Account/VerifyTotp?ReturnUrl=<the page>`; the person types the code; the Server checks it (a code works once; wrong codes count towards the lockout; audited as `2fa.step_up`); the session remembers the time (the claim `step_up_at` of the session cookie) and the person is sent back to the page, which opens. The same session is not asked again inside the window.

- The **second step of the sign-in counts**: a person who just signed in with a code is not asked again at once. So do the first code that turns two-factor on and a right code on the recovery-codes page.
- The time belongs to **this session**: another browser or device is asked on its own.
- A person **without** two-factor is not asked (there is no code to give); require two-factor for admins with `TwoFactor.Mode = Required`.
- `StepUpMinutes = 0` turns the extra question off. The setting is in the project settings page (*Code Valid For Restricted Actions (minutes)*) and is for global admins only.
- Other pages that change the account details (password, email, user name, phone) ask for the current password, as before.
- To protect one more page of the Server: after `LoadSessionAsync()` write `if (await RequireRecentTotpAsync() is { } ask) return ask;` (`ManagePageModel`).
- **Applications** that use the Server's hosted sign-in have **their own** session. Their restricted endpoints use `WithTotp()` / `[Auth2Totp]`: a browser is sent to this Server's `/Account/VerifyTotp` and comes back with a fresh proof ([client-endpoint-metadata.md](client-endpoint-metadata.md#hosted-ui-the-server-asks-for-the-code-on-its-own-page)); an API sends the code in a header.

## When to use it

- You protect accounts that can do harm (admins, finance, support staff): set `TwoFactor.Mode = Required` after your people have enrolled.
- Everybody else: leave the default `Optional` and let people turn it on in their profile.
- You do **not** want a second step at all: `Off`.

## How to enable it

### 1. The project settings

All are in `ProjectSettings.TwoFactor` and are changed by a global admin ([server-settings.md](server-settings.md)):

| Setting | Default | Meaning |
|---|---|---|
| `Mode` | `Optional` | `Off` (never asked, even for people who set it up), `Optional` (people choose), `Required` (everybody needs it). [enums-reference.md](enums-reference.md#twofactormode) |
| `AllowTotp` | `true` | Allows the authenticator app. **Must be on for 2FA to exist at all**: with it off, `setup` and `enable` answer `two_factor_not_allowed` and no second step is asked at sign-in |
| `AllowRecoveryCodes` | `true` | People get recovery codes and may use them at sign-in |
| `RecoveryCodeCount` | `10` | How many recovery codes (1 to 50) |

The settings called `OtpLength`, `OtpLifetimeSeconds`, `OtpMaxAttempts` and `OtpResendSeconds` are in the same section but rule the **email and SMS** codes, not the authenticator code. The authenticator code is always 6 digits and 30 seconds (the standard, and what the QR code says). `TwoFactor.AllowPasskeys` is not read by any code today.

```json
"twoFactor": { "mode": "Optional", "allowTotp": true, "allowRecoveryCodes": true, "recoveryCodeCount": 10 }
```

### 2. Let people turn it on

Nothing else is needed: the profile page `/Manage` shows **Set up 2FA** when `Mode` is not `Off` and `AllowTotp` is on. Your own UI can use the routes below.

### 3. Roll out `Required` safely

`Required` means a person **without** 2FA cannot sign in at all (`two_factor_setup_required`), and the routes that set it up need a signed-in session. So **enrol first, then require**:

1. Keep `Mode = Optional`. Ask people to set up 2FA (an email, a banner in your app, the profile page).
2. Check who has done it (admin users page, or `GET /users/{id}`).
3. Switch `Mode = Required`. People who are not enrolled are told `two_factor_setup_required`; an admin can lower `Mode` to `Optional` for a moment so that they can sign in and enrol.

## How to use it: the enrolment

Every route needs the token of a **signed-in person** (scope `auth2.me`) and works on the current project. They are what `TwoFactorService` offers.

| Route | Body | Answer | What happens |
|---|---|---|---|
| `POST /api/auth2/2fa/setup` | none | `{ sharedKey, authenticatorUri }` | Makes a **new key** and returns it. Show `authenticatorUri` as a QR code and `sharedKey` as text (grouped by four) |
| `POST /api/auth2/2fa/enable` | `{ "code": "123456" }` | `{ recoveryCodes: [...] }` | Checks the first code, turns 2FA on, makes the recovery codes (shown **once**) |
| `POST /api/auth2/2fa/recovery-codes` | `{ "code": "123456" }` | `{ codes: [...] }` | Needs a **current** authenticator code. The old recovery codes stop working |
| `POST /api/auth2/2fa/disable` | `{ "password": "..." }` | `204` | Needs the **password**. Refused (`forbidden`) when `Mode = Required` |

In the browser: `/Manage/TwoFactor/Enable` (shows the QR code, takes the first code, shows the recovery codes once, and has the disable form) and `/Manage/TwoFactor/RecoveryCodes`.

A walk-through with plain HTTP (a signed-in user's token in `$TOKEN`):

```http
POST /api/auth2/2fa/setup            Authorization: Bearer $TOKEN
-> 200 { "sharedKey": "abcd efgh ijkl mnop qrst uvwx yz23 4567",
         "authenticatorUri": "otpauth://totp/PaperTrade:sara@example.com?secret=ABCDEFGHIJKLMNOPQRSTUVWXYZ234567&issuer=PaperTrade&digits=6" }

   (the person scans the QR code; the app shows 123456)

POST /api/auth2/2fa/enable           { "code": "123456" }
-> 200 { "recoveryCodes": [ "A1B2C-D3E4F", "..." ] }       <- store these now; they are never shown again
```

The issuer text in the app is the project's `Localization.BrandName`, or the project name. A space or dash in the code is ignored (`123 456` and `123-456` work).

### What `TwoFactorService` does, method by method

All four check the project settings first and answer with a stable error code ([error-codes.md](error-codes.md)).

**`SetupAsync(user)`**

1. `AllowTotp` off gives `two_factor_not_allowed`.
2. 2FA already on gives `conflict` (turn it off first).
3. Makes a **new** authenticator key every time it is called, until 2FA is turned on (so an old QR code stops working). It builds the `otpauth://totp/...` address.
4. Note: the profile page calls `Setup` when it is **opened**, so opening the page in two tabs makes two keys; use the code from the QR code you scanned last.

**`EnableAsync(user, code)`**

1. `AllowTotp` off: `two_factor_not_allowed`. Already enabled, or no key was made yet: `conflict`. Locked out: `locked_out`.
2. Verifies the code with the authenticator token provider. A wrong code counts as a failed attempt (toward lockout) and answers `invalid_otp`.
3. Turns 2FA on, makes `RecoveryCodeCount` recovery codes (when `AllowRecoveryCodes`), raises the person's **access version**, writes the audit event `2fa.enabled`.

**`DisableAsync(user, password)`**

1. `Mode = Required`: `forbidden`. 2FA not on: `two_factor_not_enabled`. Locked out: `locked_out`.
2. Checks the password; a wrong one counts as a failed attempt and answers `invalid_credentials`. (A person who has no password, for example an external-only person, cannot use this route; an admin resets 2FA for them.)
3. Turns 2FA off, **removes the key and the recovery codes**, saves, changes the **security stamp** (other cookies are signed out), raises the access version, writes `2fa.disabled`.

**`RegenerateRecoveryCodesAsync(user, code)`**

1. `AllowRecoveryCodes` off: `forbidden`. 2FA not on: `two_factor_not_enabled`. Locked out: `locked_out`.
2. A wrong authenticator code: failed attempt, `invalid_otp`.
3. Makes `RecoveryCodeCount` new codes (the old ones stop working) and writes `2fa.recovery_codes_regenerated`.

## How to use it: the sign-in

The first step is the normal password login. When the person has 2FA on (and `Mode` is not `Off`, and `AllowTotp` is on) the Server does **not** finish the login; it answers `two_factor_required` together with a **two-factor token**. The token is sealed with Data Protection, valid **5 minutes**, and made for **this person and this application only** (it fails in another application).

### The hosted pages

`/Account/Login` then goes to `/Account/TwoFactor`, which asks for the code (and offers "use a recovery code" when `AllowRecoveryCodes`). A wrong code counts toward the lockout; too many redirect to `/Account/Lockout`; the right code signs the person in and goes on to `Redirects.RedirectAfterLogin` or the return URL. Nothing to code.

### The Self UI (the token endpoint)

For an application with `AllowSelfUiLogin` on ([client-setup.md](client-setup.md#self-ui)):

```http
POST /connect/token   grant_type=password&username=sara&password=...&client_id=...&client_secret=...&scope=openid auth2.me
-> 400 { "error": "two_factor_required", "two_factor_token": "CfDJ8..." }

POST /connect/token   grant_type=urn:systemx:auth2:grant:two_factor&two_factor_token=CfDJ8...&code=123456&client_id=...&client_secret=...
-> 200 { "access_token": "...", "refresh_token": "..." }          (or recovery_code=A1B2C-D3E4F instead of code)
```

With the Client package you never write that:

```csharp
var result = await web.LoginAsync(new LoginRequest(model.Login, model.Password), model.RememberMe, ct);
if (result.IsSuccess && result.Value!.TwoFactorRequired)
{
    // no cookie yet: show your code form and keep result.Value.TwoFactorToken (a hidden field is fine; it lasts 5 minutes)
}

// when the person sends the code:
var second = await web.TwoFactorLoginAsync(new TwoFactorRequest(token, Code: code), model.RememberMe, ct);
// or: new TwoFactorRequest(token, RecoveryCode: recoveryCode)
```

`IAuth2ApiClientService.LoginAsync` and `TwoFactorLoginAsync` do the same without a cookie. The Client also wraps the setup routes: `SetupTwoFactorAsync`, `EnableTwoFactorAsync`, `DisableTwoFactorAsync` and `RegenerateRecoveryCodesAsync` ([client-api-service.md](client-api-service.md#the-signed-in-person-user-token-scope-auth2me)). The sign-in is complete only when the second call succeeds; the tokens or the cookie are made then ([client-reference.md](client-reference.md#7-browser-sign-in-iauth2webclientservice)).

### What the second step checks (`CompleteTwoFactorAsync`)

In order: the token is valid and for this application (else `invalid_token`); the project and application are enabled (else `access_denied`); the person is not locked out (`locked_out`); then **either** the authenticator `code` **or** a `recovery_code` (only when `AllowRecoveryCodes`) is verified. A wrong code or a recovery code that was used already counts as a failed attempt and answers `invalid_otp`. On success the failed counter is reset, other sessions are revoked when `Sessions.SingleActiveSession` is on, and the tokens carry `amr = pwd, otp`. A **recovery code works once**: it is removed when used.

## Other ways to sign in

| Way | Is the authenticator step asked? |
|---|---|
| Password (hosted page, Self UI, token endpoint) | **Yes**, when 2FA is on |
| Email or SMS code (`EmailOtp`, `PhoneOtp`) | No: the code is the sign-in. If you need both, do not offer these methods to those people |
| External provider (Google, SAML, ...) | **Yes** (the person has a local second factor), **unless** the provider has `TrustProviderMfa` on and the provider did not report a single-factor sign-in. Then the provider's own multi-factor replaces it ([external-identity.md](external-identity.md)) |
| Directory (LDAP / Active Directory) | Yes, with the same `TrustProviderMfa` rule |
| Passkey | **No** when the passkey was unlocked by the person (user verification, the default): it already counts as several factors. A passkey **without** verification is asked ([passkeys.md](external-identity/passkeys.md)) |

And `Mode = Required` with a person who has not enrolled gives `two_factor_setup_required` on the password, external and directory paths.

## The `amr` claim after the second step

A password plus the authenticator code gives `amr = pwd, otp` (two claims). The value `mfa` is added by **passkeys** (user-verified) and by **external providers** that report several factors, not by a local password plus TOTP. To require "a stronger sign-in" in your code, test for `otp` or `mfa`:

```csharp
bool strong = client.Amr.Contains("mfa") || client.Amr.Contains("otp");
```

(`ICurrentClient.Amr`, [client-current-user.md](client-current-user.md).)

## Where the data is stored

| Data | Per-project credentials (default) | Shared credentials, single mode |
|---|---|---|
| The on/off flag | `user_projects.two_factor_enabled` | `users.two_factor_enabled` |
| The key | `user_projects.authenticator_key`, **encrypted** (Data Protection) | `user_tokens` (`[Auth2]`, `AuthenticatorKey`), encrypted |
| Recovery codes | `user_project_recovery_codes` (hash, `used_at_utc`) | `user_tokens` (`RecoveryCodeHashes`, hashes joined by `;`) |

With per-project credentials the same person has **separate** 2FA in each project: turning it on in project A does not ask for it in project B (test `Login_TwoFactorEnabledInA_IsNotAskedInB`). Details: [identity-stores.md](identity-stores.md#how-credentials-are-stored), [credential-scope.md](credential-scope.md), [database-tables.md](database-tables.md).

## A fresh code before a sensitive action (step-up)

Besides the sign-in, an application can ask for a code of the authenticator app **again** for one action, with `WithTotp()` / `[Auth2Totp]` ([client-endpoint-metadata.md](client-endpoint-metadata.md#ask-for-a-code-of-the-authenticator-app-totp)). The Client sends the code to `POST /api/auth2/2fa/verify` (`ITwoFactorService.VerifyAsync`). The rules:

- the person has two-factor on: otherwise `totp_setup_required` (the action needs it), or success when the request says `optional` (`WithOptionalTotp()`: there is nothing to check); `TwoFactor.AllowTotp` is treated the same way;
- no code from a person who has two-factor: `totp_required` (not counted as a wrong attempt);
- a code can be used **once** (kept in the replay cache for three minutes, shared by all instances); the second use answers `invalid_otp` and is audited as `2fa.step_up_replayed`;
- a wrong code calls `AccessFailedAsync`, so guesses count towards the lockout like wrong passwords (`2fa.step_up_failed`); a right code resets the count (`2fa.step_up`);
- a locked-out person gets `locked_out`; recovery codes are not accepted;
- the route is in the rate-limit policy `otp`.

The hosted page for the same check is **`/Account/VerifyTotp`** ([ui-handover.md](ui-handover.md#account-change-pages-signed-in)): a signed-in person types the code, with the same rules, and goes on to an allowed `ReturnUrl` (or sees "your code was accepted"). The hosted UI of the Client package links to it for you: `/connect/authorize` with `acr_values=urn:systemx:auth2:acr:totp` (`Auth2AcrValues.Totp`) shows it when the session has no code of the last minutes, and the tokens that come back carry `step_up_at`. Link to it from your own flows when you want the Server's page to ask for the code; `WithTotp()` / `[Auth2Totp]` is the form for checking a code **per request** in your application.

## Lockout, rate limits and audit

- A wrong authenticator code, a wrong recovery code, a wrong current password (disable) and a wrong first code (enable) all count as **failed attempts** toward the project's lockout ([server-settings.md](server-settings.md#lockout)).
- The sign-in pages and the two-factor grant use the rate-limit policy `login` (default 10 per minute per address and login); the 2FA routes under `/api/auth2` are protected by the signed-in token ([security.md](security.md)).
- Audit events: `2fa.enabled`, `2fa.disabled`, `2fa.recovery_codes_regenerated`, `user.2fa_reset`, and `login.failed` with `step = two_factor` for a wrong second step. They never contain the code, the key or a recovery code.

## The admin: someone lost the phone and the codes

```http
POST /api/auth2/admin/projects/{projectId}/users/{userId}/reset-2fa
```

(also as the bulk action `reset-2fa` of the users page). A project admin of that project or a global admin may call it. It turns 2FA **off**, replaces the key, removes the recovery codes, **revokes the person's sessions in that project**, raises the access version and writes `user.2fa_reset`. The person can then sign in with the password and set 2FA up again ([admin-api.md](admin-api.md)). Check the person's identity first: this route removes a security layer.

## Error codes

| Code | Meaning |
|---|---|
| `two_factor_required` | The password was right and a second step is needed (answer of step one; carries the two-factor token) |
| `two_factor_setup_required` | `Mode = Required` and the person has not set up 2FA |
| `two_factor_not_allowed` | `AllowTotp` is off |
| `two_factor_not_enabled` | The person has no 2FA (disable, new recovery codes) |
| `totp_required` | A code of the authenticator app is needed for this action and none was sent (`/2fa/verify`, `WithTotp()`) |
| `totp_setup_required` | The action needs two-factor (`WithTotp()`) and the person has not turned it on |
| `invalid_otp` | A wrong or used code |
| `invalid_token` | The two-factor token is expired, damaged or for another application |
| `conflict` | 2FA is already on (setup, enable), or no key was made yet (enable) |
| `forbidden` | Disable while `Required`, or recovery codes while `AllowRecoveryCodes` is off |
| `locked_out`, `invalid_credentials` | Lockout is active; the password for disable was wrong |

## Common problems

| Problem | Cause and fix |
|---|---|
| `two_factor_setup_required` for a person who just registered | `Mode = Required`. Enrol first with `Optional`, or lower the mode for a moment |
| The code is always wrong | The phone clock is wrong (TOTP depends on time: switch on automatic time), the key was renewed by opening the setup page again, or the wrong account entry was used |
| "I lost my phone" | A recovery code, or an admin `reset-2fa` |
| `invalid_token` on the second step | More than 5 minutes passed, or the token is for another application; start the sign-in again |
| Disable says `forbidden` | The project requires 2FA. Lower `Mode` first |
| Disable says `invalid_credentials` for an external-only person | They have no password to give; an admin resets 2FA |
| Codes work in one project but 2FA is not asked in another | Per-project credentials: 2FA is per project |

## How to implement or extend

- `ITwoFactorService` (`Application/AccountAbstractions.cs`) has `SetupAsync`, `EnableAsync`, `DisableAsync` and `RegenerateRecoveryCodesAsync`; `TwoFactorService` implements it on `UserManager<AppUser>` (the Identity authenticator token provider, `GenerateNewTwoFactorRecoveryCodesAsync`) and on `ICredentialState`. It is registered as a scoped service; replace it only if you change enrolment.
- To add **another second factor** (for example an SMS code as a second step): add the check next to the `VerifyTwoFactorTokenAsync` call in `LoginService.CompleteTwoFactorAsync`, store its state in `ICredentialState` (both strategies), and offer the enrolment from `TwoFactorService`.
- Tests: `LoginServiceTests` (the required step, completing with a code or a recovery code, token bound to its application, `Required` without enrolment, `Off` ignores TOTP), `OtpLoginAndTwoFactorApiTests` (the four routes, disable refused when required), `TokenEndpointTests` (the grant, not available without Self UI login), `HostedFlowTests` (the page), `ConsentAndManageTests` (the profile page), `ExternalLoginTests` and `PasskeyTests` (provider MFA and verified passkeys skip the local step), `AdminApiTests.ResetTwoFactor_TurnsItOff`.

## Related pages

- [authentication-flows.md](authentication-flows.md)
- [identity-stores.md](identity-stores.md)
- [credential-scope.md](credential-scope.md)
- [server-settings.md](server-settings.md)
- [admin-api.md](admin-api.md)
- [client-setup.md](client-setup.md)
- [error-codes.md](error-codes.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Contracts reference](contracts-reference.md)
- [Password history and password expiry](password-history.md)
- [Decisions (ADR list)](decisions.md)
- [API reference](api-reference.md)
- [Localization](localization.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
