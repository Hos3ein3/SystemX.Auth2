# Server reference

## What this is

The public surface of `SystemX.Auth2.Server` that you use or replace: the registration methods, the interfaces of the application layer (services you can call or implement), and the settings classes. The Server is an application, not a package, so most of its code is internal and is not listed here; the folders are explained in [architecture.md](architecture.md).

## When to use it

- You add the Server to a host (`AddSystemXAuth2Server`, `ServerWithSingleProject`).
- You replace a part: email or SMS delivery (`IEmailSender`, `IEmailTransport`, `ISmsTransport`), the registration defaults (`IRegistrationDefaults`), the audit service, and so on.
- You read or write settings from code (`ProjectSettings`, `ClientApplicationSettings`, `EffectiveSettings`).

## How to use it

```csharp
// src/SystemX.Auth2.Server/Program.cs
builder.Services.AddSystemXAuth2Server(builder.Configuration);
// builder.Services.ServerWithSingleProject();   // optional: single-project mode
```

Replace email delivery for the whole server (the factories are then not used), or add one more provider that an admin can select by name ([email-and-sms.md](email-and-sms.md)):

```csharp
builder.Services.AddScoped<IEmailSender, MySmtpEmailSender>();             // replaces everything
builder.Services.AddScoped<IEmailTransport, SendGridTransport>();          // adds the provider "sendgrid"
```

The settings classes (`ProjectSettings`, `ClientApplicationSettings` and their sections) have one table per section in [server-settings.md](server-settings.md): the same fields, defaults and who may edit them.

## Related pages

- [server-setup.md](server-setup.md)
- [server-settings.md](server-settings.md)
- [email-and-sms.md](email-and-sms.md)
- [architecture.md](architecture.md)

## Types (generated)

<!-- generated:start -->
## `SystemX.Auth2.Server.Application`


### AccessGate

The one rule that decides if a project and an application may be used at all. A disabled project blocks everybody in every application. A disabled application blocks only itself. Order: the project first, then the application.

| Member | What it does |
|---|---|
| `Check(Project, ClientApplicationInfo)` | Returns `project_disabled`, `application_disabled`, or null when the request may go on. |


### AdminContext

Who is calling the admin API and which application the call works on.

| Member | What it does |
|---|---|
| `(constructor)(Guid, bool, Project, Project)` | Who is calling the admin API and which application the call works on. |
| `UserId` | The admin. |
| `IsGlobalAdmin` | True for a GlobalAdmin (token from the `_global` project or the single project). |
| `Home` | The application of the token. |
| `Target` | The application the call works on (a global admin may choose it). |


### AdminDashboardMetrics

Summary metrics for the project dashboard.

| Member | What it does |
|---|---|
| `(constructor)(int, int, int, int, int, int, int, int, IReadOnlyList<AuditEventDto>, IReadOnlyList<ApplicationDto>, IReadOnlyList<ProviderStatusDto>, IReadOnlyList<ExpiringItemDto>, JobHealthStatus)` | Summary metrics for the project dashboard. |


### AdminUserView

A user with the tag used for `If-Match`.

| Member | What it does |
|---|---|
| `(constructor)(UserDto, string)` | A user with the tag used for `If-Match`. |


### ApplicationCreationResult

A new application and its client secret (plain text, shown once).

| Member | What it does |
|---|---|
| `(constructor)(ClientApplicationInfo, string)` | A new application and its client secret (plain text, shown once). |


### ApplicationSpec

What an admin gives to create an application.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, ClientApplicationSettings)` | What an admin gives to create an application. |


### AuditEntry

One audit entry. Never put secrets in it.

| Member | What it does |
|---|---|
| `(constructor)(string, Nullable<Guid>, Nullable<Guid>, string, string, string, string, string, IReadOnlyDictionary<string, object>)` | One audit entry. Never put secrets in it. |


### Auth2ServerOptions

Server options. Section `Auth2`.

| Member | What it does |
|---|---|
| `SectionName` | Configuration section name. |
| `Mode` | Server mode. Use `ServerWithSingleProject()` to set single mode. |
| `CredentialScope` | Server-wide credential scope. |
| `AllowCredentialScopeOverride` | When true, an application may override the credential scope in its settings. |
| `Issuer` | Public issuer URL. Needed in Production (HTTPS). |
| `AutoSeed` | Run the seeder at startup outside Development. |
| `AutoMigrate` | Run migrations at startup in Production. |
| `ProjectStatusCacheSeconds` | How long the "is this project / application enabled" answer is cached where tokens are validated, in seconds. A disabled project is refused at the next validation after this window (at once on the instance that made the change). 0 turns the cache off. |
| `SingleProject` | Single-project options. |
| `Messaging` | The default email and SMS providers. A project or an application can set its own (admin settings). |
| `Development` | Options that only work in Development. |
| `Redis` | Redis. Optional. Section `Auth2:Redis`. |
| `RateLimiting` | Rate limiting. Section `Auth2:RateLimiting`. |
| `Security` | HTTP hardening. Section `Auth2:Security`. |
| `Jobs` | Background jobs. Section `Auth2:Jobs`. |
| `ExternalProviders` | External identity providers. Section `Auth2:ExternalProviders`. |
| `Passkeys` | Passkeys. Section `Auth2:Passkeys`. |


### ClientApplicationInfo

A client application as the rest of the server sees it. It is a copy of the OpenIddict application row (client id, redirect URIs, scopes, consent type) and of the extra fields, so OpenIddict types stay inside the infrastructure folder.

| Member | What it does |
|---|---|
| `(constructor)(Guid, Guid, string, string, bool, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, string, ClientApplicationSettings, int, DateTimeOffset)` | A client application as the rest of the server sees it. It is a copy of the OpenIddict application row (client id, redirect URIs, scopes, consent type) and of the extra fields, so OpenIddict types stay inside the infrastructure folder. |


### ClientApplicationSettingsValidator

Rejects unsafe or broken application (client) settings.

| Member | What it does |
|---|---|
| `Validate(ClientApplicationSettings, Action<string, string>)` | Validates the settings of one application and reports every problem. |


### ClientProvisionResult

Result of a provisioning call. `NewClientSecret` is set only when the client was just created.

| Member | What it does |
|---|---|
| `(constructor)(ClientApplicationInfo, string)` | Result of a provisioning call. `NewClientSecret` is set only when the client was just created. |


### ClientScopeDefaults

Scopes a new application may request unless the admin says otherwise.

| Member | What it does |
|---|---|
| `DefaultScopes` | Default allowed scopes of a new application. |


### ConsentPolicy

Which consent mode applies.

| Member | What it does |
|---|---|
| `Effective(ProjectSettings)` | With `RequireConsent` off the mode is implicit. With it on, an implicit mode means explicit. |


### DevelopmentOptions

Development helpers. Section `Auth2:Development`. They are ignored outside Development.

| Member | What it does |
|---|---|
| `LogMessageBodies` | The development email and SMS senders log the full message (links and codes) when this is true. That puts secrets in the log, so it works only in Development. Default: false (only a masked recipient and the subject are logged). |


### EffectiveAccess

What a user may do in the current application. Lists are sorted and have no duplicates.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, int, IReadOnlyList<string>, IReadOnlyList<string>)` | What a user may do in the current application. Lists are sorted and have no duplicates. |
| `Roles` | Own roles plus roles of the user's groups. |
| `Permissions` | Direct permissions, permissions of the roles above, and permissions of the user's groups. |
| `Groups` | Names of the user's groups. |
| `AccessVersion` | The access version (application version + user version). It only grows. |
| `DirectPermissions` | Only the permissions granted to the user directly. |
| `RolePermissions` | Only the permissions that come from the user's roles or the roles of the user's groups. |


### EffectiveSettings

The value that counts for one request: the project value, overridden by the application value where the application sets one. This is the only place where the precedence is written down.

| Member | What it does |
|---|---|
| `(constructor)(ProjectSettings, ClientApplicationInfo)` | The value that counts for one request: the project value, overridden by the application value where the application sets one. This is the only place where the precedence is written down. |
| `Project` | The project level settings. |
| `Application` | The calling application, or null when the request has no client. |
| `UiMode` | UI mode: application, else the project default. |
| `CookieLifetimeMinutes` | Cookie lifetime in minutes: application, else project, else null (session cookie). |
| `AccessTokenMinutes` | Access token lifetime in minutes. |
| `RefreshTokenDays` | Refresh token lifetime in days. |
| `IdentityTokenMinutes` | Identity token lifetime in minutes. |
| `ConsentMode` | Consent mode: application, else project. |
| `AllowSelfUiLogin` | True when this client may use the password grant and the two-factor grants (Self UI). |
| `AllowedRedirectUris` | Redirect URIs of the application (OpenIddict field). Empty when the request has no client. |
| `SendConfirmationEmail` | True when the confirmation email is sent right after registration: application, else project; always true when a confirmed email is required. |
| `EmailProvider` | The email provider chosen for this request: application, else project. Null means "use the server default". |
| `SmsProvider` | The SMS provider chosen for this request: application, else project. Null means "use the server default". |
| `FilterProviders(IEnumerable<T>, Func<T, string>)` | The external providers this client shows: a subset of the project's providers (all when the application lists none). |


### EmailMessage

An email. `Data` holds the raw token or code for tests and custom templates. Never log it.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, IReadOnlyDictionary<string, string>)` | An email. `Data` holds the raw token or code for tests and custom templates. Never log it. |


### EmailProviderDto

The email provider as the admin sees it. The password is never returned.

| Member | What it does |
|---|---|
| `(constructor)(string, string, int, SmtpSecurity, string, bool, string, string, int)` | The email provider as the admin sees it. The password is never returned. |


### EmailProviderUpdate

A change of the email provider. `Password`: null keeps the saved password, an empty text removes it, any other text replaces it. Fields that are null keep their value.

| Member | What it does |
|---|---|
| `(constructor)(string, string, Nullable<int>, Nullable<SmtpSecurity>, string, string, string, string, Nullable<int>)` | A change of the email provider. `Password`: null keeps the saved password, an empty text removes it, any other text replaces it. Fields that are null keep their value. |


### EmailTransportConfig

An email provider setting with the password already decrypted. Lives for one send; never log it.

| Member | What it does |
|---|---|
| `(constructor)(string, string, int, SmtpSecurity, string, string, string, string, int)` | An email provider setting with the password already decrypted. Lives for one send; never log it. |


### ExpiringItemDto

Expiring key or certificate warning item.

| Member | What it does |
|---|---|
| `(constructor)(string, string, DateTimeOffset, int)` | Expiring key or certificate warning item. |


### ExternalCallbackContext

Input of a callback. `Parameters` are the query (GET) or form (POST) values.

| Member | What it does |
|---|---|
| `(constructor)(RegisteredProvider, string, IReadOnlyDictionary<string, string>, string)` | Input of a callback. `Parameters` are the query (GET) or form (POST) values. |


### ExternalCallbackResult

Result of a callback: the identity and the return URL that was chosen at the start.

| Member | What it does |
|---|---|
| `(constructor)(ExternalIdentity, string, string)` | Result of a callback: the identity and the return URL that was chosen at the start. |
| `IdpInitiated` | The provider started this sign-in (SAML IdP-initiated). The return URL is then never taken from the provider. |


### ExternalChallenge

Where to send the browser, and the protected state the endpoint must keep in a short-lived cookie until the callback.

| Member | What it does |
|---|---|
| `(constructor)(Uri, string, TimeSpan)` | Where to send the browser, and the protected state the endpoint must keep in a short-lived cookie until the callback. |
| `PostFields` | When the provider needs a browser form post (SAML POST binding), the data for the form. `RedirectUri` is then the form action. |


### ExternalChallengeContext

Input of a challenge (the start of a redirect sign-in).

| Member | What it does |
|---|---|
| `(constructor)(RegisteredProvider, string, string, string, string)` | Input of a challenge (the start of a redirect sign-in). |
| `Provider` | The provider. |
| `Issuer` | Public address of this Server. |
| `ReturnUrl` | Where to go afterwards. |
| `LoginHint` | Email or name to prefill at the provider. |
| `LinkUserId` | Set when a signed-in user links the provider instead of signing in. |


### ExternalIdentity

What an external source tells the Server about a person, after the Server checked the answer. `Attributes` are kept in memory only: they are never stored or logged as a whole.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, Nullable<bool>, string, string, string, IReadOnlyList<string>, IReadOnlyDictionary<string, string[]>)` | What an external source tells the Server about a person, after the Server checked the answer. `Attributes` are kept in memory only: they are never stored or logged as a whole. |
| `DisplayName` | Display name, when the source gives one. |
| `Picture` | Picture URL, when the source gives one. |
| `UsedMultiFactor` | The source says the user signed in with several factors (used with `TrustProviderMfa`). |
| `Tokens` | Upstream tokens (only when the provider setting asks for them). |


