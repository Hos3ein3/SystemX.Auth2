# TASK: Apply the Project/Application model, ICurrentClient, and catalog source changes to SystemX.Auth2

You are working on SystemX.Auth2 (ASP.NET Core Identity + OpenIddict + PostgreSQL/Npgsql, Razor Pages + Minimal API server, NuGet client, Contracts project).
Read first, in this order: the main build specification, then the work packages (Admin CRUD, External Identity v3). This prompt OVERRIDES them wherever they conflict. Where a document still uses the old terms, apply the terms below. Do not stop to ask questions; if something is ambiguous, choose the simplest option consistent with this prompt and record it in docs/decisions.md.

## Working method (mandatory, for every part)

1. Change the code.
2. Add or update tests for the change.
3. Build and run ALL tests.
4. If anything fails, fix the CODE (change a test only if the test itself is wrong, and say why in docs/decisions.md). Rerun until everything passes.
5. Only when everything passes: rewrite the docs for that part. Docs have two sections: "How to use" and "How to implement/extend".
   Never write docs before the tests pass. Never claim that something builds or passes unless you ran it.

## PART 1 — Project vs Application (domain change)

### Terms

- Project = the tenant/product boundary (example: PaperTrade).
- Application = a client of a project (examples: papertrade-api, papertrade-mvc, papertrade-api-v2). An application IS the OpenIddict application. There is ONE table for it.
- The old `applications` table was both a project and a client. Split it into `projects` and the extended OpenIddict application table.

### Tables

- `projects`: Id, ProjectKey (unique, lowercase letters/digits/hyphens, IMMUTABLE after creation), Name, IsEnabled, SettingsJson (jsonb), timestamps, concurrency token.
- `applications` = the OpenIddict application table itself, extended through a custom entity:
  - Create `Application` inheriting from the OpenIddict EF Core application entity with Guid keys. Add: ProjectId (FK to `projects`, required), IsEnabled, Name (unique inside the project), SettingsJson (jsonb, client overrides), timestamps.
  - Also create custom authorization, scope and token entities with the same Guid key type, and register all four with `ReplaceDefaultEntities<TApplication, TAuthorization, TScope, TToken, TKey>()`. Register the same entities in the DbContext through `options.UseOpenIddict<...>()`.
  - There is NO separate `OpenIddictApplicationId` column and NO second client table.
  - Do not duplicate what OpenIddict already stores: client id, client secret, client type (confidential/public), redirect URIs, post-logout redirect URIs, permissions/allowed scopes, consent type, display name. Reuse those fields. SettingsJson only holds what OpenIddict has no field for (UI mode, cookie lifetime, token lifetime overrides, CORS origins, which external providers the client shows).
  - OpenIddict's client id is globally unique. Generate it as `{projectkey}.{name}` (or accept an admin value and enforce uniqueness). `Name` is unique only inside the project.
  - Create, update and delete applications ONLY through `IOpenIddictApplicationManager` (never raw EF) so OpenIddict caching and validation stay correct. Read the extra fields from the custom entity afterwards.
  - Keep the entity classes in the Infrastructure folder. Domain and Application folders use an interface or a DTO so OpenIddict types do not spread through the code.
- Rename `user_applications` to `user_projects`. It keeps the per-project credentials data (password hash, TOTP, lockout, etc.).
- Everywhere an Identity or domain table used `ApplicationId` (roles, permissions, groups, links, logins, passkeys, tokens, audit, etc.), use `ProjectId`. The tenant query filter uses `ProjectId`.
- Audit events, sessions and synced catalog items keep an OPTIONAL `ApplicationId` (which client did it / pushed it).
- Add a new EF migration. Keep the migration job working. Note in docs that every OpenIddict major upgrade needs an EF migration review.
- Seed: project `_global` with application `_global` (admin console client). `_global` cannot be disabled or deleted by a project admin.

### Access rule (decided)

- A user who has a `user_projects` row for a project can use EVERY application of that project. No per-application access list, no extra table, no `RestrictUsersToApplications`, no `application_access_denied`.
- Registering through any application creates the `user_projects` row once. Logging in through another application of the same project needs no new registration.
- The same person in another project is a separate membership with separate credentials.
- `JoinMode` (AutoJoin, InviteOnly, Closed) is a PROJECT setting and controls who may create a `user_projects` row.

### Enabled/disabled rules (decided)

