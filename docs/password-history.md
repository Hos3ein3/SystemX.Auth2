# Password history and password expiry

## What this is

Three password rules that a security policy often asks for, all **off by default**:

1. **Remember old passwords** (`Password.SaveOldPasswords`): when a person's password changes, the **hash** of the old one is saved in the table `password_history`.
2. **Do not let people go back** (`Password.AllowOldPasswordReuse`): with the history on, the new password must differ from the current one. With reuse **not** allowed it must also differ from every remembered old one. With reuse allowed it must differ only from the current one and the one used **right before** it.
3. **Change the password regularly** (`Password.PasswordExpiryDays`): a password older than this many days is refused at sign-in (`password_expired`) until the person chooses a new one.

A fourth setting, `Password.OldPasswordsToKeep`, says how many old passwords are remembered (1 to 24, default 5).

| Setting | Default | Meaning |
|---|---|---|
| `SaveOldPasswords` | `false` | Remember the hash of each replaced password, and refuse the **current** password as a new one |
| `OldPasswordsToKeep` | `5` | How many old passwords are remembered, not counting the current one. The oldest are forgotten. Range 1 to 24 |
| `AllowOldPasswordReuse` | `false` | `false`: refuse the current and every remembered password. `true`: refuse only the current one and the one right before it |
| `PasswordExpiryDays` | `0` | `0` = a password never expires. Otherwise the number of days (up to 3650) a password may be used |