### ExternalLinkInfo

One external link of a user.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | One external link of a user. |


### ExternalLoginException

A failed external sign-in. `ErrorCode` is a stable code of `Auth2ErrorCodes`. `Reason` is a short safe word for the audit log (never user data).

| Member | What it does |
|---|---|
| `(constructor)(string, string, Exception)` | A failed external sign-in. `ErrorCode` is a stable code of `Auth2ErrorCodes`. `Reason` is a short safe word for the audit log (never user data). |
| `ErrorCode` | Stable error code. |
| `Reason` | Short reason for the audit log. |


### ExternalLoginOutcome

The decision about an external sign-in.

| Member | What it does |
|---|---|
| `Login` | The login result when a user was found or made. Null when `ErrorCode` or `NeedsEmail` is set. |
| `ErrorCode` | Stable error code when the sign-in was refused before the login pipeline. |
| `NeedsEmail` | The source gave no email and the application needs one: the user must enter and confirm it (`PendingToken`). |
| `PendingToken` | Sealed, short-lived copy of the identity for the email step. |
| `Registered` | A new user was created. |
| `Linked` | A login was added to an existing user. |
| `ReturnUrl` | Where to go afterwards. |


### ExternalLoginRequest

What `IExternalLoginService` needs to decide a sign-in that an external source proved.

| Member | What it does |
|---|---|
| `(constructor)(RegisteredProvider, ExternalIdentity, string, string, string, bool)` | What `IExternalLoginService` needs to decide a sign-in that an external source proved. |
| `Provider` | The provider (from the registry). |
| `Identity` | The checked identity. |
| `ReturnUrl` | Where to go afterwards. |
| `IpAddress` | Caller IP, for the audit log. |
| `UserAgent` | Caller user agent, for the audit log. |
| `IsDirectoryLogin` | True for LDAP: the method is `Ldap` and the amr is `pwd`. |


### ExternalProviderOptions

How the Server keeps its list of external providers fresh.

| Member | What it does |
|---|---|
| `PollSeconds` | How often every instance looks for changed provider settings, in seconds. Default 30. Redis makes changes visible at once; the poll is the safety net. |
| `AllowPrivateNetworks` | For tests and local work only: allow http and private network addresses for provider URLs. Never turn this on in production. |
| `StateMinutes` | How long the state of a started sign-in lives, in minutes. Default 10. |


### ExternalProviderSettingsValidator

Checks the external providers and the passkey rules of one application.

| Member | What it does |
|---|---|
| `MaxProviders` | Most providers one application may have. |
| `Validate(ProjectSettings, Action<string, string>)` | Validates the provider list and the passkey settings. |
| `DecodeCertificate(string)` | Reads base64 DER or PEM into bytes. |


### ExternalProviderTokens

Tokens of the upstream provider. Only filled when `StoreProviderTokens` is on.

| Member | What it does |
|---|---|
| `(constructor)(string, string, Nullable<DateTimeOffset>)` | Tokens of the upstream provider. Only filled when `StoreProviderTokens` is on. |


### ExternalProviderUrls

Builds the names and URLs of providers. URLs are unique per application and provider (protection against mix-up attacks).

| Member | What it does |
|---|---|
| `SingleModeSlug` | The project slug used in URLs: the project key in multi mode, `default` in single mode. |
| `RegistrationName(Guid, string)` | Registration name that is unique in the whole server. |
| `OidcCallbackPath(string, string)` | Path of the OIDC callback. |
| `SamlAcsPath(string, string)` | Path of the SAML assertion consumer service. |
| `SamlSloPath(string, string)` | Path of the SAML single logout service. |
| `SamlMetadataPath(string, string)` | Path of the SAML metadata. |
| `For(RegisteredProvider, string)` | The full URLs the admin must enter at the provider. |


### ExternalSignInRequest

Input of a login that an external source already proved (social, OIDC, SAML, LDAP, passkey). The user is known; this request only asks the one login pipeline (application, method, membership, lockout, two-factor, session) whether the user may enter.

| Member | What it does |
|---|---|
| `(constructor)(AppUser, LoginMethod, string, IReadOnlyList<string>, bool, string, string)` | Input of a login that an external source already proved (social, OIDC, SAML, LDAP, passkey). The user is known; this request only asks the one login pipeline (application, method, membership, lockout, two-factor, session) whether the user may enter. |
| `User` | The user that the external source identified. |
| `Method` | The login method: `External`, `Ldap` or `Passkey`. |
| `ProviderName` | Name of the provider (becomes the `idp` claim). Null for passkeys. |
| `Amr` | Authentication method references for the `amr` claim. |
| `SkipLocalTwoFactor` | The source already used several factors (trusted provider MFA, passkey with user verification). |
| `IpAddress` | Caller IP (for the audit log). |
| `UserAgent` | Caller user agent (for the audit log). |


### IAccessService

Reads and changes access data (permissions, roles, role permissions, direct grants) of the **current application**. Every change that can alter someone's access raises the access version, so cached data and the `pv` claim refresh.

| Member | What it does |
|---|---|
| `GetAccessVersionAsync(AppUser, CancellationToken)` | The access version of a user in the current application. Cheap: one small query. |
| `GetEffectiveAccessAsync(AppUser, CancellationToken)` | Effective access. Cached by (application, user, access version). |
| `GetCurrentUserAsync(AppUser, IEnumerable<string>, CancellationToken)` | Builds the `/me` answer. |
| `HasPermissionAsync(AppUser, string, CancellationToken)` | True when the user has the permission (key is normalized first). |
| `ListPermissionsAsync(CancellationToken)` | All permissions of the application. |
| `CreatePermissionAsync(PermissionDefinition, CancellationToken)` | Creates a permission (manual). Succeeds without change when it already exists. |
| `SetPermissionObsoleteAsync(string, bool, CancellationToken)` | Marks a permission as obsolete (or not). Grants stay. |
| `DeletePermissionAsync(string, CancellationToken)` | Deletes a permission and every grant of it. |
| `GrantPermissionAsync(AppUser, string, Nullable<Guid>, CancellationToken)` | Grants a permission directly to a user. Succeeds without change when already granted. |
| `RevokePermissionAsync(AppUser, string, CancellationToken)` | Removes a direct grant. |
| `ListRolesAsync(CancellationToken)` | All roles visible in the application with their permissions. |
| `CreateRoleAsync(string, string, CancellationToken)` | Creates a role in the current application. |
| `DeleteRoleAsync(string, CancellationToken)` | Deletes a role of the current application (not a system role). |
| `AssignRoleAsync(AppUser, string, CancellationToken)` | Gives a role to a user. |
| `RemoveRoleAsync(AppUser, string, CancellationToken)` | Takes a role from a user. |
| `AddRolePermissionAsync(string, string, CancellationToken)` | Adds a permission to a role. The permission must exist. |
| `RemoveRolePermissionAsync(string, string, CancellationToken)` | Removes a permission from a role. |


### IAccessVersionService

Raises the access versions, so caches of access data (the `pv` claim) are refreshed.

| Member | What it does |
|---|---|
| `BumpUserAsync(AppUser, CancellationToken)` | Raises the user's version in the current application. |
| `BumpProjectAsync(CancellationToken)` | Raises the application's version. |


### IAdminApplicationService

Admin work on the applications of a project. Every method checks that the application belongs to the project of the route.

| Member | What it does |
|---|---|
| `ListAsync(Guid, CancellationToken)` | The applications of a project. |
| `GetAsync(Guid, Guid, CancellationToken)` | One application of the project, or null. |
| `CreateAsync(Guid, CreateApplicationRequest, string, CancellationToken)` | Creates an application. The secret is in the answer and is shown once. |
| `UpdateAsync(Guid, Guid, UpdateApplicationRequest, Nullable<Guid>, CancellationToken)` | Changes name, redirect URIs, allowed scopes and the enabled flag. |
| `DeleteAsync(Guid, Guid, CancellationToken)` | Deletes an application with its tokens and authorizations. |
| `RotateSecretAsync(Guid, Guid, bool, string, CancellationToken)` | Rotates the client secret and returns the setup with the new secret (shown once). A project admin needs `Clients.AllowProjectAdminRotateSecret`. |
| `GetSetupAsync(Guid, Guid, string, CancellationToken)` | The copy-paste setup of an application (without the secret). |
| `GetSettingsAsync(Guid, Guid, CancellationToken)` | The settings overrides of an application and their version. |
| `UpdateSettingsAsync(Guid, Guid, ClientApplicationSettings, bool, Nullable<int>, CancellationToken)` | Replaces the settings overrides. |


### IAdminAuthorizationService

Admin work on OpenIddict authorizations.

| Member | What it does |
|---|---|
| `ListAsync(PageQuery, string, string, CancellationToken)` | Lists authorizations with pagination and filters. |
| `RevokeAsync(string, CancellationToken)` | Revokes one authorization and its tokens. |
| `RevokeAllAsync(string, string, CancellationToken)` | Revokes authorizations by application or subject. |


### IAdminBrandingService

Admin work on branding assets and email templates.

| Member | What it does |
|---|---|
| `ListAssetsAsync(Guid, CancellationToken)` | Lists branding assets of an application. |
| `GetAssetAsync(Guid, string, CancellationToken)` | Gets raw binary branding asset. |
| `UploadAssetAsync(Guid, string, string, Byte[], CancellationToken)` | Uploads or replaces a branding asset. |
| `DeleteAssetAsync(Guid, string, CancellationToken)` | Deletes a branding asset. |
| `ListTemplatesAsync(Guid, CancellationToken)` | Lists email templates of an application. |
| `GetTemplateAsync(Guid, string, string, CancellationToken)` | Gets one email template. |
| `SaveTemplateAsync(Guid, string, string, string, string, CancellationToken)` | Saves or updates an email template. |
| `ResetTemplateAsync(Guid, string, string, CancellationToken)` | Resets an email template to default. |


### IAdminDashboardService

Use case for generating the admin dashboard metrics.

| Member | What it does |
|---|---|
| `GetMetricsAsync(Guid, CancellationToken)` | Computes or loads cached dashboard metrics for a project. |


### IAdminExternalService

The admin side of external providers, mappings, links and passkeys. Always works on one application.

| Member | What it does |
|---|---|
| `Types()` | All provider types with their fields. |
| `ListProvidersAsync(Guid, CancellationToken)` | The providers of an application (enabled or not), without secrets. |
| `CreateProviderAsync(Guid, string, bool, CancellationToken)` | Adds a provider. `body` is the provider JSON with the secret in plain fields (`clientSecret`, `privateKey`, `bindPassword`). |
| `UpdateProviderAsync(Guid, string, string, bool, Nullable<int>, CancellationToken)` | Changes a provider. An empty or missing secret keeps the old one. `ifMatchVersion` is the settings version of the application. |
| `RotateSecretAsync(Guid, string, RotateProviderSecretRequest, CancellationToken)` | Replaces the secret. |
| `DeleteProviderAsync(Guid, string, CancellationToken)` | Removes a provider. The links of users stay in the database. |
| `TestProviderAsync(Guid, string, CancellationToken)` | Tests a provider without signing anybody in. |
| `LdapTestUserAsync(Guid, string, string, CancellationToken)` | LDAP: looks up a user name (no password). |
| `SamlRefreshMetadataAsync(Guid, string, CancellationToken)` | SAML: reads the identity provider metadata again now. |
| `SamlGenerateCertificateAsync(Guid, string, CancellationToken)` | SAML: makes a new service provider certificate. Returns only the public part. |
| `ListMappingsAsync(Guid, CancellationToken)` | Mappings of the application. |
| `CreateMappingAsync(Guid, ExternalMappingRequest, CancellationToken)` | Adds a mapping. |
| `UpdateMappingAsync(Guid, Guid, ExternalMappingRequest, CancellationToken)` | Changes a mapping. |
| `DeleteMappingAsync(Guid, Guid, CancellationToken)` | Removes a mapping. |
| `ListEmailDomainRoutesAsync(Guid, CancellationToken)` | The email domains of every provider. |
| `SetEmailDomainRoutesAsync(Guid, IReadOnlyList<EmailDomainRouteDto>, CancellationToken)` | Replaces the email domains of all providers at once. A domain belongs to one provider. |
| `ListUserLoginsAsync(Guid, CancellationToken)` | External logins of a user of the current application. |
| `RemoveUserLoginAsync(Guid, string, bool, CancellationToken)` | Removes an external login of a user (support case). A global admin may force it when it is the last way to sign in. |
| `ListUserPasskeysAsync(Guid, CancellationToken)` | Passkeys of a user of the current application. |
| `RemoveUserPasskeyAsync(Guid, string, CancellationToken)` | Removes a passkey of a user (for example a lost device). |
| `GetBlockedAaguidsAsync(CancellationToken)` | The blocked authenticator models (from the configuration and from the database). |
| `SetBlockedAaguidsAsync(IReadOnlyList<Guid>, CancellationToken)` | Replaces the blocked authenticator models that are kept in the database. |


