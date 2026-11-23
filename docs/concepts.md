# Concepts

## What this is

The main ideas of SystemX.Auth2, in simple words. Some parts are not built yet. They are marked *(planned)*.

## When to use it

Read this page first. The other pages use these words.

## Application (project) and ProjectKey

An **application** is one of your systems, for example "PaperTrade". The server stores it in the `applications` table.
Every application has its own settings, roles, permissions and groups.

The **ProjectKey** is a short name for the application: lowercase letters, digits, `-` and `_`, 3 to 64 characters.
`_global` is reserved. It is the built-in project for developers and global admins.

## Single or multi application

| | Multi application (default) | Single application |
|---|---|---|
| How to set | nothing | `services.ServerWithSingleApplication()` |
| `ProjectKey` | used | ignored by all logic |
| `user_applications` table | used | never read, never written |
| Credentials (password, 2FA, lockout) | per application | in the user row (shared) |
| Roles, permissions, groups | per application | one fixed application row |

`ServerWithSingleApplication()` exists **only on the server**. The client has no such method.
The client finds out the mode with the handshake *(planned)*.

In single mode the server still has the `_global` project (for the global admin) and one fixed application row.
Its name comes from `Auth2:SingleApplication:Name` (default `Default`), and its project key is made from the name (`default`).
The column `ApplicationId` stays filled, so you can switch to multi mode later without changing data in the role, permission and group tables.

The code never asks "is this single mode?" in many places. It asks one abstraction, `IApplicationContext`.
There are two implementations: `SingleApplicationContext` and `MultiApplicationContext`.

## User and UserApplication

One real person is one `AppUser` row. User name and email are unique on the whole server.
The `user_applications` row says "this person belongs to this application".
When credentials are per application, this row also holds the password hash, TOTP key and lockout state.
See [credential-scope.md](credential-scope.md).

## Query filters

Tables that belong to one application (roles, permissions, groups, `user_applications`, audit events, OTP codes) have a global EF Core **query filter**.
It shows only rows of the current application. If no application is selected, you see no rows. This is the safe default.

Only code that runs inside `IGlobalScope.Begin()` bypasses the filter. Use it only for developer-level work: seeding, global admin, background jobs.

Global roles (`ApplicationId` is null) are always visible.

## Scopes, roles, permissions, groups *(planned)*

- **Scope**: what a token may call (`auth2.me`).
- **Permission**: a fine right in an application (`orders.read`).
- **Role**: a named set of permissions (`Editor`).
- **Group**: a set of users that can have roles and permissions.

## Proof of ownership

A person who already has an account on the server cannot be added to a second application just because somebody typed their email there. The server sends a one-time code to the person's **confirmed** email or phone. Only with that code can they join and set a password for the new application. See [authentication-flows.md](authentication-flows.md).

## Tokens and sign-in

- **Hosted UI**: the browser goes to the server, the user signs in on the server's page, and your app gets a token. Your app never sees the password. This is the recommended way.
- **Self UI**: your app shows its own form and sends the password to the server. Off by default.
- **M2M** (machine to machine): one program calls another with a token from the client id and secret. No user.
- The access token is a signed JWT. It has the roles of one application and a version number (`pv`), not the list of permissions.

See [authentication-flows.md](authentication-flows.md).

## Related pages

- [architecture.md](architecture.md)
- [server-setup.md](server-setup.md)
- [server-settings.md](server-settings.md)
