# Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)

## What this is

ASP.NET Core Identity is built to be customised through two kinds of classes: **stores** (they read and write the database) and **managers** (they hold the rules and call the stores). The namespace `SystemX.Auth2.Server.Persistence.Identity` holds the Auth2 versions of them. Everything that makes one Auth2 server serve many projects with different rules is here.

| File | Types | In one line |
|---|---|---|
| `Auth2UserStore.cs` | `Auth2UserStore` | The user store: credentials go to the right place, logins and passkeys belong to the current project, roles are looked up inside the current project |
| `Auth2RoleStore.cs` | `Auth2RoleStore`, `Auth2RoleValidator`, `RoleScopeHelper` (internal) | The role store and the rule "a role name is unique inside one project" |
| `CredentialStates.cs` | `CredentialStateBase`, `UserRowCredentialState`, `UserProjectCredentialState`, `CredentialStateProvider` | Where passwords, two-factor and lockout live: in the `users` row or in the `user_projects` row |
| `Auth2Managers.cs` | `Auth2UserManager`, `Auth2RoleManager`, `Auth2SignInManager` | The managers. The user manager applies the **project's** lockout rules; the other two are thin |

The entities they work on (`AppUser`, `AppRole`, `AppUserLogin`, `AppUserToken`, `AppUserPasskey`, `UserProject`, ...) and their tables are in [database-tables.md](database-tables.md).

## When to use it

