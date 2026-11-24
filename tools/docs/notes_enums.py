# Hand-written guidance for docs/enums-reference.md. values: the "choose it when" text of each value (every value of the enum must have one).
N = {}
def n(name, where, why, values, ex=None): N[name] = dict(where=where, why=why, values=values, ex=ex)

# ---------------------------------------------------------------- sign-in and accounts
n('LoginMethod', '**Used by** `Methods.AllowedLoginMethods` and `Methods.AllowedRegisterMethods` of the project settings, by `ExternalSignInRequest.Method`, and by the audit data of a sign-in.',
  'One list decides which entry points work. A method that is not listed is refused with `login_method_not_allowed` before the password is checked, and a provider (SAML, LDAP, passkeys) does nothing until its method is listed.',
  {'UserNamePassword': 'People sign in with a user name. Default.', 'EmailPassword': 'People sign in with their email. Default.', 'PhonePassword': 'Phone is the identifier and there is a password.',
   'PhoneOtp': 'Passwordless sign-in with an SMS code. The code **is** the sign-in (no authenticator step).', 'EmailOtp': 'Passwordless sign-in with an email code.',
   'External': 'You added Google, Microsoft, GitHub, Apple, Facebook, OpenID Connect or SAML providers.', 'Ldap': 'Users sign in with a directory (LDAP / Active Directory) password.', 'Passkey': 'You turned passkeys on (`Passkeys.AllowPasskeys`).'},
  ('json', '"methods": { "allowedLoginMethods": ["EmailPassword", "External", "Passkey"], "allowedRegisterMethods": ["EmailPassword"] }'))
n('TwoFactorMode', '**Used by** `TwoFactor.Mode` of the project settings ([two-factor.md](two-factor.md)).',
  'It decides whether the authenticator-app step exists for a project. `Required` blocks people who have not enrolled, so enrol first.',
  {'Off': 'You do not want a second step at all (even people who set it up are not asked).', 'Optional': 'People may turn it on in their profile. Default.', 'Required': 'Admin or finance projects, after everyone has enrolled.'},
  ('json', '"twoFactor": { "mode": "Required", "allowTotp": true }'))
n('JoinMode', '**Used by** `Registration.JoinMode` of the project settings and by the join flow ([authentication-flows.md](authentication-flows.md)).',
  'A person who already has an account in another project can join this one only if the project allows it, and then only with proof of ownership (a code to a confirmed email or phone).',
  {'AutoJoin': 'Anyone with proof of ownership may join. Default.', 'InviteOnly': 'Only people an admin adds may join.', 'Closed': 'Nobody may join; the project is frozen for new members.'},
  ('json', '"registration": { "joinMode": "InviteOnly" }'))
n('ConsentMode', '**Used by** `Sessions.ConsentMode` of the project and `ConsentMode` of an application, together with `Sessions.RequireConsent`.',
  'It maps to the consent type of the OpenIddict application. With `RequireConsent` off no consent page is shown whatever the mode says; with it on, `Implicit` is treated as `Explicit`.',
  {'Implicit': 'Your own, trusted applications. No consent page.', 'Explicit': 'Third-party applications: ask once, remember the answer.', 'Systematic': 'Very sensitive scopes: ask at every authorization.'},
  ('json', '"sessions": { "requireConsent": true, "consentMode": "Explicit" }'))
n('CredentialScope', '**Used by** `Auth2:CredentialScope` (server) and `CredentialScope` of a project (when `AllowCredentialScopeOverride` is on). See [credential-scope.md](credential-scope.md) and [identity-stores.md](identity-stores.md).',
  'It decides where the password, the two-factor key and the lockout state are kept. Choose it once, before users register: changing it later makes existing passwords unreadable.',
  {'PerProject': 'Several products on one server and the same person may have a different password in each. Default.', 'Shared': 'One company, one password for all applications. Single-project mode always uses this.'},
  ('json', '"Auth2": { "CredentialScope": "Shared" }'))
