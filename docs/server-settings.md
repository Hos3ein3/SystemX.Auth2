# Server settings

## What this is

Settings live on **two levels**.

- **Project settings** (`ProjectSettings`): stored as JSON in one `jsonb` column (`projects.settings_json`). Login methods, password rules, lockout, two-factor, sessions, external providers, `JoinMode`, token defaults, localization.
- **Application settings**: the OpenIddict fields of the application (redirect URIs, post-logout redirect URIs, allowed scopes, consent type, client type) **plus** `applications.settings_json` (`ClientApplicationSettings`) for what OpenIddict has no field for. A null value there means "use the project value".

Every setting has a safe default. A **global admin** (the developer) can edit all settings.
A **project admin** can edit only the settings marked `ProjectAdminEditable`.
The code that checks the settings is `ProjectSettingsValidator` and `ClientApplicationSettingsValidator`. They reject unsafe values before anything is saved.

## When to use it

Use this page when you configure a project or an application, or when you build the admin screens.

## How to use

### The effective value

The value that counts for a request is the **project default, overridden by the application value where the application sets one**.
One class does this: `EffectiveSettings(projectSettings, application)`. Page models, token issuing, the login cookie and the handshake all use it.

| Setting | Project (`ProjectSettings`) | Application (`ClientApplicationSettings`) | Effective |
|---|---|---|---|
| UI mode | `Clients.DefaultUiMode` | `UiMode` | application, else project |
| Cookie lifetime | `Sessions.CookieLifetimeMinutes` | `CookieLifetimeMinutes` | application, else project, else session cookie |
| Access token lifetime | `Sessions.AccessTokenMinutes` | `AccessTokenMinutes` | application, else project |
| Refresh token lifetime | `Sessions.RefreshTokenDays` | `RefreshTokenDays` | application, else project |
| Identity token lifetime | `Sessions.IdentityTokenMinutes` | `IdentityTokenMinutes` | application, else project |
| Consent | `Sessions.RequireConsent`, `ConsentMode` | `ConsentMode` (also written to the OpenIddict consent type) | application, else project |
| External providers shown | all providers of the project | `ExternalProviders` (a **subset** of the project's providers, never more) | the subset, or all |
| Password grant (Self UI) | none | `AllowSelfUiLogin` | application only (off by default) |
| CORS origins | none | `AllowedCorsOrigins` | application only (CORS is off when empty) |
| Redirect URIs, scopes | none | OpenIddict fields | application only |

### Step by step

1. Read the project settings: `IProjectSettingsProvider.GetAsync(projectId)` (cached).
2. Change the object.
3. Save it with `IProjectService.UpdateSettingsAsync(projectId, settings)`.
   The service validates the settings, raises `SettingsVersion` by one, writes the consent type into every application of the project, clears the cache and writes an audit event.
4. Application values are changed with `IApplicationService.UpdateAsync(applicationId, new ApplicationSpec(...))`. It goes through the OpenIddict application manager.
5. If the settings are not valid, you get a `SettingsValidationException` with a list of `Path` and `Message`.

### Example

```csharp
var settings = await settingsProvider.GetAsync(project.Id);
settings.Password.MinLength = 12;
await projectService.UpdateSettingsAsync(project.Id, settings);

await applicationService.UpdateAsync(application.Id, new ApplicationSpec("",
    RedirectUris: ["https://app.example.com/signin-oidc"],
    Settings: new ClientApplicationSettings { AccessTokenMinutes = 5, AllowSelfUiLogin = false }));
```

### What changing a setting does to the OpenIddict application

| Setting | Effect on the application |
|---|---|
| Redirect URIs, post-logout redirect URIs | Registered redirect URIs (exact match, no wildcards) |
| Allowed scopes | Scope permissions. A scope not in the list is refused |
| `AllowSelfUiLogin` (application) | Adds or removes the password grant and the two-factor grants |
| Consent mode (project or application) | Consent type of the application |
| Token lifetimes | Used when the next token is issued |

The client secret is never changed by a settings update. Rotate it on purpose (`secret/rotate`).

## How to implement / extend

- A new **project** setting: add the property (with `[SettingEditability(...)]`, a test checks that every field has it) to `ProjectSettings`, validate it in `ProjectSettingsValidator`, and document it here.
- A new **application** setting: add a nullable property to `ClientApplicationSettings`, expose the effective value in `EffectiveSettings` (override first, then the project value), validate it in `ClientApplicationSettingsValidator`. Add a test for the precedence.
- Do not store in `SettingsJson` what OpenIddict already has a field for.

## How some settings work together

- **Consent.** `Sessions.RequireConsent=false` means no consent page, whatever `ConsentMode` says. With `RequireConsent=true`, `Implicit` is treated as `Explicit` (ask once). `Systematic` asks every time. See [authentication-flows.md](authentication-flows.md#consent).
- **Joining.** `Registration.JoinMode=AutoJoin` lets a person who already has an account join with proof of ownership. `InviteOnly` and `Closed` refuse. (Admins add people with the admin API.) `RegistrationEnabled=false` closes registration of new people.
- **OTP.** `TwoFactor.OtpLength`, `OtpLifetimeSeconds`, `OtpMaxAttempts` and `OtpResendSeconds` rule **every** one-time code: login, phone confirmation, join and password reset by SMS.
- **Lockout.** Wrong passwords, wrong OTP codes, wrong TOTP codes and a wrong current password all count as failed attempts.
- **Confirmation.** `Email.RequireConfirmedEmail` and `Phone.RequireConfirmedPhone` block the login only after the password (or code) was right.

## Validation rules

The validator rejects:

- `Lockout.MaxFailedAttempts` below 1, or `Lockout.LockoutMinutes` below 1.
- `Password.MinLength` below 6, `Password.MaxLength` below `MinLength`.
- A redirect URI of an application with a wildcard (`*`), a fragment (`#`), or a scheme other than `http` or `https`.
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
| `AllowedLoginMethods` | `List<LoginMethod>` | `[UserNamePassword, EmailPassword]` | GlobalOnly | Allowed login methods. |
| `AllowedRegisterMethods` | `List<LoginMethod>` | `[UserNamePassword, EmailPassword]` | GlobalOnly | Allowed register methods. |

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
| `BannedPasswords` | `List<string>` | `[]` | GlobalOnly | Passwords that are never allowed. A breached-list check can be plugged in later (hook only). |
| `SaveOldPasswords` | `bool` | `false` | GlobalOnly | Remember the **hash** of each replaced password (table `password_history`) and refuse the current password as a new one. |
| `OldPasswordsToKeep` | `int` | `5` | GlobalOnly | How many old passwords are remembered, not counting the current one (1 to 24). |
| `AllowOldPasswordReuse` | `bool` | `false` | GlobalOnly | `false`: refuse the current and every remembered password. `true`: refuse only the current one and the one used right before it. |
| `PasswordExpiryDays` | `int` | `0` | GlobalOnly | A password older than this many days is refused at sign-in (`password_expired`) until it is changed. `0` = never (up to 3650). |
| `SignOutAfterChangePassword` | `bool` | `false` | GlobalOnly | When a person changes their own password (the API route `/password/change` or the page `/Account/ChangePassword`), end **every** token session, also the one that made the change. The page also removes the hosted-UI cookie and sends the person to the sign-in page. Off: the other sessions end and the current one stays. A reset always ends every session. |

How the history and expiry settings work, with examples and the limits: [password-history.md](password-history.md). The change pages: [ui-handover.md](ui-handover.md#account-change-pages-signed-in).

### Email

Email rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `RequireConfirmedEmail` | `bool` | `false` | GlobalOnly | Email must be confirmed before login. |
| `AllowedDomains` | `List<string>` | `[]` | ProjectAdminEditable | If not empty, only these domains are allowed. |
| `BlockedDomains` | `List<string>` | `[]` | ProjectAdminEditable | These domains are never allowed. |

### Messaging (email and SMS providers)

Which provider sends email and SMS. It exists on the project and, as an override, on an application. It is **not** edited through the generic settings route (it holds a password): use the messaging routes. See [email-and-sms.md](email-and-sms.md).

| Field | Type | Default | Editable by | Meaning |
|---|---|---|---|---|
| `Email.Provider` | `string` | empty (inherit) | GlobalOnly | `logging`, `smtp`, `google` or a custom name |
| `Email.Host`, `Email.Port`, `Email.Security` | | `587`, `StartTls` | GlobalOnly | SMTP server (not used by `google`) |
| `Email.UserName`, `Email.PasswordProtected` | | | GlobalOnly | Login. The password is encrypted and never returned |
| `Email.FromAddress`, `Email.FromName` | | | GlobalOnly | From header. Empty name: the brand name |
| `Email.TimeoutSeconds` | `int` | `15` | GlobalOnly | Timeout of one send |
| `Sms.Provider`, `Sms.Sender` | `string` | empty (inherit) | GlobalOnly | SMS provider and sender id |

### Phone

Phone rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `PhoneNumberRegex` | `string` | `"^\+[1-9]\d{6,14}$"` | GlobalOnly | Regex for phone numbers. Default is E.164. |
| `RequireConfirmedPhone` | `bool` | `false` | GlobalOnly | Phone must be confirmed before login. |
| `RegexTimeoutMs` | `int` | `200` | GlobalOnly | Regex timeout in ms. Protects from ReDoS. |

### Registration

Registration rules.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `RegistrationEnabled` | `bool` | `true` | ProjectAdminEditable | Is registration open. |
| `JoinMode` | `JoinMode` | `AutoJoin` | ProjectAdminEditable | Join mode. |
| `LoginRequiredAfterRegister` | `bool` | `true` | ProjectAdminEditable | User must sign in again after registering. |
| `SendConfirmationEmail` | `bool` | `true` | ProjectAdminEditable | Send the confirmation email right after registration. An application can override it (same name, `null` = project value). Always sent when `Email.RequireConfirmedEmail` is on. The user can ask for it again (resend) either way. |
| `SendConfirmationSMS` | `bool` | `true` | ProjectAdminEditable | The same for the confirmation code sent by SMS. Always sent when `Phone.RequireConfirmedPhone` is on. |
| `AssignedRoles` | `List<string>` | `[]` | GlobalOnly | Role names every new member gets. The roles must exist in the project. `GlobalAdmin`, `Developer` and `ProjectAdmin` are refused. |
| `AssignedGroups` | `List<string>` | `[]` | GlobalOnly | Group names every new member gets (lowercase keys, like permission keys). The groups must exist. |
| `AssignedPermissions` | `List<string>` | `[]` | GlobalOnly | Permission keys every new member gets directly. The permissions must exist. |

Each list has at most 50 entries, no duplicates (not case sensitive). Scopes are not in this list: a scope belongs to an **application** (what a client may ask for), not to a person, so it is set on the application (`allowedScopes`).

**Example.** Everybody who registers in `papertrade` becomes an `Editor`, joins the group `customers` and may read orders:

```json
"registration": {
  "assignedRoles": ["Editor"],
  "assignedGroups": ["customers"],
  "assignedPermissions": ["orders.read"]
}
```

Create the role, the group and the permission first (admin API: [admin-api.md](admin-api.md), or sync them from your application: [permission-sync.md](permission-sync.md)). The setting is `GlobalOnly` because it decides what rights a stranger gets: only the developer changes it. Test: `RegistrationDefaultsTests`.

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
| `StepUpMinutes` | `int` | `15` | GlobalOnly | "Sudo mode": how many minutes a code of the authenticator app counts as fresh. The restricted pages (delete the account, turn two-factor off) ask a person who has two-factor on for a code again when the last one is older. The second step of the sign-in counts as a code. `0` = never ask again. 0 to 720 ([two-factor.md](two-factor.md#the-pages-and-what-each-one-is-for)). |
| `OtpResendSeconds` | `int` | `60` | GlobalOnly | Seconds before a new OTP may be sent. |
| `AllowPasskeys` | `bool` | `false` | GlobalOnly | Allow passkeys (experimental). |

### Sessions

Sessions and tokens.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `AccessTokenMinutes` | `int` | `15` | GlobalOnly | Access token lifetime in minutes. |
| `RefreshTokenDays` | `int` | `14` | GlobalOnly | Refresh token lifetime in days. |
| `IdentityTokenMinutes` | `int` | `10` | GlobalOnly | Identity token lifetime in minutes. |
| `CookieLifetimeMinutes` | `int?` | `null` | GlobalOnly | Cookie lifetime in minutes. Null means the client default. |
| `CookieSliding` | `bool` | `true` | GlobalOnly | Sliding cookie expiry. |
| `SingleActiveSession` | `bool` | `false` | GlobalOnly | On sign-in, revoke other sessions of this project (all its applications). |
| `RequireConsent` | `bool` | `false` | GlobalOnly | Show a consent screen. |
| `ConsentMode` | `ConsentMode` | `Implicit` | GlobalOnly | How consent works. |

### Redirects

Redirect fallbacks. A valid return URL wins. The allowed redirect URIs are fields of each **application** (OpenIddict), not of the project.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `RedirectAfterLogin` | `string` | `null` | ProjectAdminEditable | Where to go after login. |
| `RedirectAfterRegister` | `string` | `null` | ProjectAdminEditable | Where to go after register. |
| `RedirectAfterLogout` | `string` | `null` | ProjectAdminEditable | Where to go after logout. |

### Clients

Client behaviour: project defaults for all applications. Scopes, redirect URIs, Self UI and CORS are application values (see above).

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `AllowedClientTypes` | `List<ClientType>` | `[Api, Mvc, RazorPages, Blazor, Machine]` | GlobalOnly | Allowed client types. |
| `PermissionSyncAllowed` | `bool` | `true` | GlobalOnly | Permission sync is allowed. |
| `RoleSyncAllowed` | `bool` | `true` | GlobalOnly | Role sync is allowed. |
| `AllowRemovePermissionsFromSync` | `bool` | `false` | GlobalOnly | Sync marks permissions that are missing from the list as obsolete (never deletes). |
| `DefaultUiMode` | `UiMode` | `Hosted` | GlobalOnly | Default UI mode. |
| `AutoAssignScopeToApplication` | `bool` | `true` | GlobalOnly | With scope sync on: a pushed scope is registered **and** added to the allowed scopes of the application that pushed it. Off: it is only registered (saved in the database); an admin allows it for the applications that need it. Turn it off wherever scopes protect something ([scopes.md](scopes.md#scope-sync-and-what-a-scope-really-limits)). |
| `ScopeSyncAllowed` | `bool` | `true` | GlobalOnly | A client may register its own custom scopes and allow them for itself (never `auth2.*` or the standard scopes). |
| `GroupSyncAllowed` | `bool` | `false` | GlobalOnly | A client may add missing groups (push). Admins keep all other control. |
| `AllowProjectAdminRotateSecret` | `bool` | `false` | GlobalOnly | A project admin may rotate the client secret of the applications of the own project. |

### Localization

Localization and branding.

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `DefaultCulture` | `string` | `"en"` | ProjectAdminEditable | Default culture. |
| `SupportedCultures` | `List<string>` | `["en"]` | ProjectAdminEditable | Supported cultures. They only choose a language. |
| `BrandName` | `string` | `null` | ProjectAdminEditable | Brand name. |
| `SupportEmail` | `string` | `null` | ProjectAdminEditable | Support email. |

### ExternalProviders[]

External sign-in providers: Google, Microsoft, GitHub, Apple, Facebook, generic OIDC, SAML and LDAP. Every field is **GlobalOnly**: only a global admin (the developer) changes them.
The list is a list of three shapes, told apart by a `kind` field (`oidc`, `saml`, `ldap`). Secrets are stored encrypted and are never returned.
The generic settings route does not edit this list; use the provider routes of the admin API (see [external-identity.md](external-identity.md)).
All fields of every type are explained in [external-identity.md](external-identity.md#settings-reference).

### Passkeys

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `AllowPasskeys` | `bool` | `false` | GlobalOnly | Users may register and use passkeys. |
| `PasskeyUserVerification` | `string` | `"required"` | GlobalOnly | `required` or `preferred`. |
| `PasskeyResidentKey` | `string` | `"preferred"` | GlobalOnly | `required`, `preferred` or `discouraged`. |
| `MaxPasskeysPerUser` | `int` | `10` | GlobalOnly | Most passkeys of one user (1 to 50). |
| `PasskeyNameMaxLength` | `int` | `50` | GlobalOnly | Longest passkey name (1 to 200). |
| `RequirePasskeyBackupMethod` | `bool` | `true` | GlobalOnly | A user may not remove the last way to sign in. |

### Application settings (`ClientApplicationSettings`)

Stored in `applications.settings_json`. A null value means "use the project value".

| Field | Type | Default | Editor | Meaning |
|---|---|---|---|---|
| `UiMode` | `UiMode?` | `null` | GlobalOnly | Hosted, Self or None. Null: `Clients.DefaultUiMode` of the project. |
| `CookieLifetimeMinutes` | `int?` | `null` | GlobalOnly | Cookie lifetime in minutes. |
| `AccessTokenMinutes` | `int?` | `null` | GlobalOnly | Access token lifetime (1 to 1440). |
| `RefreshTokenDays` | `int?` | `null` | GlobalOnly | Refresh token lifetime (1 to 365). |
| `IdentityTokenMinutes` | `int?` | `null` | GlobalOnly | Identity token lifetime (1 to 1440). |
| `AllowedCorsOrigins` | `List<string>` | `empty list` | GlobalOnly | Allowed CORS origins of this client. Exact origins, no wildcards. CORS is off when empty. |
| `AllowClientToSetWebServerUrl` | `bool` | `true` | GlobalOnly | The client of this application may announce its web server address and application type in the handshake and the Server stores them. Off: only a global admin sets them ([handshake-and-startup.md](handshake-and-startup.md#the-web-server-address-and-the-application-type)). |
| `AllowSelfUiLogin` | `bool` | `false` | GlobalOnly | Enables the password grant and the two-factor grants for this client (Self UI). |
| `ExternalProviders` | `List<string>?` | `null` | ProjectAdminEditable | Names of the project's providers this client shows. Null: all. It only chooses; it never adds a provider. |
| `ConsentMode` | `ConsentMode?` | `null` | GlobalOnly | Consent mode. Null: the project value. |

The OpenIddict fields (redirect URIs, post-logout redirect URIs, allowed scopes) are edited with the application routes ([admin-api.md](admin-api.md)).

## Risks

- Lowering `Password.MinLength` or turning off lockout makes accounts easier to attack.
- `AllowSelfUiLogin` (an application setting) turns on the password grant for that application. The client app then handles passwords. Prefer the hosted UI. See [authentication-flows.md](authentication-flows.md).
- `Clients.AllowRemovePermissionsFromSync` lets a client mark synced permissions obsolete by leaving them out of a sync (nothing is deleted). Keep it off unless one client owns the whole catalog.
- `Email.RequireConfirmedEmail` is off by default so a fresh install works with the development email sender. Turn it on in production.

## Common problems

- *`SettingsValidationException` when saving:* read the `Path` of each error and fix that field.
- *A setting change has no effect:* the settings are cached. `UpdateSettingsAsync` clears the cache on this instance. With Redis the other instances are cleared at once (pub/sub); without Redis their cache lives at most 1 day. So with several instances, use Redis: without it a change saved on one instance can stay unseen on the others for a whole day. (A shorter lifetime for installs without Redis was considered and left out on purpose: settings change rarely.)

## Related pages

- [concepts.md](concepts.md)
- [server-setup.md](server-setup.md)
- [Seeding (first admin)](seeding.md)
- [Deployment and scaling](deployment-and-scaling.md)
- [Security](security.md)
- [Observability](observability.md)
- [Server reference](server-reference.md)
- [Credential scope](credential-scope.md)
- [Localization](localization.md)
- [Database tables](database-tables.md)
- [The key ring and its snapshot](key-ring.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Architecture](architecture.md)
- [API reference](api-reference.md)
- [Error codes](error-codes.md)
- [UI handover](ui-handover.md)
- [Passkeys](external-identity/passkeys.md)
- [Troubleshooting external sign-in](external-identity/troubleshooting.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Client setup](client-setup.md)
- [Client package reference](client-reference.md)
- [Handshake and startup](handshake-and-startup.md)
- [Current user and current client](client-current-user.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
- [Getting started](getting-started.md)
- [Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)](identity-stores.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Enums reference](enums-reference.md)
- [Contracts reference](contracts-reference.md)
- [Decisions (ADR list)](decisions.md)
- [Redis: what it does, how to enable it, and the Client's cache](redis.md)
- [Troubleshooting](troubleshooting.md)