### IAdminJobService

Admin work on maintenance jobs and job run history.

| Member | What it does |
|---|---|
| `GetStatusAsync(CancellationToken)` | Gets status summary of all maintenance jobs. |
| `ListRunsAsync(PageQuery, string, Nullable<bool>, CancellationToken)` | Lists job run history records. |
| `RunNowAsync(string, CancellationToken)` | Runs a maintenance job immediately. |


### IAdminMessagingService

Reads and changes how a project or an application sends email and SMS. Only a global admin may change it.

| Member | What it does |
|---|---|
| `GetProjectAsync(Guid, CancellationToken)` | Settings of a project. Null when the project does not exist. |
| `UpdateProjectAsync(Guid, MessagingUpdate, Nullable<Guid>, CancellationToken)` | Changes the settings of a project. |
| `GetApplicationAsync(Guid, Guid, CancellationToken)` | Settings of one application (`Inherited` when it has none of its own). Null when it does not exist. |
| `UpdateApplicationAsync(Guid, Guid, MessagingUpdate, Nullable<Guid>, CancellationToken)` | Changes the settings of one application. |
| `ClearApplicationAsync(Guid, Guid, Nullable<Guid>, CancellationToken)` | Removes the own settings of an application, so it uses the project's again. |


### IAdminProjectService

Admin work on projects (global admin), and on the own project (project admin: read and settings).

| Member | What it does |
|---|---|
| `ListAsync(PageQuery, CancellationToken)` | Pages through projects. Only a global admin calls this. |
| `GetAsync(Guid, CancellationToken)` | One project, or null. |
| `CreateAsync(CreateProjectRequest, CancellationToken)` | Creates a project (multi-project mode only). |
| `UpdateAsync(Guid, UpdateProjectRequest, Nullable<Guid>, CancellationToken)` | Updates name, description and the enabled flag. The project key never changes. |
| `SetEnabledAsync(Guid, bool, string, Nullable<Guid>, CancellationToken)` | Enables or disables a project. Disabling revokes its tokens and sessions and is audited with the reason. |
| `DeleteAsync(Guid, CancellationToken)` | Deletes a project with its data and its applications. Not `_global`. |
| `GetSettingsAsync(Guid, CancellationToken)` | The settings and their version. |
| `UpdateSettingsAsync(Guid, ProjectSettings, bool, Nullable<int>, CancellationToken)` | Replaces the settings. A project admin may change only `ProjectAdminEditable` settings. `ifMatchVersion` (optional) must equal the current version. |


### IAdminReportService

Use case for generating administrative reports.

| Member | What it does |
|---|---|
| `GetReportDataAsync(Guid, string, DateTimeOffset, DateTimeOffset, CancellationToken)` | Executes a standard report query. |
| `ExportCsvAsync(Guid, string, DateTimeOffset, DateTimeOffset, CancellationToken)` | Exports report data as CSV bytes. |


### IAdminScopeService

Admin work on OpenIddict scopes.

| Member | What it does |
|---|---|
| `ListAsync(PageQuery, string, CancellationToken)` | Lists scopes with pagination, search, and filtering. |
| `GetAsync(Guid, CancellationToken)` | Gets one scope by id. |
| `CreateAsync(CreateScopeRequest, bool, CancellationToken)` | Creates a scope. |
| `UpdateAsync(Guid, UpdateScopeRequest, bool, CancellationToken)` | Updates an existing scope. |
| `DeleteAsync(Guid, bool, CancellationToken)` | Deletes a scope if not in use by any application. |


### IAdminSessionService

Admin work on active/revoked sessions across the application.

| Member | What it does |
|---|---|
| `ListAsync(PageQuery, string, string, string, CancellationToken)` | Lists sessions with pagination and filters. |
| `RevokeAsync(string, CancellationToken)` | Revokes one session by authorization id. |
| `RevokeAllAsync(string, CancellationToken)` | Revokes all sessions for a subject or the whole application. |


### IAdminUserService

Admin work on the users of the **current application**.

| Member | What it does |
|---|---|
| `ListAsync(PageQuery, Nullable<bool>, CancellationToken)` | Pages through the users of the application. Sort: `username`, `email`, `created` (default), `lastlogin`. |
| `GetAsync(Guid, CancellationToken)` | One user of the application, or null. |
| `CreateAsync(AdminCreateUserRequest, CancellationToken)` | Creates a user and the membership. When the person exists already, only the membership is added: an admin can never set a password for an existing person (the person uses the password reset or join flow). |
| `UpdateAsync(Guid, AdminUpdateUserRequest, string, CancellationToken)` | Updates the profile. `ifMatch` is the tag from `ETag` (optional). |
| `RemoveAsync(Guid, CancellationToken)` | Removes the user from the application (multi mode) or deletes the user (single mode). Sessions end. |
| `LockAsync(Guid, Nullable<DateTimeOffset>, CancellationToken)` | Locks the user in this application and ends the sessions. |
| `UnlockAsync(Guid, CancellationToken)` | Unlocks and resets the failed count. |
| `ResetTwoFactorAsync(Guid, CancellationToken)` | Turns two-factor off, drops the authenticator key and recovery codes, ends the sessions. |
| `ListSessionsAsync(Guid, CancellationToken)` | Open sessions of a user in the application. |
| `RevokeSessionsAsync(Guid, string, CancellationToken)` | Ends one session, or every session when `sessionId` is null. |
| `BulkActionAsync(AdminBulkUserRequest, CancellationToken)` | Runs bulk actions on multiple users in the application. |


### IApplicationService

Creates and changes applications. Everything goes through `IOpenIddictApplicationManager`, never raw EF.

| Member | What it does |
|---|---|
| `CreateAsync(Guid, ApplicationSpec, CancellationToken)` | Creates an application in a project. The client id is `{projectkey}.{name}` unless the spec gives one. The secret is shown once. |
| `UpdateAsync(Guid, ApplicationSpec, CancellationToken)` | Changes name, redirect URIs, allowed scopes and settings. Null values stay as they are. |
| `SetEnabledAsync(Guid, bool, string, Nullable<Guid>, CancellationToken)` | Enables or disables one application. Disabling revokes only the tokens and authorizations of that client. |
| `DeleteAsync(Guid, CancellationToken)` | Deletes an application with its tokens and authorizations. |
| `RotateSecretAsync(string, CancellationToken)` | Makes a new client secret. The old one stops working. The new one is shown only once. |
| `FindAsync(Guid, CancellationToken)` | Finds an application by id (global scope). |
| `FindByClientIdAsync(string, CancellationToken)` | Finds an application by client id (global scope). |
| `ListAsync(Guid, CancellationToken)` | Lists the applications of a project. |


### IAuditReader

Reads audit events.

| Member | What it does |
|---|---|
| `QueryAsync(PageQuery, string, Nullable<Guid>, string, Nullable<DateTimeOffset>, Nullable<DateTimeOffset>, bool, CancellationToken)` | Pages through events, newest first. With `allProjects` every application is included (only a global admin may ask); otherwise the current application only. |


### IAuditService

Writes audit events.

| Member | What it does |
|---|---|
| `WriteAsync(AuditEntry, CancellationToken)` | Writes an event. |


### IAuth2Localizer

Gives localized texts from the resx files. Order: selected culture, then English, then the key itself.

| Member | What it does |
|---|---|
| `Error(string, object[])` | Text for an error code (`Auth2ErrorCodes`) or an Identity error key. |
| `Validation(string, object[])` | Text for a validation key. |


### IAuthFlowResolver

Reads the rules of the current application. Identity options are global and cannot change per request, so validators and managers ask this resolver instead.

| Member | What it does |
|---|---|
| `GetProjectAsync(CancellationToken)` | The current application. |
| `GetSettingsAsync(CancellationToken)` | Settings of the current project. |
| `GetClientAsync(CancellationToken)` | The calling application (from the client id of the request), or null when the request has none. |
| `GetEffectiveAsync(CancellationToken)` | Project settings with the application overrides applied (the effective values for this request). |
| `CheckGateAsync(CancellationToken)` | The shared gate. Returns `project_disabled` or `application_disabled` when the request must be refused, else null. Every sign-in path asks this one method. The public answer is always `access_denied`; the real reason goes to the audit log. |
| `GetCredentialScopeAsync(CancellationToken)` | Where credentials live for the current application. Single mode always gives `Shared`. |


### IClientApplicationContext

The client application of the current request (from the client id of the token or the authorize request).

| Member | What it does |
|---|---|
| `GetAsync(CancellationToken)` | The calling application, or null when the request has no client id. |
| `GetRequiredAsync(CancellationToken)` | The calling application. |


### IClientHandshakeService

The startup check a client makes. The application comes from the token; the request is only compared with it.

| Member | What it does |
|---|---|
| `CheckAsync(ClientHandshakeRequest, CancellationToken)` | Checks project key, application name and server mode against the token's application. |


### IClientIdAccessor

Where the client id of the current request comes from (token or authorize request).

| Member | What it does |
|---|---|
| `ClientId` | The client id, or null. |


### IClientProvisioner

The only code that creates, changes or deletes OpenIddict application rows. It uses the OpenIddict manager so OpenIddict validation and caching stay correct, and reads the extra fields from the same row afterwards.

| Member | What it does |
|---|---|
| `CreateAsync(Project, ApplicationSpec, DateTimeOffset, CancellationToken)` | Creates an application row with a new secret. |
| `UpdateAsync(Guid, ProjectSettings, ApplicationSpec, Nullable<bool>, DateTimeOffset, CancellationToken)` | Changes the row. Null values of the spec stay as they are. Secrets are never changed here. |
| `ResyncProjectAsync(Guid, ProjectSettings, DateTimeOffset, CancellationToken)` | Writes the values derived from the project settings (consent type, grants) into every application of the project. |
| `RotateSecretAsync(string, CancellationToken)` | Creates a new secret for the client and returns it (shown once). |
| `DeleteAsync(Guid, CancellationToken)` | Deletes the application row. Its tokens and authorizations go with it. Nothing happens when it does not exist. |
| `RevokeAsync(Guid, Nullable<Guid>, CancellationToken)` | Revokes all tokens and authorizations of one application (or of all applications of a project when `applicationId` is null). |


### IConfirmationService

Email and phone confirmation.

| Member | What it does |
|---|---|
| `ConfirmEmailAsync(Guid, string, CancellationToken)` | Confirms an email with the token from the email. |
| `ResendEmailAsync(string, string, CancellationToken)` | Sends the email confirmation again. Always looks the same to the caller. |
| `ConfirmPhoneAsync(string, string, CancellationToken)` | Confirms a phone number with the code from the SMS. |


### IConsentService

Consent rules and records.

| Member | What it does |
|---|---|
| `IsConsentNeededAsync(AppUser, ClientApplicationInfo, EffectiveSettings, IReadOnlyCollection<string>, string, CancellationToken)` | True when the user must be asked. A valid grant token (made by the consent page) satisfies it. |
| `CreateGrantToken(AppUser, ClientApplicationInfo, IEnumerable<string>)` | Makes a short-lived token that says "this user agreed to these scopes for this application". |
| `FindAuthorizationIdAsync(AppUser, ClientApplicationInfo, IReadOnlyCollection<string>, CancellationToken)` | Id of a saved (permanent) authorization that covers the scopes, or null. |
| `SaveAuthorizationAsync(AppUser, ClientApplicationInfo, ClaimsPrincipal, IReadOnlyCollection<string>, CancellationToken)` | Saves a permanent authorization (explicit mode) and returns its id. |


### ICredentialState

Where the credential state (password, security stamp, 2FA, lockout, TOTP key, recovery codes) of a user is read and written. Two strategies: the user row, or the `user_projects` row of the current project.

