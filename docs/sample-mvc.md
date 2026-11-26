# The MVC sample: one action for every kind of rule

## What this is

`samples/SystemX.Auth2.Samples.Mvc` is a small MVC application (cookie sign-in with the **hosted UI**: the Auth2 Server draws the sign-in pages). Its `HomeController` has **one action for every kind of authorization rule** of the Client package, a page that lists them (**Rules**, `/Home/Showcase`) and a page that shows what you have (`/Home/Inspect`). You open the pages as different people and see who gets in, and why.

It uses the demo people of the Server's demo data ([seeding.md](seeding.md#demo-people-for-the-samples)), so the rules have something to work with.

## When to use it

- You want to see an attribute work before you use it in your own application.
- You want a copy-paste example for a rule: every action has the attribute right above it.
- You test a change of the Client package or the Server by hand.

## Run it

1. Start the Server in `Development` with the demo data on (`Auth2:SeedDemoData=true`; it is on in `appsettings.Development.json` and in `docker-compose.yml`). The seed creates the project `sample-project`, the application `mvc-sample` and the demo people.
2. Start the sample: `docker compose up`, or `dotnet run --project samples/SystemX.Auth2.Samples.Mvc` (port 5101 in the compose file).
3. Open the sample, press **Sign in**, and use one of the demo people with the password `Demo@12345!`. Sign out and sign in as another person to compare.

At sign-in the sample asks for the scopes `reports.read`, `orders.read` and `orders.write` on top of the default ones (`UseHostedUi(o => o.Scopes.AddRange(...))` in `Program.cs`). The application is allowed to use them (the seed allows them), so **every person who signs in here has these three scopes**.

## The demo people

| User | Roles | Groups | Permissions (effective) |
|---|---|---|---|
| `alice` | `Admin` | none | all 60 |
| `bob` | `Manager`, `Editor` (from the group) | `sales` | `reports.read`, `orders.read`, `orders.write`, `users.read` |
| `carol` | `Editor` | none | `orders.read`, `orders.write` |
| `dave` | `Viewer` (from the group `support`) | `support` | `orders.read` |
| `erin` | `Auditor` (from the group) | `finance` | `reports.read` (role), `orders.read` (given to the group) |
| `frank` | none | none | `orders.read`, `reports.read` (granted to him directly) |

## The actions

A tick means: the page opens. Everything else shows the access denied page. The attribute is the one in `HomeController`.

| Action | Rule | alice | bob | carol | dave | erin | frank |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|
| `Orders` | `[Auth2Permission("orders.read")]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `OrdersWrite` | `[Auth2Permission("orders.write")]` | ✓ | ✓ | ✓ | | | |
| `OrdersWriteOrDelete` | `[Auth2Permission("orders.write", "orders.delete")]` (any) | ✓ | ✓ | ✓ | | | |
| `OrdersFullControl` | `[Auth2Permission(true, "orders.read", "orders.write", "orders.delete")]` (all) | ✓ | | | | | |
| `OrdersThroughARole` | `[Auth2Permission("orders.read")] [Auth2OnlyRolePermission]` | ✓ | ✓ | ✓ | ✓ | | |
| `OrdersGrantedToYou` | `[Auth2Permission("orders.read")] [Auth2OnlyUserPermission]` | | | | | | ✓ |
| `OrdersExport` | `[Auth2Permission("orders.export")] [Auth2Audit("orders.export")]` | ✓ | | | | | |
| `AdminPanel` | `[Auth2Role("Admin")]` | ✓ | | | | | |
| `ManagersArea` | `[Auth2Role("Manager", "Admin")]` (any) | ✓ | ✓ | | | | |
| `AdminAndManager` | `[Auth2Role(true, "Admin", "Manager")]` (all) | | | | | | |
| `FinanceTeam` | `[Auth2Group("finance")]` | | | | | ✓ | |
| `FinanceRegions` | `[Auth2Group("finance-eu", "finance-us")]` | | | | | | |
| `ReportsScope` | `[Auth2Scope("reports.read")]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `OrdersScope` | `[Auth2Scope("orders.read", "orders.write")]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ReportsScopeAndPermission` | `[Auth2Scope("reports.read")] [Auth2Permission("reports.read")]` (AND) | ✓ | ✓ | | | ✓ | ✓ |
| `PeopleOnly` | `[Auth2UserOnly]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `DeleteOrders` | `[Auth2RoleOrPermission(Roles = ["Admin"], Permissions = ["orders.delete"])]` | ✓ | | | | | |
| `BudgetReview` | `[Auth2RoleOrGroup(Roles = ["Manager"], Groups = ["finance"])]` | | ✓ | | | ✓ | |
| `ApproveOrders` | `[Auth2PermissionOrGroup(Permissions = ["orders.approve"], Groups = ["finance"])]` | ✓ | | | | ✓ | |
| `ReportsForProgramsOrAdmins` | `[Auth2ScopeOrRole(Scopes = ["reports.read"], Roles = ["Admin"])]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `AnythingGoes` | `[Auth2AnyOf(Roles, Permissions, Groups, Scopes)]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Inspect` | `[Authorize]` (the checks are written in code) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

**Rules that are not about who the person is** (so they are not in the table above; open them from the Showcase page):

