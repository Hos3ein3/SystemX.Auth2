# Admin JSON API

## What this is

The JSON API that the admin pages (and your scripts) use to manage the Server. Base route: `/api/auth2/admin`.
The pages of the admin UI do not hold logic: everything they do is available here.

## When to use it

- You build the admin screens.
- You want to script project, application, user, role, group or provider management.

## Who may call it

| Caller | How the Server knows | May do |
|---|---|---|
| **GlobalAdmin** | has the role `GlobalAdmin` and the token comes from the `_global` project (or the only project in single mode) | everything, in every project |
| **ProjectAdmin** | has the role `ProjectAdmin` in the project of the token | manage **only** that project, with all its applications and users. There is no application admin in v1 |
| Anyone else | | `403 forbidden` |

Every call needs the token of a **signed-in user** (not a machine token) with the scope `auth2.admin`. Routes that only a global admin may call also need the scope `auth2.admin.global`.
The scopes are not in the default `AllowedScopes`. Add them to the client of the `_global` project (and to the clients of projects whose admins use the API). Keep `auth2.admin*` away from normal clients.

The roles are checked **on every call**, so removing a role takes effect at once, even for a token that was issued earlier. Deactivated, locked or removed users get `401`.

### Choosing the project

The project is in the **route**: project data lives under `/projects/{projectId}/...`, applications under `/projects/{projectId}/applications/{applicationId}/...`.
A **global admin** may use any project id. A **project admin** may use only the project of the token: another project id gives `404` (it looks like it does not exist). There are no cross-project reads or writes for a project admin.
In single-project mode the only project id is the one of the single project.

## Rules for every endpoint

- JSON in, JSON out. Errors are `ApiError` (`code`, `message`, `details`).
- Lists are **paged**: `?page=1&pageSize=50&q=text&sort=name&desc=true`. `pageSize` is at most **200**. The answer is `{ items, page, pageSize, totalCount }`.
- **Writes** create an audit event with the admin as actor, the IP address and the user agent. Secrets are never stored in audit events.
- **Edits are checked with `If-Match`** where a row version exists. Users use the `ETag` of `GET /users/{id}`. Settings use `"v<number>"`. A different value gives `409 conflict`. Without the header the edit is accepted.
- An admin cannot lock, remove or take the admin role from **themselves**.

## Endpoints

### Projects

| Route | Who | Does |
|---|---|---|
| `GET /projects` | project admin (sees only the own), global admin (all) | Paged list |
| `GET /projects/{projectId}` | project (own) / global | One project |
| `POST /projects` | global | Create a project: `{ projectKey, name, description }`. The key never changes. Add applications next |
| `PUT /projects/{projectId}` | project admin (rename only) / global | Name, description, `isEnabled` (+ `reason`). **Only a global admin may disable or enable.** Disabling revokes all tokens, authorizations and sessions of the project in one transaction and writes an audit event with the admin and the reason. `_global` cannot be disabled |
| `DELETE /projects/{projectId}` | global | Delete with all its data and its applications. Not `_global` |
| `GET /projects/{projectId}/settings` | project (own) / global | Project settings JSON, with `ETag: "v3"` |
| `PUT /projects/{projectId}/settings` | project (own) / global | Replace the settings. A project admin may change only `ProjectAdminEditable` settings; others give `403` and the list of paths |

### Applications (clients of a project)

