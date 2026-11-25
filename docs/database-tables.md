# Database tables

## What this is

Every table that `Auth2DbContext` creates in PostgreSQL, what it holds, who writes it, who reads it and when its rows go away. There are 33 tables: the ones ASP.NET Core Identity needs, the OpenIddict ones, and the Auth2 ones (projects, access model, one-time codes, replay protection, key ring, audit, jobs, branding).

## When to use it

- You look into the database (pgAdmin, SQL) and want to know what a table is.
- You write a report, a backup or a migration.
- You change the data model: read [How to read the model](#how-to-read-the-model) and [Changing a table](#changing-a-table) first.

## How to read the model

**Names.** Tables and columns are `snake_case` (`user_projects.joined_at_utc`). Times are `timestamptz` and end in `_utc`. Settings and free data are `jsonb`. Ids are GUIDs made by the application.

**Project, not application.** A *project* is the tenant (`projects`); an *application* is a client of a project (`applications`, which **is** the OpenIddict application table). Most data belongs to a project and carries `project_id`. See [concepts.md](concepts.md).

**Query filters (the isolation rule).** Tables that belong to one project have an EF Core global query filter on `project_id`: a query only sees the rows of the **current project**, and sees nothing when no project is selected. Only code inside `IGlobalScope.Begin()` (seeding, global admin, background jobs) sees all rows. Filtered tables: `roles` (the project's roles **and** global roles), `user_projects`, `user_project_recovery_codes`, `user_logins`, `user_passkeys`, `permissions`, `user_permissions`, `groups`, `external_group_mappings`, `audit_events`, `otp_codes`, `branding_assets`, `email_templates`. Join tables (`user_roles`, `role_permissions`, `user_groups`, `group_roles`, `group_permissions`) have no filter of their own: they are reached through a filtered parent.

**Deletes.** All project-owned rows are removed by a **cascade** when the project is deleted.

**The snapshot of the model.** `Persistence/Migrations/Auth2DbContextModelSnapshot.cs` is the file EF Core writes next to every migration. It is the model **as the last migration left it**, so the next `dotnet ef migrations add` can compute what changed. It is not read at run time and it is not a database table. (The "snapshot" of the key ring is something else: [key-ring.md](key-ring.md#how-the-snapshot-works).) The table `__EFMigrationsHistory` is EF's own list of applied migrations.

## Map

| Group | Tables |
|---|---|
| Projects and applications | [`projects`](#projects), [`applications`](#applications), [`user_projects`](#user_projects), [`user_project_recovery_codes`](#user_project_recovery_codes) |
| Identity | [`users`](#users), [`roles`](#roles), [`user_roles`](#user_roles), [`user_claims`](#user_claims-and-role_claims), [`role_claims`](#user_claims-and-role_claims), [`user_logins`](#user_logins), [`user_tokens`](#user_tokens), [`user_passkeys`](#user_passkeys) |
| Access model | [`permissions`](#permissions), [`user_permissions`](#user_permissions), [`role_permissions`](#role_permissions), [`groups`](#groups), [`user_groups`](#user_groups), [`group_roles`](#group_roles), [`group_permissions`](#group_permissions), [`external_group_mappings`](#external_group_mappings) |
| OpenIddict | [`openiddict_authorizations`](#openiddict_authorizations), [`openiddict_tokens`](#openiddict_tokens), [`openiddict_scopes`](#openiddict_scopes) (and `applications`) |
| Security and operations | [`key_ring`](#key_ring), [`data_protection_keys`](#data_protection_keys), [`otp_codes`](#otp_codes), [`password_history`](#password_history), [`replay_entries`](#replay_entries), [`server_values`](#server_values), [`audit_events`](#audit_events), [`job_runs`](#job_runs) |
| Content | [`branding_assets`](#branding_assets), [`email_templates`](#email_templates) |

```
projects ─┬─ applications ── openiddict_tokens / openiddict_authorizations (by application_id)
          ├─ user_projects ──── users ─┬─ user_roles ── roles ── role_permissions ── permissions
          │   (membership +            ├─ user_permissions ──────────────────────────┘
          │    credentials)            ├─ user_groups ── groups ─┬─ group_roles ── roles
          │                            ├─ user_logins            └─ group_permissions ── permissions
          │                            ├─ user_passkeys
          │                            └─ otp_codes, user_project_recovery_codes
          └─ audit_events, external_group_mappings, branding_assets, email_templates, replay_entries
server-wide: key_ring, data_protection_keys, server_values, job_runs, openiddict_scopes
```

---

## Projects and applications

### projects

**What it is.** The tenant: one product or customer (for example `PaperTrade`). `_global` is the built-in project of the developers and global admins.
**Key columns.** `project_key` (unique, never changes, not a secret), `name`, `is_enabled` (false blocks every sign-in and token of the project), `settings_json` (jsonb: the project settings, see [server-settings.md](server-settings.md)), `settings_version` and `access_version` (raised on every settings change / access change; caches and the `pv` token claim depend on them), `xmin` (the PostgreSQL row version, used as the concurrency token).
**Written by** the seeder, `create-project`, the admin API and pages. **Deleted** only by an admin; the delete cascades to all project data.
<!-- columns:projects:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `access_version` | `integer` | no |
| `created_at_utc` | `timestamptz` | no |
| `description` | `text` | yes |
| `is_enabled` | `boolean` | no |
| `name` | `character varying(200)` | no |
| `project_key` | `character varying(64)` | no |
| `settings_json` | `jsonb` | no |
| `settings_version` | `integer` | no |
| `updated_at_utc` | `timestamptz` | no |
| `xmin` | `xid` | no |

Primary key: `id`.

</details>
<!-- columns:projects:end -->

### applications

**What it is.** One **client** of a project (for example `papertrade-api`). It is the OpenIddict application table, extended with Auth2 columns. OpenIddict owns `client_id` (unique over the whole server), the hashed `client_secret`, `client_type`, `redirect_uris`, `post_logout_redirect_uris`, `permissions` (the endpoints, grant types and **scopes** the client may use), `requirements` and `consent_type`.
**Auth2 columns.** `project_id` (cascade), `is_enabled` (blocks only this application), `name` (unique inside the project), `settings_json` (jsonb: UI mode, token lifetimes, CORS origins, per-application overrides, see [server-settings.md](server-settings.md#application-settings-clientapplicationsettings)), `settings_version`, `created_at_utc`, `updated_at_utc`.
**Rule.** Rows are created, changed and deleted only through `IOpenIddictApplicationManager` (the `ApplicationService`), never with SQL. The client secret is shown once when created or rotated and is stored hashed.
<!-- columns:applications:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `application_type` | `character varying(50)` | yes |
| `client_id` | `character varying(100)` | yes |
| `client_secret` | `text` | yes |
| `settings_json` | `jsonb` | no |
| `client_type` | `character varying(50)` | yes |
| `concurrency_token` | `character varying(50)` | yes |
| `consent_type` | `character varying(50)` | yes |
| `created_at_utc` | `timestamptz` | no |
| `display_name` | `text` | yes |
| `display_names` | `text` | yes |
| `is_enabled` | `boolean` | no |
| `json_web_key_set` | `text` | yes |
| `name` | `character varying(200)` | no |
| `permissions` | `text` | yes |
| `post_logout_redirect_uris` | `text` | yes |
| `project_id` | `uuid` | no |
| `properties` | `text` | yes |
| `redirect_uris` | `text` | yes |
| `requirements` | `text` | yes |
| `settings` | `text` | yes |
| `settings_version` | `integer` | no |
| `updated_at_utc` | `timestamptz` | no |

Primary key: `id`.

</details>
<!-- columns:applications:end -->

### user_projects

**What it is.** "This person belongs to this project" **and** the person's credentials in that project when credentials are per project ([credential-scope.md](credential-scope.md)). One person can have a row in many projects; a person with no row cannot sign in to that project. Not used in single-project mode.
**Key columns.** `status` (`Active` or `Suspended`: blocked in this project only), `joined_at_utc`, `last_login_utc`; credentials: `password_hash`, `password_changed_at_utc`, `security_stamp`, `two_factor_enabled`, `authenticator_key` (the TOTP key, encrypted with Data Protection), `access_failed_count`, `lockout_enabled`, `lockout_end`; access: `user_access_version` (raised when this person's roles, permissions or groups change), `active_session_id` (the current session when `SingleActiveSession` is on).
**Written by** registering, joining with proof of ownership, an external first sign-in, and the admin. **Primary key** `(user_id, project_id)`; both columns cascade.
<!-- columns:user_projects:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `user_id` | `uuid` | no |
| `project_id` | `uuid` | no |
| `access_failed_count` | `integer` | no |
| `active_session_id` | `text` | yes |
| `authenticator_key` | `text` | yes |
| `joined_at_utc` | `timestamptz` | no |
| `last_login_utc` | `timestamptz` | yes |
| `lockout_enabled` | `boolean` | no |
| `lockout_end` | `timestamptz` | yes |
| `password_changed_at_utc` | `timestamptz` | yes |
| `password_hash` | `text` | yes |
| `security_stamp` | `text` | yes |
| `status` | `character varying(32)` | no |
| `two_factor_enabled` | `boolean` | no |
| `user_access_version` | `integer` | no |

Primary key: `user_id`, `project_id`.

</details>
<!-- columns:user_projects:end -->

### user_project_recovery_codes

**What it is.** The one-time **recovery codes** of the authenticator-app two-factor login of one person in one project. A person who lost the phone types one code instead of the TOTP code.
**How it works.** When two-factor is switched on, or the codes are generated again, the old rows of that person and project are removed and new rows are written. Only a **hash** of each code is stored (`code_hash`); the clear codes are shown to the user once. Using a code sets `used_at_utc`; a used code never works again. Recovery codes are per project like the TOTP key. With `Shared` credentials (and in single mode) the hashes are kept in `user_tokens` instead (see below).
**Cascade** with the user and the project.
<!-- columns:user_project_recovery_codes:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `code_hash` | `text` | no |
| `project_id` | `uuid` | no |
| `used_at_utc` | `timestamptz` | yes |
| `user_id` | `uuid` | no |

Primary key: `id`.

</details>
<!-- columns:user_project_recovery_codes:end -->

---

## Identity (ASP.NET Core Identity, extended)

### users

**What it is.** One real **person**, once on the whole server. User name and email are unique server-wide (a person with the same email in two projects is one row here and two rows in `user_projects`).
**Identity columns.** `user_name`, `normalized_user_name`, `email`, `normalized_email`, `email_confirmed`, `phone_number`, `phone_number_confirmed`, `concurrency_stamp`. **Auth2 columns.** `first_name`, `last_name`, `display_name`, `preferred_culture` (the language of emails and pages for this person), `is_active` (false disables the person everywhere), `created_at_utc`, `profile` (free jsonb).
**Credentials.** `password_hash`, `password_changed_at_utc` (when the password was last set; used by `PasswordExpiryDays`), `security_stamp`, `two_factor_enabled`, `lockout_*` and `access_failed_count` live here **only** when credentials are shared (`CredentialScope.Shared`, and always in single mode). With the default `PerProject` scope they are in [`user_projects`](#user_projects) and these columns stay empty.
<!-- columns:users:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `access_failed_count` | `integer` | no |
| `concurrency_stamp` | `text` | yes |
| `created_at_utc` | `timestamptz` | no |
| `display_name` | `character varying(200)` | yes |
| `email` | `character varying(256)` | yes |
| `email_confirmed` | `boolean` | no |
| `first_name` | `character varying(100)` | yes |
| `is_active` | `boolean` | no |
| `last_name` | `character varying(100)` | yes |
| `lockout_enabled` | `boolean` | no |
| `lockout_end` | `timestamptz` | yes |
| `normalized_email` | `character varying(256)` | yes |
| `normalized_user_name` | `character varying(256)` | yes |
| `password_changed_at_utc` | `timestamptz` | yes |
| `password_hash` | `text` | yes |
| `phone_number` | `text` | yes |
| `phone_number_confirmed` | `boolean` | no |
| `preferred_culture` | `character varying(16)` | yes |
| `profile` | `jsonb` | yes |
| `security_stamp` | `text` | yes |
| `two_factor_enabled` | `boolean` | no |
| `user_name` | `character varying(256)` | yes |

Primary key: `id`.

</details>
<!-- columns:users:end -->

### roles

**What it is.** A role such as `Admin` or `Editor`. `project_id` is **null for a global role** (`GlobalAdmin`, `Developer`). Names are unique per project (a global role and a project role can share a name; the project's wins in a lookup).
**Auth2 columns.** `description`, `is_system` (built-in, cannot be deleted), `application_id` (the application that pushed the role by sync, or null when an admin made it).
**Written by** admins and by the client sync ([permission-sync.md](permission-sync.md)). `GlobalAdmin` and `Developer` only count in the `_global` project. The query filter shows the project's roles **and** the global ones.
<!-- columns:roles:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `application_id` | `uuid` | yes |
| `concurrency_stamp` | `text` | yes |
| `description` | `text` | yes |
| `is_system` | `boolean` | no |
| `name` | `character varying(256)` | yes |
| `normalized_name` | `character varying(256)` | yes |
| `project_id` | `uuid` | yes |

Primary key: `id`.

</details>
<!-- columns:roles:end -->

### user_roles

**What it is.** The standard Identity join: this user has this role. `(user_id, role_id)`. A role belongs to one project (or is global), so the role decides the project the assignment counts in. The roles of a user are read when a token is made and when the session pages need them.
<!-- columns:user_roles:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `user_id` | `uuid` | no |
| `role_id` | `uuid` | no |

Primary key: `user_id`, `role_id`.

</details>
<!-- columns:user_roles:end -->

### user_claims and role_claims

**What they are.** The standard Identity tables for extra claims of a user or of a role. **Auth2 does not write them today**: roles come from `user_roles` and the access model, not from claims rows. They exist so that code written for ASP.NET Core Identity (and `UserManager.AddClaimAsync`) keeps working. Both cascade with their owner.
<!-- columns:user_claims:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `integer` | no |
| `claim_type` | `text` | yes |
| `claim_value` | `text` | yes |
| `user_id` | `uuid` | no |

Primary key: `id`.

</details>
<!-- columns:user_claims:end -->
<!-- columns:role_claims:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `integer` | no |
| `claim_type` | `text` | yes |
| `claim_value` | `text` | yes |
| `role_id` | `uuid` | no |

Primary key: `id`.

</details>
<!-- columns:role_claims:end -->

### user_logins

**What it is.** The link between a person and an **external identity**: "this user is `sub` 1234 at the provider `google`". Social login, OpenID Connect, SAML and LDAP all use it.
**Key.** `(project_id, login_provider, provider_key)`. `login_provider` is the provider **name** inside the project; `provider_key` is the stable id at the provider (OIDC `sub`, SAML NameID, LDAP `entryUUID` / `objectGUID`), never a user name or DN. The same Google account in two projects is two rows. Filtered by project. Details: [external-identity.md](external-identity.md#data-model).
<!-- columns:user_logins:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `project_id` | `uuid` | no |
| `login_provider` | `character varying(64)` | yes |
| `provider_key` | `character varying(512)` | yes |
| `provider_display_name` | `text` | yes |
| `user_id` | `uuid` | no |

Primary key: `project_id`, `login_provider`, `provider_key`.

</details>
<!-- columns:user_logins:end -->

### user_tokens

**What it is.** The standard Identity table for named values of a user. Auth2 uses it for **shared credentials only** (the same cases as the credential columns in `users`): under the login provider `[Auth2]` it keeps `AuthenticatorKey` (the TOTP key, encrypted) and `RecoveryCodeHashes` (the hashes, separated by `;`). With per-project credentials these values are in `user_projects` and `user_project_recovery_codes`. Cascades with the user.
<!-- columns:user_tokens:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `user_id` | `uuid` | no |
| `login_provider` | `text` | yes |
| `name` | `text` | yes |
| `project_id` | `uuid` | yes |
| `value` | `text` | yes |

Primary key: `user_id`, `login_provider`, `name`.

</details>
<!-- columns:user_tokens:end -->

### user_passkeys

**What it is.** A **passkey** (WebAuthn credential) of a person. **Key** `(project_id, credential_id)`: a passkey made in one project is never found in another. The authenticator model id is its own column (`aaguid`, read at registration and used by the block list); the public key, the signature counter, the transports, the backup flags and the name are in the `data` jsonb column. Filtered by project. Details: [passkeys.md](external-identity/passkeys.md).
<!-- columns:user_passkeys:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `project_id` | `uuid` | no |
| `credential_id` | `bytea` | yes |
| `aaguid` | `uuid` | yes |
| `user_id` | `uuid` | no |
| `data` | `jsonb` | no |

Primary key: `project_id`, `credential_id`.

</details>
<!-- columns:user_passkeys:end -->

---

## Access model

### permissions

**What it is.** A fine-grained right of a project, such as `orders.read`. `key` is lowercase and unique per project. `source` is `Manual` (an admin made it) or `Sync` (a client pushed it); `application_id` is the pushing application; `is_obsolete` marks a permission that is no longer used (the row is kept); `display_name`, `description` and `group` are for screens. See [authorization.md](authorization.md).
<!-- columns:permissions:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `application_id` | `uuid` | yes |
| `description` | `text` | yes |
| `display_name` | `text` | yes |
| `group` | `text` | yes |
| `is_obsolete` | `boolean` | no |
| `key` | `character varying(100)` | no |
| `project_id` | `uuid` | no |
| `source` | `character varying(16)` | no |

Primary key: `id`.

</details>
<!-- columns:permissions:end -->

### user_permissions

**What it is.** A permission granted **directly** to a person, with `project_id`, `granted_at_utc` and `granted_by`. A user's effective permissions are these, plus those of their roles, plus those of their groups (and the groups' roles).
<!-- columns:user_permissions:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `user_id` | `uuid` | no |
| `permission_id` | `uuid` | no |
| `granted_at_utc` | `timestamptz` | no |
| `granted_by` | `uuid` | yes |
| `project_id` | `uuid` | no |

Primary key: `user_id`, `permission_id`.

</details>
<!-- columns:user_permissions:end -->

### role_permissions

**What it is.** The permissions of a role: `(role_id, permission_id)`. Written by admins and by role sync.
<!-- columns:role_permissions:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `role_id` | `uuid` | no |
| `permission_id` | `uuid` | no |

Primary key: `role_id`, `permission_id`.

</details>
<!-- columns:role_permissions:end -->

### groups

**What it is.** A named set of people that can carry roles and permissions. `name` is a lowercase key, unique per project; `application_id` is the pushing application (group sync is off by default); `display_name` and `description` are for screens.
<!-- columns:groups:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `application_id` | `uuid` | yes |
| `description` | `text` | yes |
| `display_name` | `text` | yes |
| `name` | `character varying(100)` | no |
| `project_id` | `uuid` | no |

Primary key: `id`.

</details>
<!-- columns:groups:end -->

### user_groups

**What it is.** Membership: `(user_id, group_id)`. Set by admins, by the registration defaults ([server-settings.md](server-settings.md#registration)) and by external group mapping.
<!-- columns:user_groups:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `user_id` | `uuid` | no |
| `group_id` | `uuid` | no |

Primary key: `user_id`, `group_id`.

</details>
<!-- columns:user_groups:end -->

### group_roles

**What it is.** The roles a group gives to its members: `(group_id, role_id)`.
<!-- columns:group_roles:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `group_id` | `uuid` | no |
| `role_id` | `uuid` | no |

Primary key: `group_id`, `role_id`.

</details>
<!-- columns:group_roles:end -->

### group_permissions

**What it is.** The permissions a group gives to its members: `(group_id, permission_id)`.
<!-- columns:group_permissions:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `group_id` | `uuid` | no |
| `permission_id` | `uuid` | no |

Primary key: `group_id`, `permission_id`.

</details>
<!-- columns:group_permissions:end -->

### external_group_mappings

**What it is.** One rule that turns a **group, claim or attribute from an external provider** into a role, group or permission **of the same project** at sign-in ([group-mapping.md](external-identity/group-mapping.md)). `provider_name` null means every provider of the project; `source_type` (Group, Claim, Attribute), `source_name` (for claims and attributes), `match_type` (Exact or Prefix, no regex), `source_value`, `target_type` (Role, Group, Permission) and `target_id`. Filtered by project.
<!-- columns:external_group_mappings:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `match_type` | `character varying(16)` | no |
| `project_id` | `uuid` | no |
| `provider_name` | `character varying(64)` | yes |
| `source_name` | `character varying(200)` | no |
| `source_type` | `character varying(16)` | no |
| `source_value` | `character varying(500)` | no |
| `target_id` | `uuid` | no |
| `target_type` | `character varying(16)` | no |

Primary key: `id`.

</details>
<!-- columns:external_group_mappings:end -->

---

## OpenIddict

OpenIddict is the OpenID Connect server. Its stores use the Auth2 entity classes with GUID keys. The application table is described [above](#applications).

### openiddict_authorizations

**What it is.** A **permanent authorization**: "this user allowed this application these scopes" (what the consent page records, and what `Manage/Sessions` lists). Columns: `application_id`, `subject` (the user id), `type`, `status`, `scopes`, `creation_date`, `properties`. A new login with `SingleActiveSession` revokes the older ones; disabling a project or application revokes theirs. The job `token-pruning` removes authorizations older than 14 days that are no longer valid.
<!-- columns:openiddict_authorizations:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `application_id` | `uuid` | yes |
| `concurrency_token` | `character varying(50)` | yes |
| `creation_date` | `timestamp with time zone` | yes |
| `properties` | `text` | yes |
| `scopes` | `text` | yes |
| `status` | `character varying(50)` | yes |
| `subject` | `character varying(400)` | yes |
| `type` | `character varying(50)` | yes |

Primary key: `id`.

</details>
<!-- columns:openiddict_authorizations:end -->

### openiddict_tokens

**What it is.** One entry per token the Server **issued** (authorization codes, refresh tokens and the entries of access and identity tokens): `application_id`, `authorization_id`, `subject`, `type`, `status`, `creation_date`, `expiration_date`, `redemption_date`, `reference_id` (unique), `payload`, `properties`. The entries make **revocation** work (userinfo, introspection, refresh) and let the Server notice that a refresh token was used twice (the whole chain is then revoked). Access tokens are JWTs, so an API that only checks the signature accepts a revoked one until it expires ([decisions.md](decisions.md)). The job `token-pruning` removes entries older than 14 days that are no longer valid (expired, redeemed or revoked). Upstream tokens of external providers are **not** stored here.
<!-- columns:openiddict_tokens:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `application_id` | `uuid` | yes |
| `authorization_id` | `uuid` | yes |
| `concurrency_token` | `character varying(50)` | yes |
| `creation_date` | `timestamp with time zone` | yes |
| `expiration_date` | `timestamp with time zone` | yes |
| `payload` | `text` | yes |
| `properties` | `text` | yes |
| `redemption_date` | `timestamp with time zone` | yes |
| `reference_id` | `character varying(100)` | yes |
| `status` | `character varying(50)` | yes |
| `subject` | `character varying(400)` | yes |
| `type` | `character varying(150)` | yes |

Primary key: `id`.

</details>
<!-- columns:openiddict_tokens:end -->

### openiddict_scopes

**What it is.** The scopes the server knows: the standard ones (`openid`, `profile`, `email`, `phone`, `roles`, `offline_access`), the `auth2.*` scopes that the seeder creates, and the **custom scopes** that clients push by sync (for example `reports.read`). A scope here is a *name that may be requested*; which application may request it is the `permissions` column of [`applications`](#applications). Server-wide, not per project.
<!-- columns:openiddict_scopes:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `concurrency_token` | `character varying(50)` | yes |
| `description` | `text` | yes |
| `descriptions` | `text` | yes |
| `display_name` | `text` | yes |
| `display_names` | `text` | yes |
| `name` | `character varying(200)` | yes |
| `properties` | `text` | yes |
| `resources` | `text` | yes |

Primary key: `id`.

</details>
<!-- columns:openiddict_scopes:end -->

---

## Security and operations

### key_ring

**What it is.** The signing and encryption keys of the Server, encrypted, shared by all instances. Full explanation: [key-ring.md](key-ring.md). Server-wide.
<!-- columns:key_ring:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `created_at_utc` | `timestamptz` | no |
| `not_after_utc` | `timestamptz` | no |
| `protected_pfx` | `text` | no |
| `purpose` | `character varying(16)` | no |
| `remove_at_utc` | `timestamptz` | yes |
| `retired_at_utc` | `timestamptz` | yes |
| `thumbprint` | `character varying(64)` | no |

Primary key: `id`.

</details>
<!-- columns:key_ring:end -->

### data_protection_keys

**What it is.** The key store of **ASP.NET Core Data Protection** (`id`, `friendly_name`, `xml`). The application name is fixed to `SystemX.Auth2`, so every instance reads the same keys. Data Protection protects: the sign-in cookies, anti-forgery tokens, confirmation and reset tokens, the sealed state of external sign-ins, the TOTP keys (`authenticator_key`), the secrets of external providers (client secrets, bind passwords, SAML keys), the SMTP password, and `key_ring.protected_pfx`.
**Important.** Never delete or truncate this table, and back it up with the database. Without these keys the values above cannot be read: users are signed out, secrets and the key ring become unreadable. The keys are unencrypted XML unless you protect them with a certificate in your own startup code ([security.md](security.md#keys-and-rotation)).
<!-- columns:data_protection_keys:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `integer` | no |
| `friendly_name` | `text` | yes |
| `xml` | `text` | yes |

Primary key: `id`.

</details>
<!-- columns:data_protection_keys:end -->

### otp_codes

**What it is.** **One-time codes** sent by email or SMS: `purpose` (`login`, `confirm_phone`, `join`, `password_reset`, and `change_phone_` plus a short hash of the new number, for the phone change page), `user_id`, `project_id`, `expires_at_utc`, `attempts` (wrong tries so far; too many closes the code), `consumed_at_utc`. Only a salted **hash** of the code is stored (`code_hash`). A new code closes the older open one, and "works once" is one conditional UPDATE, so two parallel requests cannot both succeed.
**Redis.** When `Auth2:Redis` is on, codes live in Redis and this table stays empty ([deployment-and-scaling.md](deployment-and-scaling.md)).
**Cleanup.** The job `otp-cleanup` deletes codes that expired or were used more than a day ago. Filtered by project.
<!-- columns:otp_codes:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `attempts` | `integer` | no |
| `code_hash` | `text` | no |
| `consumed_at_utc` | `timestamptz` | yes |
| `expires_at_utc` | `timestamptz` | no |
| `project_id` | `uuid` | no |
| `purpose` | `character varying(50)` | no |
| `user_id` | `uuid` | no |

Primary key: `id`.

</details>
<!-- columns:otp_codes:end -->

### password_history

**What it is.** The **hashes of the passwords a person used before**, kept when `Password.SaveOldPasswords` is on ([password-history.md](password-history.md)). A new password is compared with the current hash and with these rows, so people cannot go back to a recent password.
**Key columns.** `user_id` (cascade), `credential_project_id` (the project when credentials are per project; **`null` when they are shared**, so the history then belongs to the person; cascade with the project; it is not called `project_id` because the context fills a null `ProjectId` with the current project when it saves), `password_hash` (a real password hash from the same hasher; **never the plain text**), `saved_at_utc` (when the password was replaced).
**Written by** `Auth2UserStore.SetPasswordHashAsync` when a password is replaced (change, reset, any `UserManager` call): it adds the old hash and removes the oldest rows beyond `Password.OldPasswordsToKeep`. **Read by** the password validator and nothing else. **Not cleaned** by a job and not shown in the admin API; rows go away with the person or the project. Turning the setting off keeps the rows. Filtered: the rows of the current project **and** the rows of shared credentials. Index on `(user_id, credential_project_id, saved_at_utc)`.
<!-- columns:password_history:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `credential_project_id` | `uuid` | yes |
| `password_hash` | `text` | no |
| `saved_at_utc` | `timestamptz` | no |
| `user_id` | `uuid` | no |

Primary key: `id`.

</details>
<!-- columns:password_history:end -->

### replay_entries

**What it is.** A value that may be used **only once** until it expires. The primary key is the text `key` = *kind:provider:value*, for example `oidc-state:ext-<projectId>-google:<state>`, `saml-request:...`, `saml-assertion:...`, `saml-logout:...`. The Server inserts the key; if the insert finds the same key it is a **replay** (a copied sign-in answer or SAML assertion) and the sign-in is refused. It is in the database so that **every instance** sees it. `project_id` says which project, `expires_at_utc` when it can be removed. The job `otp-cleanup` deletes expired rows (the same key is also cleared when it is used again after it expired).
<!-- columns:replay_entries:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `key` | `character varying(400)` | yes |
| `expires_at_utc` | `timestamptz` | no |
| `project_id` | `uuid` | no |

Primary key: `key`.

</details>
<!-- columns:replay_entries:end -->

### server_values

**What it is.** A tiny server-wide key/value store for settings that an **admin changes at run time** without a deploy: `key` (primary key), `value` (text), `updated_at_utc`. Today it holds `passkeys.blocked_aaguids` (the authenticator models that cannot be registered, comma-separated; `GET` and `PUT /api/auth2/admin/passkeys/blocked-aaguids`). It is joined with `Auth2:Passkeys:BlockedAaguids` from the configuration.
<!-- columns:server_values:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `key` | `character varying(100)` | yes |
| `updated_at_utc` | `timestamptz` | no |
| `value` | `text` | no |

Primary key: `key`.

</details>
<!-- columns:server_values:end -->

### audit_events

**What it is.** The **audit trail**: who did what, when, from where. `action` (for example `login.failed`, `register.defaults_applied`, `keys.rotated`, `external.provider.created`), `actor_user_id` or `actor_client_id`, `target_type` and `target_id`, `project_id` (null for server-wide events), `application_id` (the client that did it), `ip_address`, `user_agent`, `data_json` (extra facts as jsonb). **Never holds secrets**: no passwords, tokens, codes or links.
**Cleanup.** The job `audit-retention` deletes events older than `Auth2:Jobs:AuditRetentionDays` (365; 0 keeps everything). Indexes on `occurred_at_utc` and `(project_id, occurred_at_utc)`. Filtered by project.
<!-- columns:audit_events:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `action` | `character varying(100)` | no |
| `actor_client_id` | `text` | yes |
| `actor_user_id` | `uuid` | yes |
| `application_id` | `uuid` | yes |
| `data_json` | `jsonb` | yes |
| `ip_address` | `text` | yes |
| `occurred_at_utc` | `timestamptz` | no |
| `project_id` | `uuid` | yes |
| `target_id` | `text` | yes |
| `target_type` | `text` | yes |
| `user_agent` | `text` | yes |

Primary key: `id`.

</details>
<!-- columns:audit_events:end -->

### job_runs

**What it is.** The **history of the maintenance jobs** (`otp-cleanup`, `audit-retention`, `token-pruning`, `key-removal`): `job_name`, `instance_id` (which Server instance ran it), `started_at_utc`, `finished_at_utc`, `success`, `error` (short text), `rows_affected`. Shown on `/admin/jobs`. A database advisory lock makes only one instance run a job at a time. The Server does not delete these rows.
<!-- columns:job_runs:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `error` | `character varying(1000)` | yes |
| `finished_at_utc` | `timestamptz` | yes |
| `instance_id` | `character varying(100)` | yes |
| `job_name` | `character varying(100)` | no |
| `rows_affected` | `integer` | no |
| `started_at_utc` | `timestamptz` | no |
| `success` | `boolean` | no |

Primary key: `id`.

</details>
<!-- columns:job_runs:end -->

---

## Content

### branding_assets

**What it is.** Images of a project stored **in PostgreSQL**: `kind` (`logo`, `logo-dark`, `favicon`, `background`), `content_type`, `data` (bytes), `sha256`, `size`, `updated_at_utc`. One row per `(project_id, kind)`. The admin upload accepts PNG, JPEG, WebP and ICO (checked by their first bytes, not by the file name) up to 512 KB. Filtered by project.
**Today** only the admin console reads and writes this table.
<!-- columns:branding_assets:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `content_type` | `character varying(100)` | no |
| `data` | `bytea` | no |
| `kind` | `character varying(64)` | no |
| `project_id` | `uuid` | no |
| `sha256` | `character varying(64)` | no |
| `size` | `bigint` | no |
| `updated_at_utc` | `timestamptz` | no |

Primary key: `id`.

</details>
<!-- columns:branding_assets:end -->

### email_templates

**What it is.** A custom email text per project, template name and culture: `name` (`verification`, `password-reset`, `otp`, `welcome`, `invite`), `culture` (for example `en`), `subject`, `body`, `updated_at_utc`. Unique on `(project_id, name, culture)`. Filtered by project.
**Today** only the admin console reads and writes this table. The messages that the Server sends still come from the language files `Resources/Emails.resx` ([localization.md](localization.md)).
<!-- columns:email_templates:start -->
<details><summary>All columns</summary>

| Column | Type | Null |
|---|---|---|
| `id` | `uuid` | no |
| `body` | `text` | no |
| `culture` | `character varying(16)` | no |
| `name` | `character varying(100)` | no |
| `project_id` | `uuid` | no |
| `subject` | `character varying(256)` | no |
| `updated_at_utc` | `timestamptz` | no |

Primary key: `id`.

</details>
<!-- columns:email_templates:end -->

---

## Questions

**Where is a user's password?** In `user_projects.password_hash` of the project (default), or in `users.password_hash` (shared credentials and single mode). Never both for one setup. See [credential-scope.md](credential-scope.md).

**Where are the sessions?** There is no session table. A session is the sign-in cookie plus the OpenIddict authorization and token rows of the user, and `user_projects.active_session_id` when `SingleActiveSession` is on.

**Where are secrets?** Client secrets: hashed in `applications.client_secret`. External provider secrets, the SMTP password and SAML keys: encrypted inside the `settings_json` of the project. TOTP keys: encrypted. One-time codes and recovery codes: hashed. Private keys of tokens: encrypted in `key_ring`. All encryption is Data Protection, so the key store is [`data_protection_keys`](#data_protection_keys).

**Which tables are server-wide (not per project)?** `users`, `projects`, `applications` (but each has a `project_id`), `openiddict_scopes`, `key_ring`, `data_protection_keys`, `server_values`, `job_runs`, and the global rows of `roles`.

## Changing a table

1. Change the entity in `Domain/Entities` (or the OpenIddict entity) and the mapping in `Persistence/Auth2DbContext.cs`.
2. If the table belongs to one project, add `project_id` and the **query filter**; a test checks the isolation.
3. Add a migration (`dotnet ef migrations add Name --project src/SystemX.Auth2.Server`). Review the generated SQL: data must be **kept** (write the data step by hand, like `ProjectsAndApplications` does).
4. Run `python3 tools/docs/tables.py` to refresh the column lists on this page, and describe the table in words.

## Related pages

- [concepts.md](concepts.md)
- [credential-scope.md](credential-scope.md)
- [key-ring.md](key-ring.md)
- [architecture.md](architecture.md)
- [security.md](security.md)
- [decisions.md](decisions.md)
- [Comparison and limits](comparison-and-limits.md)
- [Email and SMS](email-and-sms.md)
- [Authentication flows](authentication-flows.md)
- [Server reference](server-reference.md)
- [Seeding (first admin)](seeding.md)
- [Contributing workflow](contributing-workflow.md)
- [Documentation index](index.md)
- [Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)](identity-stores.md)
- [Current user and current client](client-current-user.md)
- [Enums reference](enums-reference.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Contracts reference](contracts-reference.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Error codes](error-codes.md)
