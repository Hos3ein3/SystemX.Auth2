# Calling the Server: `IAuth2ApiClientService`

## What this is

`IAuth2ApiClientService` is the one service of the Client package that **calls the Auth2 Server's JSON API and token endpoint** for you: register, sign in, confirm an email or phone, reset or change a password, two-factor, change the own user name, email address and phone number, `/me`, handshake, and the catalogs (permissions, roles, groups, scopes). It is a **singleton**: register once, inject anywhere (controllers, Minimal API endpoints, background services).

Two rules hold for every method:

- **It returns a `Result`, it does not throw for a failed call.** A wrong password, a locked account, a weak new password and a Server that is down are all normal answers: `result.IsSuccess`, `result.Value`, `result.Error!.Code` (a stable code, see [error-codes.md](error-codes.md)) and `result.Error.Message` (written in the language of the call, see [Language](#language-of-the-calls)).
- **It does not keep anything.** It makes no cookie, stores no token and remembers no user. A login method returns the tokens and that is all.

## When to use it

- Your **API or worker** needs to register people, sign them in with a password, or push the catalogs.
- Your **own forms** (Self UI) need the Server to check a password; for a cookie you use `IAuth2WebService`, which calls this service and then signs the cookie in ([below](#cookies-who-puts-the-user-into-the-cookie)).
- Your application shows "my account" screens (change password, user name, email, phone, two-factor) on top of the Server's API.

If you use the **hosted UI** (the Server draws the pages) you usually need none of the sign-in methods: the Server does the sign-in and your app only receives the result ([client-setup.md](client-setup.md)).

## Cookies: who puts the user into the cookie?

**This service never does, and it does not decide.** Nothing in it knows whether your application uses cookies. `LoginAsync` returns a `LoginResponse` (access token, refresh token, id token) and `RegisterAsync` returns a `RegisterResponse` (no token at all). What you do next is your choice, by **which service you call**:

| Your application | Sign in with | Where the cookie comes from |
|---|---|---|
| **API** (bearer tokens) | `IAuth2ApiClientService.LoginAsync` | No cookie. You give the tokens to your caller |
| **Cookie app, hosted UI** (`UseHostedUi()`) | You do not call a login method. `IAuth2WebService.ChallengeAsync` sends the browser to the Server | The OpenID Connect callback of the package makes the cookie |
| **Cookie app, your own forms** (`UseSelfUi()`) | `IAuth2WebService.LoginAsync(HttpContext, request, isPersistent)` | `IAuth2WebService` calls `IAuth2ApiClientService.LoginAsync`, then signs the cookie in (and keeps the tokens in it when `SaveTokens` is on) |

The same holds for the other two sign-in steps: `IAuth2WebService.TwoFactorLoginAsync` and `OtpLoginAsync` make the cookie; the methods of this service with the same names do not.

**After a registration.** `RegisterAsync` creates the person (or adds an existing one to this project) and answers `RegisterResponse(UserId, UserWasCreated, JoinedProject, ConfirmationRequired)`. It **never signs anybody in**, for any kind of application: the person may still have to confirm an email or phone (`ConfirmationRequired`), and registering is done with the application's own client token, not with the person's token. A cookie app that wants "register, then be signed in" does it in two calls:

```csharp
app.MapPost("/register", async (HttpContext http, RegisterForm form, IAuth2ApiClientService api, IAuth2WebService web, CancellationToken ct) =>
{
    var registered = await api.RegisterAsync(new RegisterRequest(form.UserName, form.Email, null, form.Password), ct);
    if (!registered.IsSuccess) return Results.BadRequest(registered.Error!.Code);
    if (registered.Value!.ConfirmationRequired) return Results.Ok(new { confirm = true });     // the person must confirm first

    var login = await web.LoginAsync(http, new LoginRequest(form.UserName, form.Password), isPersistent: false, ct);   // this makes the cookie
    return login.IsSuccess ? Results.Redirect("/") : Results.BadRequest(login.Error!.Code);
});
```

(An API application does the same with `api.LoginAsync` and returns the tokens.)

**Using the account methods in a cookie app.** The "change my details" methods need the person's **access token**. When the cookie keeps the tokens (`SaveTokens`, on by default), take it from `IAuth2AccessTokenAccessor`:

```csharp
app.MapPost("/account/user-name", async (ChangeNameForm form, IAuth2ApiClientService api, IAuth2AccessTokenAccessor tokens, CancellationToken ct) =>
{
    var token = await tokens.GetAccessTokenAsync(ct);           // the bearer header, or the token saved in the cookie
    if (token is null) return Results.Unauthorized();
    var result = await api.ChangeUserNameAsync(token, new ChangeUserNameRequest(form.NewUserName, form.CurrentPassword), ct);
    return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { result.Error!.Code, result.Error.Message });
});
```

The access token in the cookie is **not renewed**: it lives as long as an access token (15 minutes by default). When it has expired the call answers `invalid_token`; renew it with `RefreshAsync` (and the refresh token from the cookie) or ask the person to sign in again. See [client-current-user.md](client-current-user.md#cookie-sessions-hosted-ui-and-self-ui).

## The methods

Which token each call uses:

- **Client token** (a client-credentials token of *your application*, made and cached for you; the scope is listed). The call is about a person but your application is the caller.
- **Token endpoint** (`/connect/token` and `/connect/revoke`, with your client id and secret). These return tokens.
- **User token** (the access token of the person, with the scope `auth2.me`; you pass it as the first argument).

### Accounts

| Method | Server route | Token | Body → answer | Notes |
|---|---|---|---|---|
| `RegisterAsync(RegisterRequest)` | `POST /api/auth2/register` | client, `auth2.register` | → `RegisterResponse` | Sends the confirmation email or SMS when the settings say so. Idempotent for the same person ([authentication-flows.md](authentication-flows.md#register)) |
| `CompleteJoinAsync(JoinCompleteRequest)` | `POST /api/auth2/join/complete` | client, `auth2.register` | → `Result` | Finishes joining this project for a person who has an account elsewhere: the code that proves ownership and the new password |
| `RequestOtpAsync(OtpRequest)` | `POST /api/auth2/otp/request` | client, `auth2.login` | → `Result` | Purpose `login` or `confirm_phone`. The answer is always the same |
| `ForgotPasswordAsync(ForgotPasswordRequest)` | `POST /api/auth2/password/forgot` | client, `auth2.login` | → `Result` | Always the same answer |
| `ResetPasswordAsync(ResetPasswordRequest)` | `POST /api/auth2/password/reset` | client, `auth2.login` | → `Result` | The token of the email, or the code of the SMS |
| `ConfirmEmailAsync(ConfirmEmailRequest)` | `POST /api/auth2/email/confirm` | client, `auth2.login` | → `Result` | User id and the token of the link. `invalid_token` when it is wrong or used |
| `ResendEmailConfirmationAsync(ResendEmailConfirmationRequest)` | `POST /api/auth2/email/confirm/resend` | client, `auth2.login` | → `Result` | Always the same answer |
| `ConfirmPhoneAsync(ConfirmPhoneRequest)` | `POST /api/auth2/phone/confirm` | client, `auth2.login` | → `Result` | Login and the code of the SMS. `invalid_otp` when it is wrong |

### Sign in and out (token endpoint)

| Method | Grant | Answer | Notes |
|---|---|---|---|
| `LoginAsync(LoginRequest)` | `password` | `LoginResponse` | The Server must allow it (`AllowSelfUiLogin` on the application). When two-factor is needed the result **succeeds** with `TwoFactorRequired = true` and a `TwoFactorToken`: call `TwoFactorLoginAsync` next |
| `TwoFactorLoginAsync(TwoFactorRequest)` | `two_factor` | `LoginResponse` | The authenticator code **or** a recovery code |
| `OtpLoginAsync(login, code)` | `otp` | `LoginResponse` | After `RequestOtpAsync` |
| `RefreshAsync(refreshToken)` | `refresh_token` | `LoginResponse` | A refresh token works once; keep the new one. Using an old one again ends the whole chain |
| `LogoutAsync(refreshToken)` | revoke | `Result` | Revokes the refresh token |

### The signed-in person (user token, scope `auth2.me`)

| Method | Server route | Answer | Notes |
|---|---|---|---|
| `GetMeAsync(accessToken)` | `GET /api/auth2/me` | `CurrentUserInfo` | The person with roles, permissions and groups |
| `ChangePasswordAsync(accessToken, ChangePasswordRequest)` | `POST /api/auth2/password/change` | `Result` | Other sessions end. With `Password.SignOutAfterChangePassword` this one ends too ([server-settings.md](server-settings.md)) |
| `ChangeUserNameAsync(accessToken, ChangeUserNameRequest)` | `POST /api/auth2/user-name/change` | `Result` | At once. Needs the current password when the person has one |
| `RequestEmailChangeAsync(accessToken, ChangeEmailRequest)` | `POST /api/auth2/email/change` | `Result` | A link goes to the **new** address. **Nothing changes yet** |
| `ConfirmEmailChangeAsync(accessToken, ConfirmEmailChangeRequest)` | `POST /api/auth2/email/change/confirm` | `Result` | The new address and the token of the link. The address is replaced and confirmed |
| `RequestPhoneChangeAsync(accessToken, ChangePhoneNumberRequest)` | `POST /api/auth2/phone/change` | `Result` | An SMS code goes to the **new** number. **Nothing changes yet** |
| `ConfirmPhoneChangeAsync(accessToken, ConfirmPhoneChangeRequest)` | `POST /api/auth2/phone/change/confirm` | `Result` | The new number and the code. The number is replaced and confirmed |
| `SetupTwoFactorAsync(accessToken)` | `POST /api/auth2/2fa/setup` | `TwoFactorSetupResponse` | Key and `otpauth://` URI for the authenticator app |
| `EnableTwoFactorAsync(accessToken, TwoFactorEnableRequest)` | `POST /api/auth2/2fa/enable` | `TwoFactorEnableResponse` | The first code of the app. The recovery codes are shown **once** |
| `DisableTwoFactorAsync(accessToken, TwoFactorDisableRequest)` | `POST /api/auth2/2fa/disable` | `Result` | The password. Refused when the application requires two-factor |
| `RegenerateRecoveryCodesAsync(accessToken, RecoveryCodesRequest)` | `POST /api/auth2/2fa/recovery-codes` | `RecoveryCodesResponse` | A code of the app. The old codes stop working |

Every one of these needs the **current password** when the person has a password (a person who only signs in with an external account or a passkey has none). A wrong password counts as a failed attempt, like a sign-in: guessing it locks the account (`locked_out`). The new value is checked with the same rules as a sign-up (format, `UserName.*`, `Email.AllowedDomains` and `BlockedDomains`, `Phone.PhoneNumberRegex`, and "already taken") **before** anything is sent, so no email or SMS goes to a value that would be refused. What the Server does in each step is described with the pages that do the same: [ui-handover.md](ui-handover.md#account-change-pages-signed-in).

**Changing an email address, end to end.** The link in the email opens a Server page (`/Account/ChangeEmailAddress?handler=Confirm&...`) and finishes the change there. If your application wants to finish it itself (your own screen), read `email` and `token` from the link and call `ConfirmEmailChangeAsync`:

```csharp
// step 1: the person types the new address
var sent = await api.RequestEmailChangeAsync(token, new ChangeEmailRequest("new@example.com", currentPassword), ct);   // answer 202: the mail is on its way

// step 2: your page receives ?email=...&token=... from the link (use the ReturnUrl of the request to bring the person back to your site)
var done = await api.ConfirmEmailChangeAsync(token, new ConfirmEmailChangeRequest(email, emailToken), ct);
```

The phone works the same: `RequestPhoneChangeAsync` sends the code, the person types it, `ConfirmPhoneChangeAsync(token, new ConfirmPhoneChangeRequest(number, code))` finishes. The code works once and only for that number; a second request too soon answers `rate_limited`.

### Handshake and catalogs (client token)

| Method | Scope | Notes |
|---|---|---|
| `HandshakeAsync()` | `auth2.client.handshake` | Makes a handshake now and updates `Auth2ClientState` ([handshake-and-startup.md](handshake-and-startup.md)) |
| `SyncPermissionsAsync`, `SyncRolesAsync`, `SyncGroupsAsync`, `SyncScopesAsync` | `auth2.permissions.sync`, `auth2.roles.sync`, `auth2.groups.sync`, `auth2.scopes.sync` | Push. Never deletes ([permission-sync.md](permission-sync.md)) |
| `GetPermissionsAsync`, `GetRolesAsync`, `GetScopesAsync` | the sync scope | Pull |
| `GetGroupsAsync` | `auth2.groups.read` | Pull |
| `GetExternalProvidersAsync` | `auth2.client.handshake` | The sign-in providers of this application (name, display name) |

## Language of the calls

The Server writes every answer in the language of the call: the `message` of an error, the email and the SMS that the call causes. The **code never changes** with the language, so branch on `Error.Code`, show `Error.Message`.

**What the Client sends.** Every call carries `Accept-Language`:

1. the language chosen with **`api.WithCulture("fa")`** (for the calls made through the returned instance), else
2. the language of the **current request** (`CultureInfo.CurrentUICulture`), unless `Auth2ClientOptions.SendRequestCulture` is `false`.

```csharp
// every call in the language of the current request (ASP.NET Core sets CurrentUICulture with app.UseRequestLocalization(...))
var result = await api.LoginAsync(new LoginRequest("sara", password));

// or choose it
var persian = await api.WithCulture("fa").RequestEmailChangeAsync(token, new ChangeEmailRequest("new@example.com", password));
// the email arrives in Persian, and a failure says why in Persian: result.Error.Message

var persianApi = api.WithCulture("fa");        // use this instance for several calls
var again = persianApi.WithCulture(null);      // null or empty = the language of the current request again
```

`WithCulture` changes nothing on the service you call it on: it returns another instance, so it is safe in a singleton and in parallel requests. A value that is not a language tag (letters, digits, `-`) is not sent. A host that does not set a request culture sends the culture of the process; the invariant culture sends nothing.

**What the Server does with it.** The Server picks the first supported language in this order: `ui_locales` or `culture` in the query, the saved culture of a signed-in person, the culture cookie, **`Accept-Language`**, the application's default, English ([localization.md](localization.md#how-the-culture-is-chosen)). The Server has `en`, `fa`, `nl` and `it`; an application can limit them with `Localization.SupportedCultures`. Notes:

- It applies to **every** route: the JSON API (`message` of the error), and the **token endpoint** (`error_description` of a failed sign-in). The `error`, `error_code` and the status code stay the same.
- The token endpoint cannot know the application's default language before it has read the form, so there the `Accept-Language` header (or `ui_locales`) decides.
- The email and SMS of a call are written in the **saved culture of the person** when there is one, otherwise in the language of the call. A new email address or phone number of a change is written in the language of the person too.
- A text the Server has no translation for falls back to English.

## Errors and retries

- `Result.Error.Code` is a code of [error-codes.md](error-codes.md) or one of the Client's own (`Auth2ClientErrorCodes`): `server_unavailable` (the Server cannot be reached or answered `5xx`), `invalid_response`.
- **Calls that change something are never repeated by themselves** (register, login, password change, confirmations, two-factor, the change methods): a repeated wrong password would count twice towards the lockout, and a refresh token works once. Only reads and the idempotent syncs are retried, and only when you added `.EnableResilience()` ([client-builder.md](client-builder.md)).
- A client token that the Server refused (revoked, secret rotated) is replaced once and the **safe** call is repeated; a user token is never replaced for you.
- The time limit of one call is `RequestTimeout` (30 seconds).

## How to implement or extend

- **In a test of your own code**, replace the service: `services.AddSingleton<IAuth2ApiClientService>(fake)` (it is an interface; `Result` and `Result<T>` have static `Success` and `Failure` factories).
- **A route the package does not wrap yet:** call it with an `HttpClient` from `IHttpClientFactory` named `Auth2ClientDefaults.HttpClientName` and a token from `IAuth2MachineTokenProvider` (client token) or the person's access token; build the path from `Auth2Routes` and the body from the `Auth2.Contracts` records ([machine-to-machine.md](machine-to-machine.md), [contracts-reference.md](contracts-reference.md)).
- **Calling the Server without the Client package** is the same HTTP: [api-reference.md](api-reference.md).

## Common problems

| Problem | Cause |
|---|---|
| I called `RegisterAsync` and the person is not signed in | By design. Sign in with `IAuth2WebService.LoginAsync` (cookie) or `IAuth2ApiClientService.LoginAsync` (tokens), after the person has confirmed what is needed ([above](#cookies-who-puts-the-user-into-the-cookie)) |
| `LoginAsync` works but there is no cookie | `IAuth2ApiClientService` never makes a cookie. Use `IAuth2WebService.LoginAsync(HttpContext, ...)` |
| `invalid_token` from a change method | The access token is missing, expired or has no `auth2.me` scope. Ask for `auth2.me` at sign-in and renew the token |
| `forbidden` from a change method | You passed a client token. These methods need the **person's** token |
| `invalid_credentials` from a change method | The current password is wrong (it counts as a failed attempt). A person without a password sends none |
| The email or SMS of a change arrives in English | The Server has no translation, the application does not list the language in `SupportedCultures`, or the call carried no language (`SendRequestCulture` off and no `WithCulture`) |
| I always get English messages | Your host does not set the request culture, so `CurrentUICulture` is the process culture. Add `app.UseRequestLocalization(...)` or use `WithCulture` |

## Related pages

- [Client setup](client-setup.md)
- [Client package reference](client-reference.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Documentation index](index.md)
