# Contracts reference

## What this is

`SystemX.Auth2.Contracts` is the shared vocabulary of the Server and the Client: the request and answer types of every route, the enums, and the constants (routes, scopes, claims, error codes, grant types). Both packages reference it, and so can your own code.

## When to use it

When you call the Server routes yourself (with `IAuth2ApiClientService` or plain HTTP) and need the exact shape of a body, an enum value or a stable name. The routes are described in [api-reference.md](api-reference.md) and [admin-api.md](admin-api.md); error codes in [error-codes.md](error-codes.md).

## How to use it

- **Constants instead of strings.** `Auth2Routes` (every route), `Auth2Scopes` (`auth2.me`, `auth2.permissions.sync`, ...), `Auth2Claims` (`project_id`, `projectkey`, `client_id`, `application_id`, `pv`, ...), `Auth2ErrorCodes` (the stable codes), `Auth2GrantTypes`, `Auth2Roles` (`GlobalAdmin`, `Developer`, `ProjectAdmin`).
- **Records are the JSON.** Property names become camelCase JSON. Example from the Minimal API sample: `new LoginRequest("sara", "...")` is `{ "login": "sara", "password": "..." }` (`Scopes` is optional).
- **Results.** `Result` and `Result<T>` carry success or an error with a stable `Code`; `OperationResult` is the server-side form (it lives in the Server project, not here).

```csharp
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;

var result = await api.LoginAsync(new LoginRequest("sara", "Very-Secret-Pass-1!"), ct);
if (!result.IsSuccess && result.Error!.Code == Auth2ErrorCodes.InvalidCredentials) { /* wrong name or password */ }
```

## Related pages

- [api-reference.md](api-reference.md)
- [client-reference.md](client-reference.md)
- [error-codes.md](error-codes.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
- [Authentication flows](authentication-flows.md)
- [Troubleshooting](troubleshooting.md)
- [Client setup](client-setup.md)
- [Client authorization](client-authorization.md)
- [Current user and current client](client-current-user.md)
- [Handshake and startup](handshake-and-startup.md)
- [Getting started](getting-started.md)
- [Server reference](server-reference.md)
- [Documentation index](index.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)

## Types (generated)

<!-- generated:start -->
## `SystemX.Auth2.Contracts`


### ContractsAssemblyMarker

Marker type used to locate the Contracts assembly.


## `SystemX.Auth2.Contracts.Access`


### GroupDefinition

A group of users.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | A group of users. |


### GroupDto

A group with its roles and permission keys.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, IReadOnlyList<string>, IReadOnlyList<string>)` | A group with its roles and permission keys. |


### GroupSyncRequest

Groups sent by a client (push). `Groups` carries names and display names. `Names` is the short form (names only). When both are set, `Groups` wins. The server only adds groups that are missing.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<string>, IReadOnlyList<GroupDefinition>)` | Groups sent by a client (push). `Groups` carries names and display names. `Names` is the short form (names only). When both are set, `Groups` wins. The server only adds groups that are missing. |


### PermissionDefinition

A permission, for example `orders.read`.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string)` | A permission, for example `orders.read`. |


### PermissionDto

A permission as a client or an admin screen sees it.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, bool)` | A permission as a client or an admin screen sees it. |


### PermissionSyncRequest

Request to upsert permissions.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<PermissionDefinition>)` | Request to upsert permissions. |


### RoleDefinition

A role with an optional list of permission keys.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, IReadOnlyList<string>)` | A role with an optional list of permission keys. |


### RoleDto

A role of one application (or a global role) with its permission keys.

| Member | What it does |
|---|---|
| `(constructor)(string, string, bool, bool, IReadOnlyList<string>)` | A role of one application (or a global role) with its permission keys. |


### RoleSyncRequest

Request to upsert roles.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<RoleDefinition>)` | Request to upsert roles. |


### ScopeDefinition

A custom scope of an application, for example `reports.read`. Names starting with `auth2.` and the standard OpenID Connect scopes are reserved.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | A custom scope of an application, for example `reports.read`. Names starting with `auth2.` and the standard OpenID Connect scopes are reserved. |


### ScopeDto

A custom scope as the server knows it.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | A custom scope as the server knows it. |


### ScopeSyncRequest

Scopes sent by a client (push).

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<ScopeDefinition>)` | Scopes sent by a client (push). |


### SyncResponse

Result of a sync call. Sync never deletes.

| Member | What it does |
|---|---|
| `(constructor)(int, int, int, IReadOnlyList<string>)` | Result of a sync call. Sync never deletes. |


## `SystemX.Auth2.Contracts.Admin`


### AdminAuthorizationDto