| Member | What it does |
|---|---|
| `GetPasswordHashAsync(AppUser, CancellationToken)` | Password hash. |
| `SetPasswordHashAsync(AppUser, string, CancellationToken)` | Sets the password hash. |
| `GetSecurityStampAsync(AppUser, CancellationToken)` | Security stamp. |
| `SetSecurityStampAsync(AppUser, string, CancellationToken)` | Sets the security stamp. |
| `GetTwoFactorEnabledAsync(AppUser, CancellationToken)` | Is TOTP two-factor on. |
| `SetTwoFactorEnabledAsync(AppUser, bool, CancellationToken)` | Turns two-factor on or off. |
| `GetLockoutEndAsync(AppUser, CancellationToken)` | End of lockout. |
| `SetLockoutEndAsync(AppUser, Nullable<DateTimeOffset>, CancellationToken)` | Sets the end of lockout. |
| `GetAccessFailedCountAsync(AppUser, CancellationToken)` | Failed attempts so far. |
| `IncrementAccessFailedCountAsync(AppUser, CancellationToken)` | Adds one failed attempt and returns the new count. |
| `ResetAccessFailedCountAsync(AppUser, CancellationToken)` | Sets failed attempts to zero. |
| `GetLockoutEnabledAsync(AppUser, CancellationToken)` | Can this user be locked out. |
| `SetLockoutEnabledAsync(AppUser, bool, CancellationToken)` | Sets whether the user can be locked out. |
| `GetAuthenticatorKeyAsync(AppUser, CancellationToken)` | TOTP key (decrypted). |
| `SetAuthenticatorKeyAsync(AppUser, string, CancellationToken)` | Saves the TOTP key (encrypted at rest). |
| `ReplaceRecoveryCodesAsync(AppUser, IEnumerable<string>, CancellationToken)` | Replaces all recovery codes. Only hashes are stored. |
| `RedeemRecoveryCodeAsync(AppUser, string, CancellationToken)` | Uses one recovery code. True when the code was valid and unused. |
| `CountRecoveryCodesAsync(AppUser, CancellationToken)` | Number of unused recovery codes. |
| `BeforeUserCreated(AppUser)` | Called before a new user is saved, so pending credential rows can be saved in the same transaction. |


### ICredentialStateProvider

Chooses the `ICredentialState` strategy for the current application.

| Member | What it does |
|---|---|
| `GetAsync(CancellationToken)` | Returns the strategy for the current application. |


### IDistributedLock

Runs work so that only one instance does it at a time (a lock in the database).

| Member | What it does |
|---|---|
| `TryRunAsync(string, Func<CancellationToken, Task>, CancellationToken)` | Runs `work` when the lock is free. Returns false (and does nothing) when another instance holds it. |
| `RunExclusiveAsync(string, Func<CancellationToken, Task>, CancellationToken)` | Waits for the lock, then runs `work`. Only one caller (in any instance) runs it at a time. |


### IEmailFactory

Gives the email sender for the current request. The application's provider wins, then the project's, then the server default (`Auth2:Messaging`), then logging.

| Member | What it does |
|---|---|
| `ProviderNames` | The names of the providers the server has. |
| `CreateAsync(CancellationToken)` | The sender that fits the project and application of the current request. |


### IEmailSender

Sends emails. The development implementation only logs. Replace it in production.

| Member | What it does |
|---|---|
| `SendAsync(EmailMessage, CancellationToken)` | Sends one email. |


### IEmailTransport

One way to deliver email (SMTP, Google, a mail API ...). Register your own with `services.AddSingleton<IEmailTransport, MyTransport>()`; an admin then selects it by `Name`.

| Member | What it does |
|---|---|
| `Name` | Provider name, lowercase (for example `smtp`). |
| `SendAsync(EmailTransportConfig, EmailMessage, CancellationToken)` | Sends one email, or throws. |


### IExternalIdentitySource

A source of external identities.

| Member | What it does |
|---|---|
| `Handles(ExternalProviderType)` | The types this source handles. |


### IExternalLinkService

Lists, links and unlinks the external logins of a user in the current application.

| Member | What it does |
|---|---|
| `ListAsync(AppUser, CancellationToken)` | The links of the user. |
| `LinkAsync(AppUser, ExternalIdentity, CancellationToken)` | Adds a link. Fails with `external_claims_invalid` when the identity belongs to another user already. |
| `UnlinkAsync(AppUser, string, string, CancellationToken)` | Removes a link. Fails with `cannot_remove_last_login_method` when it is the last way to sign in. |


### IExternalLoginService

The one decision table for OIDC, SAML and LDAP sign-ins: find a link, link by verified email, register, map claims and groups, sign in.

| Member | What it does |
|---|---|
| `HandleAsync(ExternalLoginRequest, CancellationToken)` | Decides and signs in. |
| `CompleteWithEmailAsync(string, string, CancellationToken)` | Finishes a sign-in that needed an email (without proof: the caller must send the confirmation mail first). |
| `SendEmailConfirmationAsync(string, string, string, CancellationToken)` | Sends the confirmation mail for the email step. Returns false when the token is not valid. |
| `ConfirmEmailAsync(string, CancellationToken)` | Opens the link of a confirmation mail and finishes the sign-in. |


### IExternalMappingService

Applies the mappings of an application to a user at login.

| Member | What it does |
|---|---|
| `ApplyAsync(Guid, string, AppUser, ExternalIdentity, CancellationToken)` | Adds the roles, groups and permissions that the identity earns, and removes only those that some mapping of this provider mentions but the identity no longer earns. Things that no mapping mentions are never touched. Returns how many changes were made. |


### IGlobalScope

Explicit way to bypass the per-application query filters. Only global developer code may use it (seeding, global admin, background jobs).

| Member | What it does |
|---|---|
| `IsActive` | True while a global scope is active. |
| `Begin()` | Starts a global scope. Dispose the result to end it. Scopes can be nested. |


### IGroupService

Groups of the current application: members, roles and permissions of a group.

| Member | What it does |
|---|---|
| `ListAsync(CancellationToken)` | All groups. |
| `CreateAsync(string, string, string, CancellationToken)` | Creates a group. The name is lowercase, unique per application. |
| `DeleteAsync(string, CancellationToken)` | Deletes a group with its links. |
| `AddUserAsync(string, AppUser, CancellationToken)` | Adds a user to a group. |
| `RemoveUserAsync(string, AppUser, CancellationToken)` | Removes a user from a group. |
| `AddRoleAsync(string, string, CancellationToken)` | Gives a role to every member of the group. |
| `RemoveRoleAsync(string, string, CancellationToken)` | Takes a role from the group. |
| `AddPermissionAsync(string, string, CancellationToken)` | Gives a permission to every member of the group. |
| `RemovePermissionAsync(string, string, CancellationToken)` | Takes a permission from the group. |


### IGroupSyncService

Idempotent add of the groups a client sends (current application). Needs `Clients.GroupSyncAllowed` (off by default). Only missing groups are created; display names of existing groups are not changed; nothing is deleted.

| Member | What it does |
|---|---|
| `SyncAsync(GroupSyncRequest, CancellationToken)` | Syncs a list. Group names follow the rules of permission keys (lowercase, 1–100 characters). |


### IJoinService

Joining a second application. Never "just add a row": the person must prove they own the account with a one-time code sent to a confirmed email or phone, and then sets a password for this application.

| Member | What it does |
|---|---|
| `RequestAsync(string, CancellationToken)` | Sends the proof code. Silent when the person is unknown, has no confirmed email or phone, or asked too soon. |
| `RequestForUserAsync(AppUser, CancellationToken)` | Sends the proof code to a user that is already known. |
| `CompleteAsync(string, string, string, CancellationToken)` | Checks the code, creates the membership row and sets the password for this application (same transaction). |


### IKeyRing

Signing and encryption keys kept by the Server (when no certificate files are configured). Rotation adds a new pair and retires the old one; retired keys keep checking old tokens until they are removed.

| Member | What it does |
|---|---|
| `ListAsync(CancellationToken)` | All keys that exist now. |
| `EnsureAsync(CancellationToken)` | Creates the first signing and encryption key when there are none. Safe with several instances. |
| `RotateAsync(CancellationToken)` | Makes a new pair, retires the current one, and makes this instance use the new keys at once. |
| `RemoveExpiredAsync(CancellationToken)` | Deletes retired keys whose time is over. Returns how many. |
| `RetireAsync(Guid, CancellationToken)` | Retires a key early by id. Returns true if retired. |
| `RefreshAsync(CancellationToken)` | Reads the keys again. Returns true when they changed (another instance rotated). |


### ILoginService

The one place that decides if a login is valid. Order: application enabled, user belongs to the application (multi mode), login method allowed, lockout, password, two-factor, single active session. Consent is checked by the authorize endpoint (phase 6). Wrong user, wrong password and "not registered here" all return `invalid_credentials`.

| Member | What it does |
|---|---|
| `PasswordLoginAsync(PasswordLoginRequest, CancellationToken)` | Checks a login and password. |
| `OtpLoginAsync(OtpLoginRequest, CancellationToken)` | Login with a one-time code (`PhoneOtp` or `EmailOtp`). Every failure is `invalid_otp`. A right code also proves the email or phone, so it is marked as confirmed. |
| `ExternalLoginAsync(ExternalSignInRequest, CancellationToken)` | Login of a user that an external source proved (see `ExternalSignInRequest`). Same rules as every other login. |
| `CompleteTwoFactorAsync(string, string, string, string, CancellationToken)` | Second step: checks a TOTP code or a recovery code. |
| `IsSubjectValidAsync(AppUser, CancellationToken)` | True when the subject of an existing token or cookie may still sign in (enabled, active, member, not locked out). |


### IOtpRequestService

Asks for one-time codes (login and phone confirmation).

| Member | What it does |
|---|---|
| `RequestAsync(string, string, CancellationToken)` | Sends a code. Looks the same to the caller whether the user exists or not. Returns an error only for configuration problems. |


### IOtpService

One-time codes. Codes are hashed, short-lived, limited in attempts, can be requested again only after a delay, and work once. The database table is used when Redis is off.

| Member | What it does |
|---|---|
| `IssueAsync(AppUser, string, CancellationToken)` | Makes a new code for the user, the current application and the purpose. Older open codes are closed. |
| `VerifyAsync(AppUser, string, string, CancellationToken)` | Checks a code in constant time. |


### IPasskeyService

Passkeys (WebAuthn) as a primary way to sign in, per application. Built on the ASP.NET Core Identity passkey support. The per-application rules (allowed, limits, user verification, backup rule, blocked authenticators) are checked here.

| Member | What it does |
|---|---|
| `CreationOptionsAsync(AppUser, CancellationToken)` | Starts a registration: the `PublicKeyCredentialCreationOptions` JSON for `navigator.credentials.create`. |
| `RegisterAsync(AppUser, string, string, CancellationToken)` | Finishes a registration with the JSON the browser made. |
| `RequestOptionsAsync(string, CancellationToken)` | Starts a sign-in: the `PublicKeyCredentialRequestOptions` JSON. With a login name the options name that user's passkeys; without (null) the browser may offer any passkey (usernameless, conditional UI). |
| `SignInAsync(string, string, string, CancellationToken)` | Finishes a sign-in with the JSON the browser made. Goes through the one login pipeline. |
| `ListAsync(AppUser, CancellationToken)` | The passkeys of a user in the current application. |
| `RenameAsync(AppUser, string, string, CancellationToken)` | Renames a passkey. |
| `RemoveAsync(AppUser, string, CancellationToken)` | Removes a passkey. The last way to sign in cannot be removed. |


### IPasswordService

Forgot, reset and change password.

| Member | What it does |
|---|---|
| `ForgotAsync(string, string, CancellationToken)` | Starts a reset. Always looks the same to the caller. |
| `ResetAsync(string, string, string, CancellationToken)` | Finishes a reset with the token from the email or the code from the SMS. |
| `ChangeAsync(AppUser, string, string, string, CancellationToken)` | Changes the own password. Other sessions of this application end; `keepAuthorizationId` stays. |


### IPermissionSyncService

Idempotent upsert of permissions sent by a client application (current application only). Never deletes. Allowed only when `PermissionSyncAllowed` is on.

| Member | What it does |
|---|---|
| `SyncAsync(PermissionSyncRequest, CancellationToken)` | Syncs a list. All-or-nothing: one invalid key rejects the whole request. |


### IProfileService

Use case for self-service user profile operations.

