# Server settings (ApplicationSettings)

## What this is

Every application has a set of settings. The server stores them as JSON in one `jsonb` column (`applications.settings_json`).
Every setting has a safe default. A **global admin** (the developer) can edit all settings.
A **project admin** can edit only the settings marked `ProjectAdminEditable`.

The code that checks the settings is `ApplicationSettingsValidator`. It rejects unsafe values before they are saved.

## When to use it

Use this page when you configure an application, or when you build the admin screens.

## Step by step

1. Read the current settings: `IApplicationSettingsProvider.GetAsync(applicationId)` (cached).
2. Change the object.
3. Save it with `IApplicationService.UpdateSettingsAsync(applicationId, settings)`.
   The service validates the settings, raises `SettingsVersion` by one, clears the cache and writes an audit event.
4. If the settings are not valid, you get a `SettingsValidationException` with a list of `Path` and `Message`.

## Example

```csharp
var settings = await settingsProvider.GetAsync(app.Id);
settings.Password.MinLength = 12;
settings.Redirects.AllowedRedirectUris.Add("https://app.example.com/signin-oidc");
await applicationService.UpdateSettingsAsync(app.Id, settings);
```

## Validation rules

The validator rejects:

- `Lockout.MaxFailedAttempts` below 1, or `Lockout.LockoutMinutes` below 1.
- `Password.MinLength` below 6, `Password.MaxLength` below `MinLength`.
- A redirect URI with a wildcard (`*`), a fragment (`#`), or a scheme other than `http` or `https`.
- A regular expression that does not compile, or a `Phone.RegexTimeoutMs` outside 1–1000. A regex always runs with a timeout. This protects the server from ReDoS (a regex that runs for a very long time).
- `RedirectAfter*` values that are not a local path (one `/`) or an absolute `http(s)` URL. `//evil.com` is rejected.
- A CORS origin with a wildcard.
- A `DefaultCulture` that is not in `SupportedCultures`.
- OTP length outside 4–10, token lifetimes outside the allowed ranges, and similar range checks.

## Schema version

The JSON has a `schemaVersion` field (now `1`). It lets a later version of the server migrate old JSON.

## The fields

*Editor* is `GlobalOnly` (developer only) or `ProjectAdminEditable`.
Security-sensitive settings (lockout, password rules, two-factor, token lifetimes, redirect URI allow-lists, sync rights, password grant) are `GlobalOnly`.
Branding, cultures, fallback redirects and registration switches can be edited by a project admin.

Fields that are not inside a section: `SchemaVersion` (GlobalOnly), `CredentialScope` (GlobalOnly, default `null` = use the server option; only used when the server allows an override).


### Methods

Login and registration methods.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `AllowedLoginMethods` | `List<LoginMethod>` | `UserNamePassword, EmailPassword` | GlobalOnly | Allowed login methods. |
| `AllowedRegisterMethods` | `List<LoginMethod>` | `UserNamePassword, EmailPassword` | GlobalOnly | Allowed register methods. |

### UserName

User name rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `MinLength` | `int` | `3` | ProjectAdminEditable | Minimum length. |
| `MaxLength` | `int` | `64` | ProjectAdminEditable | Maximum length. |
| `AllowedCharacters` | `string` | `"^[a-zA-Z0-9._@+-]+$"` | GlobalOnly | Regex for allowed names. Always matched with a timeout. |
| `RequireUnique` | `bool` | `true` | GlobalOnly | User names must be unique. |

### Password

Password rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `MinLength` | `int` | `10` | GlobalOnly | Minimum length. |
| `MaxLength` | `int` | `128` | GlobalOnly | Maximum length. |
| `RequireDigit` | `bool` | `true` | GlobalOnly | Needs a digit. |
| `RequireLower` | `bool` | `true` | GlobalOnly | Needs a lowercase letter. |
| `RequireUpper` | `bool` | `true` | GlobalOnly | Needs an uppercase letter. |
| `RequireNonAlphanumeric` | `bool` | `false` | GlobalOnly | Needs a non-alphanumeric character. |
| `UniqueChars` | `int` | `4` | GlobalOnly | Minimum number of different characters. |
| `BannedPasswords` | `List<string>` | `empty list` | GlobalOnly | Passwords that are never allowed. A breached-list check can be plugged in later (hook only). |