An OpenIddict authorization for admin management.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, string, IReadOnlyList<string>, Nullable<DateTimeOffset>)` | An OpenIddict authorization for admin management. |


### AdminBulkUserRequest

Request to perform a bulk operation on users.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<Guid>, string, string)` | Request to perform a bulk operation on users. |


### AdminBulkUserResponse

Result of a bulk operation on users.

| Member | What it does |
|---|---|
| `(constructor)(int, int, IReadOnlyList<string>)` | Result of a bulk operation on users. |


### AdminCreateUserRequest

Create a user as an admin. If the person already exists, only the membership is added and no password is set.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, string, string)` | Create a user as an admin. If the person already exists, only the membership is added and no password is set. |


### AdminCreateUserResponse

Answer of "create user".

| Member | What it does |
|---|---|
| `(constructor)(Guid, bool, bool)` | Answer of "create user". |


### AdminGroupDto

A group with its id.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, IReadOnlyList<string>, IReadOnlyList<string>, int)` | A group with its id. |


### AdminPermissionDto

A permission with its id.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, string, bool, string)` | A permission with its id. |


### AdminRoleDto

A role with its id, for the admin screens.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, bool, bool, IReadOnlyList<string>)` | A role with its id, for the admin screens. |


### AdminScopeDto

A scope definition for admin management.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, IReadOnlyList<string>, bool, Nullable<Guid>)` | A scope definition for admin management. |


### AdminSessionDto

An active or revoked session for admin management.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, IReadOnlyList<string>, Nullable<DateTimeOffset>, Nullable<DateTimeOffset>, string, string)` | An active or revoked session for admin management. |


### AdminUpdateUserRequest

Change a user's profile. Null means "leave as it is".

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, Nullable<bool>)` | Change a user's profile. Null means "leave as it is". |


### AuditEventDto

One audit event. No secrets are ever stored in it.

| Member | What it does |
|---|---|
| `(constructor)(Guid, DateTimeOffset, Nullable<Guid>, Nullable<Guid>, string, string, string, string, string, string, string, Nullable<Guid>)` | One audit event. No secrets are ever stored in it. |


### BlockedAaguidsDto

The authenticator models (AAGUID) that cannot be registered as passkeys.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<Guid>)` | The authenticator models (AAGUID) that cannot be registered as passkeys. |


### BrandingAssetDto

A branding asset stored in the database.

| Member | What it does |
|---|---|
| `(constructor)(Guid, Guid, string, string, long, string, DateTimeOffset)` | A branding asset stored in the database. |


### ClientSetupDto

What a client needs in its configuration. `ClientSecret` is only set right after creation or rotation (shown once). `ProjectKey` is null on a single-project server. `Snippet` is C# code to paste into the client's startup.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, string, string)` | What a client needs in its configuration. `ClientSecret` is only set right after creation or rotation (shown once). `ProjectKey` is null on a single-project server. `Snippet` is C# code to paste into the client's startup. |


### CreateApplicationResponse

Answer of "create application". `ClientSecret` is shown only this once. `Setup` is the copy-paste client configuration.

| Member | What it does |
|---|---|
| `(constructor)(ApplicationDto, string, ClientSetupDto)` | Answer of "create application". `ClientSecret` is shown only this once. `Setup` is the copy-paste client configuration. |


### CreateGroupRequest

Create a group.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Create a group. |


### CreateProjectResponse

Answer of "create project".

| Member | What it does |
|---|---|
| `(constructor)(ProjectDto)` | Answer of "create project". |


### CreateRoleRequest

Create a role.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Create a role. |


### CreateScopeRequest

Request to create a scope.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, IReadOnlyList<string>)` | Request to create a scope. |


### EmailDomainRouteDto

One provider and its email domains (home-realm discovery).

| Member | What it does |
|---|---|
| `(constructor)(string, IReadOnlyList<string>)` | One provider and its email domains (home-realm discovery). |


### EmailTemplateDto

An email template.

| Member | What it does |
|---|---|
| `(constructor)(Guid, Guid, string, string, string, string, bool, DateTimeOffset)` | An email template. |


### ExternalMappingDto

A group, claim or attribute to role, group or permission mapping.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, string, string, string, Guid)` | A group, claim or attribute to role, group or permission mapping. |


### ExternalMappingRequest