| Member | What it does |
|---|---|
| `UpdateProfileAsync(AppUser, string, string, string, string, CancellationToken)` | Updates name, display name, and preferred culture. |
| `RequestEmailChangeAsync(AppUser, string, string, CancellationToken)` | Starts an email change workflow. |
| `ConfirmEmailChangeAsync(AppUser, string, string, CancellationToken)` | Completes an email change workflow. |
| `DeleteMembershipAsync(AppUser, Guid, CancellationToken)` | Deletes the user's membership from the current project. |


### IProjectContext

Resolves the application of the current request. One abstraction, two strategies (single and multi).

| Member | What it does |
|---|---|
| `GetRequiredAsync(CancellationToken)` | Returns the current application. Also sets `IProjectScope` so query filters work. |


### IProjectCookieKey

The project key that names the session cookie (`.SystemX.Auth2.Session.{projectkey}`). It is set very early in the request (before authentication runs) from the client id or from the only session cookie the browser sent.

| Member | What it does |
|---|---|
| `CookieProjectKey` | The project key for the session cookie of this request, or null when it is not known. |


### IProjectScope

Scoped state: which application the current request works for. Read by the EF query filters.

| Member | What it does |
|---|---|
| `ProjectId` | Current application id, or null when none is selected. |
| `IsGlobalActive` | True while a global scope is active (query filters are bypassed). |
| `Set(Guid)` | Selects the current application. |
| `Set(Guid, string)` | Selects the current application and remembers its project key (for logs). Multi mode only; pass null in single mode. |
| `CurrentProjectKey` | Project key of the current application, when known. |


### IProjectSelection

Used by the admin API: the project an admin selected.

| Member | What it does |
|---|---|
| `ProjectKey` | Selected project key, or null. |


### IProjectService

Creates and updates projects.

| Member | What it does |
|---|---|
| `CreateAsync(string, string, string, CancellationToken)` | Creates a project (no application). The project key never changes afterwards. |
| `CreateWithApplicationAsync(string, string, string, string, CancellationToken)` | Creates a project and its first application, named `default`. The client id is `clientId`, or the project key when it is null. The client secret is in the result and is shown only once. Used by the commands and by small setups. |
| `FindAsync(Guid, CancellationToken)` | Finds a project by id (global scope). |
| `FindByProjectKeyAsync(string, CancellationToken)` | Finds a project by project key (global scope). |
| `UpdateSettingsAsync(Guid, ProjectSettings, CancellationToken)` | Replaces the project settings after validation. Raises the settings version and clears the cache. |
| `SetEnabledAsync(Guid, bool, string, Nullable<Guid>, CancellationToken)` | Enables or disables a project. Disabling revokes all authorizations, tokens and sessions of the project in one transaction, writes an audit event with the admin id and the reason, and keeps all data. `_global` cannot be disabled. |


### IProjectSettingsProvider

Reads cached settings of a project.

| Member | What it does |
|---|---|
| `GetAsync(Guid, CancellationToken)` | Gets the settings. Cached per application and settings version. |
| `Invalidate(Guid)` | Removes the cached value. |


### IProjectStatusCache

Short-lived cache of "is this project / application enabled". It is used where a token is validated, so a token of a project that was disabled is refused at the next validation. The cache window is `Auth2:ProjectStatusCacheSeconds` (default 10 seconds). A change made on this instance clears the cache at once, and through Redis (when it is on) on all instances.

| Member | What it does |
|---|---|
| `IsProjectEnabledAsync(Guid, CancellationToken)` | True when the project exists and is enabled. |
| `IsApplicationEnabledAsync(string, CancellationToken)` | True when the application (client id) exists and is enabled. |
| `Invalidate(Guid)` | Forgets the cached value of a project (and tells the other instances). |
| `InvalidateApplication(string)` | Forgets the cached value of an application. |


### IRedirectIdentitySource

A source that sends the browser away and takes it back (OIDC, SAML).

| Member | What it does |
|---|---|
| `ChallengeAsync(ExternalChallengeContext, CancellationToken)` | Starts the sign-in. |
| `AuthenticateCallbackAsync(ExternalCallbackContext, CancellationToken)` | Checks the answer of the provider. |


### IRegisterService

Registration. The application always comes from the client token, never from the body. It is idempotent: it creates the person only when they do not exist, and it never changes the password of an existing person. A person who already exists but is not in this application gets a proof code instead (see `IJoinService`).

| Member | What it does |
|---|---|
| `RegisterAsync(RegisterRequest, string, CancellationToken)` | Registers a user in the current application. |


### IRegistrationDefaults

Gives a new member of the project the roles, groups and permissions of `Registration.AssignedRoles`, `AssignedGroups` and `AssignedPermissions`. A name that does not exist is skipped and audited; it never fails the registration.

| Member | What it does |
|---|---|
| `ApplyAsync(AppUser, CancellationToken)` | Assigns the defaults of the current project to `user`. Safe to call twice. |


### IReplayCache

Remembers values that may be used only once (login state, SAML ids). Shared by all instances through the database.

| Member | What it does |
|---|---|
| `TryAddAsync(Guid, string, DateTimeOffset, CancellationToken)` | Adds the key. Returns false when it was already there (a replay). |


### IReturnUrlValidator

Checks return URLs. Open redirect protection.

| Member | What it does |
|---|---|
| `IsAllowed(string, IEnumerable<string>)` | True when the URL is a safe local path, or an absolute URL that exactly matches one of `allowedAbsolute`. |
| `Resolve(string, string, EffectiveSettings, string)` | Returns the first allowed value in the order: return URL, application fallback, `home`. |


### IRoleSyncService

Idempotent upsert of roles (and permissions of roles) sent by a client application. Allowed only when `RoleSyncAllowed` is on.

| Member | What it does |
|---|---|
| `SyncAsync(RoleSyncRequest, CancellationToken)` | Syncs a list. All-or-nothing validation, then one upsert per role. |


### IScopeSyncService

Custom scopes of an application. A sync registers the scope (when it is new) and allows it for the application's client. Names starting with `auth2.` and the standard OpenID Connect scopes are reserved. Never removes a scope.

| Member | What it does |
|---|---|
| `SyncAsync(ScopeSyncRequest, CancellationToken)` | Syncs a list. `Added` counts scopes newly allowed for this application. |
| `ListAsync(CancellationToken)` | The custom scopes this application allows. |


### ISecretHasher

Hashes secrets such as recovery codes and OTP codes. Never reversible.

| Member | What it does |
|---|---|
| `Hash(string)` | Hashes a secret with a random salt. |
| `Verify(string, string)` | Checks a secret in constant time. |


### IServerUrls

Builds absolute links to the server pages (for emails).

| Member | What it does |
|---|---|
| `Build(string, IEnumerable<KeyValuePair<string, string>>)` | Builds `{base}{path}?query`. The base is `Auth2:Issuer`, or the current request. |


### IServerValues

Small server-wide values that an admin may change at run time (kept in the database).

| Member | What it does |
|---|---|
| `GetAsync(string, CancellationToken)` | A value, or null. |
| `SetAsync(string, string, CancellationToken)` | Sets a value. |


### ISessionService

Single active session and session revoking.

| Member | What it does |
|---|---|
| `RevokeAllAsync(AppUser, Project, string, string, CancellationToken)` | Revokes all tokens and authorizations of the user in the application. Returns the number of revoked tokens. An authorization id in `keepAuthorizationId` is left alone (the session that asked). |
| `ListAsync(AppUser, Project, CancellationToken)` | Lists the open sessions of the user in the application. |
| `RevokeAsync(AppUser, Project, string, CancellationToken)` | Revokes one session of the user. Returns false when it is not theirs. |


### ISettingsInvalidationBus

Tells every instance that the settings of an application changed (Redis pub/sub when Redis is on).

| Member | What it does |
|---|---|
| `PublishAsync(Guid, CancellationToken)` | Announces a change. Other instances drop their cached settings and status of the project. |


### ISmsFactory

Gives the SMS sender for the current request. Same rules as `IEmailFactory`.

| Member | What it does |
|---|---|
| `ProviderNames` | The names of the providers the server has. |
| `CreateAsync(CancellationToken)` | The sender that fits the project and application of the current request. |


### ISmsSender

Sends SMS messages. The development implementation only logs. Replace it in production.

| Member | What it does |
|---|---|
| `SendAsync(SmsMessage, CancellationToken)` | Sends one SMS. |


### ISmsTransport

One way to deliver SMS. Register your own the same way as `IEmailTransport`.

| Member | What it does |
|---|---|
| `Name` | Provider name, lowercase. |
| `SendAsync(SmsTransportConfig, SmsMessage, CancellationToken)` | Sends one SMS, or throws. |


### ITokenIssuer

Builds the claims principal for tokens. Roles are the roles of the current application.

| Member | What it does |
|---|---|
| `CreateUserPrincipalAsync(AppUser, IEnumerable<string>, IEnumerable<string>, string, CancellationToken, string)` | Creates the principal of a user for the current application. |
| `CreateMachinePrincipalAsync(IEnumerable<string>, CancellationToken)` | Creates the principal of a machine client (client credentials). |
| `RefreshUserClaimsAsync(ClaimsPrincipal, AppUser, CancellationToken)` | Refreshes roles and the access version of an existing user principal (used for the refresh token grant). |


### ITwoFactorService

Authenticator-app two-factor for a signed-in user.

| Member | What it does |
|---|---|
| `SetupAsync(AppUser, CancellationToken)` | Makes a key for the authenticator app. |
| `EnableAsync(AppUser, string, CancellationToken)` | Turns two-factor on after the first code. Returns the recovery codes (shown once). |
| `DisableAsync(AppUser, string, CancellationToken)` | Turns two-factor off. Needs the password. Refused when the application requires two-factor. |
| `RegenerateRecoveryCodesAsync(AppUser, string, CancellationToken)` | Makes new recovery codes. Needs a code from the authenticator app. |


### IUserNotifier

Builds the localized messages and sends them.

| Member | What it does |
|---|---|
| `SendEmailConfirmationAsync(AppUser, string, string, CancellationToken)` | Sends the email confirmation link. |
| `SendPasswordResetAsync(AppUser, string, string, CancellationToken)` | Sends the password reset link. |
| `SendOtpAsync(AppUser, string, string, NotificationChannel, CancellationToken)` | Sends a one-time code by email or SMS. |


### JobHealthStatus

Job health summary for GlobalAdmin dashboard.

| Member | What it does |
|---|---|
| `(constructor)(bool, int, Nullable<DateTimeOffset>)` | Job health summary for GlobalAdmin dashboard. |


### JobsOptions

Background jobs. Section `Auth2:Jobs`. One instance runs each job at a time (a database lock).

| Member | What it does |
|---|---|
| `Enabled` | Master switch. |
| `IntervalMinutes` | Minutes between runs. |
| `AuditRetentionDays` | Audit events older than this many days are deleted. 0 keeps them forever. |


### KeyInfo

Public facts about one key of the key ring. The private key is never shown.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, DateTimeOffset, DateTimeOffset, Nullable<DateTimeOffset>, Nullable<DateTimeOffset>)` | Public facts about one key of the key ring. The private key is never shown. |


### LoginResult

Result of a login step.

| Member | What it does |
|---|---|
| `Succeeded` | True when the user is fully signed in. |
| `User` | The user, when `Succeeded`. |
| `Application` | The application the login was for. |
| `ErrorCode` | Stable error code (`Auth2ErrorCodes`), when not succeeded. |
| `TwoFactorToken` | Short-lived token for the second step, when `ErrorCode` is `two_factor_required`. |
| `Amr` | Authentication methods used (`pwd`, `otp`). |
| `SessionId` | Id of the new session (`sid` claim). |
| `IdentityProvider` | Name of the external provider the user came from (`idp` claim), or null for local logins. |
| `Fail(string)` | Failure result. |


### MessagingDto

The messaging settings of a project or an application, with the provider names the server has.

| Member | What it does |
|---|---|
| `(constructor)(bool, EmailProviderDto, SmsProviderSettings, IReadOnlyCollection<string>, IReadOnlyCollection<string>)` | The messaging settings of a project or an application, with the provider names the server has. |


### MessagingUpdate

A change of the messaging settings. A null part is left as it is.

| Member | What it does |
|---|---|
| `(constructor)(EmailProviderUpdate, SmsProviderSettings)` | A change of the messaging settings. A null part is left as it is. |


### NotificationChannel

Where a code or link is sent.

| Member | What it does |
|---|---|
| `Email` | Email. |
| `Sms` | SMS. |


### OperationResult

Result of an account operation. Identity errors are kept so the caller can show the password rules.

| Member | What it does |
|---|---|
| `Succeeded` | True when it worked. |
| `ErrorCode` | Stable error code, or null. |
| `Errors` | All errors (for example every broken password rule). |
| `Ok` | Success. |
| `Fail(string)` | Failure with a code. |
| `Fail(IEnumerable<IdentityError>)` | Failure from Identity errors. The first error gives the code. |


### OtpIssueResult

Result of `IssueAsync`.

| Member | What it does |
|---|---|
| `(constructor)(string, bool)` | Result of `IssueAsync`. |
| `Issued` | True when a code was made. |


### OtpLoginRequest

Input of a login with a one-time code.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string)` | Input of a login with a one-time code. |
| `Login` | Email or phone number. |
| `Code` | The one-time code. Never logged. |
| `IpAddress` | Caller IP (for the audit log). |
| `UserAgent` | Caller user agent (for the audit log). |