n('ServerMode', '**Used by** `Auth2:Mode` and the call `ServerWithSingleProject()` ([server-setup.md](server-setup.md)).',
  'In `MultiProject` every request is about one project (found from the client id) and people have memberships; in `SingleProject` there is one fixed project and no membership rows.',
  {'SingleProject': 'One product, one set of users.', 'MultiProject': 'Several tenants. Default.'},
  ('csharp', 'builder.Services.AddSystemXAuth2Server(builder.Configuration);\nbuilder.Services.ServerWithSingleProject();   // sets Mode = SingleProject'))
n('UserProjectStatus', '**Stored in** `user_projects.status` and read wherever membership is checked (`UserLookup.IsMemberAsync`).',
  'A person can be blocked in one project without being deactivated everywhere (`users.is_active`). **Only `Active` memberships count**: a `Suspended` person gets the generic `invalid_credentials` in that project and no access data. No admin route sets `Suspended` today; it is set in the database.',
  {'Active': 'Normal. Every membership the Server creates.', 'Suspended': 'Block a person in this project only (set it in the database).'},
  ('sql', "update user_projects set status = 'Suspended' where user_id = '...' and project_id = '...';"))
n('NotificationChannel', '**Used by** `IUserNotifier.SendOtpAsync` and the join flow.',
  'It says where a one-time code goes. Proof of ownership uses `Email` when the person has a **confirmed** email, otherwise `Sms` when the phone is confirmed.',
  {'Email': 'The code goes to the email address.', 'Sms': 'The code goes to the phone number.'})
n('OtpVerifyResult', '**Returned by** `IOtpService.VerifyAsync`.',
  'It tells the caller why a code failed. The one-time-code sign-in and the join flow turn every value except `Ok` into the one answer `invalid_otp`, so a stranger learns nothing about whether a code existed.',
  {'Ok': 'The code is right and is used up now.', 'Invalid': 'A wrong code (counts as an attempt).', 'Expired': 'The code is too old (`TwoFactor.OtpLifetimeSeconds`).', 'TooManyAttempts': 'The code is closed (`TwoFactor.OtpMaxAttempts`).', 'NotFound': 'No open code for this person and purpose.'})

# ---------------------------------------------------------------- settings and administration
n('SettingEditor', '**Used by** the attribute `[SettingEditability(...)]` on every setting field, checked when a project or application setting is saved.',
  'A project admin must not change security-relevant settings (password rules, lockout, token lifetimes). A test fails when a setting has no attribute. A project admin who changes a `GlobalOnly` field gets `403` with the field names.',
  {'GlobalOnly': 'The setting is security- or cost-relevant: only the developer (global admin) changes it.', 'ProjectAdminEditable': 'A project admin may change it (names, redirects, cultures, registration switches).'},
  ('csharp', '[SettingEditability(SettingEditor.GlobalOnly)] public int MinLength { get; set; } = 10;'))
n('PermissionSource', '**Stored in** `permissions.source`.',
  'It tells admins whether a permission came from a person or from code, so a sync never overwrites what an admin typed.',
  {'Manual': 'An admin made the permission (admin API or pages).', 'Sync': 'A client pushed it ([permission-sync.md](permission-sync.md)).'})
n('ClientType', '**Used by** `Clients.AllowedClientTypes` of the project settings.',
  'It lists the kinds of applications a project is meant to have. **No code reads it today**: it is stored and shown in the admin settings page only.',
  {'Api': 'A web API.', 'Mvc': 'An MVC application.', 'RazorPages': 'A Razor Pages application.', 'Blazor': 'A Blazor application.', 'Machine': 'A background program or service.'})
n('UiMode', '**Used by** `Clients.DefaultUiMode` (project) and `UiMode` (application). **The Server does not enforce it**: the Client package decides with `UseHostedUi()` / `UseSelfUi()`.',
  'It records how an application is meant to show its sign-in screens. What is enforced is `AllowSelfUiLogin` on the application (the password grant).',
  {'None': 'An API: only 401 / 403 answers.', 'Hosted': 'Recommended: the Server shows the sign-in pages and your application never sees a password.', 'Self': 'Your application shows its own form (and `AllowSelfUiLogin` must be on).'},
  ('csharp', 'builder.Services.AddAuth2Client(section).UseHostedUi();   // UiMode.Hosted'))