Create or change a mapping. `SourceType` is `Group`, `Claim` or `Attribute`; `MatchType` is `Exact` or `Prefix`; `TargetType` is `Role`, `Group` or `Permission`.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, string, string, Guid)` | Create or change a mapping. `SourceType` is `Group`, `Claim` or `Attribute`; `MatchType` is `Exact` or `Prefix`; `TargetType` is `Role`, `Group` or `Permission`. |


### ExternalProviderAdminDto

A provider of an application as the admin API shows it. Secrets are never included: `SecretIsSet` says which secrets exist. `Settings` is the settings object (with the `kind` field) without any `*Protected` value. `Urls` are the addresses to enter at the provider (callback, or ACS, SLO and metadata).

| Member | What it does |
|---|---|
| `(constructor)(string, ExternalProviderType, bool, string, JsonElement, IReadOnlyDictionary<string, bool>, IReadOnlyDictionary<string, string>, string, IReadOnlyList<string>)` | A provider of an application as the admin API shows it. Secrets are never included: `SecretIsSet` says which secrets exist. `Settings` is the settings object (with the `kind` field) without any `*Protected` value. `Urls` are the addresses to enter at the provider (callback, or ACS, SLO and metadata). |


### ExternalProviderTestResult

Result of a provider test. `Details` never contain secrets.

| Member | What it does |
|---|---|
| `(constructor)(bool, string, string, IReadOnlyDictionary<string, string>)` | Result of a provider test. `Details` never contain secrets. |


### ExternalProviderTypeDto

A provider type with its fields, defaults and the page that explains the setup.

| Member | What it does |
|---|---|
| `(constructor)(ExternalProviderType, string, string, IReadOnlyList<ProviderFieldDto>, IReadOnlyList<string>, string, bool)` | A provider type with its fields, defaults and the page that explains the setup. |


### JobRunDto

History record of a maintenance job run.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, DateTimeOffset, Nullable<DateTimeOffset>, bool, string, int)` | History record of a maintenance job run. |


### JobStatusDto

Status summary of a maintenance job.

| Member | What it does |
|---|---|
| `(constructor)(string, Nullable<DateTimeOffset>, Nullable<DateTimeOffset>, string, bool)` | Status summary of a maintenance job. |


### LdapTestUserRequest

A user name to look up in the directory (no password).

| Member | What it does |
|---|---|
| `(constructor)(string)` | A user name to look up in the directory (no password). |


### LdapTestUserResult

What a directory lookup found.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyDictionary<string, string[]>, IReadOnlyList<string>)` | What a directory lookup found. |


### LockUserRequest

Lock a user. Null `UntilUtc` means until an admin unlocks.

| Member | What it does |
|---|---|
| `(constructor)(Nullable<DateTimeOffset>)` | Lock a user. Null `UntilUtc` means until an admin unlocks. |


### ProviderFieldDto

One setting field of a provider type (for a form in the admin UI). `LabelKey` and `HelpKey` are resource keys.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, bool, string, string)` | One setting field of a provider type (for a form in the admin UI). `LabelKey` and `HelpKey` are resource keys. |


### RedirectUrisDto

Redirect URIs of an application (the OpenIddict fields). Exact match, no wildcards.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<string>, IReadOnlyList<string>)` | Redirect URIs of an application (the OpenIddict fields). Exact match, no wildcards. |


### RotateProviderSecretRequest

New secret values. Fill only the one that fits the provider type: `ClientSecret` (OIDC), `PrivateKey` (Apple), `BindPassword` (LDAP).

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | New secret values. Fill only the one that fits the provider type: `ClientSecret` (OIDC), `PrivateKey` (Apple), `BindPassword` (LDAP). |


### RotateSecretResponse

Answer of "rotate secret". The new secret is shown only this once.

| Member | What it does |
|---|---|
| `(constructor)(string, ClientSetupDto)` | Answer of "rotate secret". The new secret is shown only this once. |


### SamlSpCertificateDto

The public part of a new SAML service provider certificate.

| Member | What it does |
|---|---|
| `(constructor)(string, DateTimeOffset, string)` | The public part of a new SAML service provider certificate. |


### SaveEmailTemplateRequest

Request to save an email template.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Request to save an email template. |


### SessionDto

One open session (an OpenIddict authorization) of a user.

| Member | What it does |
|---|---|
| `(constructor)(string, Nullable<DateTimeOffset>, IReadOnlyList<string>)` | One open session (an OpenIddict authorization) of a user. |


### SetObsoleteRequest

Mark a permission obsolete (or not).

| Member | What it does |
|---|---|
| `(constructor)(bool)` | Mark a permission obsolete (or not). |


### UpdateGroupRequest

Request to update a group.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Request to update a group. |


### UpdatePermissionRequest

Request to update a permission.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Request to update a permission. |


### UpdateRoleRequest

Request to update a role.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Request to update a role. |


### UpdateScopeRequest

Request to update a scope.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyList<string>)` | Request to update a scope. |


### UploadBrandingAssetRequest

Request to upload a branding asset.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Request to upload a branding asset. |


### UserExternalLoginDto

The external login of a user (the provider key is the id at the provider).

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | The external login of a user (the provider key is the id at the provider). |


