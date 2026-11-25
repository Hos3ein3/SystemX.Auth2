# Enums reference

## What this is

Every enum of SystemX.Auth2: the values, what each one means, where the enum is used, which value to choose and why. The values and their short meanings are taken from the XML comments of the code (the generator is `tools/docs/enums.py`); the guidance next to them is written by hand.

## When to use it

When a setting or an option offers a list of values and you must pick one, or when you read a value in the database or an answer and want to know what it means.

## How to use it

- In **JSON** (settings, admin API) enum values are written as their names, for example `"mode": "Required"`. Numbers are not accepted in settings.
- In **configuration** (`appsettings.json`, environment variables) use the names too: `Auth2:Mode = SingleProject`, `Auth2__Messaging__Email__Security=StartTls`.
- In **C#** use the type: `PermissionSyncPolicy.Push`. The Contracts enums live in `SystemX.Auth2.Contracts.Enums`.
- Where a value is **stored** (a column of `projects.settings_json`, `external_group_mappings`, `permissions.source`), the same name is stored.

## Sign-in and accounts

The values you choose in the project and server settings that decide who can sign in and how.

### LoginMethod

`SystemX.Auth2.Contracts.Enums` · Ways a user can sign in.

**Used by** `Methods.AllowedLoginMethods` and `Methods.AllowedRegisterMethods` of the project settings, by `ExternalSignInRequest.Method`, and by the audit data of a sign-in.

| Value | Meaning | Choose it when |
|---|---|---|
| `UserNamePassword` | User name and password. | People sign in with a user name. Default. |
| `EmailPassword` | Email and password. | People sign in with their email. Default. |
| `PhonePassword` | Phone number and password. | Phone is the identifier and there is a password. |
| `PhoneOtp` | Phone number and one-time code. | Passwordless sign-in with an SMS code. The code **is** the sign-in (no authenticator step). |
| `EmailOtp` | Email and one-time code. | Passwordless sign-in with an email code. |
| `External` | An external provider (social login, OpenID Connect, SAML). | You added Google, Microsoft, GitHub, Apple, Facebook, OpenID Connect or SAML providers. |
| `Ldap` | A directory (LDAP / Active Directory) user name and password. | Users sign in with a directory (LDAP / Active Directory) password. |
| `Passkey` | A passkey (WebAuthn). | You turned passkeys on (`Passkeys.AllowPasskeys`). |

**Why it exists.** One list decides which entry points work. A method that is not listed is refused with `login_method_not_allowed` before the password is checked, and a provider (SAML, LDAP, passkeys) does nothing until its method is listed.

```json
"methods": { "allowedLoginMethods": ["EmailPassword", "External", "Passkey"], "allowedRegisterMethods": ["EmailPassword"] }
```

### TwoFactorMode

`SystemX.Auth2.Server.Domain.Settings` · Two-factor mode.