### Email

Email rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `RequireConfirmedEmail` | `bool` | `null` | GlobalOnly | Email must be confirmed before login. |
| `AllowedDomains` | `List<string>` | `empty list` | ProjectAdminEditable | If not empty, only these domains are allowed. |
| `BlockedDomains` | `List<string>` | `empty list` | ProjectAdminEditable | These domains are never allowed. |

### Phone

Phone rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `PhoneNumberRegex` | `string` | `"^\+[1-9]\d{6,14}$"` | GlobalOnly | Regex for phone numbers. Default is E.164. |
| `RequireConfirmedPhone` | `bool` | `null` | GlobalOnly | Phone must be confirmed before login. |
| `RegexTimeoutMs` | `int` | `200` | GlobalOnly | Regex timeout in ms. Protects from ReDoS. |

### Registration

Registration rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `RegistrationEnabled` | `bool` | `true` | ProjectAdminEditable | Is registration open. |
| `JoinMode` | `JoinMode` | `AutoJoin` | ProjectAdminEditable | Join mode. |
| `LoginRequiredAfterRegister` | `bool` | `true` | ProjectAdminEditable | User must sign in again after registering. |

### Lockout

Lockout rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `LockoutEnabled` | `bool` | `true` | GlobalOnly | Is lockout on. |
| `MaxFailedAttempts` | `int` | `5` | GlobalOnly | Failed attempts before lockout. |
| `LockoutMinutes` | `int` | `15` | GlobalOnly | Lockout time in minutes. |
| `LockoutForNewUsers` | `bool` | `true` | GlobalOnly | Lockout also applies to new users. |

### TwoFactor

Two-factor and OTP rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `Mode` | `TwoFactorMode` | `Optional` | GlobalOnly | Two-factor mode. |
| `AllowTotp` | `bool` | `true` | GlobalOnly | Allow TOTP apps. |
| `AllowRecoveryCodes` | `bool` | `true` | GlobalOnly | Allow recovery codes. |
| `RecoveryCodeCount` | `int` | `10` | GlobalOnly | Number of recovery codes. |
| `OtpLength` | `int` | `6` | GlobalOnly | OTP length. |
| `OtpLifetimeSeconds` | `int` | `120` | GlobalOnly | OTP lifetime in seconds. |
| `OtpMaxAttempts` | `int` | `5` | GlobalOnly | Max wrong OTP attempts. |
| `OtpResendSeconds` | `int` | `60` | GlobalOnly | Seconds before a new OTP may be sent. |
| `AllowPasskeys` | `bool` | `null` | GlobalOnly | Allow passkeys (experimental). |

### Sessions

Sessions and tokens.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `AccessTokenMinutes` | `int` | `15` | GlobalOnly | Access token lifetime in minutes. |
| `RefreshTokenDays` | `int` | `14` | GlobalOnly | Refresh token lifetime in days. |
| `IdentityTokenMinutes` | `int` | `10` | GlobalOnly | Identity token lifetime in minutes. |
| `CookieLifetimeMinutes` | `int?` | `null` | GlobalOnly | Cookie lifetime in minutes. Null means the client default. |
| `CookieSliding` | `bool` | `true` | GlobalOnly | Sliding cookie expiry. |
| `SingleActiveSession` | `bool` | `null` | GlobalOnly | On sign-in, revoke other sessions of this application. |
| `RequireConsent` | `bool` | `null` | GlobalOnly | Show a consent screen. |
| `ConsentMode` | `ConsentMode` | `Implicit` | GlobalOnly | How consent works. |