## `SystemX.Auth2.Contracts.Applications`


### ApplicationDto

An application (a client of a project) as shown in the admin API. The secret is never part of it.

| Member | What it does |
|---|---|
| `(constructor)(Guid, Guid, string, string, bool, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, int, DateTimeOffset)` | An application (a client of a project) as shown in the admin API. The secret is never part of it. |


### ClientHandshakeRequest

Handshake check sent by the client. `ProjectKey` is null for a single-project server. `ApplicationName` is the name of the application (the client) inside the project. The body never selects the project: the server finds it from the client id. The project key is only an identifier check, never proof of anything, and it is not a secret.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Handshake check sent by the client. `ProjectKey` is null for a single-project server. `ApplicationName` is the name of the application (the client) inside the project. The body never selects the project: the server finds it from the client id. The project key is only an identifier check, never proof of anything, and it is not a secret. |


### ClientHandshakeResponse

Handshake answer. `ServerMode` tells the client which mode the server uses. `ProjectKey` is null on a single-project server.

| Member | What it does |
|---|---|
| `(constructor)(Guid, Guid, string, string, ServerMode, string, int, Nullable<TimeSpan>, bool)` | Handshake answer. `ServerMode` tells the client which mode the server uses. `ProjectKey` is null on a single-project server. |


### CreateApplicationRequest

Create an application in a project. The client id is `{projectkey}.{name}` unless one is given. It must be unique on the server.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>)` | Create an application in a project. The client id is `{projectkey}.{name}` unless one is given. It must be unique on the server. |


### CreateProjectRequest

Create a project. Only a global admin may do this. The project key never changes afterwards.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Create a project. Only a global admin may do this. The project key never changes afterwards. |


### ProjectDto

A project as shown in the admin API.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, bool, int, DateTimeOffset)` | A project as shown in the admin API. |


### UpdateApplicationRequest

Update an application. Null lists stay as they are. Disabling blocks only this application.

| Member | What it does |
|---|---|
| `(constructor)(string, bool, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, string)` | Update an application. Null lists stay as they are. Disabling blocks only this application. |


### UpdateProjectRequest

Update a project. The project key cannot be changed. Disabling a project blocks every application of it.

| Member | What it does |
|---|---|
| `(constructor)(string, string, bool, string)` | Update a project. The project key cannot be changed. Disabling a project blocks every application of it. |


## `SystemX.Auth2.Contracts.Auth`


### ChangePasswordRequest

Change the own password for the token's application.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Change the own password for the token's application. |


### ConfirmEmailRequest

Confirm an email address with the token from the email.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string)` | Confirm an email address with the token from the email. |


### ConfirmPhoneRequest

Confirm a phone number with the code from the SMS.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Confirm a phone number with the code from the SMS. |


### ExternalProviderInfoDto

An external sign-in choice for the login page of an application.

| Member | What it does |
|---|---|
| `(constructor)(string, string, ExternalProviderType, string, bool)` | An external sign-in choice for the login page of an application. |
| `Name` | Provider name inside the application (lowercase, used in URLs). |
| `DisplayName` | Text for the button. |
| `Kind` | The kind of provider. |
| `ChallengeUrl` | Where to send the user (a POST form on the server) for redirect-based providers. Null for LDAP. |
| `LoginFormRequired` | True for LDAP: the user types a user name and password. |


### ExternalProvidersResponse

Answer of `GET /connect/external/providers`.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<ExternalProviderInfoDto>, string)` | Answer of `GET /connect/external/providers`. |
| `Providers` | The enabled providers of the application. |
| `SuggestedProvider` | Home-realm discovery: the provider that matches the `hint` (an email domain or a provider name), or null. |


### ForgotPasswordRequest

Start a password reset. The answer is always the same.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Start a password reset. The answer is always the same. |


### JoinCompleteRequest

Finish joining an application: the code that proves ownership, and the new password for this application.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Finish joining an application: the code that proves ownership, and the new password for this application. |


### LoginRequest

Login input used by the client's Self UI helpers. The token endpoint is the only issuing path.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyList<string>)` | Login input used by the client's Self UI helpers. The token endpoint is the only issuing path. |
| `Login` | User name, email or phone number. |
| `Password` | The password. Never logged. |
| `Scopes` | Optional scopes to request. |


### LoginResponse