# ---------------------------------------------------------------- client package
n('StartupFailureBehavior', '**Used by** `Auth2ClientOptions.OnServerUnavailable` and `OnProjectNotFound` ([client-builder.md](client-builder.md)).',
  'It chooses between "fail fast" and "stay up" when the start-up handshake fails.',
  {'Throw': 'The application is useless without the Server (a worker), or the failure is a configuration error you want to see at once.', 'Continue': 'A website that can serve some requests without the Server. The handshake is retried in the background.'},
  ('csharp', 'o.OnServerUnavailable = StartupFailureBehavior.Continue;\no.OnProjectNotFound = StartupFailureBehavior.Throw;'))
n('HandshakeStatus', '**Held by** `Auth2ClientState` and carried by `Auth2StartupException`; the `auth2` health check reads it.',
  'It is the result of the check the client makes at start.',
  {'Ok': 'Everything is right.', 'ServerUnavailable': 'The Server did not answer (it is retried when `OnServerUnavailable = Continue`).', 'NotFound': 'The Server does not know the application (404); never retried.', 'Rejected': 'Wrong secret, name, key or server mode; never retried.'})
n('AccessLoadMode', '**Used by** `Auth2CookieOptions.AccessMode` ([client-setup.md](client-setup.md)).',
  'It decides when the roles, permissions and groups of a cookie user are loaded. The access token in the cookie is not renewed, so a late read can fail.',
  {'Lazy': 'Default: load when first needed.', 'OnSignIn': 'Long sessions and fast first page: load once at sign-in into the store.', 'RolesInCookie': 'Small role sets (a cookie is limited to 4 KB); roles become claims.', 'None': 'You never use the package\'s access data.'},
  ('csharp', '.UseCookieAuthentication(o => o.AccessMode = AccessLoadMode.OnSignIn)'))
n('CurrentUserSource', '**Used by** `Auth2CurrentUserOptions.Source` ([client-current-user.md](client-current-user.md)).',
  'It trades completeness against calls to the Server.',
  {'TokenOnly': 'A small API that only checks roles in the token. No permissions, no groups, no calls.', 'TokenAndServer': 'Default: roles, permissions and groups from `/me`, cached.'},
  ('csharp', '.EnableCurrentUser(o => o.Source = CurrentUserSource.TokenAndServer)'))
n('PermissionSyncPolicy', '**Used by** `Auth2PermissionOptions.SyncPolicy`. `RoleSyncPolicy`, `ScopeSyncPolicy` and `GroupSyncPolicy` have the same four values and do the same for roles, custom scopes and groups ([permission-sync.md](permission-sync.md)).',
  'It says which way the list moves. Push adds and updates on the Server and never deletes; pull fills `IAuth2Catalog`.',
  {'None': 'Nothing moves. Default.', 'Push': 'Your code is the source of truth. The normal choice.', 'Pull': 'Admins define the list and your UI shows it.', 'Both': 'Push first, then pull.'},
  ('csharp', '.EnablePermissions(o => o.SyncPolicy = PermissionSyncPolicy.Push)'))
n('RoleSyncPolicy', '**Used by** `Auth2RoleOptions.SyncPolicy`. Same values and meaning as `PermissionSyncPolicy`, for roles; the Server must allow it (`Clients.RoleSyncAllowed`).',
  'See `PermissionSyncPolicy`.',
  {'None': 'Nothing moves. Default.', 'Push': 'Your code defines the roles.', 'Pull': 'Admins define the roles.', 'Both': 'Push first, then pull.'},
  ('csharp', '.EnableRoles(o => o.SyncPolicy = RoleSyncPolicy.Push)'))
