# UI handover

Every page of the Server is a Razor Page. The page model holds the logic and the public properties; the `.cshtml` is the HTML (Tailwind CSS, strict content security policy, no inline script or style).
**Not drawn yet:** the sign-in page (`/Account/Login`) has no provider buttons and no passkey button, and `/Account/JoinProject` is still a placeholder. The external sign-in pages (`/Account/External/*`), `/Manage/ExternalLogins` and `/Manage/Passkeys` have views. This page lists what a UI needs to build the missing parts, or what your own page must call. Other pages are described in [authentication-flows.md](authentication-flows.md).

## How a page works

- A page finds its application from `ClientId` (query or form) or from the `client_id` inside `ReturnUrl`; the application knows its project. In single-project mode the project needs no client id. A page model exposes `Project`, `Client` (the calling application) and `Effective` (project settings with the application overrides). The redirect URIs that `ReturnUrl` is checked against are the ones of the application.
- Forms are posted with an anti-forgery token (`__RequestVerificationToken`). The UI must include it.
- On an error the page sets the status code (usually 400) and exposes `ErrorCode` (a stable code, see [error-codes.md](error-codes.md)) and `ErrorMessage` (localized).
- Public properties of a page model are the data the UI shows.

## Inputs and rules come from the project settings

The sign-in and sign-up pages do **not** have fixed inputs. What they draw and check comes from the settings saved in the database (`Methods`, `UserName`, `Email`, `Phone`, `Password`), through `AccountFormRules` (`Pages/Account/AccountFormRules.cs`):

| Page | What follows the settings |
|---|---|
| **Login** | The one identifier input: its label, type, keyboard and `autocomplete` come from `Methods.AllowedLoginMethods` (password methods only): user name -> "Username" (text), email -> "Email" (`type=email`), phone -> "Phone number" (`type=tel`), and the combinations ("Username or email", "Email or phone number", "Username, email or phone number"). The value must fit **one** accepted kind while typing: a user name follows `UserName.MinLength`, `MaxLength` and `AllowedCharacters` (every account has such a user name, an email or a phone number is the user name when none was chosen), an email needs an email format, a phone number must match `Phone.PhoneNumberRegex`. When it fits none, the message is the one of the kind it looks like (`@` is an email, digits with an optional `+` a phone number, anything else a user name), for example "Username or email must be at least 3 characters." The email domain lists are not checked on sign-in (they apply when an address is added). The password follows the same saved rules as when it is chosen (`Password.MinLength`, `MaxLength`, `RequireDigit`, `RequireLower`, `RequireUpper`, `RequireNonAlphanumeric`, `UniqueChars`), told one message at a time without the checklist. A person whose password was chosen under older, weaker rules cannot send the form from this page until it follows the current rules (they can still use the forgot-password flow). |
| **Register** | The email, user name and phone inputs that `Methods.AllowedRegisterMethods` allows. When there is only one way to register that input is required; with several, one is enough, so none is marked required alone. User name: `UserName.MinLength`, `MaxLength`, `AllowedCharacters`. Email: format and `Email.AllowedDomains` / `BlockedDomains`. Phone: `Phone.PhoneNumberRegex`. Password: a live list of the password rules that turns green as the person types (`Password.MinLength`, `MaxLength`, `RequireDigit`, `RequireLower`, `RequireUpper`, `RequireNonAlphanumeric`, `UniqueChars`); the repeat field must equal it. |
| **Forgot password, Join** | The same identifier input and the same rules, built from the password **and** one-time-code methods. |
| **Change password, Reset password, Join** | The password rule list and the required fields. |
| **Change user name, email, phone** | The user name, email (with domains) and phone rules above. |

**How it reaches the browser.** The page writes the rules on the input as `data-a2-rule` (JSON), with the messages already in the language of the page (the same texts as the server errors: `Errors` and `Validation` resources). `wwwroot/js/a2.js` reads them and shows a message under the input when the person leaves it (and while typing once it has been touched), blocks the submit with the first message, and updates the password list on every key. The layout needs nothing extra: every page already loads `a2.js`.

