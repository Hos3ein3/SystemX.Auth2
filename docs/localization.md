# Localization

## What this is

Texts for users (error messages, page texts, emails) come from **resx files only**. There is no translations table in the database.

Resource files in `src/SystemX.Auth2.Server/Resources/`:

| File | Content |
|---|---|
| `Errors.resx` | Error messages. One key for each code in `Auth2ErrorCodes`, and `identity.*` keys for standard Identity errors |
| `Validation.resx` | Validation messages |
| `Pages.resx` | Page texts (titles and so on) |
| `Emails.resx` | Email subjects and texts |

`Errors.resx` (no culture in the name) is the English text. `Errors.fa.resx`, `Errors.nl.resx` and `Errors.it.resx` are small examples with a few keys each. Missing keys fall back to English.

## When to use it

When you want to change a text, or add a language.

## How a text is found

1. The resx of the selected culture (for example `Errors.fa.resx`).
2. The English resx (`Errors.resx`).
3. The key itself.

The .NET resource manager does steps 1 and 2. Step 3 is what `IStringLocalizer` returns for a missing key.

## How the culture is chosen

In this order. The first supported value wins:

1. OIDC `ui_locales` (also read from `ReturnUrl`, because the login page is opened with the authorize URL inside `ReturnUrl`)
2. The saved culture of the user (claim `locale`; comes from `AppUser.PreferredCulture`)
3. The culture cookie `.SystemX.Auth2.Culture`
4. The `Accept-Language` header
5. The `DefaultCulture` of the application
6. `en`

A culture is used only if **both** the server has a resx file for it **and** the application lists it in `SupportedCultures`. The setting `DefaultCulture` must be one of `SupportedCultures`.
`fa-IR` matches `fa`.

The code is `Auth2CultureResolver` (the order) and `Auth2RequestCultureProvider` (reads the request). Add `app.UseAuth2Localization()` to the pipeline. The Server `Program.cs` already does.

The application is found from the `client_id` of the request (in the **query**). If no application is known, the server's own cultures are used. This holds for every route: the messages of the JSON API and the `error_description` of the token endpoint (a failed sign-in) are written in the chosen language, while the `error`, `error_code` and the status code never change. The Client package sends the language of the call as `Accept-Language` ([client-api-service.md](client-api-service.md#language-of-the-calls)).

## Step by step: add a language

1. Copy `Errors.resx` to `Errors.de.resx` (and the other files if you want).
2. Translate the `value` of each key. You may leave keys out. They fall back to English.
3. Keep the `{0}` and `{1}` markers in the texts. They are replaced by numbers or names.
4. Add the culture code to `Auth2CultureResolver.ServerCultures` (`"de"`).
5. In the settings of each application that should offer it, add `de` to `Localization.SupportedCultures`.
6. Run the tests. A test checks that a translation has no key that English does not have.

## Change a text

Edit the value in the resx file in the Server project and deploy. There are no database overrides. (They are listed in [roadmap.md](roadmap.md) as a possible future feature.)

## In code

```csharp
public class MyService(IAuth2Localizer text)
{
    public string Message() => text.Error(Auth2ErrorCodes.PasswordTooShort, 12);   // "The password must be at least 12 characters long."
}
```

## Identity errors (`Auth2IdentityErrorDescriber`)

`Auth2IdentityErrorDescriber` replaces the standard `IdentityErrorDescriber`. Its texts come from `Errors.resx`.

- Password and user-rule errors use the stable codes from `Auth2ErrorCodes` (for example `password_too_short`) and the key is the code.
- Other errors keep the standard Identity code (for example `DuplicateEmail`). Their key is `identity.` plus the code (`identity.DuplicateEmail`).

API errors always return the stable `code` too, so your program never has to read the text.

## Common problems

- *I see the key instead of a text:* the key is missing in `Errors.resx`. Add it.
- *A user sees English although they sent `Accept-Language: nl`:* the application does not list `nl` in `SupportedCultures`, or a higher item in the order matched first (cookie, `ui_locales`).
- *`FormatException` when showing an error:* a text with `{0}` was called without an argument. Pass the arguments.

## Related pages

- [error-codes.md](error-codes.md)
- [server-settings.md](server-settings.md)
- [Server reference](server-reference.md)
- [Credential scope](credential-scope.md)
- [Email and SMS](email-and-sms.md)
- [Admin JSON API](admin-api.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [UI handover](ui-handover.md)
- [Passkeys](external-identity/passkeys.md)
- [Authentication flows](authentication-flows.md)
- [Documentation index](index.md)
- [Client setup](client-setup.md)
- [Client package reference](client-reference.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [API reference](api-reference.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
