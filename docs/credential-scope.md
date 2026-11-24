# Credential scope

## What this is

"Credentials" are the things that prove who a user is: the password, the security stamp, the two-factor (TOTP) key, the recovery codes, and the lockout state (failed attempts, lockout end).

**Credential scope** says where the server keeps them:

| Scope | Where | Meaning |
|---|---|---|
| `PerProject` (default) | `user_projects` row of the project | One person can have a different password, different 2FA and a different lockout in each project |
| `Shared` | the `users` row (and `user_tokens`) | One set of credentials for all projects |

In single-project mode the scope is always `Shared`, and `user_projects` is never read or written.

## When to use it

- Choose `PerProject` when your projects are separate products and a leak in one must not open the others.
- Choose `Shared` when all projects are one system and users expect one password.

## Step by step

1. Set the server option `Auth2:CredentialScope` to `PerProject` or `Shared`.
2. Optional: set `Auth2:AllowCredentialScopeOverride=true`. Then a global admin can set `CredentialScope` in the settings of one project.
3. Do not change the scope on a server that already has users. Moving credentials between the two places needs the `convert-to-*` commands (planned). Without them, users would look like they have no password.

## How it works in the code

- `ICredentialState` has two implementations: `UserRowCredentialState` and `UserProjectCredentialState`.
- `Auth2UserStore` asks `ICredentialStateProvider` for the right one on every call. The provider reads the effective scope from `IAuthFlowResolver`.
- The identity data (name, email, phone, confirmed flags) is always global in `users`. One person is one row. User name and email are unique on the whole server.
- Email and phone confirmation are global, because it is the same mailbox.

What the store keeps where:

| Data | `PerProject` | `Shared` |
|---|---|---|
| Password hash, security stamp | `user_projects` | `users` |
| Two-factor on/off, lockout, failed count | `user_projects` | `users` |
| TOTP key (encrypted with Data Protection) | `user_projects.authenticator_key` | `user_tokens` |
| Recovery codes (hashed, one use) | `user_project_recovery_codes` | `user_tokens` |

## Joining a second project

A user must not be added to another project without proof that they own the account (pitfall: account pre-hijacking).
The rules in the code:

- A **new** user gets the `user_projects` row saved in the same transaction as the user. The row is created *before* the password is stored.
- For a person who **already exists**, the store never creates the row by itself. Setting a password for them in an project where they have no row throws `InvalidOperationException` (`user_not_in_project`).
- Reading credentials where there is no row gives "no password". So the password from project A never works in project B.
- The join flow with the proof step is `IJoinService` (a code to a confirmed email or phone, then the row and the new password in one transaction). See [authentication-flows.md](authentication-flows.md#joining-a-second-project-proof-of-ownership).
- A membership row that exists without credentials (for example added by an admin) gets a security stamp the first time it is needed, so Identity can save the user.

## Lockout

Lockout rules come from the settings of the project (`Lockout.MaxFailedAttempts`, `LockoutMinutes`, `LockoutEnabled`, `LockoutForNewUsers`), not from the global Identity options.
With `PerProject`, a lockout in project A does not lock the user in project B.

## Example

```csharp
// Inside a request for the client "papertrade" (the client id sets the project, and the project knows its project)
var result = await userManager.CreateAsync(new AppUser { UserName = "sara", Email = "sara@example.com" }, "A-Strong-Pass-1");
// password hash is now in user_projects for "papertrade"
```

## Common problems

- *`ProjectNotFoundException` from UserManager:* the request has no project. In a request the client id comes from the token (phase 5). In your own code, set `IClientIdAccessor.ClientId` or `IApplicationSelection.ProjectKey` first.
- *`InvalidOperationException: user_not_in_project`:* the person exists but has no membership row in this project. Use the join flow (phase 6).
- *Passwords stopped working after I changed the scope:* see step 3.

## External logins and passkeys

Links to external identities (`user_logins`) and passkeys (`user_passkeys`) always belong to **one project**. The same Google account linked in project A is not known in project B:
the user links it again there. Reason: the projects can have different providers, different rules and different credentials, so they must not share logins. See [external-identity.md](external-identity.md).

## Related pages

- [concepts.md](concepts.md)
- [server-setup.md](server-setup.md)
- [localization.md](localization.md)