| Route | Who | Does |
|---|---|---|
| `GET /projects/{projectId}/applications` | project (own) / global | The applications of the project |
| `GET /projects/{projectId}/applications/{applicationId}` | project (own) / global | One application. An application of another project is `404` |
| `POST /projects/{projectId}/applications` | global | `{ name, clientId?, redirectUris?, postLogoutRedirectUris?, allowedScopes? }`. The client id is `{projectkey}.{name}` unless given (it must be unique on the server). The answer holds `clientSecret` (**shown once**) and `setup`, a copy-paste client configuration |
| `PUT /projects/{projectId}/applications/{applicationId}` | global | Name, `isEnabled` (+ `reason`), redirect URIs, scopes. Disabling revokes only the tokens of that client |
| `DELETE /projects/{projectId}/applications/{applicationId}` | global | Delete with its tokens and authorizations. The `_global` application cannot be deleted |
| `GET .../{applicationId}/setup` | project (own) / global | The client setup (server URL, `ProjectKey`, `ApplicationName`, client id and a C# snippet). The secret is **not** part of it |
| `POST .../{applicationId}/secret/rotate` | global; project admin only if `Clients.AllowProjectAdminRotateSecret` | New client secret, **shown once** (also in `setup.clientSecret`) |
| `GET /projects/{projectId}/messaging`, `PUT` | global | How the project sends email and SMS (SMTP, Google). The password is never returned (`passwordIsSet`). See [email-and-sms.md](email-and-sms.md) |
| `GET`, `PUT`, `DELETE .../{applicationId}/messaging` | global | The application's own email / SMS provider. `DELETE` goes back to the project's |
| `GET .../{applicationId}/settings`, `PUT` | project (own) read / global write | Application settings JSON (UI mode, cookie lifetime, token lifetimes, CORS origins, Self UI, shown providers), with `ETag` |
| `GET`, `PUT .../{applicationId}/redirect-uris` | read: project (own) / write: global | `{ redirectUris, postLogoutRedirectUris }`. Exact match, no wildcards. These are fields of the OpenIddict application |

Applications are created, changed and deleted only through the OpenIddict application manager, never with raw SQL.
In single-project mode `POST` and `DELETE` on projects give `403`, and `projectKey` is `null`.

### Users (of the project in the route)

The routes of users, roles, permissions, groups, audit and external identity are relative to `/projects/{projectId}`; for example `GET /projects/{projectId}/users`.


| Route | Does |
|---|---|
| `GET /users` | Paged. `q` searches user name, email and phone. `sort`: `username`, `email`, `created`. `lockedOut=true/false` |
| `GET /users/{id}` | One user, with `ETag` |
| `POST /users` | Create. If the person **already exists**, only the membership is added and **no password is set** (the person uses password reset or the join flow). Answer: `{ userId, userWasCreated, joinedProject }` |
| `PUT /users/{id}` | First name, last name, display name, culture, `isActive`. Deactivating ends the sessions |
| `DELETE /users/{id}` | Multi mode: removes the person from this project (roles, groups, grants of this project go too). Single mode: deletes the user |
| `POST /users/{id}/lock` | Body `{ "untilUtc": null }`. Ends the sessions |
| `POST /users/{id}/unlock` | Unlock and reset the failed count |
| `POST /users/{id}/reset-2fa` | Two-factor off, key and recovery codes gone, sessions end |
| `GET`, `DELETE /users/{id}/sessions`, `DELETE /users/{id}/sessions/{sessionId}` | List and end sessions |
| `GET /users/{id}/access` | Effective roles, permissions, groups |
| `PUT`, `DELETE /users/{id}/roles/{roleId}` | Give or take a role. Only a global admin may handle `GlobalAdmin` |
| `PUT /users/{id}/project-admin` | Make the user a project admin of the project (creates the role the first time) |
| `PUT`, `DELETE /users/{id}/permissions/{key}` | Direct permission |
| `PUT`, `DELETE /users/{id}/groups/{groupId}` | Group membership |

### Roles, permissions, groups

| Route | Does |
|---|---|
| `GET`, `POST /roles`, `DELETE /roles/{id}` | List (with permission keys), create, delete (not system roles) |
| `GET /roles/{id}/permissions`, `PUT`, `DELETE /roles/{id}/permissions/{key}` | Permissions of a role |
| `GET`, `POST /permissions`, `PUT /permissions/{id}/obsolete`, `DELETE /permissions/{id}` | List, create, mark obsolete, delete |
| `GET`, `POST /groups`, `DELETE /groups/{id}` | List (roles, permissions, member count), create, delete |
| `GET /groups/{id}/members`, `PUT`, `DELETE /groups/{id}/members/{userId}` | Members |
| `PUT`, `DELETE /groups/{id}/roles/{roleId}`, `/groups/{id}/permissions/{key}` | Roles and permissions of a group |

The effects on access are described in [authorization.md](authorization.md).

### Audit

`GET /audit-events?action=&actorUserId=&targetType=&from=&to=&q=&page=&pageSize=` returns events, newest first, of the project in the route (`/projects/{projectId}/audit-events`). Every event has `projectId` and an optional `applicationId` (the client that did it).
A global admin may add `all=true` to see every project. A project admin who adds it gets `403`.

## External identity routes

Providers, mappings, home-realm routes and the support routes for logins and passkeys. They follow the same rules as the other routes: a project admin works on **the own project only** (other projects answer 404), a global admin may name any project in the route.
Every write creates an audit event (`external.provider.created`, `.updated`, `.deleted`, `.secret_rotated`, `external.mapping.*`, `passkey.*`, ...) with names and counts, **never secrets**, raises the `SettingsVersion` of the project and reloads the providers on all instances ([external-identity.md](external-identity.md#changing-providers-at-runtime)).

| Route | Who | What |
|---|---|---|
| `GET /external-provider-types` | Project | The eight provider types with their `fields`, `defaultScopes`, `setupDocsAnchor` and `emailVerifiedByDefault` (a form can be drawn from it) |
| `GET /projects/{projectId}/external-providers` | Project | Providers, **without secrets**: `secretIsSet` says which secrets exist; `urls` are the addresses to enter at the provider (`callback`, or `acs`, `slo`, `metadata`); `warnings` (certificate expires in 30 days, IdP-initiated on, certificate check off) |
| `POST /projects/{projectId}/external-providers` | Project | Create. Body is the provider JSON with a `kind` (`oidc`, `saml`, `ldap`). Secrets are sent in plain fields: `clientSecret`, `privateKey` (Apple), `bindPassword` (LDAP). They are encrypted and never come back. `409` when the name exists |
| `PUT /projects/{projectId}/external-providers/{name}` | Project | Update. **`If-Match: "v{settingsVersion}"` is required** (`409` when it is old). An empty or missing secret keeps the old one. The name and the type cannot change |
| `POST .../{name}/rotate-secret` | Project | Body `{ "clientSecret" }`, `{ "privateKey" }` or `{ "bindPassword" }` |
| `POST .../{name}/test` | Project | OIDC: reads discovery, reports issuer and endpoints. SAML: reads metadata, reports entity id, endpoints, certificate expiry. LDAP: TLS connect and service-account bind. Answer `{ ok, errorCode, reason, details }` (never secrets) |
| `POST .../{name}/ldap/test-user` | Project | Body `{ "userName" }`. Shows the DN, unique id, attributes and groups of that user, **no password** |
| `POST .../{name}/saml/refresh-metadata` | Project | Reads the identity provider metadata again now |
| `POST .../{name}/saml/generate-sp-certificate` | Project | Makes a new service provider certificate. Answer: the **public** PEM, expiry and thumbprint |
| `DELETE /projects/{projectId}/external-providers/{name}` | Project | Removes the provider. The links of users stay in the database |
| `GET/POST/PUT/DELETE /projects/{projectId}/external-group-mappings[/{mappingId}]` | Project | Mappings. `sourceType` Group, Claim or Attribute; `matchType` Exact or Prefix (no regex); `targetType` Role, Group or Permission. The target must belong to the project; `GlobalAdmin` and system roles are refused |
| `GET/PUT /projects/{projectId}/email-domain-routes` | Project | `[ { "providerName", "domains": [] } ]`. PUT replaces all; one provider per domain |
| `GET /users/{id}/external-logins`, `DELETE /users/{id}/external-logins/{provider}` | Project | Support: list and remove. The last way to sign in is protected (`cannot_remove_last_login_method`); a global admin may add `?force=true` |
| `GET /users/{id}/passkeys`, `DELETE /users/{id}/passkeys/{passkeyId}` | Project | List and remove (a lost device) |
| `GET/PUT /passkeys/blocked-aaguids` | Global | Authenticator models that cannot be registered. Joined with `Auth2:Passkeys:BlockedAaguids` |

**Who may change what.** Some provider fields are only for a global admin: a project admin cannot **turn on** `TrustProviderEmailVerification`, `TrustProviderMfa`, `AllowIdpInitiated` or turn **off** `ValidateServerCertificate` (answer `403 forbidden` with the field names). The generic `PUT /projects/{projectId}/settings` never changes the provider list (it keeps it as it is), and `GET .../settings` shows providers without any secret.

```json
POST /api/auth2/admin/projects/{projectId}/external-providers
{ "kind": "oidc", "name": "google", "type": "Google", "enabled": true,
  "clientId": "123.apps.googleusercontent.com", "clientSecret": "GOCSPX-...", "hostedDomain": "corp.com" }

201 Created
{ "name": "google", "type": "Google", "enabled": true, "displayName": null,
  "settings": { "kind": "oidc", "name": "google", "clientId": "123.apps.googleusercontent.com", "hostedDomain": "corp.com", "...": "..." },
  "secretIsSet": { "clientSecret": true, "privateKey": false },
  "urls": { "callback": "https://auth.example.com/callback/login/shop/google" },
  "registrationName": "ext-0198...-google", "warnings": [] }
```

## Example

```http
POST /api/auth2/admin/projects/0198.../users
Authorization: Bearer <admin user token>

{ "userName": "nina", "email": "nina@example.com", "password": "A-Strong-Pass-1" }
```

```json
{ "userId": "0198...", "userWasCreated": true, "joinedProject": true }
```

## Common problems

| Problem | Cause |
|---|---|
| `403 insufficient_scope` | The token has no `auth2.admin` scope. Add it to the client's `AllowedScopes` and request it |
| `403 forbidden` for a person you made admin | Global admins must sign in to `_global`. Project admins need the `ProjectAdmin` role **in that project** (`PUT /users/{id}/project-admin`) |
| `404` for an object you can see elsewhere | The project id in the route is not the project of your token, or the application belongs to another project |
| `409 conflict` on update | Someone else changed it. Read again and send the new `If-Match` |
| `403` on settings | A project admin touched a `GlobalOnly` setting. The error lists the paths |

## How to implement / extend

- Routes are in `Endpoints/AdminEndpoints.cs`. The filter `AdminApiFilter` finds who calls (in the home project of the token), reads `projectId` from the route and works as the target project. Add a project-level route to the `p` group (`/projects/{projectId:guid}`), a global one to `g`.
- Logic lives in services (`IAdminProjectService`, `IAdminApplicationService`, ...), not in the endpoints. A new service gets a test that also checks that a project admin gets `404` for another project.
- Every write calls `IAuditService`. Never put a secret into an audit entry.

## Related pages

- [authorization.md](authorization.md)
- [server-settings.md](server-settings.md)
- [seeding.md](seeding.md)
- [api-reference.md](api-reference.md)
- [Server reference](server-reference.md)
- [Credential scope](credential-scope.md)
- [Localization](localization.md)
- [Permission, role and group sync](permission-sync.md)
- [Client authorization](client-authorization.md)
- [Current user and current client](client-current-user.md)
- [Concepts](concepts.md)
- [Client setup](client-setup.md)
- [Client package reference](client-reference.md)
- [The key ring and its snapshot](key-ring.md)
- [UI handover](ui-handover.md)
- [Social login with OpenIddict.Client.WebIntegration](external-identity/social-webintegration.md)
- [SAML 2.0: enable and use](external-identity/saml.md)
- [LDAP and Active Directory: enable and use](external-identity/ldap.md)
- [Passkeys](external-identity/passkeys.md)
- [Brokering: your app trusts only the Server](external-identity/brokering.md)
- [Group mapping](external-identity/group-mapping.md)
- [Troubleshooting external sign-in](external-identity/troubleshooting.md)
- [Authentication flows](authentication-flows.md)
- [Documentation index](index.md)