### OtpVerifyResult

Result of checking a one-time code.

| Member | What it does |
|---|---|
| `Ok` | The code is right. It is used up now. |
| `Invalid` | Wrong code. |
| `Expired` | The code is too old. |
| `TooManyAttempts` | Too many wrong attempts. The code is dead. |
| `NotFound` | There is no open code for this user and purpose. |


### PageQuery

Paging, search and sorting of a list. Page size is limited to `MaxPageSize`.

| Member | What it does |
|---|---|
| `(constructor)(int, int, string, string, bool)` | Paging, search and sorting of a list. Page size is limited to `MaxPageSize`. |
| `SafePage` | Page, at least 1. |
| `SafePageSize` | Page size between 1 and 200. |
| `Skip` | Rows to skip. |
| `SafeSearch` | Search text, trimmed, or null. |


### PasskeyOutcome<T>

Result of a passkey operation with a value (the JSON for the browser, or the new passkey).

| Member | What it does |
|---|---|
| `(constructor)(OperationResult, T)` | Result of a passkey operation with a value (the JSON for the browser, or the new passkey). |
| `Succeeded` | True when it worked. |
| `Ok(T)` | Success with a value. |
| `Fail(string)` | Failure with an error code. |


### PasskeyServerOptions

Server-wide passkey options. The per-application rules are in the application settings (`Passkeys`).

| Member | What it does |
|---|---|
| `ServerDomain` | The relying party id: the domain (without scheme and port) that passkeys are bound to, for example `auth.example.com`. **Required in Production**: without it passkeys are refused. Changing it later makes every existing passkey useless. In Development the host of the request is used. |
| `BlockedAaguids` | Authenticator models (AAGUIDs) that cannot be registered. Global admin only (a server setting). The AAGUID is as reported by the authenticator. |
| `AttestationMode` | `None` (default): attestation is not asked for. `AaguidAllowList`: only the models in `AllowedAaguids` can be registered. |
| `AllowedAaguids` | Allowed authenticator models when `AttestationMode` is `AaguidAllowList`. |


### PasswordLoginRequest

Input of a password login. The application always comes from `IProjectContext`.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string)` | Input of a password login. The application always comes from `IProjectContext`. |
| `Login` | User name, email or phone number. |
| `Password` | The password. Never logged. |
| `IpAddress` | Caller IP (for the audit log). |
| `UserAgent` | Caller user agent (for the audit log). |


### ProjectCreationResult

A new project with its first application and the client secret (plain text, shown once).

| Member | What it does |
|---|---|
| `(constructor)(Project, ClientApplicationInfo, string)` | A new project with its first application and the client secret (plain text, shown once). |


### ProjectNotFoundException

No application matches the request.

| Member | What it does |
|---|---|
| `(constructor)(string)` | No application matches the request. |


### ProjectSettingsValidator

Rejects unsafe or broken project settings.

| Member | What it does |
|---|---|
| `AbsoluteMinPasswordLength` | Smallest allowed password length. |
| `Validate(ProjectSettings)` | Validates settings. Returns an empty list when all is fine. |
| `ValidateOrThrow(ProjectSettings)` | Throws `SettingsValidationException` when the settings are not valid. |
| `ReservedRoles` | Roles that a registration may never hand out: they open the administration. |
| `MaxAssigned` | Most names in one assigned list. |
| `CheckUris(string, IEnumerable<string>, Action<string, string>)` | Checks that every URI is absolute, http(s), without wildcards and without a fragment. |


### ProviderStatusDto

Provider status item on the dashboard.

| Member | What it does |
|---|---|
| `(constructor)(string, string, bool)` | Provider status item on the dashboard. |


### RateLimitRule

Limit of one named rate-limit policy: at most `PermitLimit` requests per `WindowSeconds`.

| Member | What it does |
|---|---|
| `PermitLimit` | Requests allowed in the window. |
| `WindowSeconds` | Window length in seconds. |


### RateLimitingOptions

Rate limiting. Policies: login, token, register, otp, password-reset, handshake, permission-sync, admin.

| Member | What it does |
|---|---|
| `PolicyNames` | Names of the policies. |
| `Enabled` | Master switch. |
| `Policies` | Overrides per policy name. Missing policies use `Default` values. |
| `RuleFor(string)` | The rule of a policy (configured, else the built-in default). |
| `Default(string)` | Built-in defaults. Per client address and login or client id. |


### RedisOptions

Redis settings. Redis is on when `ConnectionString` is set.

| Member | What it does |
|---|---|
| `ConnectionString` | StackExchange.Redis connection string. Keep it in a secret store. |
| `KeyPrefix` | Prefix of every key and channel. Lets several servers share one Redis. |
| `Enabled` | True when Redis is configured. |


### RegisterOutcome

Result of a registration.

| Member | What it does |
|---|---|
| `Result` | Operation result (errors such as weak password). |
| `UserId` | The user id. |
| `UserWasCreated` | The user was created by this call. |
| `JoinedProject` | The user belongs to the application now. |
| `ConfirmationRequired` | Something must be confirmed first (email, phone, or the proof code for joining). |
| `User` | The new user (only when `UserWasCreated`). |


### RegisteredProvider

An enabled provider as the Server uses it at runtime. Secrets stay encrypted inside `Settings` and are decrypted only at the moment of use (by the external secret protector).

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, ExternalProviderSettings, int)` | An enabled provider as the Server uses it at runtime. Secrets stay encrypted inside `Settings` and are decrypted only at the moment of use (by the external secret protector). |
| `Name` | Provider name inside the application. |
| `Type` | Provider type. |
| `RegistrationName` | Globally unique registration name: `ext-{applicationId:N}-{name}`. |


### ReportRow

A row in an administrative report.

| Member | What it does |
|---|---|
| `(constructor)(string, long, Nullable<DateTimeOffset>, string)` | A row in an administrative report. |


### ReturnUrlValidator

A local URL starts with `/` and not with `//` or `/\`. An absolute URL must match the allowed list exactly. Control characters, spaces and backslashes are never accepted, and the check is repeated on the URL-decoded value, so encoded tricks like `/%2Fevil.com` do not pass.

| Member | What it does |
|---|---|
| `IsAllowed(string, IEnumerable<string>)` |  |
| `Resolve(string, string, EffectiveSettings, string)` |  |


### SecurityOptions

HTTP hardening. Section `Auth2:Security`.

| Member | What it does |
|---|---|
| `RequireHttps` | Redirect HTTP to HTTPS and send HSTS. Default: on outside Development and Testing. |
| `UseForwardedHeaders` | Read `X-Forwarded-For` and `X-Forwarded-Proto` from a reverse proxy. Turn on behind a proxy. |
| `TrustAllProxies` | Trust forwarded headers from any proxy. Only for a network where nobody else can reach the server. Otherwise list `KnownProxies`. |
| `KnownProxies` | Addresses of trusted proxies. |
| `ContentSecurityPolicy` | Value of `Content-Security-Policy`. `{nonce}` is replaced by a random value per request; the branding style block carries it. No inline scripts, no inline event handlers, no `unsafe-eval`, no CDN. |


### ServerEmailOptions

Server default email provider. The password is plain here: keep it in a secret store or an environment variable, never in a file in the repository.

| Member | What it does |
|---|---|
| `Provider` | Provider name: `logging` (default), `smtp`, `google` or a custom one. |
| `Host` | SMTP host. |
| `Port` | SMTP port. |
| `Security` | Connection security. |
| `UserName` | SMTP user name (Google: the Gmail address). |
| `Password` | SMTP password (Google: an app password). |
| `FromAddress` | From address. |
| `FromName` | From display name. |
| `TimeoutSeconds` | Timeout of one send in seconds. |


### ServerMessagingOptions

Server default for sending messages (`Auth2:Messaging`). Used when neither the application nor the project chose a provider.

| Member | What it does |
|---|---|
| `Email` | Default email provider. |
| `SmsProvider` | Default SMS provider name (`logging` unless you register your own). |
| `SmsSender` | Default SMS sender id. |


### SessionInfo

One signed-in session of a user in an application (an OpenIddict authorization).

| Member | What it does |
|---|---|
| `(constructor)(string, Nullable<DateTimeOffset>, IReadOnlyList<string>)` | One signed-in session of a user in an application (an OpenIddict authorization). |


### SettingsError

One validation error. `Path` is like `Password.MinLength`.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | One validation error. `Path` is like `Password.MinLength`. |


### SettingsValidationException

The settings are not valid.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<SettingsError>)` | The settings are not valid. |
| `Errors` | All errors. |


### SingleProjectOptions

Options for single-project mode. Section `Auth2:SingleProject`.

| Member | What it does |
|---|---|
| `Name` | Name of the one fixed application. |


### SmsMessage

An SMS. `Data` holds the raw code for tests. Never log it.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyDictionary<string, string>)` | An SMS. `Data` holds the raw code for tests. Never log it. |


### SmsTransportConfig

An SMS provider setting.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | An SMS provider setting. |


### SyncOutcome

Result of a sync call.

| Member | What it does |
|---|---|
| `(constructor)(OperationResult, SyncResponse)` | Result of a sync call. |
| `Result` | Success or the error. |
| `Response` | The counts when it worked. |


## `SystemX.Auth2.Server.Application.Lists`


### CsvExport

Writes a list as CSV: UTF-8 with BOM, a formula-injection guard on every cell, no secrets (rows hold none).

| Member | What it does |
|---|---|
| `DefaultRowLimit` | The default row limit of an export. |
| `Cell(object)` | Escapes one cell. A cell that starts with `= + - @` (or a tab or carriage return) gets a leading quote, so a spreadsheet shows text, not a formula. |
| `WriteAsync(Stream, IAsyncEnumerable<T>, int, CancellationToken)` | Streams the rows. The properties of are the columns. |


### FilterRule<T>

A filter of a list. It turns the raw values of one name into a where clause. It never builds a column name from the value.

| Member | What it does |
|---|---|
| `Build` | Makes the predicate from the values (already trimmed). Return null when a value is not valid. |
| `BuildRange` | Range filters (`f.name` + `From` and `To`) use `BuildRange` instead. |


### ListDefinition<T>

What an entity declares so that its list works: the whitelists, the search, the default sort and the projection to a row.

| Member | What it does |
|---|---|
| `Sortable` | Sort fields (name to key selector). Only these names can be sorted. |
| `Filters` | Filter names. Only these can be used as `f.name`. |
| `Search` | Builds the search predicate from a text whose wildcard characters are already escaped (`EscapeLike`). |
| `DefaultSort` | The sort when none is given (for example `-createdAt`). |
| `Tiebreaker` | The stable tiebreaker that is always added last (normally the id). |
| `Select` | The projection to the row DTO. Lists never load entities. |


### ListEngine

Applies a `ListQuery` to a query in SQL: filters, search, sort, tiebreaker and paging, then projects to the row. Never loads a whole table.

| Member | What it does |
|---|---|
| `CountCap` | The count is capped at this value for large tables (the page says "10,000+"). |
| `Apply(IQueryable<T>, ListQuery, ListDefinition<T, T>, List<ListProblem>)` | Filters, searches and sorts (with the tiebreaker). No paging yet. |
| `PageAsync(IQueryable<T>, ListQuery, ListDefinition<T, T>, List<ListProblem>, CancellationToken)` | Offset paging with an exact total. |
| `PageCappedAsync(IQueryable<T>, ListQuery, ListDefinition<T, T>, List<ListProblem>, CancellationToken)` | Offset paging with a capped count (for large tables): the total stops at `CountCap`. |
| `KeysetAsync(IQueryable<T>, Expression<Func<T, DateTimeOffset>>, Expression<Func<T, Guid>>, string, int, Expression<Func<T, T>>, Func<T, ValueTuple<DateTimeOffset, Guid>>, CancellationToken)` | Keyset paging for big append-only lists (audit, sessions, authorizations): newest first by a time and an id. The cursor is the last row's time and id, so inserts between two requests change nothing about the pages already read. No count, no offset. |


### ListPage<T>

One page of a list.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<T>, int, int, long, bool, int, bool, bool, string, string)` | One page of a list. |