**Used by** `TwoFactor.Mode` of the project settings ([two-factor.md](two-factor.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `Off` | No two-factor. | You do not want a second step at all (even people who set it up are not asked). |
| `Optional` | User may turn it on. | People may turn it on in their profile. Default. |
| `Required` | Everybody needs it. | Admin or finance projects, after everyone has enrolled. |

**Why it exists.** It decides whether the authenticator-app step exists for a project. `Required` blocks people who have not enrolled, so enrol first.

```json
"twoFactor": { "mode": "Required", "allowTotp": true }
```

### JoinMode

`SystemX.Auth2.Server.Domain.Settings` · Join mode of a project.

**Used by** `Registration.JoinMode` of the project settings and by the join flow ([authentication-flows.md](authentication-flows.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `AutoJoin` | A user with proof of ownership may join. | Anyone with proof of ownership may join. Default. |
| `InviteOnly` | Only invited users may join. | Only people an admin adds may join. |
| `Closed` | Nobody may join. | Nobody may join; the project is frozen for new members. |

**Why it exists.** A person who already has an account in another project can join this one only if the project allows it, and then only with proof of ownership (a code to a confirmed email or phone).

```json
"registration": { "joinMode": "InviteOnly" }
```

### ConsentMode

`SystemX.Auth2.Contracts.Enums` · How the consent screen is used.

**Used by** `Sessions.ConsentMode` of the project and `ConsentMode` of an application, together with `Sessions.RequireConsent`.

| Value | Meaning | Choose it when |
|---|---|---|
| `Implicit` | No consent screen for trusted clients. | Your own, trusted applications. No consent page. |
| `Explicit` | Ask for consent once. | Third-party applications: ask once, remember the answer. |
| `Systematic` | Always ask for consent. | Very sensitive scopes: ask at every authorization. |

**Why it exists.** It maps to the consent type of the OpenIddict application. With `RequireConsent` off no consent page is shown whatever the mode says; with it on, `Implicit` is treated as `Explicit`.

```json
"sessions": { "requireConsent": true, "consentMode": "Explicit" }
```

### CredentialScope

`SystemX.Auth2.Contracts.Enums` · Where credentials (password, 2FA, lockout) are stored.

**Used by** `Auth2:CredentialScope` (server) and `CredentialScope` of a project (when `AllowCredentialScopeOverride` is on). See [credential-scope.md](credential-scope.md) and [identity-stores.md](identity-stores.md).

| Value | Meaning | Choose it when |
|---|---|---|
| `PerProject` | Each application has its own credentials. | Several products on one server and the same person may have a different password in each. Default. |
| `Shared` | All applications share one set of credentials. | One company, one password for all applications. Single-project mode always uses this. |

**Why it exists.** It decides where the password, the two-factor key and the lockout state are kept. Choose it once, before users register: changing it later makes existing passwords unreadable.

```json
"Auth2": { "CredentialScope": "Shared" }
```

### ServerMode

`SystemX.Auth2.Contracts.Enums` · Whether the server serves one project or many.

**Used by** `Auth2:Mode` and the call `ServerWithSingleProject()` ([server-setup.md](server-setup.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `SingleProject` | The server has exactly one project. | One product, one set of users. |
| `MultiProject` | The server serves many projects. | Several tenants. Default. |

**Why it exists.** In `MultiProject` every request is about one project (found from the client id) and people have memberships; in `SingleProject` there is one fixed project and no membership rows.

```csharp
builder.Services.AddSystemXAuth2Server(builder.Configuration);
builder.Services.ServerWithSingleProject();   // sets Mode = SingleProject
```

### UserProjectStatus

`SystemX.Auth2.Server.Domain.Entities` · Status of a user inside one project.

**Stored in** `user_projects.status` and read wherever membership is checked (`UserLookup.IsMemberAsync`).

| Value | Meaning | Choose it when |
|---|---|---|
| `Active` | Normal. | Normal. Every membership the Server creates. |
| `Suspended` | Blocked in this project only. | Block a person in this project only (set it in the database). |

**Why it exists.** A person can be blocked in one project without being deactivated everywhere (`users.is_active`). **Only `Active` memberships count**: a `Suspended` person gets the generic `invalid_credentials` in that project and no access data. No admin route sets `Suspended` today; it is set in the database.

```sql
update user_projects set status = 'Suspended' where user_id = '...' and project_id = '...';
```

### NotificationChannel

`SystemX.Auth2.Server.Application` · Where a code or link is sent.

**Used by** `IUserNotifier.SendOtpAsync` and the join flow.

| Value | Meaning | Choose it when |
|---|---|---|
| `Email` | Email. | The code goes to the email address. |
| `Sms` | SMS. | The code goes to the phone number. |

**Why it exists.** It says where a one-time code goes. Proof of ownership uses `Email` when the person has a **confirmed** email, otherwise `Sms` when the phone is confirmed.

### OtpVerifyResult

`SystemX.Auth2.Server.Application` · Result of checking a one-time code.

**Returned by** `IOtpService.VerifyAsync`.

| Value | Meaning | Choose it when |
|---|---|---|
| `Ok` | The code is right. It is used up now. | The code is right and is used up now. |
| `Invalid` | Wrong code. | A wrong code (counts as an attempt). |
| `Expired` | The code is too old. | The code is too old (`TwoFactor.OtpLifetimeSeconds`). |
| `TooManyAttempts` | Too many wrong attempts. The code is dead. | The code is closed (`TwoFactor.OtpMaxAttempts`). |
| `NotFound` | There is no open code for this user and purpose. | No open code for this person and purpose. |

**Why it exists.** It tells the caller why a code failed. The one-time-code sign-in and the join flow turn every value except `Ok` into the one answer `invalid_otp`, so a stranger learns nothing about whether a code existed.

## Settings and administration

Values that describe settings themselves and what an application is meant to be.

### SettingEditor

`SystemX.Auth2.Server.Domain.Settings` · Who may edit a setting.

**Used by** the attribute `[SettingEditability(...)]` on every setting field, checked when a project or application setting is saved.

| Value | Meaning | Choose it when |
|---|---|---|
| `GlobalOnly` | Only a global admin (the developer). | The setting is security- or cost-relevant: only the developer (global admin) changes it. |
| `ProjectAdminEditable` | A project admin may edit it too. | A project admin may change it (names, redirects, cultures, registration switches). |

**Why it exists.** A project admin must not change security-relevant settings (password rules, lockout, token lifetimes). A test fails when a setting has no attribute. A project admin who changes a `GlobalOnly` field gets `403` with the field names.

```csharp
[SettingEditability(SettingEditor.GlobalOnly)] public int MinLength { get; set; } = 10;
```

### PermissionSource

`SystemX.Auth2.Server.Domain.Entities` · How a permission was created.

**Stored in** `permissions.source`.

| Value | Meaning | Choose it when |
|---|---|---|
| `Manual` | Created by an admin. | An admin made the permission (admin API or pages). |
| `Sync` | Created by client sync. | A client pushed it ([permission-sync.md](permission-sync.md)). |

**Why it exists.** It tells admins whether a permission came from a person or from code, so a sync never overwrites what an admin typed.

### ClientType

`SystemX.Auth2.Contracts.Enums` · The kind of client application.

**Used by** `Clients.AllowedClientTypes` of the project settings.

| Value | Meaning | Choose it when |
|---|---|---|
| `Api` | A web API. | A web API. |
| `Mvc` | An MVC app. | An MVC application. |
| `RazorPages` | A Razor Pages app. | A Razor Pages application. |
| `Blazor` | A Blazor app. | A Blazor application. |
| `Machine` | A background or machine client. | A background program or service. |

**Why it exists.** It lists the kinds of applications a project is meant to have. **No code reads it today**: it is stored and shown in the admin settings page only.

### UiMode

`SystemX.Auth2.Contracts.Enums` · Who shows the login screens.

**Used by** `Clients.DefaultUiMode` (project) and `UiMode` (application). **The Server does not enforce it**: the Client package decides with `UseHostedUi()` / `UseSelfUi()`.

| Value | Meaning | Choose it when |
|---|---|---|
| `None` | No sign-in screens (an API: only 401 / 403 answers). | An API: only 401 / 403 answers. |
| `Hosted` | The server shows the login pages. | Recommended: the Server shows the sign-in pages and your application never sees a password. |
| `Self` | The client app shows its own login pages. | Your application shows its own form (and `AllowSelfUiLogin` must be on). |

**Why it exists.** It records how an application is meant to show its sign-in screens. What is enforced is `AllowSelfUiLogin` on the application (the password grant).

```csharp
builder.Services.AddAuth2Client(section).UseHostedUi();   // UiMode.Hosted
```

## The Client package

The options of `AddAuth2Client` and the `Enable...` / `Use...` methods ([client-builder.md](client-builder.md)).

### StartupFailureBehavior

`SystemX.Auth2.Contracts.Enums` · What the client does when the startup handshake fails.

**Used by** `Auth2ClientOptions.OnServerUnavailable` and `OnProjectNotFound` ([client-builder.md](client-builder.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `Throw` | Stop the app with an exception. | The application is useless without the Server (a worker), or the failure is a configuration error you want to see at once. |
| `Continue` | Log a warning and keep running. | A website that can serve some requests without the Server. The handshake is retried in the background. |

**Why it exists.** It chooses between "fail fast" and "stay up" when the start-up handshake fails.

```csharp
o.OnServerUnavailable = StartupFailureBehavior.Continue;
o.OnProjectNotFound = StartupFailureBehavior.Throw;
```

### HandshakeStatus

`SystemX.Auth2.Contracts.Enums` · Result of the client handshake.

**Held by** `Auth2ClientState` and carried by `Auth2StartupException`; the `auth2` health check reads it.

| Value | Meaning | Choose it when |
|---|---|---|
| `Ok` | The handshake worked. | Everything is right. |
| `ServerUnavailable` | The server could not be reached. | The Server did not answer (it is retried when `OnServerUnavailable = Continue`). |
| `NotFound` | The application was not found. | The Server does not know the application (404); never retried. |
| `Rejected` | The server rejected the client (secret, name, key or mode). | Wrong secret, name, key or server mode; never retried. |

**Why it exists.** It is the result of the check the client makes at start.

### AccessLoadMode

`SystemX.Auth2.Contracts.Enums` · When the client loads access data (roles and permissions).

**Used by** `Auth2CookieOptions.AccessMode` ([client-setup.md](client-setup.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `Lazy` | Load on first use. | Default: load when first needed. |
| `OnSignIn` | Load when the user signs in. | Long sessions and fast first page: load once at sign-in into the store. |
| `RolesInCookie` | Put roles in the cookie. | Small role sets (a cookie is limited to 4 KB); roles become claims. |
| `None` | Nothing happens. | You never use the package's access data. |

**Why it exists.** It decides when the roles, permissions and groups of a cookie user are loaded. The access token in the cookie is not renewed, so a late read can fail.

```csharp
.UseCookieAuthentication(o => o.AccessMode = AccessLoadMode.OnSignIn)
```

### CurrentUserSource

`SystemX.Auth2.Contracts.Enums` · Where the client reads current user data from.

**Used by** `Auth2CurrentUserOptions.Source` ([client-current-user.md](client-current-user.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `TokenOnly` | Read only from the token. | A small API that only checks roles in the token. No permissions, no groups, no calls. |
| `TokenAndServer` | Read from the token and from the server. | Default: roles, permissions and groups from `/me`, cached. |

**Why it exists.** It trades completeness against calls to the Server.

```csharp
.EnableCurrentUser(o => o.Source = CurrentUserSource.TokenAndServer)
```

### PermissionSyncPolicy

`SystemX.Auth2.Contracts.Enums` · How permissions move between the client and the server.

**Used by** `Auth2PermissionOptions.SyncPolicy`. `RoleSyncPolicy`, `ScopeSyncPolicy` and `GroupSyncPolicy` have the same four values and do the same for roles, custom scopes and groups ([permission-sync.md](permission-sync.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `None` | Nothing happens. | Nothing moves. Default. |
| `Push` | Client sends its list to the server. | Your code is the source of truth. The normal choice. |
| `Pull` | Client reads the list from the server. | Admins define the list and your UI shows it. |
| `Both` | Push first, then pull. | Push first, then pull. |

**Why it exists.** It says which way the list moves. Push adds and updates on the Server and never deletes; pull fills `IAuth2Catalog`.

```csharp
.EnablePermissions(o => o.SyncPolicy = PermissionSyncPolicy.Push)
```

### RoleSyncPolicy

`SystemX.Auth2.Contracts.Enums` · How roles move between the client and the server.

**Used by** `Auth2RoleOptions.SyncPolicy`. Same values and meaning as `PermissionSyncPolicy`, for roles; the Server must allow it (`Clients.RoleSyncAllowed`).

| Value | Meaning | Choose it when |
|---|---|---|
| `None` | Nothing happens. | Nothing moves. Default. |
| `Push` | Client sends its list to the server. | Your code defines the roles. |
| `Pull` | Client reads the list from the server. | Admins define the roles. |
| `Both` | Push first, then pull. | Push first, then pull. |

**Why it exists.** See `PermissionSyncPolicy`.

```csharp
.EnableRoles(o => o.SyncPolicy = RoleSyncPolicy.Push)
```

### ScopeSyncPolicy

`SystemX.Auth2.Contracts.Enums` · How the custom scopes of an application move between the client and the server.

**Used by** `Auth2ScopeOptions.SyncPolicy`. Same values, for custom scopes such as `reports.read`; the Server must allow it (`Clients.ScopeSyncAllowed`). A pushed scope is registered and allowed for the application at once.

| Value | Meaning | Choose it when |
|---|---|---|
| `None` | Nothing happens. | Nothing moves. Default. |
| `Push` | Client sends its list to the server. | Your code defines the scopes. |
| `Pull` | Client reads the list from the server. | Admins define the scopes. |
| `Both` | Push first, then pull. | Push first, then pull. |

**Why it exists.** See `PermissionSyncPolicy`.

```csharp
.EnableScopes(o => o.SyncPolicy = ScopeSyncPolicy.Push)
```

### GroupSyncPolicy

`SystemX.Auth2.Contracts.Enums` · How groups move between the client and the server. Pushing groups is off on the server unless the application allows it (Clients.GroupSyncAllowed), because admins normally decide groups. Push only adds missing groups; it never changes or removes admin work.

**Used by** `Auth2GroupOptions.SyncPolicy`. Same values, for groups. Pushing groups needs `Clients.GroupSyncAllowed` on the Server (off by default) and only **adds missing** groups.

| Value | Meaning | Choose it when |
|---|---|---|
| `None` | Nothing happens. | Nothing moves. Default. |
| `Pull` | Client reads the list from the server. | Admins define the groups. |
| `Push` | Client sends its list to the server (adds missing groups only). | Your code names the groups it uses. |
| `Both` | Push first, then pull. | Push first, then pull. |

**Why it exists.** Admins normally own groups, because they decide who is in them.

```csharp
.EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Both)
```

### ScanningPolicy

`SystemX.Auth2.Contracts.Enums` · Where the client finds permissions and roles.

**Used by** the permission, role, scope and group options (`ScanningPolicy`).

| Value | Meaning | Choose it when |
|---|---|---|
| `Manual` | Only the lists written in options. | Only what you list and the sources you add. Default. |
| `AssemblyScan` | Find items from endpoints and attributes. | Only what the attributes and endpoint extensions say. |
| `Both` | Your lists and sources, plus what the scan finds in endpoints and attributes. | The usual choice: a list with descriptions plus the scan. |

**Why it exists.** It says where the entries come from. Scanning keeps the list next to the code that needs it; manual entries win over scanned ones (they carry names and descriptions).

```csharp
.EnablePermissions(o => { o.ScanningPolicy = ScanningPolicy.Both; o.AssembliesToScan = [typeof(Program).Assembly]; })
```

### DuplicatePolicy

`SystemX.Auth2.Client.Options` · What happens when two sources give the same key with different data.

**Used by** `CatalogOptions<T>.DuplicatePolicy` of every catalog.

| Value | Meaning | Choose it when |
|---|---|---|
| `Fail` | The application does not start. The message names both sources. The default. | Default: the application does not start and the message names both sources. |
| `FirstWins` | The first source wins. Later definitions of the same key are ignored. | Several sources overlap on purpose; the first definition stays. |

**Why it exists.** It decides what happens when two sources define the same key with different data.

```csharp
.EnablePermissions(o => o.DuplicatePolicy = DuplicatePolicy.FirstWins)
```

### KeyStyle

`SystemX.Auth2.Client.Options` · How an enum member name becomes a key.

**Used by** `AddFromEnum<T>(KeyStyle)`.

| Value | Meaning | Choose it when |
|---|---|---|
| `DotCase` | OrdersRead becomes orders.read. The default. | Default: `OrdersRead` becomes `orders.read`. |
| `AsIs` | The member name as it is: OrdersRead. | Roles (`Admin` stays `Admin`). Not allowed for permissions, which must be lowercase. |
| `SnakeCase` | OrdersRead becomes orders_read. | You prefer `orders_read`. |
| `KebabCase` | OrdersRead becomes orders-read. | You prefer `orders-read`. |

**Why it exists.** It turns an enum member name into a key, so a C# enum can be the list of permissions.

```csharp
o.AddFromEnum<ReportKeys>(KeyStyle.SnakeCase);   // ReportKeys.OrdersRead -> "orders_read"
```

### Auth2PermissionSource

`SystemX.Auth2.Client.Authorization` · Where a required permission must come from.

**Used by** the attributes `[Auth2OnlyRolePermission]` and `[Auth2OnlyUserPermission]` and the extensions `WithOnlyRolePermission()` / `WithOnlyUserPermission()` ([client-authorization.md](client-authorization.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `Any` | Anywhere (direct grant, role, group). The default. | Default: a direct grant, a role or a group is enough. |
| `RoleOnly` | From a role, or from the role of a group. A direct grant is not enough. | The right must come from a role (or a role of a group): good for rights you manage by role only. |
| `UserOnly` | From a direct grant to the user. A role is not enough. | The right must have been granted to the person directly. |

**Why it exists.** It says where a required permission must come from. The answer of `/me` carries `DirectPermissions` and `RolePermissions` for exactly this check.

```csharp
app.MapGet("/api/orders/role-only", () => Results.Ok()).WithPermission("orders.read").WithOnlyRolePermission();
```

## External identity

The settings of external providers ([external-identity.md](external-identity.md)).

### ExternalProviderType

`SystemX.Auth2.Contracts.Enums` · The kinds of external identity source. Passkeys are not a provider: they are a credential of the user.

**Used by** the `Type` of every external provider ([external-identity.md](external-identity.md)). Passkeys are not a provider.

| Value | Meaning | Choose it when |
|---|---|---|
| `Google` | Google (OpenID Connect). | Sign in with Google ([google.md](external-identity/google.md)). |
| `Microsoft` | Microsoft account or Entra ID (OpenID Connect). | Microsoft accounts or Entra ID ([microsoft.md](external-identity/microsoft.md)). |
| `GitHub` | GitHub (OAuth 2.0). | GitHub ([github.md](external-identity/github.md)). |
| `Apple` | Sign in with Apple. | Sign in with Apple ([apple.md](external-identity/apple.md)). |
| `Facebook` | Facebook Login (OAuth 2.0). | Facebook Login ([facebook.md](external-identity/facebook.md)). |
| `GenericOidc` | Any OpenID Connect provider with a discovery document. | Keycloak, Okta, Auth0 or any OpenID Connect provider ([generic-oidc.md](external-identity/generic-oidc.md)). |
| `Saml` | A SAML 2.0 identity provider. The server is the service provider. | A company identity provider that speaks SAML 2.0 ([saml.md](external-identity/saml.md)). |
| `Ldap` | An LDAP directory or Active Directory (user name and password are checked against it). | A directory ([ldap.md](external-identity/ldap.md)). |

**Why it exists.** It picks the engine and the extra fields: the five services come from OpenIddict.Client.WebIntegration, `GenericOidc` from discovery, `Saml` from the SAML service provider, `Ldap` from the directory client.

```json
{ "kind": "oidc", "name": "google", "type": "Google", "enabled": true, "clientId": "...", "clientSecret": "..." }
```

### GroupMappingMode

`SystemX.Auth2.Server.Domain.Settings` · Whether the groups of an external source become local roles and groups.

**Used by** `GroupMappingMode` of an external provider ([group-mapping.md](external-identity/group-mapping.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `None` | Groups of the source are ignored. | Default: groups of the source are ignored. |
| `Sync` | At every login the mappings are applied (added, and removed only when a mapping mentions them). | At every sign-in the mappings are applied: matching groups add roles, groups or permissions, and are removed only when a mapping mentions them. |

**Why it exists.** It switches the group-to-role rules on for that provider.

```json
{ "groupMappingMode": "Sync", "groupsAttribute": "groups" }
```

### ProfileSyncMode

`SystemX.Auth2.Server.Domain.Settings` · When the profile of a user is taken from the source.

**Used by** `ProfileSyncMode` of an external provider.

| Value | Meaning | Choose it when |
|---|---|---|
| `ImportOnce` | Names and phone are copied when the user is created. Later logins do not change them. | Default: copy the names and phone when the user is created; later sign-ins do not change them. |
| `UpdateOnLogin` | Names and phone are updated at every login. | The provider is the source of truth: refresh at every sign-in. |

**Why it exists.** It decides whether the directory or provider stays the owner of the person's names and phone.

```json
{ "profileSyncMode": "UpdateOnLogin" }
```

### MappingSourceType

`SystemX.Auth2.Server.Domain.Entities` · What the mapping reads from the external source.

**Stored in** `external_group_mappings.source_type`.

| Value | Meaning | Choose it when |
|---|---|---|
| `Group` | A group name of the source. | A group name from a provider or a directory. |
| `Claim` | A claim value (OIDC). | A claim value of an OpenID Connect token. |
| `Attribute` | An attribute value (SAML, LDAP). | An attribute value of a SAML assertion or an LDAP entry. |

**Why it exists.** It says what a mapping reads from the external answer.

### MappingMatchType

`SystemX.Auth2.Server.Domain.Entities` · How the value is compared. No regex: only exact and prefix, case-insensitive.

**Stored in** `external_group_mappings.match_type`.

| Value | Meaning | Choose it when |
|---|---|---|
| `Exact` | Same text (ignoring case). | The whole value must be the same. |
| `Prefix` | Starts with the text (ignoring case). | A family of values: `eng-` matches `eng-backend` and `eng-frontend`. |

**Why it exists.** There is no regex on purpose (a regular expression from an admin could hang the Server). Both comparisons ignore case.

### MappingTargetType

`SystemX.Auth2.Server.Domain.Entities` · What the mapping gives.

**Stored in** `external_group_mappings.target_type`.

| Value | Meaning | Choose it when |
|---|---|---|
| `Role` | A role. | Give a role. |
| `Group` | A group. | Add the person to a group. |
| `Permission` | A direct permission. | Give a direct permission. |

**Why it exists.** It says what a matching mapping gives. The target must belong to the same project; `GlobalAdmin` and system roles are refused.

### SamlBinding

`SystemX.Auth2.Server.Domain.Settings` · SAML request binding.

**Used by** `AuthnRequestBinding` of a SAML provider.

| Value | Meaning | Choose it when |
|---|---|---|
| `Redirect` | HTTP-Redirect. | Default: a redirect with the request in the address. |
| `Post` | HTTP-POST. | The identity provider needs a form post; the Server then gives your UI `postAction` and `postFields` to post. |

**Why it exists.** It is how the sign-in request travels to the identity provider.

```json
{ "kind": "saml", "authnRequestBinding": "Post" }
```

### SamlNameIdFormat

`SystemX.Auth2.Server.Domain.Settings` · SAML NameID format.

**Used by** `NameIdFormat` of a SAML provider (in the name-id policy of the request).

| Value | Meaning | Choose it when |
|---|---|---|
| `Unspecified` | Unspecified. | Let the identity provider choose. |
| `EmailAddress` | Email address. | The email is the id: only if emails never change. |
| `Persistent` | Persistent identifier (best for linking). | Default and best: a stable id. |
| `Transient` | Transient (changes every login: cannot be used to link users). | Changes at every sign-in: **cannot link users**, avoid it. |

**Why it exists.** The NameID is how a person is **linked** to the account. A value that changes at every sign-in creates a new account each time.

```json
{ "nameIdFormat": "Persistent" }
```

### LdapSecurity

`SystemX.Auth2.Server.Domain.Settings` · Transport security of an LDAP connection. Plain LDAP is not offered.

**Used by** `Security` of an LDAP provider.

| Value | Meaning | Choose it when |
|---|---|---|
| `Ldaps` | TLS from the start (port 636). | Default: TLS from the start (port 636). |
| `StartTls` | A plain connection that is upgraded with StartTLS (port 389). The upgrade is required. | A directory that only offers port 389 with an upgrade; the upgrade is required. |

**Why it exists.** Plain LDAP is not offered: a password must never cross the network in clear text. The certificate chain and name are always checked (unless an admin turns that off).

```json
{ "kind": "ldap", "host": "dc.corp.com", "port": 636, "security": "Ldaps" }
```

### LdapMode

`SystemX.Auth2.Server.Domain.Settings` · How LDAP and local passwords work together for password login in one application.

**Used by** `Mode` of an LDAP provider.

| Value | Meaning | Choose it when |
|---|---|---|
| `LdapOnly` | Only the directory. | Default: the directory is the only authority. |
| `LocalThenLdap` | The local password first, then the directory. | Try the local password first, then the directory (people who already have a local password keep it). |
| `LdapThenLocal` | The directory first, then the local password. | Try the directory first, then the local password (a local account stays as a fallback). |

**Why it exists.** It sets how a directory password and a local password work together for the same login.

```json
{ "mode": "LdapThenLocal" }
```

### ProviderFieldKind

`SystemX.Auth2.Server.Infrastructure.External` · The kind of a settings field. A future admin UI draws a form from these.

**Returned by** `GET /api/auth2/admin/external-provider-types` for every field of a provider type.

| Value | Meaning | Choose it when |
|---|---|---|
| `Text` | One line of text. | A one-line text box. |
| `Secret` | A secret. Written, never read. | A write-only box (a secret is written, never read back). |
| `Select` | One value of a list. | A drop-down of fixed values. |
| `List` | A list of texts. | A list of texts. |
| `Bool` | On or off. | A switch. |
| `Certificate` | A certificate (PEM or base64). | A text area for a PEM or base64 certificate. |
| `Map` | Key and value pairs. | Key and value rows (claim and attribute mappings). |

**Why it exists.** It lets an admin UI draw a form from the data without knowing the types.

## Keys and mail

Server-side values for the key ring and for sending email.

### KeyPurpose

`SystemX.Auth2.Server.Domain.Entities` · What a key is for.

**Stored in** `key_ring.purpose` ([key-ring.md](key-ring.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `Signing` | Signs tokens. | Signs access and identity tokens (the public part is in `/.well-known/jwks`). |
| `Encryption` | Encrypts refresh tokens and authorization codes. | Encrypts authorization codes and refresh tokens. |

**Why it exists.** Signing and encryption use separate keys so that a leak of one does not break the other, and each rotates with its own pair.

### SmtpSecurity

`SystemX.Auth2.Server.Domain.Settings` · How an SMTP connection is secured. Written as text in JSON.

**Used by** `Messaging.Email.Security` (project and application) and `Auth2:Messaging:Email:Security` (server) ([email-and-sms.md](email-and-sms.md)).

| Value | Meaning | Choose it when |
|---|---|---|
| `StartTls` | Start in clear text and upgrade with STARTTLS when the server offers it (port 587). | Default, port 587: the connection is upgraded to TLS. |
| `SslOnConnect` | TLS from the first byte (port 465). | Port 465: TLS from the first byte. |
| `None` | No encryption. Only for a trusted local relay. | Only a trusted local relay: the password travels in clear text. |

**Why it exists.** It decides how the connection to the mail server is protected. The password is sent over it.

```json
"messaging": { "email": { "provider": "smtp", "host": "smtp.example.com", "port": 587, "security": "StartTls" } }
```

## Related pages

- [server-settings.md](server-settings.md)
- [client-builder.md](client-builder.md)
- [contracts-reference.md](contracts-reference.md)
- [database-tables.md](database-tables.md)
- [Documentation index](index.md)
- [Scope, permission, role, group: which is which](access-concepts.md)
- [Minimal API methods and attributes: when to use which](client-endpoint-metadata.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Concepts](concepts.md)