Result of a login. Either tokens, or a pending second factor.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, Nullable<int>, bool, string)` | Result of a login. Either tokens, or a pending second factor. |


### OtpRequest

Ask for a one-time code. `Purpose` is `login` or `confirm_phone` (see `Auth2OtpPurposes`). The answer is always the same.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Ask for a one-time code. `Purpose` is `login` or `confirm_phone` (see `Auth2OtpPurposes`). The answer is always the same. |


### PasskeyDto

One passkey of a user, as the UI shows it. The id is the credential id in base64url.

| Member | What it does |
|---|---|
| `(constructor)(string, string, DateTimeOffset, UInt32, bool, bool, bool, IReadOnlyList<string>)` | One passkey of a user, as the UI shows it. The id is the credential id in base64url. |


### RecoveryCodesRequest

Make new recovery codes. A code from the authenticator app is needed.

| Member | What it does |
|---|---|
| `(constructor)(string)` | Make new recovery codes. A code from the authenticator app is needed. |


### RecoveryCodesResponse

New recovery codes. They are shown once.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<string>)` | New recovery codes. They are shown once. |


### RegisterRequest

Register a user. The application always comes from the client token, never from this body.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string, string, string, string, string)` | Register a user. The application always comes from the client token, never from this body. |


### RegisterResponse

Result of register. Registration is idempotent.

| Member | What it does |
|---|---|
| `(constructor)(Guid, bool, bool, bool)` | Result of register. Registration is idempotent. |


### ResendEmailConfirmationRequest

Send the email confirmation again. The answer is always the same.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Send the email confirmation again. The answer is always the same. |


### ResetPasswordRequest

Finish a password reset with the token from the email or SMS.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Finish a password reset with the token from the email or SMS. |


### TwoFactorDisableRequest

Turn off two-factor. The password is needed.

| Member | What it does |
|---|---|
| `(constructor)(string)` | Turn off two-factor. The password is needed. |


### TwoFactorEnableRequest

Turn on two-factor with a code from the authenticator app.

| Member | What it does |
|---|---|
| `(constructor)(string)` | Turn on two-factor with a code from the authenticator app. |


### TwoFactorEnableResponse

The recovery codes. They are shown once.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<string>)` | The recovery codes. They are shown once. |


### TwoFactorRequest

Second step of login. Send `Code` or `RecoveryCode`.

| Member | What it does |
|---|---|
| `(constructor)(string, string, string)` | Second step of login. Send `Code` or `RecoveryCode`. |


### TwoFactorSetupResponse

Data for the authenticator app. Show `SharedKey` as text and `AuthenticatorUri` as a QR code.

| Member | What it does |
|---|---|
| `(constructor)(string, string)` | Data for the authenticator app. Show `SharedKey` as text and `AuthenticatorUri` as a QR code. |


## `SystemX.Auth2.Contracts.Common`


### ApiError

Error body returned by the server API. Compatible with problem details.

| Member | What it does |
|---|---|
| `(constructor)(string, string, IReadOnlyDictionary<string, string[]>)` | Error body returned by the server API. Compatible with problem details. |
| `Code` | Stable machine-readable code. See `Auth2ErrorCodes`. |
| `Message` | Localized human-readable message. |
| `Details` | Optional extra data, for example field errors. |


### PagedResult<T>

One page of a list.

| Member | What it does |
|---|---|
| `(constructor)(IReadOnlyList<T>, int, int, int)` | One page of a list. |
| `MaxPageSize` | Largest page size the server accepts. |
| `TotalPages` | Total number of pages. |


### Result

Result of an operation without a value.

| Member | What it does |
|---|---|
| `IsSuccess` | True when the operation worked. |
| `Error` | The error, or null on success. |
| `Success()` | Creates a success result. |
| `Failure(string, string)` | Creates a failure result. |


### Result<T>

Result of an operation with a value.

| Member | What it does |
|---|---|
| `Value` | The value. Only set on success. |
| `Success(T)` | Creates a success result. |
| `Failure(string, string)` | Creates a failure result. |


## `SystemX.Auth2.Contracts.Constants`


### Auth2Claims

Claim names used in tokens.

| Member | What it does |
|---|---|
| `Subject` | Subject (user id). |
| `PreferredUserName` | User name. |
| `Email` | Email. |
| `PhoneNumber` | Phone number. |
| `GivenName` | First name. |
| `FamilyName` | Last name. |
| `Name` | Full name. |
| `Role` | Role of the project. |
| `Scope` | Scope. |
| `ClientId` | Client id. |
| `AccessVersion` | Access version. |
| `ApplicationId` | Application id (the client that asked for the token). |
| `ProjectId` | Project id (the tenant). |
| `ProjectKey` | Project key. Omitted in single-project mode. |
| `Amr` | Authentication methods reference. |
| `IdentityProvider` | Name of the external provider the user signed in with (`idp`). Absent for local sign-in. |
| `SessionId` | Session id. |
| `Locale` | Saved culture of the user. |
| `Machine` | Set to `true` on machine (client credentials) tokens. User tokens never have it. |