n('ScopeSyncPolicy', '**Used by** `Auth2ScopeOptions.SyncPolicy`. Same values, for custom scopes such as `reports.read`; the Server must allow it (`Clients.ScopeSyncAllowed`). A pushed scope is registered and allowed for the application at once.',
  'See `PermissionSyncPolicy`.',
  {'None': 'Nothing moves. Default.', 'Push': 'Your code defines the scopes.', 'Pull': 'Admins define the scopes.', 'Both': 'Push first, then pull.'},
  ('csharp', '.EnableScopes(o => o.SyncPolicy = ScopeSyncPolicy.Push)'))
n('GroupSyncPolicy', '**Used by** `Auth2GroupOptions.SyncPolicy`. Same values, for groups. Pushing groups needs `Clients.GroupSyncAllowed` on the Server (off by default) and only **adds missing** groups.',
  'Admins normally own groups, because they decide who is in them.',
  {'None': 'Nothing moves. Default.', 'Push': 'Your code names the groups it uses.', 'Pull': 'Admins define the groups.', 'Both': 'Push first, then pull.'},
  ('csharp', '.EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Both)'))
n('ScanningPolicy', '**Used by** the permission, role, scope and group options (`ScanningPolicy`).',
  'It says where the entries come from. Scanning keeps the list next to the code that needs it; manual entries win over scanned ones (they carry names and descriptions).',
  {'Manual': 'Only what you list and the sources you add. Default.', 'AssemblyScan': 'Only what the attributes and endpoint extensions say.', 'Both': 'The usual choice: a list with descriptions plus the scan.'},
  ('csharp', '.EnablePermissions(o => { o.ScanningPolicy = ScanningPolicy.Both; o.AssembliesToScan = [typeof(Program).Assembly]; })'))
n('DuplicatePolicy', '**Used by** `CatalogOptions<T>.DuplicatePolicy` of every catalog.',
  'It decides what happens when two sources define the same key with different data.',
  {'Fail': 'Default: the application does not start and the message names both sources.', 'FirstWins': 'Several sources overlap on purpose; the first definition stays.'},
  ('csharp', '.EnablePermissions(o => o.DuplicatePolicy = DuplicatePolicy.FirstWins)'))
n('KeyStyle', '**Used by** `AddFromEnum<T>(KeyStyle)`.',
  'It turns an enum member name into a key, so a C# enum can be the list of permissions.',
  {'DotCase': 'Default: `OrdersRead` becomes `orders.read`.', 'AsIs': 'Roles (`Admin` stays `Admin`). Not allowed for permissions, which must be lowercase.', 'SnakeCase': 'You prefer `orders_read`.', 'KebabCase': 'You prefer `orders-read`.'},
  ('csharp', 'o.AddFromEnum<ReportKeys>(KeyStyle.SnakeCase);   // ReportKeys.OrdersRead -> "orders_read"'))
n('Auth2PermissionSource', '**Used by** the attributes `[Auth2OnlyRolePermission]` and `[Auth2OnlyUserPermission]` and the extensions `WithOnlyRolePermission()` / `WithOnlyUserPermission()` ([client-authorization.md](client-authorization.md)).',
  'It says where a required permission must come from. The answer of `/me` carries `DirectPermissions` and `RolePermissions` for exactly this check.',
  {'Any': 'Default: a direct grant, a role or a group is enough.', 'RoleOnly': 'The right must come from a role (or a role of a group): good for rights you manage by role only.', 'UserOnly': 'The right must have been granted to the person directly.'},
  ('csharp', 'app.MapGet("/api/orders/role-only", () => Results.Ok()).WithPermission("orders.read").WithOnlyRolePermission();'))

