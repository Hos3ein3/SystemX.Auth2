# UI handover

The Server has **no HTML**. Every page is a Razor Page whose `.cshtml` only has `@page`, `@model` and a comment `@* UI: to be done *@`. The page models do all the work.
The UI team writes the HTML, CSS and JavaScript later. This page lists what a UI must know for the external sign-in pages. Other pages are described in [authentication-flows.md](authentication-flows.md).

## How a page works

- A page finds its application from `ClientId` (query or form) or from the `client_id` inside `ReturnUrl`. In single-application mode no client id is needed.
- Forms are posted with an anti-forgery token (`__RequestVerificationToken`). The UI must include it.
- On an error the page sets the status code (usually 400) and exposes `ErrorCode` (a stable code, see [error-codes.md](error-codes.md)) and `ErrorMessage` (localized).
- Public properties of a page model are the data the UI shows.

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
| `ReturnUrl` | The authorize URL (or another allowed URL) to return to after sign-in. An unsafe value is replaced by the application default |
| `Hint` | Optional email or user name to prefill at the provider |
| `__RequestVerificationToken` | Anti-forgery token |

The answer is a `302` to the provider. A wrong or disabled provider gives `400` with `external_provider_disabled`.

### Callback: `/callback/login/{slug}/{name}`

The provider sends the browser here. The page does everything and answers with a redirect:

| Result | Where the browser goes |
|---|---|
| Signed in | The `ReturnUrl` (or `RedirectAfterLogin`) |
| Second factor needed | `/Account/TwoFactor` (the token is in TempData) |
| No email from the provider, application needs one | `/Account/External/Confirm` |
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

## Related pages

- [external-identity.md](external-identity.md)
- [authentication-flows.md](authentication-flows.md)