### Auth2ErrorCodes

Stable error codes. Also used as localization keys.

| Member | What it does |
|---|---|
| `InvalidCredentials` | Wrong user or wrong password (same code on purpose). |
| `LockedOut` | The account is locked. |
| `TwoFactorRequired` | A second factor is needed. |
| `NotRegisteredInProject` | The user does not belong to this project. |
| `ProjectNotFound` | The project does not exist. |
| `ProjectDisabled` | The project is disabled. Only the audit log shows this; callers see `AccessDenied`. |
| `ApplicationDisabled` | The application (client) is disabled. Only the audit log shows this; callers see `AccessDenied`. |
| `AccessDenied` | The generic public answer when a project or an application is disabled. The real reason is only in the audit log. |
| `ProjectKeyOrNameMismatch` | ProjectKey or application name does not match. |
| `ServerModeMismatch` | Client and server use different modes. |
| `PasswordTooShort` | Password is too short. |
| `PasswordRequiresDigit` | Password needs a digit. |
| `PasswordRequiresLower` | Password needs a lowercase letter. |
| `PasswordRequiresUpper` | Password needs an uppercase letter. |
| `PasswordRequiresNonAlphanumeric` | Password needs a non-alphanumeric character. |
| `PasswordTooLong` | Password is too long. |
| `PasswordRequiresUniqueChars` | Password needs more different characters. |
| `PasswordBanned` | Password is on the banned list. |
| `InvalidUserName` | User name has a wrong length or wrong characters. |
| `InvalidEmail` | Email is not valid. |
| `EmailDomainNotAllowed` | The email domain is not allowed in this application. |
| `InvalidPhoneNumber` | Phone number is not valid. |
| `UserNotInProject` | The user does not belong to this application (no row yet). |
| `EmailNotConfirmed` | Email must be confirmed before login. |
| `PhoneNotConfirmed` | Phone number must be confirmed before login. |
| `TwoFactorSetupRequired` | Two-factor is required by the application but the user has not set it up yet. |
| `GrantNotAllowed` | The grant type is not allowed for this application or client. |
| `TwoFactorNotEnabled` | Two-factor is not on for this user. |
| `TwoFactorNotAllowed` | The application does not allow authenticator-app two-factor. |
| `ConsentRequired` | Consent is needed (OIDC `prompt=none` cannot ask). |
| `LoginMethodNotAllowed` | Login method not allowed. |
| `RegistrationDisabled` | Registration is closed. |
| `InvalidToken` | Token is missing or invalid. |
| `InsufficientScope` | Scope is missing. |
| `Forbidden` | The caller may not do this. |
| `ValidationFailed` | Validation failed. |
| `NotFound` | The record was not found. |
| `Conflict` | The record was changed by someone else. |
| `RateLimited` | Too many requests. |
| `InvalidOtp` | OTP code is wrong or expired. |
| `OwnershipProofRequired` | Proof of ownership is needed to join an application. |
| `SyncNotAllowed` | Sync is not allowed for this application. |
| `InvalidPermissionKey` | A permission key is invalid. |
| `InvalidScopeName` | A scope name is invalid, or it is reserved (`auth2.*` and the standard OpenID Connect scopes). |
| `ExternalEmailExistsNotVerified` | The provider gave an email that is not verified, and a user with that email exists: nothing was linked. |
| `ExternalRegistrationNotAllowed` | Registration through this external provider is not allowed. |
| `ExternalProviderDisabled` | The external provider is disabled or unknown. |
| `ExternalClaimsInvalid` | The provider's answer is missing something or is not valid. |
| `CannotRemoveLastLoginMethod` | The user cannot remove the last way to sign in. |
| `SamlResponseInvalid` | A SAML response did not pass validation. |
| `SamlReplayDetected` | A SAML response, assertion or request id was used before. |
| `LdapUnavailable` | The directory could not be reached. |
| `LdapAccountDisabled` | The directory account is disabled (only shown when the application reveals it). |
| `PasskeyNotAllowed` | Passkeys are not allowed here. |
| `PasskeyLimitReached` | The user has the maximum number of passkeys. |
| `PasskeyBlockedAuthenticator` | This authenticator model is blocked. |
| `TooManyItems` | Too many items in one request. |
| `InvalidRedirectUri` | Redirect URI not allowed. |
| `ServerError` | Unexpected server error. |
| `All` | All codes. Each one needs an English text in `Errors.resx`. |


### Auth2GrantTypes

Custom OAuth grant types of SystemX.Auth2.

| Member | What it does |
|---|---|
| `TwoFactor` | Second step of a login. Inputs: `two_factor_token` and `code` or `recovery_code`. |
| `Otp` | Login with a one-time code. The code is requested with `POST /api/auth2/otp/request`. |


