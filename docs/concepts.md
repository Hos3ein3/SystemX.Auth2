# Concepts

## What this is

The main ideas of SystemX.Auth2, in simple words. The other pages use these words.

## When to use it

Read this page first. If a word in another page is not clear, come back here.

## Project and Application

These two words are different. Do not mix them.

| | Project | Application |
|---|---|---|
| What is it | The tenant: one product or customer, for example **PaperTrade** | One **client** of a project, for example `papertrade-api`, `papertrade-mvc`, `papertrade-api-v2` |
| Stored in | table `projects` | table `applications` (it **is** the OpenIddict application table, with a few extra columns) |
| Identified by | `ProjectKey` (lowercase letters, digits, `-`, `_`; never changes) | `ClientId` (globally unique) and `Name` (unique inside the project) |
| Owns | users (membership), roles, permissions, groups, credentials, external providers, sessions, audit | redirect URIs, allowed scopes, client secret, UI mode, token lifetimes, CORS origins |
| Can be disabled | yes: nobody can sign in, in any application | yes: only that application is blocked |

`_global` is the built-in project for developers and global admins. It has one application, also named `_global`.
The key `_global` is reserved. `_global` can not be disabled or deleted.

### The ProjectKey is not a secret

The `ProjectKey` is an **identifier**. It is not hashed and not proof of anything.
The server finds the project from the **application** (the client id of the token).
The client only sends the key, and the server checks that the client really belongs to that project and has the name it sent.
The real security is the client id and secret (confidential clients), PKCE and exact redirect URI checks (public clients).

### The client id

A new application gets the client id `{projectkey}.{name}` (for example `papertrade.api`), or a value the admin chooses.
It must be unique on the whole server (OpenIddict needs that). The applications that the seeder and the `CreateWithApplicationAsync` helper make use the project key as client id.

## Access rule: one membership, every application

A person who has a `user_projects` row for a project can use **every** application of that project.
There is no list of allowed applications. Registering through any application creates the row once.
Signing in through another application of the same project needs no new registration.

The same person in another project is a **separate membership with separate credentials**
(password, two-factor, lockout) when credentials are per project. See [credential-scope.md](credential-scope.md).
`JoinMode` (`AutoJoin`, `InviteOnly`, `Closed`) is a **project** setting. It decides who may create a `user_projects` row.

## Single or multi project

| | Multi project (default) | Single project |
|---|---|---|
| Server | nothing to call | `services.ServerWithSingleProject()` |
| Client | `.ServerWithMultiProject("PaperTrade")` | do not call it |
| `ProjectKey` | used | ignored (a client that sends one gets a warning in the log) |
| `user_projects` table | used | never read, never written |
| Credentials (password, 2FA, lockout) | per project | in the user row (shared) |

In single mode the server still has the `_global` project (for the global admin) and one fixed project.
Its name comes from `Auth2:SingleProject:Name` (default `Default`). Applications still exist.
A client of a multi-project server that sends **no** key fails at start with a clear message.
See [handshake-and-startup.md](handshake-and-startup.md).

The code does not ask "is this single mode?" in many places. It asks two abstractions:

- `IProjectContext`: the project of the request (the tenant). Two implementations (`SingleProjectContext`, `MultiProjectContext`).
- `IClientApplicationContext`: the calling application (from the client id of the token or the authorize request).

## User and membership

One real person is one `AppUser` row. The **user name is the identity**: it is always unique on the whole server (any letter case), and it can be a plain name, an email address or a phone number (a registration by email or phone sets it to that email or phone). The email is unique too. The rule that ties them together: **one text never names two people.** A person's email or phone number cannot be the user name of somebody else, and a user name cannot be the email or phone number of somebody else (`DuplicateEmail`, `DuplicateUserName`, `phone_number_taken`). A person may use their **own** email or phone number as their user name. See [sign-in lookup](authentication-flows.md#how-a-person-is-found-at-sign-in).
The `user_projects` row says "this person belongs to this project".
When credentials are per project, this row also holds the password hash, TOTP key and lockout state.

## Enabled and disabled

