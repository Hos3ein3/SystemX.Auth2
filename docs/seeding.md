# Seeding (first admin)

## What this is

The **seeder** (`Auth2Seeder`) creates the data a new server needs. You can run it many times. It does not change existing data.

## When to use it

It runs for you. Read this page before you go to production, because the first admin has a **public default password**.

## What the seed does

In one database transaction:

1. Creates the `_global` project and its application `_global` (the admin console client, an OpenIddict application with the client id `_global`).
2. Creates the standard OpenIddict scopes: `openid`, `profile`, `email`, `phone`, `roles`, `offline_access` and all `auth2.*` scopes.
3. Creates the `GlobalAdmin` role (a system role, global, no project).
4. Creates the user below **only if no GlobalAdmin user exists**. The email is confirmed. The user is added to `GlobalAdmin`.
   The user is created through `UserManager`, so the password follows the credential scope: in multi mode with `PerProject` credentials the hash is in the `user_projects` row for `_global`; otherwise it is in the `users` row. In multi mode the `user_projects` row for `_global` always exists. See [credential-scope.md](credential-scope.md).
5. In single mode it also creates the one fixed project and its application `default`.
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

## Demo people for the samples

The seeder adds **demo data to the project `sample-project`**: permissions, roles, groups, people, and who has what. `sample-project` is the sample project of the two sample applications (`mvc-sample`, `minimal-api-sample`); it is **not a real project**, so the demo data is added **whenever the seed runs with `Auth2:SeedDemoData=true`** (multi-project mode, not in the automated tests). It is **off by default**. It is on in `appsettings.Development.json` (so `dotnet run` in `Development` has it) and in `docker-compose.yml`, which are for trying things out.

- The demo data comes **after** the main seed is saved, and a problem in it is only logged: the server starts anyway.
- It runs in **its own scope of the services**, so the data always goes into `sample-project`, whatever project the code that started it worked in. (An earlier version of the seed put the demo data into `_global`. When the seed finds demo people that are members of `_global` and not of the sample project, it removes that data from `_global`, logs a warning, and adds it again to the sample project.)
- It only adds what is missing, so it is safe to run many times (`DemoDataSeeder`, `Persistence/Seeding/DemoDataSeeder.cs`).
- **Every demo person has the public password `Demo@12345!`.** On any server that is not for trying things out leave `Auth2:SeedDemoData` at `false` (the default); to switch it on elsewhere set `Auth2__SeedDemoData=true`. Remove the sample project and its people in the admin console. (The sample project itself, with its public client secrets, is also seeded wherever the seed runs; delete it too on a real server.)

**What is created** (all in `sample-project`):

| Kind | How many | Names |
|---|---|---|
| Permissions | 60 | 12 areas × 5 actions: `orders`, `invoices`, `customers`, `products`, `inventory`, `shipping`, `payments`, `reports`, `users`, `settings`, `audit`, `support`, each with `.read`, `.write`, `.delete`, `.export`, `.approve` (for example `orders.write`) |
| Roles | 55 | `Admin` (all 60 permissions), `Manager`, `Editor`, `Viewer`, `Auditor`; for each area a `Viewer`, `Editor` and `Manager` (`OrdersViewer`, `OrdersEditor`, `OrdersManager`, `InvoicesViewer`, ... = 36); 14 job roles (`BillingClerk`, `WarehouseOperator`, `ShippingCoordinator`, `SupportAgent`, `SupportLead`, `DataAnalyst`, `ComplianceOfficer`, `SecurityOfficer`, `OperationsLead`, `FinanceController`, `SalesRepresentative`, `SalesLead`, `CustomerSuccess`, `ContentEditor`). Each role has the permissions its name says |
| Groups | 53 | `finance`, `sales`, `support`, and 10 departments × 5 regions (`finance-eu`, `finance-us`, `finance-asia`, `finance-africa`, `finance-latam`, `sales-eu`, ... `product-latam`). Each department group has the role of its department; the `-us` groups also have the permission `reports.read` directly |
| Scopes | 60 | `api.<area>.<action>` (for example `api.orders.read`), registered and **allowed for both sample applications** (`mvc-sample` and `minimal-api-sample`) |
| Users | 56 | the six below, and 50 generated people (`liam.smith`, `olivia.johnson`, `noah.williams`, ...) |