| Page | Rule | What you see |
|---|---|---|
| `FinanceEurope` | `[Auth2Group(true, "finance", "finance-eu")]`: **all** of the groups | A person in `finance` **and** `finance-eu` passes; one of the two gets 403 |
| `SignedInWithPassword` | `[Auth2SignInMethod("pwd")]` | A password sign-in passes; a passkey sign-in gets 403 |
| `MultiFactorOnly` | `[Auth2MultiFactor]` | A verified passkey, or a password plus a one-time code, passes; a password alone gets 403 |
| `TotpForm` (then `TotpAction`) | `[Auth2Totp]`: a **fresh code of the authenticator app**, posted in the field `auth2_totp` | You need two-factor on (`/Manage/TwoFactor/Enable`). A right code passes once; a wrong or missing code gets 403 with the header `X-Auth2-Totp-Status` |
| `ScopeFails` | `[Auth2Scope("payments.refund")]`: a scope this application does not have | **Always 403** |
| `ScopeFailsInCode` | `currentClient.HasScope("payments.refund")` in code | **Always 403** with `{ "error": "insufficient_scope", "required": ..., "granted": [...] }` |

(The attributes in the code write the arrays as `new[] { ... }`; the table shortens them.) The other generated demo people have roles and groups too: `FinanceRegions` opens for `william.hernandez`, `benjamin.gonzalez` and `logan.wright`, who are in `finance-us` or `finance-eu`.

### What each group of actions teaches

| Kind | Actions | The point |
|---|---|---|
| **Permission** | `OrdersWrite`, `OrdersWriteOrDelete`, `OrdersFullControl` | One key; several keys in one attribute are **any**; `true` as the first argument makes them **all** |
| **Where the permission comes from** | `OrdersThroughARole`, `OrdersGrantedToYou` | `erin` has `orders.read` only through a permission of her group, so she is refused by **both**: a group's own permission is neither "from a role" nor "granted to you" ([client-endpoint-metadata.md](client-endpoint-metadata.md)) |
| **Audit** | `OrdersExport` | `[Auth2Audit]` reports every try, allowed or refused, to `IAuth2AuditSink` (a log line by default: `Auth2 audit orders.export: allowed|refused`) |
| **Role** | `AdminPanel`, `ManagersArea`, `AdminAndManager` | Roles are in your token; nobody has both `Admin` and `Manager`, so `AdminAndManager` always refuses |
| **Group** | `FinanceTeam`, `FinanceRegions` | Membership of a group, not what the group may do |
| **Scope** | `ReportsScope`, `OrdersScope` | A scope belongs to the **application**: everybody who signs in to this application has it. The page checks the client, not the person |
| **Scope AND permission** | `ReportsScopeAndPermission` | Two attributes are two rules: the **application** may use the reports API and **you** may read reports. `carol` and `dave` have the scope but not the permission |
| **Token kind** | `PeopleOnly` | A person's token, not a program's |
| **Any of** | `DeleteOrders`, `BudgetReview`, `ApproveOrders`, `ReportsForProgramsOrAdmins`, `AnythingGoes` | OR **across kinds**, which chaining cannot say ([client-endpoint-metadata.md](client-endpoint-metadata.md#any-of-rules-a-role-or-a-permission)). In this application everybody has the scope `reports.read` and `orders.write`, so the two pages with a scope open for every signed-in person; a program with the scope would pass without any person |
| **In code** | `Inspect` | The same checks with `ICurrentUser` and `ICurrentClient` (`HasPermissionAsync`, `HasRoleAsync`, `HasScope`, groups), for a screen that shows or hides parts instead of refusing the page |

## Resources in the token

The seed gives the sample scopes **resources** (`reports.read` lists `reports-api`; `orders.read` and `orders.write` list `orders-api`, [scopes.md](scopes.md)). Because the sample asks for those three scopes, the access token of every person has

```
aud = ["mvc-sample", "reports-api", "orders-api"]
```

The **Inspect** page decodes the access token that the cookie keeps and shows the audiences, so you can see the client id first and then the resources. Change the resources of a scope in the admin console, sign out and in again (or wait for a refresh), and the list changes.

## The catalog: scanned by the Client

The sample uses `ScanningPolicy.Both` and `AssembliesToScan = [typeof(Program).Assembly]`, so at start-up the Client **scans these attributes** and pushes the roles, permissions, groups and scopes they name (the any-of rules too) to the Server. Open the admin console of `sample-project` after the first start: names like `orders.export`, `orders.approve`, `finance-eu` and the scope `reports.read` are there, with the **source** application `mvc-sample` ([permission-sync.md](permission-sync.md)). The demo seed already creates most of them; the push adds what is missing and never deletes.

## Common problems

| Problem | Cause |
|---|---|
| Every rule page is refused for every person | The demo data is off, so the people and the permissions are missing (`Auth2:SeedDemoData`) |
| Sign-in fails with `invalid_scope` | The application does not allow `reports.read`, `orders.read` or `orders.write`. The seed allows them for `mvc-sample`; a hand-made application needs them in its allowed scopes |
| `OrdersExport` and `ApproveOrders` are refused for everybody | `orders.export` and `orders.approve` exist only in the demo data (and `alice` has them through `Admin`) |
| A rule seems to ignore my new role | The role claim is a snapshot of the token: sign out and in again ([access-concepts.md](access-concepts.md#role-in-depth)) |
| `Inspect` shows no `reports-api` in `aud` | The scope `reports.read` has no resource in your database yet, or you signed in before the change. Restart the Server (the seed adds the resources of the sample scopes) and sign in again |

## How to implement or extend

- A new demo page: add an action with the attribute, and one line to the `demos` list in `Views/Home/Showcase.cshtml`. `Rule(title, attribute, explanation)` shows the shared `Rule` view.
- Your own application: copy the attribute you need ([client-endpoint-metadata.md](client-endpoint-metadata.md)) and the sign-in setup of `Program.cs` ([client-setup.md](client-setup.md)).

## Related pages

- [Scope, permission, role, group: which is which](access-concepts.md)
- [Getting started](getting-started.md)
- [Documentation index](index.md)