- **Project disabled**: nobody can sign in by any method (password, passkey, social, OIDC, SAML, LDAP, two-factor, password reset, join, refresh token, authorization code, client credentials), in any application.
- **Application disabled**: only that application is blocked. Other applications of the project work.
- One shared check decides this for every path (`AccessGate`). The public answer is always the generic `access_denied`. The real reason (`project_disabled` or `application_disabled`) goes only to the audit log.
- Disabling a project revokes all its tokens, authorizations and sessions in one transaction, keeps all data, and writes an audit event with the admin id and the reason. Disabling an application revokes only the tokens of that client.
- A token issued before is refused at its next validation. The status is cached for a short time (`Auth2:ProjectStatusCacheSeconds`, default 10 seconds; at once on the instance that made the change, through Redis on the other instances).

Details: [authentication-flows.md](authentication-flows.md) and [security.md](security.md).

## Two levels of settings

- **Project settings**: login methods, password rules, lockout, two-factor, external providers, `JoinMode`, `SingleActiveSession`, token defaults, localization.
- **Application settings**: the OpenIddict fields (redirect URIs, post-logout redirect URIs, allowed scopes, consent type) plus a small `SettingsJson` (UI mode, cookie lifetime, token lifetime overrides, CORS origins, Self UI password grant, which external providers this client shows).

The **effective value** is the project default, overridden by the application value where the application sets one.
One resolver (`EffectiveSettings`) does this. See [server-settings.md](server-settings.md).

## Query filters

Tables that belong to one project (roles, permissions, groups, `user_projects`, logins, passkeys, audit events, OTP codes) have a global EF Core **query filter** by `ProjectId`.
It shows only rows of the current project. If no project is selected, you see no rows. This is the safe default.

Only code that runs inside `IGlobalScope.Begin()` bypasses the filter. Use it only for developer-level work: seeding, global admin, background jobs.

Global roles (`ProjectId` is null) are always visible in `_global` and in single-project mode.
Audit events, sessions and synced catalog items also keep an **optional** `ApplicationId`: the client that did it, or pushed it.

## Scopes, roles, permissions, groups

- **Scope**: what a token may call (`auth2.me`). A scope belongs to the **client** (`ICurrentClient`).
- **Permission**: a fine right in a project (`orders.read`). A permission belongs to the **user** (`ICurrentUser`).
- **Role**: a named set of permissions (`Editor`).
- **Group**: a set of users that can have roles and permissions.

An endpoint can require a scope **and** a permission. See [client-authorization.md](client-authorization.md) and [client-current-user.md](client-current-user.md).

## Proof of ownership

A person who already has an account on the server cannot be added to a second project just because somebody typed their email there. The server sends a one-time code to the person's **confirmed** email or phone. Only with that code can they join and set a password for the new project. See [authentication-flows.md](authentication-flows.md).

## Tokens and sign-in

- **Hosted UI**: the browser goes to the server, the user signs in on the server's page, and your app gets a token. Your app never sees the password. This is the recommended way. The server's login cookie is **per project** (its name has the project key), so a login in one project never counts in another project on the same domain.
- **Self UI**: your app shows its own form and sends the password to the server. Off by default, switched on per application (the setting `AllowSelfUiLogin` of that application).
- **M2M** (machine to machine): one program calls another with a token from the client id and secret. No user.
- The access token is a signed JWT. It has `project_id`, `projectkey`, `client_id`, `application_id`, the roles of the project and a version number (`pv`), not the list of permissions.

See [authentication-flows.md](authentication-flows.md).

## Related pages

- [architecture.md](architecture.md)
- [server-setup.md](server-setup.md)
- [server-settings.md](server-settings.md)
- [admin-api.md](admin-api.md)
- [decisions.md](decisions.md)
- [Getting started](getting-started.md)
- [Client setup](client-setup.md)
- [Seeding (first admin)](seeding.md)
- [Troubleshooting](troubleshooting.md)
- [Database tables](database-tables.md)
- [Comparison and limits](comparison-and-limits.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Permission, role and group sync](permission-sync.md)
- [Documentation index](index.md)
- [Scope, permission, role, group: which is which](access-concepts.md)
- [Minimal API methods and attributes: when to use which](client-endpoint-metadata.md)
- [Enums reference](enums-reference.md)
- [Scopes and their resources](scopes.md)