**The browser is a convenience, not the check.** The Server checks every rule again when the form is sent (`ILoginService`, `IRegisterService`, the Identity validators). A browser without JavaScript works as before, with the messages after the click. A regular expression that JavaScript cannot read is skipped in the browser and still checked by the Server. The rules of the banned password list and the password history are checked only by the Server, so the list is never sent to a browser.

**Own pages.** A page of your own (a custom UI) can copy this: read the settings through `GET` of the project settings and write the same `data-a2-rule` JSON, or only set the HTML attributes (`required`, `minlength`, `pattern`) yourself.

## Privacy and Terms pages

`/Privacy` and `/Terms` are public Razor Pages (no sign-in, and no application is needed). Their text is English and describes what the software itself does: what data it stores, the cookies it sets, who receives data, the user's choices, the MIT licence and the usual terms of use. Both end with a note to the **operator**: the text is a general template, not legal advice, and the operator must check it against how the server is really run (providers, log retention, the law that applies) and change or replace it.

- With `?ClientId=` (or on a single-project server) the pages show the project's brand name (`Localization.BrandName`) and, when set, the support address (`Localization.SupportEmail`) as a `mailto:` link; an unknown application is not an error.
- Both pages use the wide card of `_LayoutAuth` (`ViewData["Wide"] = true`). The footer of `_LayoutAuth` links to them and keeps the `ClientId`.
- To change the text, edit `Pages/Privacy.cshtml` and `Pages/Terms.cshtml`. The page models (`PrivacyModel`, `TermsModel`) share `LegalPageModel`.

## External sign-in pages

### Login page: provider buttons

`GET /connect/external/providers?client_id={clientId}&hint={emailOrProviderName}` returns JSON (no secrets):

```json
{
  "providers": [
    { "name": "corp", "displayName": "Company login", "kind": "GenericOidc",
      "challengeUrl": "/Account/External/Challenge", "loginFormRequired": false },
    { "name": "ldap-hq", "displayName": "Directory", "kind": "Ldap", "challengeUrl": null, "loginFormRequired": true }
  ],
  "suggestedProvider": "corp"
}
```

- Show one button per provider. For `loginFormRequired: true` (LDAP) show a user name and password form that posts to the normal login page (see [external-identity/ldap-openldap.md](external-identity/ldap-openldap.md)).
- `suggestedProvider` is home-realm discovery: when the user typed an email, send the email as `hint`; if its domain belongs to a provider, that provider is suggested. A UI may go to it directly.
- `AutoRedirect`: when exactly one provider exists and no local login method is allowed, a UI may skip the picker.

### Start a sign-in

Post a form to `/Account/External/Challenge?ClientId={clientId}` (or `/connect/external/challenge`):

| Field | Meaning |
|---|---|
| `Provider` | Provider name from the list |
| `ReturnUrl` | The authorize URL (or another allowed URL) to return to after sign-in. An unsafe value is replaced by the project default |
| `Hint` | Optional email or user name to prefill at the provider |
| `__RequestVerificationToken` | Anti-forgery token |

The answer is a `302` to the provider. A wrong or disabled provider gives `400` with `external_provider_disabled`.

### Callback: `/callback/login/{slug}/{name}`

The provider sends the browser here. The page does everything and answers with a redirect:

| Result | Where the browser goes |
|---|---|
| Signed in | The `ReturnUrl` (or `RedirectAfterLogin`) |
| Second factor needed | `/Account/TwoFactor` (the token is in TempData) |
| No email from the provider, project needs one | `/Account/External/Confirm` |
| Locked out | `/Account/Lockout` |
| Refused | The page itself with status 400 and `ErrorCode` (for example `external_email_exists_not_verified`) |

### `/Account/External/Confirm`

Used when the provider gave no email. Properties: `PendingToken` (hidden field), `EmailSent`, `ErrorCode`.
POST `PendingToken` and `Email` to send a confirmation mail (the page answers the same for every address). The link in the mail opens the same page with `ConfirmationToken` and finishes the sign-in.

### `/Manage/ExternalLogins` (signed in)

Properties: `Links` (`provider`, `providerKey`, `displayName`), `AvailableProviders`. Handlers:

| Handler | Form fields | Result |
|---|---|---|
| `?handler=Link` | `Provider` | Redirect to the provider; the callback adds the link and returns to this page |
| `?handler=Unlink` | `Provider`, `ProviderKey` | Removes the link. `cannot_remove_last_login_method` when it is the last way to sign in |