### ListProblem

A query value that is not valid. The API answers 400 naming the parameter; the page ignores it and shows a notice.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | A query value that is not valid. The API answers 400 naming the parameter; the page ignores it and shows a notice. |


### ListQuery

The parsed query of every admin list (page and API). Built by `ListQueryParser`; state lives in the URL.

| Member | What it does |
|---|---|
| `(constructor)(int, int, IReadOnlyList<SortSpec>, string, IReadOnlyDictionary<string, string[]>, string)` | The parsed query of every admin list (page and API). Built by `ListQueryParser`; state lives in the URL. |
| `AllowedPageSizes` | The page sizes a caller may pick. |
| `DefaultPageSize` | The default page size. |
| `ApiMaxPageSize` | Largest page size of the API. |
| `MinSearchLength` | Shortest search text. |
| `MaxSearchLength` | Longest search text. |
| `MaxSortFields` | Most sort fields. |


### ListQueryParser

Parses and checks the query string of a list. One implementation for pages and API.

| Member | What it does |
|---|---|
| `EscapeChar` | The escape character used with `EF.Functions.ILike(..., escape)`. |
| `EscapeLike(string)` | Escapes `%`, `_` and the escape character, so a search text is only text. |
| `Parse(IEnumerable<KeyValuePair<string, string[]>>, ListDefinition<T, T>, List<ListProblem>, bool)` | Parses `page`, `pageSize`, `sort`, `q`, `cursor` and `f.*`. Values that are not valid are reported in `problems` and replaced by the default. Sort and filter names are checked against the definition. |
| `ToQueryString(ListQuery, Nullable<int>, string)` | The query as a query string again (for links that keep the state), without the page. |


### SortSpec

One sort field. `Descending` comes from the `-` prefix.

| Member | What it does |
|---|---|
| `(constructor)(string, bool)` | One sort field. `Descending` comes from the `-` prefix. |


### UserList

The list definition of users: whitelists for sort, filters and search. Index plan: (project_id, user_id) of user_projects, normalized email and user name of users.

| Member | What it does |
|---|---|
| `For(Auth2DbContext, Guid, bool)` | The users of one project. Every list starts from this query, so it is always under the project (tenant) filter. |
| `Definition` | The definition. |


### UserListEntity

A user together with the membership in the project of the list (the membership is null in single-project mode).

| Member | What it does |
|---|---|
| `User` | The person. |
| `Membership` | The membership row, or null. |


### UserListRow