The six first users show the ways to get access:

| User | Role | Group | Direct permissions | What it shows |
|---|---|---|---|---|
| `alice` | `Admin` | | | Everything: all 60 permissions |
| `bob` | `Manager` | `sales` | | A role, plus the role `Editor` that the group gives |
| `carol` | `Editor` | | | `orders.read`, `orders.write` |
| `dave` | `Viewer` | `support` | | Only `orders.read` |
| `erin` | | `finance` | | No role of her own: the group gives the role `Auditor` (`reports.read`) and the permission `orders.read` |
| `frank` | | | `orders.read`, `reports.read` | No role and no group: direct grants only (`WithOnlyUserPermission`) |

Each of the 50 generated people (the list is `DemoDataSeeder.UserNames`) gets one role, a second role for every second person, one group, a second group for every third person, and a direct permission for every fourth person, chosen in a fixed pattern, so the same server always has the same data. Every demo user has the email `<user name>@example.com` (confirmed); alice, bob and every third generated person also have a confirmed phone number.

The first start with the demo data takes some seconds longer (it hashes 56 passwords and makes about 700 assignments). A finished seed is recognised at the next start (the first and the last person exist) and skipped at once.

The seed also creates the **sample applications** of the project (with their types: `mvc-sample` is `Web`, `minimal-api-sample` is `Api`, `worker-sample` is `Service`; no web server address is seeded, the samples announce it themselves): `mvc-sample`, `minimal-api-sample` and `worker-sample` (the background job of `samples/SystemX.Auth2.Samples.Worker`, which has only the scopes it needs), and the custom scopes `reports.read`, `orders.read`, `orders.write` and `jobs.run` (with resources, [scopes.md](scopes.md)).

Sign in to the MVC sample with any of them (user name and password `Demo@12345!`); the sample project and its two applications are created by the same seed. The docker-compose file sets `Auth2__SeedDemoData: "true"`, so the people are there at once after `docker compose up`. To remove them, delete the users, roles, groups and permissions in the admin console (and set `SeedDemoData` to `false`, or the people come back at the next start).

## After the first login

Change the admin password with the page `/Manage/ChangePassword` (or `POST /api/auth2/password/change`).
To add more admins, use the `create-admin` command (see [server-setup.md](server-setup.md)) or the admin pages.

## Common problems

- *I cannot log in as `globaladmin`.* Check that the seed ran (`seed` command, or `Development` startup). Check the log for the warning "Default GlobalAdmin created". If an admin already existed before, the seeder did not create a new one. Then try the sign-in again.
- *The seed ran twice.* This is safe. The second run changes nothing.
- *I forgot the admin password.* Use `create-admin` (planned) to add a new GlobalAdmin.
- *The seeder says a user named `globaladmin` exists but is not a GlobalAdmin.* The seeder will not touch it. Rename that user or add it to the `GlobalAdmin` role yourself.

## Related pages

- [server-setup.md](server-setup.md)
- `security.md` and `getting-started.md` (planned; they will link here)
- [Getting started](getting-started.md)
- [Concepts](concepts.md)
- [Client setup](client-setup.md)
- [Troubleshooting](troubleshooting.md)
- [Server settings](server-settings.md)
- [Deployment and scaling](deployment-and-scaling.md)
- [Security](security.md)
- [Observability](observability.md)
- [Email and SMS](email-and-sms.md)
- [Server reference](server-reference.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Database tables](database-tables.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Documentation index](index.md)
- [The MVC sample: one action for every kind of rule](sample-mvc.md)
- [Minimal API methods and attributes: when to use which](client-endpoint-metadata.md)
- [Scope, permission, role, group: which is which](access-concepts.md)
- [Scopes and their resources](scopes.md)
- [Permission, role and group sync](permission-sync.md)

## Using the admin API

The seeded `globaladmin` can call the [admin API](admin-api.md) after you add the scopes `auth2.admin` and `auth2.admin.global` to the `AllowedScopes` of the `_global` client and sign in there.