## Account change pages (signed in)

Four pages in `/Account` let a signed-in person change their own details. They need the hosted-UI session cookie (a person who is not signed in is sent to the sign-in page) and are protected by the same `Auth2.Session` policy as `/Manage`. **Their `.cshtml` files hold only `@page` and `@model`: the page models are finished, the HTML and CSS are still to be drawn.** The models share one base (`AccountChangePageModel`).

All four expose `ErrorCode`, `ErrorMessage` and `ErrorMessages` as every page does, and `PasswordRequired`: `true` when the person has a password, so the form must show a **current password** field (`Input.CurrentPassword`). A person without a password (external account or passkey only) needs none. A wrong current password counts as a failed attempt (it can lock the account) and answers `invalid_credentials`; a locked account answers `locked_out`.

Before anything is sent or saved, the new value goes through the same validators as every other change (format, the project rules `UserName.*`, `Email.AllowedDomains` / `BlockedDomains`, `Phone.PhoneNumberRegex`, and "already taken"), so the page shows the reason (`ErrorMessages`) and nothing is sent to an address or number that would be refused.

| Page | Properties | Handlers and form fields | Result |
|---|---|---|---|
| `/Account/ChangePassword` | `Changed` | POST: `Input.CurrentPassword`, `Input.NewPassword` | The password changes; every token session of the person ends. `Changed` is `true` and the person stays signed in, **unless** `Password.SignOutAfterChangePassword` is on: then the cookie is removed and the page redirects to `/Account/Login` |
| `/Account/ChangeUserName` | `CurrentUserName`, `Changed` | POST: `Input.NewUserName`, `Input.CurrentPassword` | The user name changes at once (audit `user.name_changed`); the cookie is renewed so the session shows the new name |
| `/Account/ChangeEmailAddress` | `CurrentEmail`, `EmailSent`, `Changed` | POST: `Input.NewEmail`, `Input.CurrentPassword`. **Link handler** `?handler=Confirm&userId=&email=&token=` | POST sends a confirmation link **to the new address** (audit `email.change_requested`), `EmailSent` is `true` and **nothing changes yet**. Opening the link (a GET, like `ConfirmEmail`) replaces the address and marks it confirmed (audit `email.changed`). The link works once, for that person and that address only; another signed-in person gets `invalid_token`. The link of the email goes through the sign-in page when the person is not signed in |
| `/Account/ChangePhoneNumber` | `CurrentPhoneNumber`, `CodeSent`, `Changed` | `?handler=SendCode`: `Input.NewPhoneNumber`, `Input.CurrentPassword`. POST: `Input.NewPhoneNumber`, `Input.Code` | `SendCode` sends an SMS with a code **to the new number** (audit `phone.change_requested`); `CodeSent` is `true`. POST checks the code and replaces the number and marks it confirmed (audit `phone.changed`). A second `SendCode` inside `TwoFactor.OtpResendSeconds` answers `rate_limited` (429). A wrong code answers `invalid_otp`; the attempts, the lifetime and the length follow the `TwoFactor.Otp*` settings. The code is tied to the number, so it cannot confirm another number |

The email and the SMS are written in the language of the person and sent through the same email and SMS senders as every other message ([email-and-sms.md](email-and-sms.md)), so each application can use its own provider. The texts are `change_email.subject`, `change_email.body` and `sms.change_phone` in the `Emails` resource.

The same changes are available as JSON routes (`/api/auth2/user-name/change`, `/email/change`, `/email/change/confirm`, `/phone/change`, `/phone/change/confirm`; [api-reference.md](api-reference.md)) and as methods of `IAuth2ApiClientService` ([client-api-service.md](client-api-service.md)): the pages and the routes use the same service on the Server, so they behave the same.

`/Manage/ChangePassword` and `/Manage/ChangeEmail` are older pages of the signed-in area; the pages above are the complete ones.

## Passkey routes (JSON for the UI script)