# ---------------------------------------------------------------- external identity
n('ExternalProviderType', '**Used by** the `Type` of every external provider ([external-identity.md](external-identity.md)). Passkeys are not a provider.',
  'It picks the engine and the extra fields: the five services come from OpenIddict.Client.WebIntegration, `GenericOidc` from discovery, `Saml` from the SAML service provider, `Ldap` from the directory client.',
  {'Google': 'Sign in with Google ([google.md](external-identity/google.md)).', 'Microsoft': 'Microsoft accounts or Entra ID ([microsoft.md](external-identity/microsoft.md)).', 'GitHub': 'GitHub ([github.md](external-identity/github.md)).', 'Apple': 'Sign in with Apple ([apple.md](external-identity/apple.md)).', 'Facebook': 'Facebook Login ([facebook.md](external-identity/facebook.md)).',
   'GenericOidc': 'Keycloak, Okta, Auth0 or any OpenID Connect provider ([generic-oidc.md](external-identity/generic-oidc.md)).', 'Saml': 'A company identity provider that speaks SAML 2.0 ([saml.md](external-identity/saml.md)).', 'Ldap': 'A directory ([ldap.md](external-identity/ldap.md)).'},
  ('json', '{ "kind": "oidc", "name": "google", "type": "Google", "enabled": true, "clientId": "...", "clientSecret": "..." }'))
n('GroupMappingMode', '**Used by** `GroupMappingMode` of an external provider ([group-mapping.md](external-identity/group-mapping.md)).',
  'It switches the group-to-role rules on for that provider.',
  {'None': 'Default: groups of the source are ignored.', 'Sync': 'At every sign-in the mappings are applied: matching groups add roles, groups or permissions, and are removed only when a mapping mentions them.'},
  ('json', '{ "groupMappingMode": "Sync", "groupsAttribute": "groups" }'))
n('ProfileSyncMode', '**Used by** `ProfileSyncMode` of an external provider.',
  'It decides whether the directory or provider stays the owner of the person\'s names and phone.',
  {'ImportOnce': 'Default: copy the names and phone when the user is created; later sign-ins do not change them.', 'UpdateOnLogin': 'The provider is the source of truth: refresh at every sign-in.'},
  ('json', '{ "profileSyncMode": "UpdateOnLogin" }'))
n('MappingSourceType', '**Stored in** `external_group_mappings.source_type`.',
  'It says what a mapping reads from the external answer.',
  {'Group': 'A group name from a provider or a directory.', 'Claim': 'A claim value of an OpenID Connect token.', 'Attribute': 'An attribute value of a SAML assertion or an LDAP entry.'})
n('MappingMatchType', '**Stored in** `external_group_mappings.match_type`.',
  'There is no regex on purpose (a regular expression from an admin could hang the Server). Both comparisons ignore case.',
  {'Exact': 'The whole value must be the same.', 'Prefix': 'A family of values: `eng-` matches `eng-backend` and `eng-frontend`.'})
n('MappingTargetType', '**Stored in** `external_group_mappings.target_type`.',
  'It says what a matching mapping gives. The target must belong to the same project; `GlobalAdmin` and system roles are refused.',
  {'Role': 'Give a role.', 'Group': 'Add the person to a group.', 'Permission': 'Give a direct permission.'})
n('SamlBinding', '**Used by** `AuthnRequestBinding` of a SAML provider.',
  'It is how the sign-in request travels to the identity provider.',
  {'Redirect': 'Default: a redirect with the request in the address.', 'Post': 'The identity provider needs a form post; the Server then gives your UI `postAction` and `postFields` to post.'},
  ('json', '{ "kind": "saml", "authnRequestBinding": "Post" }'))
n('SamlNameIdFormat', '**Used by** `NameIdFormat` of a SAML provider (in the name-id policy of the request).',
  'The NameID is how a person is **linked** to the account. A value that changes at every sign-in creates a new account each time.',
  {'Unspecified': 'Let the identity provider choose.', 'EmailAddress': 'The email is the id: only if emails never change.', 'Persistent': 'Default and best: a stable id.', 'Transient': 'Changes at every sign-in: **cannot link users**, avoid it.'},
  ('json', '{ "nameIdFormat": "Persistent" }'))
