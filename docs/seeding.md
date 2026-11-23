# Seeding (first admin)

## What this is

The **seeder** (`Auth2Seeder`) creates the data a new server needs. You can run it many times. It does not change existing data.

## When to use it

It runs for you. Read this page before you go to production, because the first admin has a **public default password**.

## What the seed does

In one database transaction:

1. Creates the `_global` application, and its OpenIddict client.
2. Creates the standard OpenIddict scopes: `openid`, `profile`, `email`, `phone`, `roles`, `offline_access` and all `auth2.*` scopes.
3. Creates the `GlobalAdmin` role (a system role, global, no application).
4. Creates the user below **only if no GlobalAdmin user exists**. The email is confirmed. The user is added to `GlobalAdmin`.
   The user is created through `UserManager`, so the password follows the credential scope: in multi mode with `PerApplication` credentials the hash is in the `user_applications` row for `_global`; otherwise it is in the `users` row. In multi mode the `user_applications` row for `_global` always exists. See [credential-scope.md](credential-scope.md).
5. In single mode it also creates the one fixed application row.
6. Writes the audit event `seed.global_admin_created` (it never contains the password) and logs a warning.

The seeder never overwrites or resets the password of an existing user.

## When it runs

- By the `migrate` and `seed` commands.
- At startup in `Development`.
- At startup in other environments only when `Auth2:AutoSeed=true`.

In `Production`, at every startup the server also checks if `globaladmin` still has the default password. If yes, it logs a warning. It does **not** stop the application.

## The default admin

| | Value |
|---|---|
| User name | `globaladmin` |
| Email | `globaladmin@systemx.local` |
| Password | `Admin@12345!ChangeMe` |

> **Warning: never use the default password in production.** It is public in the source code.

## Step by step: move the values to environment variables

The values are constants in `Persistence/Seeding/Auth2Seeder.cs`. To read them from configuration:

1. Add a class `Auth2SeedOptions` with `AdminUserName`, `AdminEmail` and `AdminPassword`. Bind it to the section `Auth2:Seed`.
2. In `Auth2Seeder`, read `IOptions<Auth2SeedOptions>`. Use the constants only as a fallback in `Development`.
3. Set environment variables (`__` replaces `:`):

   ```bash
   Auth2__Seed__AdminUserName=admin
   Auth2__Seed__AdminEmail=admin@example.com
   Auth2__Seed__AdminPassword=<a strong password>
   ```

4. Where to keep the password:
   - Kubernetes: put it in a `Secret` and map the Secret to the variable.
   - Azure: use Key Vault.
   - Local work: `dotnet user-secrets set "Auth2:Seed:AdminPassword" "<password>"`.
5. In `Production`, make the app refuse to seed when the password is missing or equals the old default.
6. Remove the constants from the code. Change the password after the first login.

## After the first login

Change the admin password. The self-service password change arrives in phase 6.
To add more admins, use the `create-admin` command (planned, later phase).

## Common problems

- *I cannot log in as `globaladmin`.* Check that the seed ran (`seed` command, or `Development` startup). Check the log for the warning "Default GlobalAdmin created". If an admin already existed before, the seeder did not create a new one. Login itself arrives in phase 5.
- *The seed ran twice.* This is safe. The second run changes nothing.
- *I forgot the admin password.* Use `create-admin` (planned) to add a new GlobalAdmin.
- *The seeder says a user named `globaladmin` exists but is not a GlobalAdmin.* The seeder will not touch it. Rename that user or add it to the `GlobalAdmin` role yourself.

## Related pages

- [server-setup.md](server-setup.md)
- `security.md` and `getting-started.md` (planned; they will link here)

## Using the admin API

The seeded `globaladmin` can call the [admin API](admin-api.md) after you add the scopes `auth2.admin` and `auth2.admin.global` to the `AllowedScopes` of the `_global` client and sign in there.
