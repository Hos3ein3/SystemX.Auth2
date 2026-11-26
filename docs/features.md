# Features

## What this is

One page that lists what SystemX.Auth2 can do today: every way to sign in, every way to register, every account page, the access model, the admin tools, the Client package and the security and operations features. Each row says in one line what the feature is and links to the page with the details.

For the state of each area (done, not built) read [feature-status.md](feature-status.md). For what is not supported on purpose read [comparison-and-limits.md](comparison-and-limits.md).

## When to use it

Use it to answer "can it do X?" before you read a long guide. Use the guides for the settings and the steps.

## Sign-in methods

The methods a project allows are the setting `Methods.AllowedLoginMethods` ([server-settings.md](server-settings.md#methods)). The default is `UserNamePassword` and `EmailPassword`. Every method ends in the same pipeline: lockout, project membership, enabled / disabled rules, two-factor rule, consent, single active session ([authentication-flows.md](authentication-flows.md)).

| Method (`LoginMethod`) | What the person does | Notes |
|---|---|---|
| `UserNamePassword` | Types a user name and a password | Default |
| `EmailPassword` | Types an email address and a password | Default |
| `PhonePassword` | Types a phone number and a password | |
| `EmailOtp` | Gets a one-time code by email and types it | No password. A right code also confirms the address |
| `PhoneOtp` | Gets a one-time code by SMS and types it | No password. A right code also confirms the phone |
| `External` | Clicks "Sign in with Google / Microsoft / GitHub / Apple / Facebook", a generic OpenID Connect provider or a SAML 2.0 identity provider | Set per project, no restart ([external-identity.md](external-identity.md)) |
| `Ldap` | Types a directory user name and password | LDAP and Active Directory |
| `Passkey` | Uses a passkey (WebAuthn) of a phone, laptop or security key | Needs HTTPS and an explicit server domain ([passkeys](external-identity/passkeys.md)) |

On top of a method:

- **Two-factor** with an authenticator app (TOTP) and recovery codes: `Optional` or `Required` per project ([two-factor.md](two-factor.md)).
- **Lockout** after too many wrong attempts, per project ([server-settings.md](server-settings.md#lockout)).
- **Confirmed email / phone** can be required before a sign-in works.
- **Password expiry** and **no reuse of old passwords** ([password-history.md](password-history.md)).
- **Single active session**: a new sign-in ends the others of the project.
- **Consent screen**: `Implicit`, `Explicit` (once) or `Systematic` (every time).

## Grants and tokens

| Flow | For | Notes |
|---|---|---|
| Authorization code + PKCE (`S256` only) | Web apps and APIs with users | The recommended flow. The app never sees the password. Uses the hosted pages |
| Client credentials | Server to server, no user | [machine-to-machine.md](machine-to-machine.md) |
| Refresh token | Long sessions | Rotation, and reuse of an old token revokes the chain |
| Password grant | Self UI and mobile apps | Off by default |
| Two-factor grant | Second step of the password grant | |
| OTP grant (`urn:systemx:auth2:grant:otp`) | Sign in with an email or SMS code | Same rules as the password grant |
| Step-up (`acr_values=urn:systemx:auth2:acr:totp`) | An action that needs a fresh authenticator code | See [Step-up](#step-up-a-fresh-code-before-a-sensitive-action) |

Endpoints: `/connect/authorize`, `/connect/token`, `/connect/endsession`, `/connect/userinfo`, `/connect/revoke`, `/connect/introspect`, `/.well-known/openid-configuration`, `/.well-known/jwks`. The server uses OpenIddict. It is **not** a certified OpenID Connect provider.

Token details: signed JWTs, claims for the project, roles, permissions, groups, `amr` (how the person signed in), `idp` (which external provider), phone and email confirmation, and the step-up claims. Keys come from certificate files or from a key ring in the database with rotation ([key-ring.md](key-ring.md)).

## Registration and joining

| Feature | What it does |
|---|---|
| Register | `POST /api/auth2/register` (backend, client token with the scope `auth2.register`) and the page `Account/Register`. The identifiers accepted follow `Methods.AllowedRegisterMethods` |
| Register with a passkey | Page `Account/RegisterPasskey` |
| Register through an external provider | The first external sign-in creates the person and applies the provider mappings |
| Registration switch | `Registration.RegistrationEnabled` closes it (`registration_disabled`) |
| Default access for new members | `AssignedRoles`, `AssignedGroups`, `AssignedPermissions` ([server-settings.md](server-settings.md#registration)) |
| Confirmation | Email link and SMS code, sent unless the switches in the settings turn them off |
| One account for the whole server | The same person in a second project **joins** with a proof code sent to a **confirmed** email or phone and a new password for that project (`Account/JoinProject`) |
| Idempotent and quiet | Calling register again creates nothing and never changes the password. Login, register, forgot and resend answer the same for known and unknown people |
| Rules | Rules for user name, password, email (blocked domains) and phone, per project ([server-settings.md](server-settings.md)) |
| Rules in the forms | The hosted forms read the rules and check the input while the person types |

## Account pages for the person

These are pages of the hosted UI. Each one has JSON routes for your own UI ([ui-handover.md](ui-handover.md)).

| Page | What it does |
|---|---|
| `Account/Login`, `LoginPasskey`, `Logout` | Sign in and out |
| `Account/TwoFactor` | Second step of the sign-in: a code of the authenticator app or a recovery code |
| `Account/VerifyTotp` | A fresh authenticator code for a restricted action (step-up) |
| `Account/VerifyOtp`, `ConfirmEmail`, `ConfirmPhone` | One-time codes and confirmation |
| `Account/ForgotPassword`, `ResetPassword` | Reset by email link or by SMS code. A reset ends all sessions of the application |
| `Account/ChangePassword`, `ChangeUserName`, `ChangeEmailAddress`, `ChangePhoneNumber` | Change a detail. A new email or phone must be confirmed first |
| `Account/Consent` | Agree to the scopes an application asks for |
| `Account/Lockout`, `AccessDenied` | Messages |
| `Manage/Profile`, `Manage/Sessions` | Profile, and the list of sign-ins with "end this session" |
| `Manage/TwoFactor/Enable`, `RecoveryCodes` | Turn two-factor on (key, QR URI, first code) or off, and make new recovery codes |
| `Manage/Passkeys`, `Manage/ExternalLogins` | Add and remove passkeys and linked providers |
| `Manage/ChangeEmail`, `ChangePassword`, `DeleteAccount` | Own account changes. Restricted ones ask for a recent authenticator code (sudo mode) |

The code boxes take digits only. The pages show an error once.

## Two-factor and step-up

| Feature | What it does |
|---|---|
| Authenticator app (TOTP) | Key, `otpauth://` URI for a QR code, six-digit codes. The key is stored encrypted |
| Recovery codes | `TwoFactor.RecoveryCodeCount` codes, shown once, stored hashed. A code works once. It signs in or turns two-factor off. New codes replace the old ones |
| Mode | `TwoFactor.Mode`: `Optional` or `Required`. Disabling is refused when it is `Required` |
| Lost phone | A global admin resets the two-factor of a person |
| Sudo mode (Server pages) | Restricted pages (delete the account, change the email address, and similar) ask for a code that is newer than `TwoFactor.StepUpMinutes` ([two-factor.md](two-factor.md#sudo-mode-a-fresh-code-before-a-restricted-page)) |

### Step-up: a fresh code before a sensitive action

In an application that uses the hosted UI, the Client package can ask the Server for a fresh authenticator code before one action:

- Minimal API: `.WithTotp()` (or `.WithOptionalTotp()`) on an endpoint.
- MVC and Razor Pages: `[Auth2Totp]` on an action or a controller.

The browser goes to the Server page `Account/VerifyTotp`, the person types a code, and comes back to the action. A person without two-factor is told to turn it on first. APIs answer `403` with `totp_required` or `totp_setup_required`. **One setting** decides how long a code counts as fresh: `TwoFactor.StepUpMinutes`. See [client-endpoint-metadata.md](client-endpoint-metadata.md) and [sample-mvc.md](sample-mvc.md).

## External identity

All of it is set per project, in the database, without a restart ([external-identity.md](external-identity.md)).

| Feature | Notes |
|---|---|
| Social login | Google, Microsoft, GitHub, Apple, Facebook ([guide](external-identity/social-webintegration.md)) |
| Generic OpenID Connect | Keycloak, Okta, Auth0, Entra ID and others ([guide](external-identity/generic-oidc.md)) |
| SAML 2.0 | The Server is the service provider. Metadata, assertion consumer service, single logout, certificate rotation ([guide](external-identity/saml.md)) |
| LDAP and Active Directory | Password check against the directory, groups read at sign-in ([guide](external-identity/ldap.md)) |
| Passkeys | Per project, limits, backup rule, block list of authenticators ([guide](external-identity/passkeys.md)) |
| Brokering | Your app trusts only the Server and gets Server tokens. Claims and groups of the provider are mapped to local roles, groups and permissions ([brokering](external-identity/brokering.md), [group mapping](external-identity/group-mapping.md)) |
| Linking | A person links and unlinks providers. Unlinking the last way to sign in is refused |
| Secrets | Client secrets, private keys and bind passwords are encrypted. They are never in an answer or an audit event |

The Server is **not** a SAML identity provider.

## Access model

| Feature | Notes |
|---|---|
| Projects and applications | A project has many applications (clients). One person has one account and one membership per project ([concepts.md](concepts.md)) |
| Single or multi project mode | `ServerMode` |
| Permissions, roles, groups, scopes | Per project. Roles and groups give permissions. Direct grants are possible ([authorization.md](authorization.md), [access-concepts.md](access-concepts.md)) |
| Effective access | `/me` and the claims of the token |
| Sync from code | Your application pushes the permissions, roles, groups and scopes it uses. The policies decide what may be created ([permission-sync.md](permission-sync.md)) |
| Startup handshake | The Client checks the Server at start and reports its type ([handshake-and-startup.md](handshake-and-startup.md)) |
| Credential scope | Passwords and two-factor per project or shared by all ([credential-scope.md](credential-scope.md)) |
| Enabled / disabled rules | A disabled project blocks every sign-in path and token of the project. A disabled application blocks only itself |
| Two levels of settings | Project settings and application overrides, with an effective value. Some settings are for global admins only ([server-settings.md](server-settings.md)) |

## Administration

| Feature | Notes |
|---|---|
| Admin JSON API | Projects, applications, users, roles, permissions, groups, scopes, assignments, providers, settings, keys ([admin-api.md](admin-api.md)) |
| Admin console (`/admin/...`) | Dashboard with health of the database, Redis and two-way clients. Projects, applications, users, roles, permissions, groups, assignments, scopes, authorizations, providers, sessions, audit, reports, settings, branding, messaging and email templates |
| Project admins | A project admin edits only the settings marked as editable for them |
| Branding | Colours, uploaded images (logo and similar) and brand texts per project. Saving the branding or the messaging page changes only its own part of the settings |
| Messaging | Email (SMTP, Google) and SMS providers per project and application ([email-and-sms.md](email-and-sms.md)) |
| Languages | Texts and culture order, more languages can be added ([localization.md](localization.md)) |
| Seeding | The first admin and sample data ([seeding.md](seeding.md)) |
| Audit | Sign-ins, lockouts, password and two-factor changes, role and permission changes, admin writes, sync, key rotation. Never secrets |

## Client package

| Feature | Notes |
|---|---|
| UI modes | `None` (API), `Hosted` (the Server's pages), `Self` (your own pages) ([client-setup.md](client-setup.md)) |
| App types | API (JWT), cookie apps (MVC, Razor Pages), Blazor, background services |
| Authorization | Attributes and Minimal API methods for scopes, permissions, roles, groups and two-factor ([client-authorization.md](client-authorization.md), [client-endpoint-metadata.md](client-endpoint-metadata.md)) |
| Current user | `ICurrentUser` (roles, permissions, groups, confirmed email and phone, `HasGroupAsync`) and `ICurrentClient`, with a cache ([client-current-user.md](client-current-user.md)) |
| API service | `IAuth2ApiClientService` (bearer token) and `IAuth2WebClientService` (cookie): account calls and an `Admin` part for users, roles, permissions, groups and scopes ([client-api-service.md](client-api-service.md)) |
| Scanning | Finds the rules in your code and syncs them to the Server |
| Health check | One-way and two-way, shown on the dashboard ([client-health-check.md](client-health-check.md)) |
| SPA with its own API | Angular, React or Vue sign in on the hosted pages of the Server, with no login page in the SPA ([spa-with-backend.md](spa-with-backend.md)) |
| Samples | `Mvc` and `MinimalApi`, and a `docker-compose.yml` ([getting-started.md](getting-started.md)) |

## Security and operations

| Feature | Notes |
|---|---|
| Rate limits | Per policy, counted per address and login, shared by instances with Redis ([security.md](security.md)) |
| HTTP hardening | HTTPS and HSTS, security headers and a content policy, anti-forgery, safe cookies, forwarded headers, CORS off unless listed |
| Open redirect guard | Exact match or local paths only |
| Redis | Optional. Holds shared counters, one-time codes and the settings cache with a version check ([redis.md](redis.md)) |
| Settings cache | Memory plus Redis, one day, cleared at once when a setting is saved |
| Observability | Serilog logs with redaction, OpenTelemetry traces and metrics, health checks ([observability.md](observability.md)) |
| Deployment | Several instances, probes, background jobs ([deployment-and-scaling.md](deployment-and-scaling.md)) |
| Database | PostgreSQL, every table documented ([database-tables.md](database-tables.md)) |

## Not included

- A SAML identity provider, and a certified OpenID Connect provider.
- Background directory sync (LDAP data is read at sign-in).
- The gateway ([gateway.md](gateway.md)).

The full list is in [comparison-and-limits.md](comparison-and-limits.md) and [roadmap.md](roadmap.md).

## Related pages

- [Feature status](feature-status.md)
- [Authentication flows](authentication-flows.md)
- [Concepts](concepts.md)
- [Comparison and limits](comparison-and-limits.md)
- [Documentation index](index.md)