- You wonder where a password, a recovery code or a passkey is stored, and why the answer depends on the project: read [How credentials are stored](#how-credentials-are-stored).
- You call `UserManager<AppUser>` / `RoleManager<AppRole>` from your own server-side code and get `ProjectNotFoundException` or `user_not_in_project`: read [The current project](#the-current-project).
- You change how Identity behaves (a new rule, a new store method): read [How to implement or extend](#how-to-implement-or-extend).

## How they are registered

`AddSystemXAuth2Server` registers Identity in `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
services.AddIdentityCore<AppUser>(o =>
{
    o.User.RequireUniqueEmail = false;        // Auth2UserValidator checks the email (a person may have no email: phone only)
    o.User.AllowedUserNameCharacters = "";    // Auth2UserValidator checks the characters with the project's regex
    o.Lockout.AllowedForNewUsers = false;     // Auth2UserManager decides, per project
})
.AddRoles<AppRole>()
.AddUserStore<Auth2UserStore>().AddRoleStore<Auth2RoleStore>()
.AddUserManager<Auth2UserManager>().AddRoleManager<Auth2RoleManager>().AddSignInManager<Auth2SignInManager>()
.AddDefaultTokenProviders();
```

Why the global options are switched off: **`IdentityOptions` is one object for the whole process**, so it cannot hold rules that differ per project. The rules that depend on the project (password, user name, email, phone, lockout) come from the project's settings through `IAuthFlowResolver` and are applied by `Auth2PasswordValidator`, `Auth2UserValidator`, `Auth2UserManager` (lockout) and the credential states. The password and role validators are **replaced** (`RemoveAll` then add), the user validator is added.

## The current project

Almost every method of the stores needs to know **which project** the request is about. The project comes from `IProjectContext` (from the client id of the token or request, or the selected project key), which also sets `IProjectScope`. The database context uses `IProjectScope` for its **query filters** ([database-tables.md](database-tables.md#how-to-read-the-model)).

| Situation | What happens |
|---|---|
| A request: the token or the `client_id` says which application it is | The project is resolved on first use; the stores work |
| Your own code outside a request (a job, a script) | There is no project: `ProjectNotFoundException`. Select one first (`IProjectSelection.ProjectKey`, or `IClientIdAccessor.ClientId`) |
| `IGlobalScope.Begin()` is active (seeding, global admin, jobs) | The filters are off. Role lookups do not need a project (see `RoleScopeHelper`) |
| A person without a `user_projects` row, per-project credentials | Reading credentials gives "no credentials"; **writing** them throws `InvalidOperationException` with `user_not_in_project` (joining needs proof of ownership, [authentication-flows.md](authentication-flows.md#joining-a-second-project-proof-of-ownership)) |

---

## `Auth2UserStore`

```csharp
public class Auth2UserStore(Auth2DbContext context, ICredentialStateProvider credentials, IProjectContext applications,
    IProjectScope scope, IOptions<Auth2ServerOptions> options, IdentityErrorDescriber? describer = null)
    : UserStore<AppUser, AppRole, Auth2DbContext, Guid, IdentityUserClaim<Guid>, IdentityUserRole<Guid>, AppUserLogin, AppUserToken, IdentityRoleClaim<Guid>, AppUserPasskey>
```

It inherits the standard Entity Framework user store and **overrides four groups** of methods.

### 1. Credentials: delegated to the credential state

Each of these methods asks `ICredentialStateProvider` for the strategy of the current project and forwards the call ([How credentials are stored](#how-credentials-are-stored)):

| Override | What it reads or writes |
|---|---|
| `GetPasswordHashAsync`, `SetPasswordHashAsync`, `HasPasswordAsync` | The password hash. `HasPasswordAsync` is "the hash is not null" |
| `GetSecurityStampAsync`, `SetSecurityStampAsync` | The security stamp (changing it signs out the other cookies) |
| `GetTwoFactorEnabledAsync`, `SetTwoFactorEnabledAsync` | Whether the authenticator-app second step is on |
| `GetLockoutEndDateAsync`, `SetLockoutEndDateAsync` | When the lock ends |
| `GetAccessFailedCountAsync`, `IncrementAccessFailedCountAsync`, `ResetAccessFailedCountAsync` | The failed-attempt counter |
| `GetLockoutEnabledAsync`, `SetLockoutEnabledAsync` | Whether this person can be locked |
| `SetAuthenticatorKeyAsync`, `GetAuthenticatorKeyAsync` | The TOTP key (encrypted at rest) |
| `ReplaceCodesAsync`, `RedeemCodeAsync`, `CountCodesAsync` | The recovery codes of the authenticator step |

`CreateAsync` first calls `BeforeUserCreated` on the strategy, so a pending membership row is saved **in the same transaction** as the new user ([`UserProjectCredentialState`](#userprojectcredentialstate)).

### 2. External logins and passkeys: they belong to the current project

| Override | Rule |
|---|---|
| `AddLoginAsync`, `RemoveLoginAsync`, `GetLoginsAsync`, `FindByLoginAsync` | First require a current project (`applications.GetRequiredAsync`), then use the standard code. The query filter on `user_logins` then shows only this project's links |
| `CreateUserLogin` | Creates an `AppUserLogin` with `ProjectId = scope.ProjectId`; without a project it throws "A login needs an application." |
| `FindByPasskeyIdAsync`, `FindPasskeyAsync`, `AddOrUpdatePasskeyAsync`, `RemovePasskeyAsync`, `GetPasskeysAsync` | Same: they require a project first |
| `CreateUserPasskey` | Sets `ProjectId` on the new `AppUserPasskey`; without a project it throws "A passkey needs an application." |

Effect: the same Google account (or the same passkey) in two projects is **two rows**, and a link or passkey of project A is never found while the request is about project B. Tables: `user_logins`, `user_passkeys` ([database-tables.md](database-tables.md#user_logins)).

### 3. Roles: looked up inside the current project

| Override | What it does |
|---|---|
| `FindRoleAsync(normalizedName)` | Finds the role by normalised name. **A role of the current project wins over a global role with the same name** (ordering: current project, then global, then others) |
| `GetRolesAsync(user)` | The names of the user's roles in the current project. **Global roles** (for example `GlobalAdmin`) are included only in the `_global` project and in single-project mode |
| `AddToRoleAsync`, `RemoveFromRoleAsync` | Do the standard work, then remember that roles changed |
| `UpdateAsync(user)` | Saves the user. **If roles were added or removed through this store**, the access version is raised **after** the save: in single-project mode `projects.access_version + 1`, otherwise `user_projects.user_access_version + 1` of this person in this project |

The last point matters: the access version (`pv` in tokens) is what makes caches and the Client's cached access data notice a change. Raising it here means **any** code that calls `UserManager.AddToRoleAsync` (not only `IAccessService`) keeps the caches honest ([client-current-user.md](client-current-user.md#how-the-access-data-is-found-iauth2accessprovider)).

---

## `Auth2RoleStore`

```csharp
public class Auth2RoleStore(Auth2DbContext context, IProjectScope scope, IProjectContext applications, IdentityErrorDescriber? describer = null)
    : RoleStore<AppRole, Auth2DbContext, Guid, IdentityUserRole<Guid>, IdentityRoleClaim<Guid>>
```

One override: `FindByNameAsync(normalizedName)`. It makes sure the project is resolved (`RoleScopeHelper`), then orders the matches **current project first, global role second**. So `roleManager.FindByNameAsync("Editor")` returns the project's `Editor` even when a global role with the same name exists. Roles are created with the `ProjectId` you set; a role with no `ProjectId` is a **global** role. Table: `roles` ([database-tables.md](database-tables.md#roles)).

## `Auth2RoleValidator`

The standard validator rejects a role name that exists anywhere. Auth2 needs the opposite: **the same name is fine in two projects, but not twice in one project**.

```csharp
if (string.IsNullOrWhiteSpace(role.Name)) fail InvalidRoleName
duplicate = db.Roles.IgnoreQueryFilters().Any(r => r.NormalizedName == normalized && r.ProjectId == role.ProjectId && r.Id != role.Id)
```

It reads with `IgnoreQueryFilters()` **on purpose**: the rule must hold even when a global scope is active (and for global roles, where `ProjectId` is `null`, a second global role with the same name is a duplicate). The database backs it with a unique index on `(project_id, normalized_name)` that treats nulls as equal (`NULLS NOT DISTINCT`).

## `RoleScopeHelper` (internal)

`EnsureProjectAsync(applications, scope, ct)`: if `scope.ProjectId` is already set it does nothing; otherwise it asks `IProjectContext` to resolve the project. If there is **no** project and a **global scope is active** (developer code, seeding), it swallows the `ProjectNotFoundException` and lets the role lookup run without a project (a global lookup). Without a global scope the exception is thrown. It exists so that both `Auth2RoleStore.FindByNameAsync` and `Auth2UserStore.FindRoleAsync` behave the same.

---

## How credentials are stored

A person's password, two-factor state, lockout state and recovery codes can live in two places. Which one is used is decided **per request** by `CredentialStateProvider`, from the effective credential scope of the project ([credential-scope.md](credential-scope.md)):

| Credential scope | Strategy | Where the data is |
|---|---|---|
| `PerProject` (default) | `UserProjectCredentialState` | the `user_projects` row of this person in this project; recovery codes in `user_project_recovery_codes` |
| `Shared`, and always in single-project mode | `UserRowCredentialState` | the `users` row; the TOTP key and recovery code hashes in `user_tokens` |

Both implement `ICredentialState` (in `Application/IdentityAbstractions.cs`), so the user store does not care which one it talks to.

### `ICredentialState`

| Method | Meaning |
|---|---|
| `GetPasswordHashAsync` / `SetPasswordHashAsync` | The password hash |
| `GetSecurityStampAsync` / `SetSecurityStampAsync` | The security stamp |
| `GetTwoFactorEnabledAsync` / `SetTwoFactorEnabledAsync` | TOTP on or off |
| `GetLockoutEndAsync` / `SetLockoutEndAsync` | End of the lock |
| `GetAccessFailedCountAsync` / `IncrementAccessFailedCountAsync` / `ResetAccessFailedCountAsync` | Failed attempts |
| `GetLockoutEnabledAsync` / `SetLockoutEnabledAsync` | Can this person be locked |
| `GetAuthenticatorKeyAsync` / `SetAuthenticatorKeyAsync` | The TOTP key, decrypted when read, encrypted when saved |
| `ReplaceRecoveryCodesAsync` / `RedeemRecoveryCodeAsync` / `CountRecoveryCodesAsync` | Recovery codes: only **hashes** are stored |
| `BeforeUserCreated` | Called before a new user is saved, so pending rows join the same transaction |

### `CredentialStateBase`

The shared parent. It holds the secret hasher (`ISecretHasher`, used for recovery codes) and a Data Protection protector with the purpose string `SystemX.Auth2.AuthenticatorKey`; `Protect` encrypts a TOTP key before it is stored and `Unprotect` decrypts it. A TOTP key is therefore never in clear text in the database, in either strategy.

### `UserRowCredentialState`

Used for `Shared` credentials and single-project mode. The person has **one set of credentials for the whole server**:

- Password hash, security stamp, two-factor flag, lockout end, failed count and lockout flag are plain properties of the `AppUser` row (`users` table).
- The TOTP key and the recovery codes are rows of `user_tokens` under the login provider `[Auth2]` with the names `AuthenticatorKey` (encrypted) and `RecoveryCodeHashes` (the hashes of the unused codes joined with `;`).
- Redeeming a recovery code removes its hash from that list, so a code works once.
- Setting a value that is `null` removes the token row.

Use it when one password per person is what you want (a company with several internal applications and one directory).

### `UserProjectCredentialState`

Used for `PerProject` credentials (the default). The same person has **separate credentials in each project**: the same email can have different passwords, different two-factor settings and different lockout state in two projects.

- Everything lives in the `user_projects` row of `(user, current project)`. Recovery codes are rows of `user_project_recovery_codes` (hash, `used_at_utc`).
- **Reading with no row** returns "nothing": no password, no stamp, `TwoFactorEnabled = false`, lockout off. A login with a password from another project therefore fails.
- **Reading the security stamp** for a member who has no stamp yet (added by an admin) creates one on the fly, because Identity refuses to save a user without a stamp.
- **Writing for a new user** (the `AppUser` is not saved yet): the row is created in memory (`_pending`) and added to the context by `BeforeUserCreated`, so the user and the membership are saved **together** in one transaction. There is never a password without a membership row, and a failed validation leaves no rows.
- **Writing for an existing person without a row** throws `InvalidOperationException` (`user_not_in_project`). A person who already exists cannot be added to a second project by typing their email: they must **join** with proof of ownership.
- `ReplaceRecoveryCodesAsync` removes the person's old rows for this project and adds new ones (`CodeHash` from `ISecretHasher`); `RedeemRecoveryCodeAsync` finds an unused row whose hash matches and sets `used_at_utc`; `CountRecoveryCodesAsync` counts the unused ones.
- The TOTP key is stored encrypted in `user_projects.authenticator_key`.

This is the strategy behind the rule that makes Auth2 safe against *account pre-hijacking* (a stranger registering an existing email in a new project and setting their own password).

### `CredentialStateProvider`

```csharp
public async Task<ICredentialState> GetAsync(CancellationToken ct = default) =>
    await flow.GetCredentialScopeAsync(ct) == CredentialScope.PerProject ? perProject : userRow;
```

It is scoped and cheap: it only asks the flow resolver for the effective scope and returns one of the two registered strategies. Both strategies are registered as scoped services (`UserRowCredentialState`, `UserProjectCredentialState`).

---

## The managers (`Auth2Managers.cs`)

### `Auth2UserManager`

A `UserManager<AppUser>` with **four overrides, all about lockout**. Why only lockout: `IdentityOptions.Lockout` is global, but the lockout rules are project settings (`Lockout.LockoutEnabled`, `MaxFailedAttempts`, `LockoutMinutes`, `LockoutForNewUsers`; [server-settings.md](server-settings.md)). Everything else Identity needs per project is done by the validators and the stores.

| Override | Behaviour |
|---|---|
| `CreateAsync(user)` | Before saving, sets the new person's lockout flag to `LockoutEnabled && LockoutForNewUsers` of the current project |
| `GetLockoutEnabledAsync(user)` | `true` only when the project has lockout **on** and the person's own flag is on |
| `AccessFailedAsync(user)` | If lockout is off for the project or the person, nothing happens. Otherwise it adds one failed attempt; when the count reaches `MaxFailedAttempts` it sets the lock end to *now + `LockoutMinutes`* and resets the counter, then saves the user |

Wrong passwords, wrong one-time codes, wrong authenticator codes and a wrong current password all end up in `AccessFailedAsync`, so they all count ([server-settings.md](server-settings.md#how-some-settings-work-together)). The clock is `TimeProvider`, so tests control it.

### `Auth2RoleManager`

A plain `RoleManager<AppRole>`: it only fixes the generic type arguments and passes the injected store, validators and describer. All role behaviour is in `Auth2RoleStore` and `Auth2RoleValidator`.

### `Auth2SignInManager`

A plain `SignInManager<AppUser>`. It is **thin on purpose**: login rules (allowed methods, lockout, confirmation, two-factor, single active session, external logins) are not in the sign-in manager but in `ILoginService` and `IExternalLoginService`, so they are the same for every entry point ([authentication-flows.md](authentication-flows.md)). The sign-in manager is still registered because ASP.NET Core Identity's passkey and two-factor helpers (`MakePasskeyCreationOptionsAsync`, `PerformPasskeyAssertionAsync`, ...) live on it and are used by `PasskeyService`.

---

## Examples

**Create a person and a password in the current project** (inside a request, or after `IProjectSelection.ProjectKey = "papertrade"`):

```csharp
var user = new AppUser { UserName = "sara", Email = "sara@example.com" };
var result = await userManager.CreateAsync(user, "Very-Secret-Pass-1!");
// per-project credentials: the membership row and the password are saved with the user, in one transaction
```

**Give a role** (the access version is raised for you, so the Client's cache refreshes):

```csharp
await userManager.AddToRoleAsync(user, "Editor");   // finds "Editor" in the current project first, then a global one
```

**Read where a password is** (no code needed): `PerProject` → `user_projects.password_hash` of the project; `Shared` → `users.password_hash`.

## Common problems

| Symptom | Cause and fix |
|---|---|
| `ProjectNotFoundException: No client id or project selection is available` | The code runs outside a request and no project is selected. Set `IProjectSelection.ProjectKey` (or `IClientIdAccessor.ClientId`) first, or run inside `IGlobalScope.Begin()` for global work |
| `InvalidOperationException: user_not_in_project` | You set a password, stamp, two-factor or lockout value for a person who has no `user_projects` row (per-project credentials). Make them join first |
| A role added with `UserManager` is not seen by the Client | It is: the store raises the access version on `UpdateAsync`. Make sure `UpdateAsync` ran (the standard manager calls it) and that the token is newer than the change, see the cache rules |
| The same email has different passwords in two projects | That is `PerProject` credentials working as intended ([credential-scope.md](credential-scope.md)) |
| "A login needs an application." | You called a login method with no project; select one |
| Two roles with the same name in one project | The validator and the unique index refuse it; across projects it is allowed |

## How to implement or extend

- **A new per-person value that must depend on the credential scope** (for example a "password changed at" date): add it to `ICredentialState`, implement it in **both** `UserRowCredentialState` (a property or a `user_tokens` entry) and `UserProjectCredentialState` (a column of `user_projects`), and call it from an `Auth2UserStore` override. Add a migration for the column ([database-tables.md](database-tables.md#changing-a-table)).
- **A new rule that depends on the project**: do not put it in `IdentityOptions` (global). Read it from `IAuthFlowResolver.GetSettingsAsync()` in a validator or manager like the existing ones.
- **A new project-owned Identity table**: give it a `ProjectId`, a query filter in `Auth2DbContext`, and override the store methods that touch it so that they require a project first (like logins and passkeys).
- **Tests** (`tests/SystemX.Auth2.Server.IntegrationTests`): `PerProjectCredentialTests`, `SharedCredentialTests` and `SingleModeIdentityTests` in `IdentityTests.cs` (the three credential layouts, lockout), `RolesAndRulesTests` (roles per project, the project rules), and `PasskeyStoreTests` in `ExternalIdentityDataTests.cs` (the per-project passkey store).

## Related pages

- [credential-scope.md](credential-scope.md)
- [database-tables.md](database-tables.md)
- [authentication-flows.md](authentication-flows.md)
- [authorization.md](authorization.md)
- [server-settings.md](server-settings.md)
- [architecture.md](architecture.md)
- [Documentation index](index.md)