n('LdapSecurity', '**Used by** `Security` of an LDAP provider.',
  'Plain LDAP is not offered: a password must never cross the network in clear text. The certificate chain and name are always checked (unless an admin turns that off).',
  {'Ldaps': 'Default: TLS from the start (port 636).', 'StartTls': 'A directory that only offers port 389 with an upgrade; the upgrade is required.'},
  ('json', '{ "kind": "ldap", "host": "dc.corp.com", "port": 636, "security": "Ldaps" }'))
n('LdapMode', '**Used by** `Mode` of an LDAP provider.',
  'It sets how a directory password and a local password work together for the same login.',
  {'LdapOnly': 'Default: the directory is the only authority.', 'LocalThenLdap': 'Try the local password first, then the directory (people who already have a local password keep it).', 'LdapThenLocal': 'Try the directory first, then the local password (a local account stays as a fallback).'},
  ('json', '{ "mode": "LdapThenLocal" }'))
n('ProviderFieldKind', '**Returned by** `GET /api/auth2/admin/external-provider-types` for every field of a provider type.',
  'It lets an admin UI draw a form from the data without knowing the types.',
  {'Text': 'A one-line text box.', 'Secret': 'A write-only box (a secret is written, never read back).', 'Select': 'A drop-down of fixed values.', 'List': 'A list of texts.', 'Bool': 'A switch.', 'Certificate': 'A text area for a PEM or base64 certificate.', 'Map': 'Key and value rows (claim and attribute mappings).'})

# ---------------------------------------------------------------- keys and mail
n('KeyPurpose', '**Stored in** `key_ring.purpose` ([key-ring.md](key-ring.md)).',
  'Signing and encryption use separate keys so that a leak of one does not break the other, and each rotates with its own pair.',
  {'Signing': 'Signs access and identity tokens (the public part is in `/.well-known/jwks`).', 'Encryption': 'Encrypts authorization codes and refresh tokens.'})
n('SmtpSecurity', '**Used by** `Messaging.Email.Security` (project and application) and `Auth2:Messaging:Email:Security` (server) ([email-and-sms.md](email-and-sms.md)).',
  'It decides how the connection to the mail server is protected. The password is sent over it.',
  {'StartTls': 'Default, port 587: the connection is upgraded to TLS.', 'SslOnConnect': 'Port 465: TLS from the first byte.', 'None': 'Only a trusted local relay: the password travels in clear text.'},
  ('json', '"messaging": { "email": { "provider": "smtp", "host": "smtp.example.com", "port": 587, "security": "StartTls" } }'))

GROUPS = [
 ('Sign-in and accounts', 'The values you choose in the project and server settings that decide who can sign in and how.',
  ['LoginMethod', 'TwoFactorMode', 'JoinMode', 'ConsentMode', 'CredentialScope', 'ServerMode', 'UserProjectStatus', 'NotificationChannel', 'OtpVerifyResult']),
 ('Settings and administration', 'Values that describe settings themselves and what an application is meant to be.',
  ['SettingEditor', 'PermissionSource', 'ClientType', 'UiMode']),
 ('The Client package', 'The options of `AddAuth2Client` and the `Enable...` / `Use...` methods ([client-builder.md](client-builder.md)).',
  ['StartupFailureBehavior', 'HandshakeStatus', 'AccessLoadMode', 'CurrentUserSource', 'PermissionSyncPolicy', 'RoleSyncPolicy', 'ScopeSyncPolicy', 'GroupSyncPolicy', 'ScanningPolicy', 'DuplicatePolicy', 'KeyStyle', 'Auth2PermissionSource']),
 ('External identity', 'The settings of external providers ([external-identity.md](external-identity.md)).',
  ['ExternalProviderType', 'GroupMappingMode', 'ProfileSyncMode', 'MappingSourceType', 'MappingMatchType', 'MappingTargetType', 'SamlBinding', 'SamlNameIdFormat', 'LdapSecurity', 'LdapMode', 'ProviderFieldKind']),
 ('Keys and mail', 'Server-side values for the key ring and for sending email.', ['KeyPurpose', 'SmtpSecurity']),
]