One row of the user list. No hash, key or code is in it.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, string, bool, bool, DateTimeOffset, Nullable<DateTimeOffset>)` | One row of the user list. No hash, key or code is in it. |


## `SystemX.Auth2.Server.Application.Lists.ListEngine`


### Cursor

The keyset cursor: time and id of the last row, as one opaque text.

| Member | What it does |
|---|---|
| `Encode(DateTimeOffset, Guid)` | Encodes a cursor. |
| `TryDecode(string, ValueTuple<DateTimeOffset, Guid>)` | Decodes a cursor. A bad cursor gives false and the first page is shown. |


## `SystemX.Auth2.Server.Domain.Settings`


### ClientApplicationSettings

Settings of one application (client). Stored in `SettingsJson` next to the OpenIddict fields. It holds only what OpenIddict has no field for. A null value means "use the project value". Redirect URIs, allowed scopes, client type and consent type are OpenIddict fields and are not repeated here.

| Member | What it does |
|---|---|
| `CurrentSchemaVersion` | Current JSON schema version. |
| `SchemaVersion` | Schema version of the stored JSON. |
| `UiMode` | Which UI the client uses (hosted, self, none). Null: the project default. |
| `CookieLifetimeMinutes` | Cookie lifetime in minutes. Null: the project value. |
| `AccessTokenMinutes` | Access token lifetime in minutes. Null: the project value. |
| `RefreshTokenDays` | Refresh token lifetime in days. Null: the project value. |
| `IdentityTokenMinutes` | Identity token lifetime in minutes. Null: the project value. |
| `AllowedCorsOrigins` | Allowed CORS origins of this client. CORS is off when empty. |
| `AllowSelfUiLogin` | Enables the password grant and the two-factor grants for this client (Self UI). |
| `ExternalProviders` | Names of the project's external providers this client shows. Null: all of them. A subset only; it never adds a provider. |
| `ConsentMode` | Consent mode. Null: the project value. |
| `SendConfirmationEmail` | Send the confirmation email after registration through this application. Null: the project value. |
| `Messaging` | This application's own email / SMS providers. Null: the project's. Changed only through the messaging admin routes. |


### ClientSettings

Client behaviour (project defaults for all applications).

| Member | What it does |
|---|---|
| `AllowedClientTypes` | Allowed client types. |
| `PermissionSyncAllowed` | Permission sync is allowed. |
| `ScopeSyncAllowed` | Custom scope sync is allowed. A client can then register its own scopes (not `auth2.*`, not the standard ones) and allow them for itself. |
| `GroupSyncAllowed` | Group sync (push) is allowed. Off by default: admins normally decide groups. Push only adds missing groups. |
| `RoleSyncAllowed` | Role sync is allowed. |
| `AllowRemovePermissionsFromSync` | Sync may remove permissions. |
| `AllowProjectAdminRotateSecret` | A project admin may rotate the client secret of the own application. |
| `DefaultUiMode` | Default UI mode. |


### EmailProviderSettings

How emails are sent. The provider is a name; the other fields are used by the providers that need them.

| Member | What it does |
|---|---|
| `Provider` | Provider name (`EmailProviders`, or a custom one). Empty means inherit. |
| `Host` | SMTP host. Not used by `google` (it is fixed). |
| `Port` | SMTP port. Default 587 (STARTTLS). |
| `Security` | Connection security. Not used by `google` (always STARTTLS). |
| `UserName` | SMTP user name (for Google: the full Gmail address). Empty means no login. |
| `PasswordProtected` | SMTP password (for Google: an app password), encrypted. Never returned. |
| `FromAddress` | Address in the From header. |
| `FromName` | Display name in the From header. Empty: the brand name of the project. |
| `TimeoutSeconds` | Timeout of the whole send in seconds. |


### EmailProviders

Names of the email providers that ship with the server. Your own provider is any other name you register.

| Member | What it does |
|---|---|
| `Inherit` | Empty: use the next level (application, then project, then the server default). |
| `Logging` | Writes a log line only (development). |
| `Smtp` | Any SMTP server. |
| `Google` | Gmail / Google Workspace SMTP (smtp.gmail.com). Needs an app password. |


### EmailSettings

Email rules.

| Member | What it does |
|---|---|
| `RequireConfirmedEmail` | Email must be confirmed before login. |
| `AllowedDomains` | If not empty, only these domains are allowed. |
| `BlockedDomains` | These domains are never allowed. |


### ExternalProviderSettings

One external identity provider of an application. A base with three kinds: `OidcProviderSettings`, `SamlProviderSettings` and `LdapProviderSettings`. Secrets are stored encrypted (fields ending in `Protected`) and are never returned.

| Member | What it does |
|---|---|
| `Name` | Unique inside the application. Lowercase letters, digits and `-`. Used in URLs and as the login provider name. |
| `Type` | The kind of provider. |
| `DisplayName` | Text for the button on the login page. |
| `Enabled` | On or off for this application. |
| `AllowRegister` | A new local user may be created from this source (just-in-time provisioning). |
| `AllowLink` | A login may be linked to an existing user with the same **verified** email. |
| `TrustProviderEmailVerification` | The email of this source counts as proof of ownership. Null means the default of the type: true for Google, Microsoft and Apple; false for the others. Only a global admin can turn it on for the types where the default is false. |
| `TrustProviderMfa` | The source's own multi-factor sign-in replaces the local two-factor step. |
| `ClaimMappings` | Source attribute name to local field (`email`, `given_name`, `family_name`, `name`, `phone_number`, `picture`). |
| `GroupMappingMode` | Whether group mappings are applied at login. |
| `ProfileSyncMode` | Whether the profile is updated at every login. |
| `AutoRedirect` | When this is the only enabled provider and no local login method is allowed, the login page goes straight to it. |
| `EmailDomains` | Email domains that are sent to this provider (home-realm discovery). A domain belongs to one provider per application. |


### GroupMappingMode

Whether the groups of an external source become local roles and groups.

| Member | What it does |
|---|---|
| `None` | Groups of the source are ignored. |
| `Sync` | At every login the mappings are applied (added, and removed only when a mapping mentions them). |


### IdentityMethodSettings

Login and registration methods.

| Member | What it does |
|---|---|
| `AllowedLoginMethods` | Allowed login methods. |
| `AllowedRegisterMethods` | Allowed register methods. |


### JoinMode

Join mode of a project.

| Member | What it does |
|---|---|
| `AutoJoin` | A user with proof of ownership may join. |
| `InviteOnly` | Only invited users may join. |
| `Closed` | Nobody may join. |


### LdapMode

How LDAP and local passwords work together for password login in one application.

| Member | What it does |
|---|---|
| `LdapOnly` | Only the directory. |
| `LocalThenLdap` | The local password first, then the directory. |
| `LdapThenLocal` | The directory first, then the local password. |


### LdapProviderSettings

An LDAP directory or Active Directory. The user's password is checked by the directory.

| Member | What it does |
|---|---|
| `Host` | Host name of the directory server. |
| `Port` | Port. 636 for LDAPS, 389 for StartTLS. |
| `Security` | TLS mode. |
| `ValidateServerCertificate` | Check the server certificate (chain and name). Default true. Turning it off is global-admin only and meant for tests. |
| `CustomCaCertificate` | A certificate authority to trust (PEM), besides the system store. |
| `TlsServerName` | The name that must be in the server certificate. Default: `Host`. |
| `BindDn` | The service account used to search. Anonymous bind is not allowed. |
| `BindPasswordProtected` | Password of the service account, encrypted. Never returned. |
| `UserSearchBase` | Where users are searched. |
| `UserSearchFilter` | Search filter. `{0}` (exactly once) is replaced by the escaped user name. Example: `(&(objectClass=person)(sAMAccountName={0}))`. |
| `UserNameAttribute` | Attribute with the user name. |
| `UniqueIdAttribute` | Attribute with a stable unique id (`objectGUID`, `entryUUID`). Used as the subject, never the DN. |
| `EmailAttribute` | Attribute with the email. |
| `FirstNameAttribute` | Attribute with the first name. |
| `LastNameAttribute` | Attribute with the last name. |
| `PhoneAttribute` | Attribute with the phone number. |
| `GroupSearchBase` | Where groups are searched. Empty: groups are read from `MemberOfAttribute`. |
| `GroupFilter` | Filter for groups. `{0}` is replaced by the escaped DN of the user. |
| `GroupMemberAttribute` | Attribute of a group entry that holds the member DNs. Default `member`. |
| `MemberOfAttribute` | Attribute of the user entry that lists groups (for example `memberOf`). |
| `NestedGroups` | Read groups of groups. Default false. Depth is limited to 5. |
| `TimeoutSeconds` | Time limit for connect, bind and search, in seconds. Default 5. |
| `FollowReferrals` | Follow referrals. Default false. |
| `Mode` | How the directory and local passwords are combined. |
| `SyncOnLogin` | Update profile and groups at every login. |
| `RevealDirectoryAccountState` | Show "account disabled" to the user. Default false (the answer is the same as for a wrong password). |


### LdapSecurity

Transport security of an LDAP connection. Plain LDAP is not offered.

| Member | What it does |
|---|---|
| `Ldaps` | TLS from the start (port 636). |
| `StartTls` | A plain connection that is upgraded with StartTLS (port 389). The upgrade is required. |


### LocalizationSettings

Localization and branding.

| Member | What it does |
|---|---|
| `DefaultCulture` | Default culture. |
| `SupportedCultures` | Supported cultures. They only choose a language. |
| `BrandName` | Brand name. |
| `SupportEmail` | Support email. |


### LockoutSettings

Lockout rules.

| Member | What it does |
|---|---|
| `LockoutEnabled` | Is lockout on. |
| `MaxFailedAttempts` | Failed attempts before lockout. |
| `LockoutMinutes` | Lockout time in minutes. |
| `LockoutForNewUsers` | Lockout also applies to new users. |


### MessagingSettings

The sending channels of a project, or of one application (an application may use its own provider). A level that leaves the provider empty uses the next one: application, then project, then the server default (`Auth2:Messaging`), then logging.

| Member | What it does |
|---|---|
| `Email` | Email provider. |
| `Sms` | SMS provider. |


### OidcProviderSettings

Google, Microsoft, GitHub, Apple, Facebook and generic OpenID Connect providers. Fields that do not belong to the type are ignored.

| Member | What it does |
|---|---|
| `ClientId` | Client id at the provider. |
| `ClientSecretProtected` | Client secret, encrypted. Never returned (the API shows only `secretIsSet`). |
| `Scopes` | Scopes to ask for. Empty means the defaults of the type. |
| `StoreProviderTokens` | Store the provider's access and refresh tokens. Default false. |
| `HostedDomain` | Google: only this hosted domain (`hd`). |
| `TenantId` | Microsoft: `common`, `organizations`, `consumers` or a tenant id (GUID). |
| `AllowedTenants` | Microsoft: only these tenant ids may sign in (checked on the `tid` claim). |
| `RequireVerifiedPrimaryEmail` | GitHub: the user needs a verified primary email. Default true. |
| `AllowedOrganizations` | GitHub: the user must be a member of one of these organizations. |
| `TeamId` | Apple: team id. |
| `KeyId` | Apple: key id. |
| `PrivateKeyProtected` | Apple: the private key (PEM), encrypted. The client secret is a short-lived signed token made from it. |
| `ApiVersion` | Facebook: Graph API version, for example `v19.0`. |
| `Authority` | Generic OIDC: the authority (https). Its discovery document is read. |
| `NameClaim` | Generic OIDC: claim for the display name. Default `name`. |
| `EmailClaim` | Generic OIDC: claim for the email. Default `email`. |
| `GroupsClaim` | Generic OIDC: claim that holds groups. |
| `AllowedIssuers` | Generic OIDC: issuers that are accepted besides the discovery issuer (multi-tenant providers). |


### PasskeySettings

Passkey (WebAuthn) rules of one application.

| Member | What it does |
|---|---|
| `AllowPasskeys` | Users may register and use passkeys in this application. Default false. |
| `PasskeyUserVerification` | `required` (default) or `preferred`. |
| `PasskeyResidentKey` | `preferred` (default), `required` or `discouraged`. |
| `MaxPasskeysPerUser` | Most passkeys one user may have here. Default 10. |
| `PasskeyNameMaxLength` | Longest passkey name. Default 50. |
| `RequirePasskeyBackupMethod` | A user without a password or other login must keep at least two passkeys (or another method) before removing one. Default true. |


### PasswordSettings

Password rules.

| Member | What it does |
|---|---|
| `MinLength` | Minimum length. |
| `MaxLength` | Maximum length. |
| `RequireDigit` | Needs a digit. |
| `RequireLower` | Needs a lowercase letter. |
| `RequireUpper` | Needs an uppercase letter. |
| `RequireNonAlphanumeric` | Needs a non-alphanumeric character. |
| `UniqueChars` | Minimum number of different characters. |
| `BannedPasswords` | Passwords that are never allowed. A breached-list check can be plugged in later (hook only). |


### PhoneSettings

Phone rules.

| Member | What it does |
|---|---|
| `PhoneNumberRegex` | Regex for phone numbers. Default is E.164. |
| `RequireConfirmedPhone` | Phone must be confirmed before login. |
| `RegexTimeoutMs` | Regex timeout in ms. Protects from ReDoS. |


### ProfileSyncMode

When the profile of a user is taken from the source.

| Member | What it does |
|---|---|
| `ImportOnce` | Names and phone are copied when the user is created. Later logins do not change them. |
| `UpdateOnLogin` | Names and phone are updated at every login. |


### ProjectKeyRules

Rules for project keys: lowercase letters, digits, `-`, `_`; 3–64 chars; `_global` is reserved.

| Member | What it does |
|---|---|
| `GlobalProjectKey` | Key of the built-in global project. |
| `IsValidShape(string)` | True when the key has a valid shape (the reserved key is also valid in shape). |
| `IsValidForNewProject(string)` | True when the key may be used for a new, user-created application. |


### ProjectSettings

All settings of one project (the project level). Applications override a few of them, see `ClientApplicationSettings`. Stored as jsonb. Every field has a safe default. See `docs/server-settings.md`.

| Member | What it does |
|---|---|
| `CurrentSchemaVersion` | Current JSON schema version. |
| `SchemaVersion` | Schema version of the stored JSON. |
| `Methods` | Login and registration methods. |
| `UserName` | User name rules. |
| `Password` | Password rules. |
| `Email` | Email rules. |
| `Phone` | Phone rules. |
| `Registration` | Registration rules. |
| `Lockout` | Lockout rules. |
| `TwoFactor` | Two-factor and OTP rules. |
| `Sessions` | Sessions and tokens. |
| `Redirects` | Redirect rules. |
| `Clients` | Client behaviour. |
| `Localization` | Localization and branding. |
| `ExternalProviders` | External identity providers (social login, OIDC, SAML, LDAP). Changed only through the provider admin routes (they check each field): the generic settings route ignores this list. |
| `Messaging` | How emails and SMS are sent for this project. Changed only through the messaging admin routes (they hold a secret). |
| `Passkeys` | Passkey rules. |
| `CredentialScope` | Per-project override of the server credential scope. Only used if the server allows it. |


### RedirectSettings

Redirect fallbacks. A valid return URL wins. The allowed redirect URIs are OpenIddict fields of each application.

| Member | What it does |
|---|---|
| `RedirectAfterLogin` | Where to go after login. |
| `RedirectAfterRegister` | Where to go after register. |
| `RedirectAfterLogout` | Where to go after logout. |


### RegistrationSettings

Registration rules.

| Member | What it does |
|---|---|
| `RegistrationEnabled` | Is registration open. |
| `JoinMode` | Join mode. |
| `LoginRequiredAfterRegister` | User must sign in again after registering. |
| `SendConfirmationEmail` | Send the confirmation email (the link) right after registration. An application can override it. It is always sent when `Email.RequireConfirmedEmail` is on, because the user could not sign in otherwise. The user can ask for it again later. |
| `AssignedRoles` | Role names every new member of the project gets (by self registration, joining with proof, or the first external sign-in). The roles must exist in the project. A missing one is skipped and written to the audit log. The built-in admin roles are refused. |
| `AssignedGroups` | Group names every new member gets (keys: lowercase letters, digits and `. _ : -`). The groups must exist. A missing one is skipped and audited. |
| `AssignedPermissions` | Permission keys every new member gets directly (for example `orders.read`). The permissions must exist. A missing one is skipped and audited. |
| `SendConfirmationSMS` | Send the confirmation SMS right after registration. |


### SamlBinding

SAML request binding.

| Member | What it does |
|---|---|
| `Redirect` | HTTP-Redirect. |
| `Post` | HTTP-POST. |


### SamlNameIdFormat

SAML NameID format.

| Member | What it does |
|---|---|
| `Unspecified` | Unspecified. |
| `EmailAddress` | Email address. |
| `Persistent` | Persistent identifier (best for linking). |
| `Transient` | Transient (changes every login: cannot be used to link users). |


### SamlProviderSettings

A SAML 2.0 identity provider. The Server is the service provider (SP).

| Member | What it does |
|---|---|
| `IdpMetadataUrl` | URL of the identity provider's metadata (https). Fetched with the SSRF guard and cached. |
| `IdpMetadataXml` | The identity provider's metadata XML (stored as is). |
| `IdpEntityId` | Manual: entity id of the identity provider. |
| `IdpSsoUrl` | Manual: single sign-on URL (https). |
| `IdpSloUrl` | Manual: single logout URL (https). |
| `IdpSigningCertificates` | Manual: signing certificates (base64 DER or PEM). Two are allowed at once for rotation. |
| `SpEntityId` | Entity id of this application as a service provider. Empty means the generated one. |
| `NameIdFormat` | NameID format to ask for. |
| `AuthnRequestBinding` | How the request is sent. |
| `SignAuthnRequests` | Sign the authentication requests. |
| `WantAssertionsSigned` | The assertion must be signed. Default true. |
| `WantResponseSigned` | The whole response must be signed. |
| `WantAssertionsEncrypted` | Assertions must be encrypted. |
| `SpSigningCertificateProtected` | The SP certificate and private key (PFX, base64), encrypted. Made by the Server on request. Never returned. |
| `ForceAuthn` | Ask the identity provider to authenticate the user again. |
| `AllowIdpInitiated` | Accept responses that were not asked for (IdP-initiated). Default false. Only a global admin may turn it on. |
| `ClockSkewSeconds` | Allowed clock difference in seconds. Default 60, maximum 300. |
| `AttributeMappings` | SAML attribute name to local field. |
| `GroupsAttribute` | Attribute that holds groups. |
| `EmailAttribute` | Attribute that holds the email. Default: the mapped `email`. |
| `SubjectAttribute` | The user's stable id comes from the `NameId` (default) or from this attribute. |
| `RequiredAuthnContext` | Optional required authentication context class. |
| `AllowSingleLogout` | Allow single logout (SLO). Off by default: logout is local only. |


### SessionSettings

Sessions and tokens.

| Member | What it does |
|---|---|
| `AccessTokenMinutes` | Access token lifetime in minutes. |
| `RefreshTokenDays` | Refresh token lifetime in days. |
| `IdentityTokenMinutes` | Identity token lifetime in minutes. |
| `CookieLifetimeMinutes` | Cookie lifetime in minutes. Null means the client default. |
| `CookieSliding` | Sliding cookie expiry. |
| `SingleActiveSession` | On sign-in, revoke other sessions of this application. |
| `RequireConsent` | Show a consent screen. |
| `ConsentMode` | How consent works. |


### SettingEditabilityAttribute

Marks who may edit a setting. Every setting field must have it (a test checks this).

| Member | What it does |
|---|---|
| `(constructor)(SettingEditor)` | Marks who may edit a setting. Every setting field must have it (a test checks this). |
| `Editor` | The editor level. |


### SettingEditor

Who may edit a setting.

| Member | What it does |
|---|---|
| `GlobalOnly` | Only a global admin (the developer). |
| `ProjectAdminEditable` | A project admin may edit it too. |


### SmsProviderSettings

How SMS messages are sent.

| Member | What it does |
|---|---|
| `Provider` | Provider name (`SmsProviders`, or a custom one). Empty means inherit. |
| `Sender` | Sender id or number shown to the user, for the providers that support it. |


### SmsProviders

Names of the SMS providers that ship with the server.

| Member | What it does |
|---|---|
| `Inherit` | Empty: use the next level. |
| `Logging` | Writes a log line only (development). |


### SmtpSecurity

How an SMTP connection is secured. Written as text in JSON.

| Member | What it does |
|---|---|
| `StartTls` | Start in clear text and upgrade with STARTTLS when the server offers it (port 587). |
| `SslOnConnect` | TLS from the first byte (port 465). |
| `None` | No encryption. Only for a trusted local relay. |


### TwoFactorMode

Two-factor mode.

| Member | What it does |
|---|---|
| `Off` | No two-factor. |
| `Optional` | User may turn it on. |
| `Required` | Everybody needs it. |


### TwoFactorSettings

Two-factor and OTP rules.

| Member | What it does |
|---|---|
| `Mode` | Two-factor mode. |
| `AllowTotp` | Allow TOTP apps. |
| `AllowRecoveryCodes` | Allow recovery codes. |
| `RecoveryCodeCount` | Number of recovery codes. |
| `OtpLength` | OTP length. |
| `OtpLifetimeSeconds` | OTP lifetime in seconds. |
| `OtpMaxAttempts` | Max wrong OTP attempts. |
| `OtpResendSeconds` | Seconds before a new OTP may be sent. |
| `AllowPasskeys` | Allow passkeys (experimental). |


### UserNameSettings

User name rules.

| Member | What it does |
|---|---|
| `MinLength` | Minimum length. |
| `MaxLength` | Maximum length. |
| `AllowedCharacters` | Regex for allowed names. Always matched with a timeout. |
| `RequireUnique` | User names must be unique. |
<!-- generated:end -->