All four are **global admin only** settings of the project ([server-settings.md](server-settings.md#password)).

## When to use it

- A rule of your customer, company or standard says "no reuse of the last N passwords" or "change your password every N days".
- You want to stop the habit of changing a password and changing it straight back.

About expiry: current guidance (for example NIST SP 800-63B) advises **against** forcing periodic changes without a reason, because people then pick weaker, predictable passwords. Use `PasswordExpiryDays` when a rule demands it, and prefer the history alone when you only want to stop reuse.

## How to enable it

Change the project settings with the admin API or the settings page ([admin-api.md](admin-api.md)):

```json
"password": {
  "saveOldPasswords": true,
  "oldPasswordsToKeep": 5,
  "allowOldPasswordReuse": false,
  "passwordExpiryDays": 90
}
```

Nothing else is needed. There is no migration step for people: the history starts empty when you turn it on, and fills as passwords change.

## How it works

### What is saved, and when

A password is **replaced** when a person changes it (`POST /api/auth2/password/change`, the page `/Manage/ChangePassword`), resets it (the forgot-password flow) or when any other server code replaces it through `UserManager`. All of these end in one place, `Auth2UserStore.SetPasswordHashAsync`, and it does three things when the new hash differs from the old one:

1. If `SaveOldPasswords` is on and there **was** an old hash, it hands the old hash to `IPasswordHistoryService.RememberAsync`, which adds a row and removes the oldest rows beyond `OldPasswordsToKeep`.
2. It stores the new hash.
3. It records **when the password was set** (`password_changed_at_utc`), used by the expiry.

What is **not** a change: the first password of a new person (nothing to remember), and a **rehash**. Identity sometimes stores the same password again with a stronger hash while it checks a sign-in; `Auth2UserManager.CheckPasswordAsync` marks that check, so the store neither saves a history row nor renews the date (test `ARehashWhileLoggingIn_IsNotAPasswordChange`).

What a row holds ([database-tables.md](database-tables.md#password_history)): the person, the **hash** (made by the same hasher as the real password; the plain text is never stored), when it was saved, and `credential_project_id`: the project when credentials are per project, `null` when they are shared ([credential-scope.md](credential-scope.md)). With shared credentials the history belongs to the person and works in every project; with per-project credentials each project has its own.

### What is checked, and when

`Auth2PasswordValidator` runs for **every** new password (register, change, reset). After the normal rules (length, characters, banned list) pass, it asks `IsReusedAsync`:

1. The new password is hashed-and-compared with the **current** hash: equal means refused. (Only when `SaveOldPasswords` is on. Without it changing to the same password is allowed, as before.)
2. Then it compares with the remembered old hashes, newest first: **all** of them (`OldPasswordsToKeep`) when reuse is not allowed, **only the newest one** when reuse is allowed.
3. A match gives the Identity error `password_reused`.

The checks use the real password hasher, so each comparison costs as much as a sign-in check (tens of milliseconds). That is why `OldPasswordsToKeep` is limited to 24 and defaults to 5.

**Example.** The person had `A`, then `B`, then `C` (now). Remembered: `B` (right before), `A` (older).

| New password | Reuse **not** allowed | Reuse allowed |
|---|---|---|
| `C` (the current one) | refused | refused |
| `B` (right before) | refused | refused |
| `A` (older) | refused | **allowed** |
| `D` (never used) | allowed | allowed |

If `OldPasswordsToKeep = 2` and the person then goes on `C→D→E`, the remembered ones are `D` and `C`; `A` and `B` are forgotten and may be used again.

### Expiry

`PasswordExpiryDays > 0` is checked at the **password sign-in** (the hosted page, the password grant, `IAuth2WebClientService.LoginAsync`), **after** the password was proved right and before the second factor. A password older than the limit answers **`password_expired`** and the person cannot sign in; they are told to use "Forgot password". A **wrong** password still answers the normal `invalid_credentials`, so the age reveals nothing to a guesser. Expiry does not count toward the lockout.

How old a password is: `password_changed_at_utc` of the current project's credentials (or of the user for shared credentials). A password **without a recorded date** (set before this feature, or by SQL) counts from the day the person **joined the project** (shared credentials: from the day the person was created). So turning expiry on can make old accounts expire at once; that is the meaning of the rule. The way out is the reset flow, which sets a new password and a new date.

Not affected by expiry: sign-in with an email or SMS code, an external provider, a directory (LDAP) password or a passkey; these are not the Server's password.

## How to use it

**Change a password (the user's view).** `POST /api/auth2/password/change` with a signed-in token:

```http
POST /api/auth2/password/change   Authorization: Bearer ...
{ "currentPassword": "Third-Pass-333!", "newPassword": "Second-Pass-22!" }

400 { "code": "password_reused", "message": "(the text of the error, localized)" }
```

The hosted page `/Manage/ChangePassword` shows the same message.

**An expired password.**

```http
POST /connect/token   grant_type=password&username=sara&password=...&client_id=...
400 { "error": "password_expired" ... }
```

With the Client package (Self UI):

```csharp
var result = await web.LoginAsync(new LoginRequest(model.Login, model.Password), model.RememberMe, ct);
if (!result.IsSuccess && result.Error!.Code == "password_expired")
    return RedirectToAction("ForgotPassword");   // your page: ask for the email, then api.ForgotPasswordAsync(new ForgotPasswordRequest(email))
```

The hosted sign-in page shows "Your password has expired. Use "Forgot password" to choose a new one." (English and Persian texts).

**Looking at the data.** `select * from password_history where user_id = '...' order by saved_at_utc desc;`. There is no admin API to list or clear a person's history. A person's rows go away when the person (or the project) is deleted.

## What is not done

- Turning `SaveOldPasswords` **off** keeps the saved rows (they are not deleted) and stops using them. Turning it on again uses the rows that are still there.
- No warning before a password expires ("your password ends in 7 days"). Only the refusal.
- No *minimum* age ("do not change twice in a day").
- The history is not shared between servers or exported; it is a table of the Server's database.
- Old hashes are as sensitive as current ones (same algorithm): protect the database and its backups like the `users` table.

## Common problems

| Problem | Cause and fix |
|---|---|
| Everybody got `password_expired` after I set `PasswordExpiryDays` | Their passwords are older than the limit (or have no date and the membership is older). Raise the value, or let people reset |
| "Change password" says `password_reused` for a password I never used | It is the **current** password (refused whenever the history is on), or it is one remembered from a reset |
| An old password is accepted although the history is on | `AllowOldPasswordReuse` is on (only the last one is refused), or `OldPasswordsToKeep` is smaller than you think, or the history was turned on after that password was replaced |
| `OldPasswordsToKeep` rejected | The range is 1 to 24 |
| Sign-in is slow after raising `OldPasswordsToKeep` | Each remembered hash is checked when a password is **changed** (not at sign-in); keep the number small |
| Expiry blocks a person who signs in with Google | It does not: only the Server's own password sign-in is checked |

## How to implement or extend

- `IPasswordHistoryService` (`Application/IdentityAbstractions.cs`) with `IsReusedAsync`, `RememberAsync` and `IsExpiredAsync`; the implementation is `PasswordHistoryService` (`Infrastructure/Auth/PasswordHistoryService.cs`). The user store calls `RememberAsync`, the password validator calls `IsReusedAsync`, the login service calls `IsExpiredAsync`.
- The date is part of the credential state: `ICredentialState.GetPasswordChangedAtAsync` / `SetPasswordChangedAtAsync`, implemented by both strategies ([identity-stores.md](identity-stores.md)). A new credential strategy must implement them.
- Why the column is `credential_project_id` and not `project_id`: the database context fills a null `ProjectId` with the current project when it saves, which would turn a shared-credentials row into a project row.
- To also forbid passwords from a breach list, add a rule in `Auth2PasswordValidator.Check` (a hook for the banned list exists).
- Tests (`tests/SystemX.Auth2.Server.IntegrationTests/PasswordHistoryTests.cs`): `PasswordHistoryTests` (off by default; hashed not raw; the current and old passwords refused; reuse allowed; only the last N kept; reset follows the rules; turning it on and off; per project; rehash is not a change), `PasswordExpiryTests` (too old blocks; the date is set; never expires when 0; no date counts from the membership; texts exist), `SharedPasswordHistoryTests` (shared credentials). Unit tests for the setting ranges are in `SettingsTests`.

## Related pages

- [server-settings.md](server-settings.md)
- [identity-stores.md](identity-stores.md)
- [database-tables.md](database-tables.md)
- [credential-scope.md](credential-scope.md)
- [authentication-flows.md](authentication-flows.md)
- [error-codes.md](error-codes.md)
- [security.md](security.md)
- [Documentation index](index.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Decisions (ADR list)](decisions.md)