### Redirects

Redirect rules. Fallbacks only; a valid return URL wins.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `RedirectAfterLogin` | `string?` | `null` | ProjectAdminEditable | Where to go after login. |
| `RedirectAfterRegister` | `string?` | `null` | ProjectAdminEditable | Where to go after register. |
| `RedirectAfterLogout` | `string?` | `null` | ProjectAdminEditable | Where to go after logout. |
| `AllowedRedirectUris` | `List<string>` | `empty list` | GlobalOnly | Allowed redirect URIs. Exact match, no wildcards. |
| `AllowedPostLogoutRedirectUris` | `List<string>` | `empty list` | GlobalOnly | Allowed post-logout redirect URIs. Exact match, no wildcards. |

### Clients

Client behaviour.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `AllowedClientTypes` | `List<ClientType>` | `Api, Mvc, RazorPages, Blazor, Machine` | GlobalOnly | Allowed client types. |
| `AllowedScopes` | `List<string>` | `"openid", "profile", "email", "phone", "roles", "offline_access", "auth2.me", "auth2.client.handshake"` | GlobalOnly | Scopes clients may request. |
| `PermissionSyncAllowed` | `bool` | `true` | GlobalOnly | Permission sync is allowed. |
| `RoleSyncAllowed` | `bool` | `true` | GlobalOnly | Role sync is allowed. |
| `AllowRemovePermissionsFromSync` | `bool` | `null` | GlobalOnly | Sync may remove permissions. |
| `DefaultUiMode` | `UiMode` | `Hosted` | GlobalOnly | Default UI mode. |
| `AllowSelfUiLogin` | `bool` | `null` | GlobalOnly | Enables the password grant for this application. |
| `AllowedCorsOrigins` | `List<string>` | `empty list` | GlobalOnly | Allowed CORS origins. CORS is off when empty. |

### Localization

Localization and branding.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `DefaultCulture` | `string` | `"en"` | ProjectAdminEditable | Default culture. |
| `SupportedCultures` | `List<string>` | `"en"` | ProjectAdminEditable | Supported cultures. They only choose a language. |
| `BrandName` | `string?` | `null` | ProjectAdminEditable | Brand name. |
| `SupportEmail` | `string?` | `null` | ProjectAdminEditable | Support email. |

### ExternalProviders[]

External login provider (experimental).

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `Name` | `string` | `""` | GlobalOnly | Provider name. |
| `Type` | `ExternalProviderType` | `null` | GlobalOnly | Provider type. |
| `ClientId` | `string` | `""` | GlobalOnly | Client id. |
| `ClientSecretProtected` | `string?` | `null` | GlobalOnly | Client secret, encrypted with Data Protection. |
| `Authority` | `string?` | `null` | GlobalOnly | Authority URL (generic OIDC). |
| `Scopes` | `List<string>` | `empty list` | GlobalOnly | Scopes to request. |
| `Enabled` | `bool` | `null` | GlobalOnly | Enabled. |

## Risks

- Lowering `Password.MinLength` or turning off lockout makes accounts easier to attack.
- `Clients.AllowSelfUiLogin` turns on the password grant for the application. The client app then handles passwords. Prefer the hosted UI. See `security.md` (planned).
- `Clients.AllowRemovePermissionsFromSync` lets a client delete permissions by syncing. Keep it off.
- `Email.RequireConfirmedEmail` is off by default so a fresh install works with the development email sender. Turn it on in production.

## Common problems

- *`SettingsValidationException` when saving:* read the `Path` of each error and fix that field.
- *A setting change has no effect:* the settings are cached. `UpdateSettingsAsync` clears the cache on this instance. Other instances are cleared in phase 10 (Redis pub/sub); until then the cache lives at most 10 minutes.

## Related pages

- [concepts.md](concepts.md)
- [server-setup.md](server-setup.md)