- Project disabled: nobody can log in by any method, in any application. This covers password, passkey, social, OIDC brokering, SAML, LDAP, 2FA, password reset, registration/join, refresh tokens, authorization code exchange and client credentials. Enforce it in ONE shared check used by every path, not per handler.
- Application disabled: only that application is blocked. Other applications of the project work. OpenIddict has no enabled flag, so check `Application.IsEnabled` in your own OpenIddict event handlers (authorize, token, validation) as part of the same shared check.
- On disabling a project: revoke all authorizations, refresh tokens and sessions of the project in one transaction, write an audit event with the admin id and reason, keep all data. Existing access tokens must be rejected at the next validation (short cached project status, invalidated through Redis when available, with a documented cache window). Disabling an application revokes only its client's tokens.
- Public error is generic (`access_denied`); the real reason goes only to the audit log.
- Login check order: project enabled, application enabled, user in the project, lockout, password/passkey/external, 2FA, consent.

### Single vs multi project (decided)

- Server: `ServerWithSingleProject()` replaces the old single-application mode. It has one project; ProjectKey and `user_projects` logic are bypassed (all users belong to that one project). Applications still exist.
- Client: `.ServerWithMultiProject("PaperTrade")` sets the ProjectKey. In single-project mode the client does NOT call it. The client always sets ApplicationName and its client credentials.
- If the client sends a ProjectKey to a single-project server: ignore it and log a warning. If a client sends no ProjectKey to a multi-project server: fail at client startup with a clear message.
- The server decides the project from the application (client id lookup). The ProjectKey is only an identifier check, never proof of authorization. It is NOT a secret and must not be hashed. Real security: client id + secret (confidential), PKCE and exact redirect-URI validation (public).
- The handshake request carries ProjectKey (multi mode only) and ApplicationName; the response returns ProjectId and ApplicationId. The server verifies that the calling client belongs to the project with that key and has that name.
- Token claims: `project_id`, `projectkey`, `client_id`. Remove the old `app` claim. Keep `idp`, `amr`, session id.
- The server login cookie MUST be per project (cookie name includes the project key), so a login in one project never counts in another project on the same domain. Add a test.
- Add a per-application "Client setup" admin page that shows a copy-paste config snippet (ServerUrl, ProjectKey, ApplicationName, client id) and shows the client secret ONCE at creation.

### Settings (two levels)