### Auth2OtpPurposes

Why a one-time code was made. A code only works for its own purpose.

| Member | What it does |
|---|---|
| `Login` | Login with a one-time code. |
| `ConfirmPhone` | Confirm a phone number. |
| `Join` | Proof of ownership when joining a second application. |
| `PasswordReset` | Password reset by phone. |


### Auth2Roles

Built-in role and project names.

| Member | What it does |
|---|---|
| `GlobalAdmin` | Global admin role (global, no application). |
| `Developer` | Developer role with access to developer dashboard and system administration. |
| `ProjectAdmin` | Project admin role (one per application). |
| `GlobalProjectKey` | Project key of the built-in global project. |


### Auth2Routes

Every route string. Client and Server both use this class so they never disagree.

| Member | What it does |
|---|---|
| `Base` | Base path of the JSON API. |
| `Register` | Register. |
| `OtpRequest` | Request OTP. |
| `PasswordForgot` | Forgot password. |
| `PasswordReset` | Reset password. |
| `PasswordChange` | Change password. |
| `EmailConfirm` | Confirm email. |
| `PhoneConfirm` | Confirm phone. |
| `EmailConfirmResend` | Send the email confirmation again. |
| `JoinComplete` | Finish joining a second application (proof of ownership + new password). |
| `TwoFactorSetup` | Start TOTP setup. |
| `TwoFactorEnable` | Enable TOTP. |
| `TwoFactorDisable` | Disable TOTP. |
| `TwoFactorRecoveryCodes` | Recovery codes. |
| `Me` | Current user. |
| `Handshake` | Client handshake. |
| `Permissions` | Permissions (POST sync, GET pull). |
| `PermissionsSync` | Permission sync. |
| `Roles` | Roles (GET pull). |
| `RolesSync` | Role sync. |
| `Groups` | Groups (GET pull). |
| `GroupsSync` | Group sync (POST). |
| `Scopes` | Custom scopes of the application (GET pull). |
| `ExternalProviders` | Lists the enabled external sign-in providers of the application (client-credentials token with the handshake scope). |
| `ScopesSync` | Scope sync (POST). |
| `UserAccess` | Access of one user. Replace `{id}`. |
| `Admin` | Base path of the admin API. |
| `HealthLive` | Liveness check. |
| `HealthReady` | Readiness check. |
| `Authorize` | OpenIddict authorize endpoint. |
| `Token` | OpenIddict token endpoint. |
| `EndSession` | OpenIddict end-session endpoint. |
| `UserInfo` | OpenIddict userinfo endpoint. |
| `Revoke` | OpenIddict revocation endpoint. |
| `Introspect` | OpenIddict introspection endpoint. |
| `UserAccessFor(Guid)` | Builds the user access route for a user id. |


### Auth2Scopes

Scope names defined by SystemX.Auth2.

| Member | What it does |
|---|---|
| `Register` | Register users. |
| `Login` | Request OTP, reset passwords and similar login helpers. |
| `Me` | Read the current user. |
| `ClientHandshake` | Run the client handshake. |
| `PermissionsSync` | Sync permissions. |
| `RolesSync` | Sync roles. |
| `GroupsRead` | Read groups. |
| `GroupsSync` | Send groups (only when the application allows group sync). |
| `ScopesSync` | Send and read the custom scopes of an application. |
| `Admin` | Project admin. |
| `AdminGlobal` | Global admin. |
| `All` | All SystemX.Auth2 scopes. |


## `SystemX.Auth2.Contracts.Enums`


### AccessLoadMode

When the client loads access data (roles and permissions).

| Member | What it does |
|---|---|
| `Lazy` | Load on first use. |
| `OnSignIn` | Load when the user signs in. |
| `RolesInCookie` | Put roles in the cookie. |
| `None` | Nothing happens. |


### ClientType

The kind of client application.

| Member | What it does |
|---|---|
| `Api` | A web API. |
| `Mvc` | An MVC app. |
| `RazorPages` | A Razor Pages app. |
| `Blazor` | A Blazor app. |
| `Machine` | A background or machine client. |


### ConsentMode

How the consent screen is used.

| Member | What it does |
|---|---|
| `Implicit` | No consent screen for trusted clients. |
| `Explicit` | Ask for consent once. |
| `Systematic` | Always ask for consent. |


### CredentialScope

Where credentials (password, 2FA, lockout) are stored.

| Member | What it does |
|---|---|
| `PerProject` | Each application has its own credentials. |
| `Shared` | All applications share one set of credentials. |


### CurrentUserSource

Where the client reads current user data from.