All routes take the application in `?client_id=` (the project follows from it) (not needed when signed in; the session knows it). Errors have the usual body `{ "code": "...", "message": "..." }`. The routes that need a session also need the anti-forgery token in the header `RequestVerificationToken` (the cookie is set by any page that renders a form; the UI gets the token the same way as for forms).

### Register (signed in)

1. `POST /account/passkey/creation-options` (body `{}`) returns the **PublicKeyCredentialCreationOptions JSON**. Give it to `PublicKeyCredential.parseCreationOptionsFromJSON(...)` and `navigator.credentials.create({ publicKey })`.
2. `POST /account/passkey/register` with body `{ "credential": <credential.toJSON()>, "name": "MacBook" }` returns the new passkey:

```json
{ "id": "base64url", "name": "MacBook", "createdAtUtc": "2026-11-24T10:00:00Z", "signCount": 0,
  "isBackedUp": false, "isBackupEligible": true, "isUserVerified": true, "transports": ["internal"] }
```

Errors: `passkey_not_allowed`, `passkey_limit_reached`, `passkey_blocked_authenticator`, `validation_failed` (name too long, no user verification, bad answer), `conflict` (credential already registered).

### Sign in (anonymous)

1. `POST /account/passkey/request-options` with `{ "login": "sara" }` (or `{ "login": null }` for a usernameless sign-in, good for the browser's conditional UI) returns the **PublicKeyCredentialRequestOptions JSON**. An unknown name gets options like any other. Use `navigator.credentials.get({ publicKey })`.
2. `POST /account/passkey/signin` with `{ "credential": <credential.toJSON()>, "returnUrl": "..." }`:
   - success: `{ "succeeded": true, "redirectTo": "/..." }` and the session cookie is set (its name is `.SystemX.Auth2.Session.{projectkey}`);
   - second factor needed (the passkey was not user verified): `{ "succeeded": false, "errorCode": "two_factor_required", "twoFactorToken": "..." }`;
   - failure: status 400 with `invalid_credentials` (the same code for wrong, replayed or unknown passkeys), `login_method_not_allowed`, `passkey_not_allowed`, `locked_out`, or 429 `rate_limited`.

### Manage (signed in)

| Route | Result |
|---|---|
| `GET /account/passkeys` | `[ { id, name, createdAtUtc, signCount, isBackedUp, isBackupEligible, isUserVerified, transports } ]` |
| `PATCH /account/passkeys/{id}` body `{ "name": "..." }` | 204, or 404 / `validation_failed` |
| `DELETE /account/passkeys/{id}` | 204, 404, or `cannot_remove_last_login_method` |

The page `/Manage/Passkeys` exposes the same list as properties (`Passkeys`, `PasskeysAllowed`, `CanAddMore`, `SuggestBackupPasskey`) and has the handlers `Rename` (`Id`, `Name`) and `Delete` (`Id`). See [external-identity/passkeys.md](external-identity/passkeys.md).

## SAML pages

`POST /saml/{slug}/{name}/acs` (the identity provider posts here; same redirects as the OIDC callback), `GET /saml/{slug}/{name}/metadata` (XML), `GET /saml/{slug}/{name}/slo` (single logout). When a provider uses the POST binding, `Account/External/Challenge` returns **200** with the properties `PostAction` and `PostFields`: the UI renders an auto-submitting form with these fields. The route `POST /connect/external/challenge` returns `{ "postAction": "...", "postFields": { ... } }` in this case.

## Related pages

- [external-identity.md](external-identity.md)
- [authentication-flows.md](authentication-flows.md)
- [API reference](api-reference.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Credential scope](credential-scope.md)
- [Server settings](server-settings.md)
- [Admin JSON API](admin-api.md)
- [Email and SMS](email-and-sms.md)
- [The key ring and its snapshot](key-ring.md)
- [Localization](localization.md)
- [Server reference](server-reference.md)
- [Social login with OpenIddict.Client.WebIntegration](external-identity/social-webintegration.md)
- [SAML 2.0: enable and use](external-identity/saml.md)
- [LDAP and Active Directory: enable and use](external-identity/ldap.md)
- [Brokering: your app trusts only the Server](external-identity/brokering.md)
- [Group mapping](external-identity/group-mapping.md)
- [Troubleshooting external sign-in](external-identity/troubleshooting.md)
- [Documentation index](index.md)