- Project settings: login methods, password rules, lockout, 2FA, external providers, JoinMode, SingleActiveSession, shared token defaults, project-wide options.
- Application settings: OpenIddict fields (redirect URIs, post-logout URIs, allowed scopes, consent type) plus SettingsJson (UI mode hosted/self/none, cookie lifetime, token lifetime overrides, CORS origins, which external providers that client shows, a subset of the project's providers).
- Effective value = project default, overridden by the application value where set. Implement one resolver service and test the precedence.

### Admin routes and roles

- Project-level data: `/projects/{projectId}/users|roles|permissions|groups|settings|audit|sessions`.
- Applications: `/projects/{projectId}/applications/{applicationId}/...` (client, secrets, redirect URIs, scopes, settings, setup page).
- GlobalAdmin sees everything. ProjectAdmin sees the whole project including all its applications and users. No ApplicationAdmin in v1.
- Cross-project protections: a project admin can never read or change another project's data. Add tests.

### Context services

- Replace `IApplicationContext` with `IProjectContext` (tenant) and `IClientApplicationContext` (the calling client).

### Tests for Part 1

- Create/update/delete of an application goes through the manager, and the extra fields (ProjectId, IsEnabled, Name, SettingsJson) persist on the same row.
- Same Name in two projects works; same Name in one project fails; client id is globally unique.
- One user, one project, three applications: register once, log in through all three.
- Same person in two projects is two separate users with separate credentials.
- Project disabled: password, passkey, social, SAML, LDAP, refresh, client credentials, registration all fail; a token issued before is rejected after the cache window.
- Application disabled: only that application fails.
- Per-project cookie isolation; settings precedence; single-project mode ignores ProjectKey (warning logged); multi-project client without ProjectKey fails at startup; handshake rejects a client that does not belong to the project; cross-project admin access is denied; `_global` cannot be disabled by a project admin.

## PART 2 — ICurrentClient and ICurrentUser split

- `ICurrentClient`: IsTokenAuthenticated, ClientId, ApplicationId, ProjectId, ProjectKey, Scopes, HasScope(string), IsMachine, SessionId, Idp, Amr. All synchronous, read from the ClaimsPrincipal.
- `ICurrentUser`: person only. IsAuthenticated is false for machine tokens. User id, profile, and async roles/permissions/groups with caching.
- Both derive from the same ClaimsPrincipal. Register both in DI for the client package and for the server.
- `.WithScope(...)` and `.WithMachineOnly()` use ICurrentClient. `.WithPermission(...)` and `.WithRole(...)` use ICurrentUser. Scope AND permission can both be required.
- Update every use of the old ICurrentUser scope/machine members.
- Tests: machine token, user token, token with scope but no permission, permission but no scope, both.

## PART 3 — Catalog sources (permissions, roles, scopes, groups)

- One generic `CatalogOptions<TDefinition>` shared by all four catalogs. Keep SyncPolicy (None, Push, Pull, Both) and ScanningPolicy exactly as they are. Pull and Both STAY.
- Keep the existing list properties as shortcuts. Add these methods to ALL four catalogs:
  - `Add(definition)`
  - `AddFromClass(Type)`: scans public `const string` and `static readonly string` fields, including nested classes. The VALUE is the key. Metadata from `[Auth2Permission]`, `[Auth2Role]`, `[Auth2Scope]` or `[Auth2Group]` on the field; otherwise the display name is built from the field name, description empty, category = class name (`[Auth2Category]` on the class overrides). Types are passed explicitly (no hidden whole-assembly scan, trimming-friendly).
  - `AddFromEnum<TEnum>(keyStyle)` and `AddFromEnum(Type)`: key = `[Auth2Key]` value if present, else member name converted by KeyStyle (DotCase default: OrdersRead becomes orders.read; also AsIs, SnakeCase, KebabCase). Metadata from the attribute on the member; category defaults to the enum name. `[Flags]` enums and two members that produce the same key FAIL startup.
  - `AddFromString(string, char separator = ',', string? category = null)`: split, trim, drop empty parts, merge duplicates, key only (name = key, description empty). A key containing the separator is an error that names the bad part.
  - `AddFromListString(IEnumerable<string>, string? category = null)`: same rules, from a list.
  - `AddProvider<T>()` and `AddFactory(sp => ...)`: `IPermissionProvider`-style interfaces per catalog, resolved from DI when the host starts, just before the first sync. Support async.
  - `AddFromConfiguration(IConfigurationSection)`.
- Merge by key, ordinal case-sensitive. Same key with identical data is ignored. Same key with different data fails startup unless `DuplicatePolicy = FirstWins`.
- Validate keys at startup (format, length, forbidden characters). Scope keys must not contain spaces, double quotes or backslashes (OAuth scope rules) and stay separate from permission keys. Errors must name the source, class, field or part that is wrong.
- Roles may carry permission keys and groups may carry members, but only through full definitions, attributes, providers or configuration (not through string or enum sources).
- Everything pushed goes to the PROJECT catalog and records the source application. Sync never deletes; items that disappear are marked obsolete.
- The same constants can be used in `[Authorize(Policy = ...)]` and `.WithPermission(...)`; show this in the docs.
- Tests: every source on all four catalogs; enum key styles and `[Auth2Key]` override; `[Flags]` failure; string edge cases (spaces, empty parts, custom separator, duplicates); scope with a space rejected; mixed sources merge; conflicting definitions fail and FirstWins accepts; a provider that throws gives a clear startup error; attribute metadata reaches the server; Pull and Both policies still work with the new sources.

## Definition of done for Parts 1-3

- Builds without warnings that you introduced; ALL tests pass (unit, integration, multi-instance where they exist).
- No remaining use of the old names (`user_applications`, the old single-application mode, the `app` claim, old IApplicationContext, a separate OpenIddict client table) except in migration history and docs/decisions.md.
- Then rewrite the docs: concepts glossary (Project vs Application), setup for single and multi mode, handshake, settings precedence, enable/disable behavior, ICurrentClient vs ICurrentUser, catalog sources with examples, and how to extend each. Update docs/decisions.md with every decision above.

## PART 4 — Continue with the other planned work

After Parts 1-3 are complete and green, continue in this order. For each item use the same loop: code, tests, run, fix, then docs. Apply the Project/Application terms above to everything.

1. Admin CRUD work package: users, roles, permissions, applications, groups, link assignments, settings, sessions and audit, with the cross-project protections, using ProjectId and the new routes.
2. External Identity v3 work package: social login (OpenIddict.Client.WebIntegration), OIDC brokering, SAML service provider (ITfoxtec.Saml2 versus Sustainsys: do the spike and record the decision), LDAP/LDAPS/AD login (library spike; plain LDAP refused, only LDAPS or StartTLS), and passkeys using .NET 10 Identity passkeys. Passkeys, external logins and sessions are stored per project. Never link accounts by an unverified email. Provider secrets are encrypted and never returned or logged. The passkey browser JavaScript is NOT written here; it is specified in ui-handover.md.
3. Security, operations and scale-out items from the main specification that are not done yet: rate limiting, audit events, key rotation, health checks, migration job, proxy/issuer support, shared signing and Data Protection keys, multi-instance tests.
4. Finish with a full test run, a docs consistency pass (all docs use the same terms), and a final report listing what was built, what tests ran and passed, which decisions were taken, and anything left undone.

Report honestly at each step: what you ran, what failed, what you fixed.