| Member | What it does |
|---|---|
| `TokenOnly` | Read only from the token. |
| `TokenAndServer` | Read from the token and from the server. |


### ExternalProviderType

The kinds of external identity source. Passkeys are not a provider: they are a credential of the user.

| Member | What it does |
|---|---|
| `Google` | Google (OpenID Connect). |
| `Microsoft` | Microsoft account or Entra ID (OpenID Connect). |
| `GitHub` | GitHub (OAuth 2.0). |
| `Apple` | Sign in with Apple. |
| `Facebook` | Facebook Login (OAuth 2.0). |
| `GenericOidc` | Any OpenID Connect provider with a discovery document. |
| `Saml` | A SAML 2.0 identity provider. The server is the service provider. |
| `Ldap` | An LDAP directory or Active Directory (user name and password are checked against it). |


### GroupSyncPolicy

How groups move between the client and the server. Pushing groups is off on the server unless the application allows it (`Clients.GroupSyncAllowed`), because admins normally decide groups. Push only adds missing groups; it never changes or removes admin work.

| Member | What it does |
|---|---|
| `None` | Nothing happens. |
| `Pull` | Client reads the list from the server. |
| `Push` | Client sends its list to the server (adds missing groups only). |
| `Both` | Push first, then pull. |


### HandshakeStatus

Result of the client handshake.

| Member | What it does |
|---|---|
| `Ok` | The handshake worked. |
| `ServerUnavailable` | The server could not be reached. |
| `NotFound` | The application was not found. |
| `Rejected` | The server rejected the client (secret, name, key or mode). |


### LoginMethod

Ways a user can sign in.

| Member | What it does |
|---|---|
| `UserNamePassword` | User name and password. |
| `EmailPassword` | Email and password. |
| `PhonePassword` | Phone number and password. |
| `PhoneOtp` | Phone number and one-time code. |
| `EmailOtp` | Email and one-time code. |
| `External` | An external provider (social login, OpenID Connect, SAML). |
| `Ldap` | A directory (LDAP / Active Directory) user name and password. |
| `Passkey` | A passkey (WebAuthn). |


### PermissionSyncPolicy

How permissions move between the client and the server.

| Member | What it does |
|---|---|
| `None` | Nothing happens. |
| `Push` | Client sends its list to the server. |
| `Pull` | Client reads the list from the server. |
| `Both` | Push first, then pull. |


### RoleSyncPolicy

How roles move between the client and the server.

| Member | What it does |
|---|---|
| `None` | Nothing happens. |
| `Push` | Client sends its list to the server. |
| `Pull` | Client reads the list from the server. |
| `Both` | Push first, then pull. |


### ScanningPolicy

Where the client finds permissions and roles.

| Member | What it does |
|---|---|
| `Manual` | Only the lists written in options. |
| `AssemblyScan` | Find items from endpoints and attributes. |
| `Both` | Push first, then pull. |


### ScopeSyncPolicy

How the custom scopes of an application move between the client and the server.

| Member | What it does |
|---|---|
| `None` | Nothing happens. |
| `Push` | Client sends its list to the server. |
| `Pull` | Client reads the list from the server. |
| `Both` | Push first, then pull. |


### ServerMode

Whether the server serves one project or many.

| Member | What it does |
|---|---|
| `SingleProject` | The server has exactly one project. |
| `MultiProject` | The server serves many projects. |


### StartupFailureBehavior

What the client does when the startup handshake fails.

| Member | What it does |
|---|---|
| `Throw` | Stop the app with an exception. |
| `Continue` | Log a warning and keep running. |


### UiMode

Who shows the login screens.

| Member | What it does |
|---|---|
| `None` | Nothing happens. |
| `Hosted` | The server shows the login pages. |
| `Self` | The client app shows its own login pages. |


## `SystemX.Auth2.Contracts.Users`


### CurrentUserInfo

The signed-in user and their access, for the token's application. `DirectPermissions` are grants made to the user. `RolePermissions` come from the user's roles or the roles of the user's groups. `Permissions` is everything (those two, plus permissions of the user's groups). The two lists are null when talking to an older server.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, string, string, string, string, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, int, IReadOnlyList<string>, IReadOnlyList<string>)` | The signed-in user and their access, for the token's application. `DirectPermissions` are grants made to the user. `RolePermissions` come from the user's roles or the roles of the user's groups. `Permissions` is everything (those two, plus permissions of the user's groups). The two lists are null when talking to an older server. |


### UserDto

A user as shown in the admin API.

| Member | What it does |
|---|---|
| `(constructor)(Guid, string, string, bool, string, bool, string, string, string, bool, bool, bool, DateTimeOffset, Nullable<DateTimeOffset>)` | A user as shown in the admin API. |
<!-- generated:end -->
