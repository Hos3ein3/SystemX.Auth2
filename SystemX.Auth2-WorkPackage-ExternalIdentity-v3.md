# SystemX.Auth2 — Work Package: External Identity (Social Login, Brokering, SAML, LDAP/AD, Passkeys)

> Version 3. Give this file to the implementing LLM **after** `SystemX.Auth2-Build-Specification-v2.md`. It replaces `SystemX.Auth2-WorkPackage-ExternalProviders-v2.md`.
> It **replaces** these parts of the main spec: section 2 (feature table rows for social login, brokering, passkeys, SAML, LDAP), section 5.3 (`ExternalProviders` setting), section 5.10 (`Account/External/*`, `Manage/Passkeys`), phase 14 (all of it), and the "Experimental" and "Not in v1" items in section 13 and section 14 for these features.
> **All five features are now core features of v1:** social login, identity brokering, SAML, LDAP/AD, passkeys. Nothing in this file is "experimental" or "extension point only".
> If this file and the main file disagree, this file wins for these features.
> Main-spec rules still apply: no HTML/CSS/JavaScript (page models and empty pages only), B1–B2 English docs, no secrets in logs.

---

## 0. Working method for every step (MUST follow)

For **every** piece of work in this package, in this exact order:

1. **Write the code.**
2. **Write the tests** (unit tests; integration tests when it touches the database, Redis, HTTP, LDAP or SAML). Minimum list in section 11.
3. **Run the tests until they all pass.** Do not write docs for code with failing tests. Do not start the next step with a red test suite.
4. **Write the docs** in `/docs`, in simple English (B1–B2). Every docs page has two parts:
   - **How to use** (for developers who run the Server): what it is, when to use it, step-by-step setup, settings, examples, expected results, common problems.
   - **How to implement / extend** (for contributors): classes and interfaces, internal flow, how to add a new provider type or change behavior, which tests protect it, design reasons (link to the ADR).
5. **Check the docs against the code:** code blocks must compile (copied from samples or tests), every setting, route and error code in the docs must exist. Update `docs/index.md`.
6. Update `CHANGELOG.md` and `docs/feature-status.md` (feature level + names of the tests that prove it). Commit. Go to the next step.

If a test cannot be written for a requirement, write the reason in `docs/decisions.md`. A step is **not finished** until its tests pass **and** its docs (both parts) exist.

---

## 1. Goal and scope

Developers can let users log in and register in several ways that are **not** a local password:

| Kind | What it is | How the user signs in | Library |
|---|---|---|---|
| **Social login** | Google, Microsoft, GitHub, Apple, Facebook | Redirect to the provider (OIDC/OAuth, code + PKCE) | `OpenIddict.Client.WebIntegration` |
| **Generic OIDC** | Any OpenID Connect provider (Keycloak, Okta, Auth0, Entra ID, ...) | Redirect | `OpenIddict.Client` (generic registration) |
| **Identity brokering** | The Server sits between the app and an upstream identity provider (OIDC or SAML). The app only trusts the Server and gets **Server tokens**. The Server maps upstream claims, groups and roles to local ones | Redirect, optionally automatic by email domain or hint | Uses the OIDC and SAML providers above |
| **SAML 2.0** | The Server acts as a SAML **Service Provider** and accepts logins from enterprise SAML identity providers (Entra ID, Okta, ADFS, Keycloak, ...) | Redirect (SP-initiated) | `ITfoxtec.Identity.Saml2` (see spike) |
| **LDAP / Active Directory** | The user types a directory user name and password; the Server checks them against the directory | Form login on the Server (hosted UI), or the token endpoint when self-UI login is allowed | `Novell.Directory.Ldap.NETStandard` or `System.DirectoryServices.Protocols` (see spike) |
| **Passkeys** | WebAuthn/FIDO2 passkeys as a primary sign-in method | Browser + authenticator | Built-in ASP.NET Core Identity (.NET 10) |

Rules for all of them:
- Configured **per application** (project) by a developer (global admin) or a project admin, stored in the database. No code change for a new provider instance.
- The same person using Google in App A and App B is one `AppUser` with two `user_applications` rows.
- Only the Server is involved. The Client package needs no provider code (see section 10).
- The Server can **not** act as a SAML *identity provider* in v1 (issuing SAML assertions to other apps). Write this limit in `comparison-and-limits.md` and `roadmap.md` with the reason (apps use OIDC with this Server).

## 2. Technology decisions (locked unless the spike says otherwise)

- Add to `Directory.Packages.props`: `OpenIddict.Client`, `OpenIddict.Client.AspNetCore`, `OpenIddict.Client.WebIntegration`, `ITfoxtec.Identity.Saml2` and `ITfoxtec.Identity.Saml2.MvcCore`, and one LDAP library (chosen in the spike).
- Provider **kinds** in code: `ExternalProviderType` = `Google`, `Microsoft`, `GitHub`, `Apple`, `Facebook`, `GenericOidc`, `Saml`, `Ldap`.
- Passkeys are **not** a provider; they are a per-user credential (section 5.7).
- Use the **ASP.NET Core Identity link table** `AspNetUserLogins` (`IdentityUserLogin<Guid>`: `LoginProvider`, `ProviderKey`, `ProviderDisplayName`, `UserId`) for all provider links (OIDC, SAML and LDAP). Do not invent another link table.
- Redirect-based providers (OIDC, SAML) and credential-based providers (LDAP) share one abstraction layer, so login, register, link, claim mapping, audit and admin code is written once:

```csharp
public interface IExternalIdentitySource
{
    ExternalProviderType Type { get; }
}
public interface IRedirectIdentitySource : IExternalIdentitySource     // OIDC, SAML
{
    Task<IActionResult> ChallengeAsync(ExternalChallengeContext ctx, CancellationToken ct);
    Task<ExternalIdentity> AuthenticateCallbackAsync(ExternalCallbackContext ctx, CancellationToken ct);
}
public interface ICredentialIdentitySource : IExternalIdentitySource   // LDAP
{
    Task<ExternalAuthResult> AuthenticateAsync(string userName, string password, CancellationToken ct);
}
public sealed record ExternalIdentity(string Provider, string Subject, string? Issuer, string? Email, bool? EmailVerified,
    string? FirstName, string? LastName, string? Phone, IReadOnlyList<string> Groups, IReadOnlyDictionary<string, string[]> Attributes);
```

## 3. Facts the implementer must know (checked against docs)

**OpenIddict client (social login, generic OIDC)**
1. Providers are normally registered **at startup**: `options.UseWebProviders().AddGitHub(o => o.SetClientId(...).SetClientSecret(...).SetRedirectUri("callback/login/github"))`. The web integration package supports 100+ services.
2. Several instances of the same provider are supported; each needs its own provider name (`SetProviderName`) for challenges by name.
3. OpenIddict recommends a **different redirect URI per provider** to reduce mix-up attacks.
4. A challenge uses `Challenge(properties, providerName)`; the docs example uses a POST action with an anti-forgery token and a local-only return URL.
5. **Dynamic registration from a database is not officially supported.** The maintainer describes: (a) an `IConfigureOptions<OpenIddictClientOptions>` service plus an `IOptionsChangeTokenSource<OpenIddictClientOptions>` that fires when settings change (preferred), or (b) a derived `OpenIddictClientService`.

**SAML**
6. `ITfoxtec.Identity.Saml2` is an open-source SAML 2.0 library for .NET apps that need to act as a Service Provider (SP), Relying Party or Identity Provider; it lists .NET 10, 9 and 8 as supported frameworks and is maintained by FoxIDs. `ITfoxtec.Identity.Saml2.MvcCore` adds ASP.NET Core helpers (`AddSaml2`, metadata, bindings, logout).
7. `Sustainsys.Saml2` is an alternative: an ASP.NET Core authentication handler for SAML (SP only). Its configuration is centered on authentication schemes, which is harder to drive from a database for many applications. The spike decides; record the decision.

**LDAP**
8. `System.DirectoryServices.Protocols` is cross-platform since .NET 5 but is a low-level API that needs native LDAP libraries on Linux. `Novell.Directory.Ldap.NETStandard` is a cross-platform library used on Linux and containers. The spike must test the chosen one **inside a Linux container with LDAPS/StartTLS**.

**Passkeys (ASP.NET Core Identity, .NET 10)**
9. Built-in: `SignInManager.MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`, `UserManager.AddOrUpdatePasskeyAsync`, `MakePasskeyRequestOptionsAsync`, `PasskeySignInAsync`. Options class: `IdentityPasskeyOptions` (`ServerDomain`, `AuthenticatorTimeout`, `ChallengeSize`, `UserVerificationRequirement`, `ResidentKeyRequirement`, `VerifyAttestationStatement`, `ValidateOrigin`). `IPasskeyHandler<TUser>` can be wrapped for special needs.
10. Limits: no default attestation validation; passkeys are a **primary** factor, not a second factor; only the Blazor template has UI. All passkey operations need **HTTPS**; set `ServerDomain` explicitly (do not rely on the host header). Registration state must be protected: use the `SignInManager` methods (they store the state in a data-protected, single-use cookie). Do not call `IPasskeyHandler` directly unless you keep the state safe and compare the attested user with the signed-in user.
11. Recommended safety: limit the number of passkeys per user and the length of display names; prompt users to add a backup method; watch `IsBackedUp`; keep the attestation object (it contains the AAGUID) so compromised authenticator models can be revoked.

Because of facts 5, 7 and 8, **the first task is a spike** (section 6).

## 4. Data model changes

### 4.1 `AspNetUserLogins` becomes per application
- Add `ApplicationId` (**non-null**) to `IdentityUserLogin`. In single-application mode use the fixed application row.
- Primary key `(ApplicationId, LoginProvider, ProviderKey)`; index `(ApplicationId, UserId)`.
- `LoginProvider` = provider **instance name** (`google`, `corp-saml`, `ldap-hq`), unique inside the application. `ProviderKey` = the stable external id (OIDC `sub`, SAML NameID or persistent attribute, LDAP `objectGUID`/`entryUUID`, **not** the user name or DN). `ProviderDisplayName` = text for the UI.
- Override the login store methods in `Auth2UserStore` to use the current application: `AddLoginAsync`, `RemoveLoginAsync`, `GetLoginsAsync`, `FindByLoginAsync`.
- Tests: same Google account linked in App A and App B; `FindByLoginAsync` in App A never returns an App B link.

### 4.2 Provider settings (jsonb, polymorphic with a `type` discriminator and `schemaVersion`)

`ApplicationSettings.ExternalProviders` is a list of `ExternalProviderSettings`.

**Common fields**

| Field | Meaning |
|---|---|
| `Name` | Unique inside the application. Lowercase letters, digits, `-`. Used in URLs and as `LoginProvider` |
| `Type` | `Google`, `Microsoft`, `GitHub`, `Apple`, `Facebook`, `GenericOidc`, `Saml`, `Ldap` |
| `DisplayName` | Text for the future UI |
| `Enabled` | On or off for this application |
| `AllowRegister` | A new local user may be created from this source (just-in-time provisioning) |
| `AllowLink` | A login may be linked to an existing user with the same **verified** email |
| `TrustProviderEmailVerification` | The source's email counts as proof of ownership (defaults: true for Google/Microsoft/Apple; **false** for Facebook, GenericOidc, Saml, Ldap; only the global admin can turn it on for the false ones) |
| `TrustProviderMfa` | Skip the local 2FA step for this source (default false) |
| `ClaimMappings` | Map source attributes to `email`, `given_name`, `family_name`, `name`, `phone_number`, `picture` |
| `GroupMappingMode` | `None`, `Sync` (apply mappings in 4.4 at every login) |
| `ProfileSyncMode` | `ImportOnce` (default) or `UpdateOnLogin` (update names/phone from the source on each login) |
| `StoreProviderTokens` | OIDC only. Default false |
| `AutoRedirect` | If true and this is the only enabled provider and no local login method is allowed, the login page redirects to it |
| `EmailDomains` | Optional list; used by home-realm discovery (4.5) |

**OIDC kinds (`Google`, `Microsoft`, `GitHub`, `Apple`, `Facebook`, `GenericOidc`)**: `ClientId`, `ClientSecretProtected` (encrypted, never returned; the API shows only `secretIsSet`), `Scopes`, plus:

| Type | Extra fields |
|---|---|
| `Google` | `HostedDomain` |
| `Microsoft` | `TenantId` (`common`, `organizations`, `consumers`, or GUID), `AllowedTenants` |
| `GitHub` | `RequireVerifiedPrimaryEmail` (default true), `AllowedOrganizations` |
| `Apple` | `TeamId`, `KeyId`, `PrivateKeyProtected` (client secret is a signed JWT; generate on demand and refresh before expiry) |
| `Facebook` | `ApiVersion` |
| `GenericOidc` | `Authority` (HTTPS), fixed `code` + PKCE, `NameClaim`, `EmailClaim`, `GroupsClaim`, `AllowedIssuers` |

**`Saml` (Service Provider settings)**

| Field | Meaning |
|---|---|
| `IdpMetadataUrl` or `IdpMetadataXml` | Import the identity provider's metadata (URL is fetched with the SSRF guard and cached; XML is stored). Or fill the manual fields below |
| `IdpEntityId`, `IdpSsoUrl`, `IdpSloUrl`, `IdpSigningCertificates[]` | Manual configuration |
| `SpEntityId` | Generated: `{issuer}/saml/{projectSlug}/{name}` (editable by global admin) |
| `AcsUrl`, `SloUrl`, `MetadataUrl` | Generated and shown (see 5.2) |
| `NameIdFormat` | `Unspecified`, `EmailAddress`, `Persistent`, `Transient` (default `Persistent` if the IdP supports it) |
| `AuthnRequestBinding` | `Redirect` (default) or `Post` |
| `SignAuthnRequests`, `WantAssertionsSigned` (default true), `WantResponseSigned`, `WantAssertionsEncrypted` | Signing and encryption |
| `SpSigningCertificateProtected` | SP certificate + private key (generated by the Server on request, encrypted, expiry shown) |
| `ForceAuthn`, `AllowIdpInitiated` (default **false**) | Behavior |
| `ClockSkewSeconds` (default 60, max 300) | |
| `AttributeMappings` | SAML attribute name → local field; `GroupsAttribute`, `EmailAttribute`, `SubjectSource` (`NameId` or attribute) |
| `RequiredAuthnContext` | Optional |

**`Ldap` (credential-based)**

| Field | Meaning |
|---|---|
| `Host`, `Port` | Directory server |
| `Security` | `Ldaps` or `StartTls` (**plain LDAP is refused** unless `Auth2:Ldap:AllowInsecure=true` in Development) |
| `ValidateServerCertificate` (default true), `CustomCaCertificate` (optional) | TLS trust |
| `BindDn`, `BindPasswordProtected` | Service account used to search (never the user's password). Anonymous bind is not allowed |
| `UserSearchBase`, `UserSearchFilter` | Example: `(&(objectClass=person)(sAMAccountName={0}))`; `{0}` is replaced with the **escaped** user name |
| `UserNameAttribute`, `UniqueIdAttribute` (`objectGUID` / `entryUUID`), `EmailAttribute`, `FirstNameAttribute`, `LastNameAttribute`, `PhoneAttribute` | Attribute mapping |
| `GroupSearchBase`, `GroupFilter`, `GroupMemberAttribute` / `MemberOfAttribute`, `NestedGroups` (default false) | Group reading |
| `TimeoutSeconds` (default 5), `PoolSize`, `FollowReferrals` (default false) | Connection behavior |
| `Mode` | `LdapOnly`, `LocalThenLdap`, `LdapThenLocal` for password login in this application |
| `SyncOnLogin` | Update profile and mapped groups at each successful login |

**Passkey settings** (in application settings, not in `ExternalProviders`): `AllowPasskeys`, `PasskeyUserVerification` (`required` default / `preferred`), `PasskeyResidentKey` (`preferred` default), `MaxPasskeysPerUser` (default 10), `PasskeyNameMaxLength` (default 50), `RequirePasskeyBackupMethod` (default true). Global (server) settings: `Auth2:Passkeys:ServerDomain` (**required in Production**), `BlockedAaguids` (global admin only), `AttestationMode` (`None` default, `AaguidAllowList` optional).

**Validation (`ExternalProviderSettingsValidator`)**: name pattern and uniqueness; max 20 providers per application; required fields per type; HTTPS for `Authority`, metadata URLs and `Ldaps`; `UserSearchFilter` must contain `{0}` exactly once; SAML certificates parse and are not expired; `WantAssertionsSigned=false` is rejected unless `WantResponseSigned=true`; claim mappings only to allowed targets; reject saving an enabled provider with missing client id/secret or missing IdP trust material.

### 4.3 Descriptors (one per provider type)

```csharp
public interface IExternalProviderDescriptor
{
    ExternalProviderType Type { get; }
    string DisplayName { get; }
    Type SettingsType { get; }
    IReadOnlyList<ProviderField> Fields { get; }      // name, label key, kind (text/secret/select/list/bool/certificate/map), required, default, help key
    IReadOnlyList<string> DefaultScopes { get; }      // OIDC only
    string SetupDocsAnchor { get; }
    string BuildRedirectInfo(ExternalProviderSettings s, string issuer, string projectSlug); // URLs the admin must enter at the provider
}
```
`Fields` lets a future admin UI draw forms without code per provider. Labels and help are **resx keys**.

### 4.4 Role / group mapping table (new, shared by OIDC, SAML and LDAP)

Table `external_group_mappings`: `Id`, `ApplicationId`, `ProviderName` (nullable = all providers), `SourceType` (`group`, `claim`, `attribute`), `SourceName` (claim/attribute name, or empty for groups), `MatchType` (`exact`, `prefix`), `SourceValue`, `TargetType` (`role`, `group`, `permission`), `TargetId`.
- **No regex** (avoids ReDoS). Only exact and prefix matching, case-insensitive.
- When `GroupMappingMode = Sync`: at every login compute the wanted targets from the mappings; **add** targets that match and **remove only targets that appear in some mapping of this provider** but no longer match. Targets that no mapping mentions are never touched (manual assignments stay).
- A mapping can only target roles/groups/permissions of the same application. A mapping to `GlobalAdmin` or system roles is refused.
- Every change bumps access versions and writes audit events (`external.mapping.applied`, counts only).

### 4.5 Home-realm discovery (brokering)
- `EmailDomains` on a provider (for example `contoso.com`) lets the login flow pick the provider from the email domain. A domain can belong to **one** provider per application (validated).
- A login request may carry a hint `provider=<name>` (like an IdP hint). The hint is checked against the enabled providers; unknown hints are ignored.
- The Server adds claims `idp` (provider name) and `amr` (`pwd`, `fed`, `mfa`, `hwk`/`swk` for passkeys) to its tokens so apps can see how the user signed in.

### 4.6 Passkey store
- Use the Identity passkey store (`IUserPasskeyStore<AppUser>`; check the exact table name and schema version in the .NET 10 Identity EF package and use its default model).
- Make it **per application**: add `ApplicationId` (non-null; fixed row in single mode) to the passkey table; the primary key must include it. Override the store methods (`AddOrUpdatePasskeyAsync`, `FindByPasskeyIdAsync`, `GetPasskeysAsync`, `RemovePasskeyAsync`) so every call is filtered by the current application. A passkey found for another application must behave as "not found".
- Store the attestation object (for AAGUID checks), name, created date, last used, `IsBackedUp`, `IsBackupEligible`, signature counter.
- Tests: the same authenticator registered in App A is not accepted in App B; limits (`MaxPasskeysPerUser`, name length).

## 5. Runtime design

### 5.1 Registry and reload
- `IExternalProviderRegistry` loads enabled providers of **all applications**: `Get(applicationId, name)`, `ListForApplication(applicationId)`, `FindByEmailDomain(applicationId, domain)`.
- OIDC registration name inside OpenIddict (globally unique): `ext-{applicationId:N}-{name}`.
- Redirect/callback URLs are **unique per provider and per application** (mix-up protection). `projectSlug` is the `ProjectKey` (multi mode) or `default` (single mode):
  - OIDC callback: `/callback/login/{projectSlug}/{name}`
  - SAML: ACS `/saml/{projectSlug}/{name}/acs`, SLO `/saml/{projectSlug}/{name}/slo`, metadata `/saml/{projectSlug}/{name}/metadata`
- The admin API and docs show the full URLs, because the developer must enter them at the provider or identity provider.
- Build registrations from the database using the approach chosen in the spike. When settings change, bump `SettingsVersion` and refresh on **all instances**: Redis pub/sub when Redis is on; otherwise poll `applications.SettingsVersion` every 30 seconds (configurable).
- Decrypt secrets only when building a registration; keep them in memory; never log them.

### 5.2 Endpoints and pages (page models only; empty `.cshtml`)

| Route | Purpose |
|---|---|
| `GET /connect/external/providers?returnUrl=&hint=` | Enabled providers for the current application (name, display name, kind, challenge URL, `loginFormRequired` for LDAP) |
| `GET /api/auth2/external-providers` (M2M token) | Same list for apps with their own pages |
| `POST /connect/external/challenge` | Inputs `provider`, `returnUrl`. Anti-forgery. Checks application enabled, provider enabled, `returnUrl` via `IReturnUrlValidator`. Starts OIDC challenge or SAML AuthnRequest |
| `GET\|POST /callback/login/{projectSlug}/{name}` | OIDC callback |
| `POST /saml/{projectSlug}/{name}/acs` | SAML assertion consumer service |
| `GET\|POST /saml/{projectSlug}/{name}/slo` | SAML single logout (optional, behind a setting) |
| `GET /saml/{projectSlug}/{name}/metadata` | SP metadata XML (public) |
| `Account/Login` handler `OnPostLdapAsync` | LDAP user name + password (rate limited) |
| `Account/External/Confirm` | When the source gave no email and one is required: user enters email, server sends a confirmation mail first |
| `Manage/ExternalLogins` | List, link, unlink (OIDC/SAML/LDAP links) |
| `POST /account/passkey/creation-options`, `/account/passkey/register` | Passkey registration (authenticated). Return JSON for the future UI script |
| `POST /account/passkey/request-options`, `/account/passkey/signin` | Passkey sign-in (anonymous, rate limited) |
| `GET /account/passkeys`, `PATCH /account/passkeys/{id}` (rename), `DELETE /account/passkeys/{id}` | Manage own passkeys |

Page models: `Account/External/Challenge`, `Callback`, `Confirm`, `Account/Passkey/*` (handlers only), `Manage/ExternalLogins`, `Manage/Passkeys`. The browser-side JavaScript for WebAuthn belongs to the UI LLM; write the exact JSON contract of each passkey endpoint in `docs/ui-handover.md`.

### 5.3 `IExternalLoginService.HandleAsync` — one decision table for OIDC, SAML and LDAP

Input: application, provider settings, `ExternalIdentity` (subject, issuer, email, verified flag, names, phone, groups, attributes), `returnUrl`.

1. **Validate the identity**: subject present; issuer matches the registration (OIDC/SAML); provider restrictions (Google `HostedDomain`, Microsoft `AllowedTenants`, GenericOidc `AllowedIssuers`, GitHub `AllowedOrganizations`, SAML audience/time checks already done in 5.6).
2. **Find an existing link**: `FindByLoginAsync(provider name, subject)` in the current application. Found → step 6.
3. **No link, a user with the same email exists:**
   - Email **verified** (`TrustProviderEmailVerification` and the source says verified) **and** `AllowLink` → add the link, ensure the `user_applications` row exists (counts as proof of ownership for joining; document it) → step 6.
   - Otherwise do **not** link: error `external_email_exists_not_verified`; tell the user to log in with the existing method and link from `Manage/ExternalLogins`.
4. **No link, no user:**
   - `AllowRegister` false, or `JoinMode` is `Closed`/`InviteOnly` without invite → `external_registration_not_allowed`.
   - Email required but missing → `Account/External/Confirm`.
   - Otherwise create `AppUser` (no local password; `EmailConfirmed` only if verified), add the login row, add the `user_applications` row (credential columns empty). In single mode no `user_applications` row. Generate a unique user name.
5. **Claim and group mapping**: apply `ClaimMappings` (respecting `ProfileSyncMode`) and group mappings (4.4).
6. **Sign in** through the same `ILoginService` pipeline as password login, method `External` (OIDC/SAML), `Ldap` or `Passkey`: application enabled → user belongs to application → not locked out → 2FA rule (skip if `TrustProviderMfa`; passkeys with user verification count as multi-factor; otherwise require the local second factor when `TwoFactorMode` is `Required`) → consent → `SingleActiveSession` → local cookie → redirect to a valid `returnUrl`, else `RedirectAfterLogin`.
7. **Audit** (reason codes only, no tokens, no raw assertions or claims dumps): `external.login.success`, `external.login.linked`, `external.register`, `external.login.failed`, `ldap.login.failed`, `saml.assertion.rejected`.
8. Add to Contracts: `LoginMethod.External`, `LoginMethod.Ldap`, `LoginMethod.Passkey`. A method that is not in `AllowedLoginMethods` is refused for that application.

### 5.4 Brokering behavior (checklist)
- The app only trusts this Server (issuer, keys). Upstream tokens/assertions never go to the app.
- Server tokens carry `idp` and `amr` claims (4.5). Upstream provider tokens are **not stored** unless `StoreProviderTokens` is on (OIDC only).
- Upstream claims, attributes and groups become local roles/groups/permissions only through mappings (4.4). Nothing is copied automatically.
- Home-realm discovery (4.5) and the `hint` parameter work for OIDC and SAML providers.
- `AutoRedirect` skips the provider picker when exactly one choice exists.
- Two providers in one application may point to the same upstream IdP only if their names and redirect URLs differ.
- Logout: local logout always works. Optional upstream logout (OIDC end-session, SAML SLO) only if enabled for the provider. Tests must cover "local logout when upstream logout fails".

### 5.5 LDAP / Active Directory flow (credential-based)
1. User submits user name and password to the Server (hosted login handler, or the token endpoint when `AllowSelfUiLogin`). Rate limit **before** any directory call (policy `ldap`, per user + IP).
2. `LdapIdentitySource.AuthenticateAsync`:
   - Open a TLS connection (LDAPS or StartTLS; validate the certificate), bind with the **service account**.
   - Search `UserSearchBase` with `UserSearchFilter`, where `{0}` is the user name escaped with an LDAP filter escape function (RFC 4515: `* ( ) \ NUL`). **Never** build filters by string concatenation. Ask for max 2 entries; if 0 or more than 1 → same error `invalid_credentials`.
   - Re-bind as the found DN with the user's password (reject empty passwords: an empty password can cause an anonymous "success" bind on some servers).
   - Read attributes and groups (nested groups only if `NestedGroups` is on; use a bounded depth).
   - Return `ExternalIdentity` with `Subject` = `UniqueIdAttribute` value (not the DN, which can change).
3. Hand the result to `IExternalLoginService` (5.3). The user has **no local password**; the directory is checked at every login. `Mode` decides the order with local passwords.
4. Failures: wrong password, unknown user, disabled account, expired password, server down, timeout → map to stable error codes (`invalid_credentials` for wrong user/password, `ldap_unavailable` for connection problems, `ldap_account_disabled` only if the setting `RevealDirectoryAccountState` is on, default off). Count failed attempts toward the local lockout of the linked user (if one exists) and the rate limiter. Never log the password or the full filter with the user input.
5. `SyncOnLogin`: update profile and groups (4.4). A separate admin action "test user lookup" shows the mapped attributes without signing anyone in.
6. Pool connections, set timeouts, do not follow referrals by default, dispose connections correctly, and expose metrics (bind duration, failures).

### 5.6 SAML 2.0 Service Provider flow (redirect-based)
1. `Challenge`: build an AuthnRequest (ID, issue instant, destination, ACS URL, `ForceAuthn`, NameID policy, optional RequestedAuthnContext), sign it if `SignAuthnRequests`, send with HTTP-Redirect or HTTP-POST. Store `RequestId`, provider name, `returnUrl` and a nonce in a data-protected, short-lived (10 minutes), single-use state (cookie or Redis; shared across instances).
2. `ACS` (POST): read `SAMLResponse` and `RelayState`. Validate **all** of these (each has a test):
   - XML parsed with DTD processing disabled and no external entities (XXE); reject oversized messages.
   - Signature on the Assertion (and on the Response if `WantResponseSigned`) verified with the configured IdP certificates; reject unsigned assertions; protect against XML signature wrapping (use the library's validated element, never re-read the XML by XPath).
   - `Issuer` equals `IdpEntityId`; `Destination` equals the ACS URL; `Audience` contains `SpEntityId`.
   - `InResponseTo` equals the stored request id (unless `AllowIdpInitiated`, default false); the request id is **single use** (replay cache in Redis or the database, kept until the assertion's `NotOnOrAfter`).
   - `NotBefore` / `NotOnOrAfter` and `SubjectConfirmationData` times with `ClockSkewSeconds`.
   - `StatusCode` is success; assertion ID not seen before (replay cache).
   - If encrypted assertions are required, decrypt with the SP key.
3. Build `ExternalIdentity` (subject from NameID or the configured attribute; attributes and groups from `AttributeMappings`). Hand to `IExternalLoginService`.
4. Metadata: publish SP metadata (entity id, ACS, SLO, signing/encryption certs, NameID formats). Import IdP metadata from a URL or XML; refresh periodically when from a URL; warn in the admin API when a certificate expires within 30 days. Support **two IdP signing certificates** at once for rotation.
5. Logout: optional SLO (SP-initiated and IdP-initiated) behind a setting; otherwise local logout only.
6. `AllowIdpInitiated=false` by default. If enabled, the response must be signed, the `RelayState` is not trusted for redirects (use `RedirectAfterLogin`), and audit marks it.

### 5.7 Passkey flow (built-in ASP.NET Core Identity, per application)
- Configure `IdentityPasskeyOptions`: `ServerDomain` from `Auth2:Passkeys:ServerDomain` (**required in Production; the app refuses to enable passkeys without it**), `UserVerificationRequirement`, `ResidentKeyRequirement`, `AuthenticatorTimeout`, `ChallengeSize`. Use `ValidateOrigin` to allow only the Server's own origins.
- Registration (authenticated user): `MakePasskeyCreationOptionsAsync` → return JSON → browser → `PerformPasskeyAttestationAsync` → check limits (`MaxPasskeysPerUser`, name length) → `AddOrUpdatePasskeyAsync` (per application store). Use the `SignInManager` methods so the registration state stays in the protected, single-use cookie. Do **not** call `IPasskeyHandler` directly.
- Sign-in: `MakePasskeyRequestOptionsAsync(user?)` (with a user name for the normal flow, without for username-less/conditional UI) → browser → `PasskeySignInAsync`. After success, the pipeline in 5.3 step 6 runs (lockout, `user_applications` check, consent, `SingleActiveSession`, `amr` claim). The signature counter and last-used date are updated. A passkey that belongs to another application is "not found".
- Passkeys are a **primary** factor. They do not replace TOTP as a second factor. If `RequirePasskeyBackupMethod` is on, a user who has no password and no external login must register at least two passkeys or a recovery method before removing the last other method (rule `cannot_remove_last_login_method`).
- Prompt (flag in the page model) to add another passkey when all registered passkeys are not backed up.
- Admin: list a user's passkeys, remove one, and a global `BlockedAaguids` list. A blocked AAGUID cannot register; existing passkeys with a blocked AAGUID are listed and can be removed (attestation validation is not on by default, so AAGUID reliability is documented as "as reported by the authenticator").
- All passkey endpoints require HTTPS and are rate limited (policy `passkey`).

### 5.8 Unlink rules
A user cannot remove the **last** way to log in in an application (no password, one external login/passkey, no other method): `cannot_remove_last_login_method`. LDAP users have a directory login that cannot be unlinked while `Mode = LdapOnly`.

## 6. Spike and decision (first task, before anything else)

Write spikes in `spikes/` (not shipped) and record each result in `docs/decisions.md` as an ADR. Add a test that proves each chosen behavior.

1. **OIDC dynamic registration.** Prove that a provider added or changed in the database works **without restarting**, and two providers of the same type for two applications work at once with different redirect URIs. Try (a) `IConfigureOptions<OpenIddictClientOptions>` + `IOptionsChangeTokenSource<OpenIddictClientOptions>`; then (b) a derived `OpenIddictClientService`. If neither is reliable, use the **fallback**: build registrations at startup and apply changes after a controlled refresh; the docs must say "A change to an OIDC provider may need a restart."
2. **SAML library.** Prove with `ITfoxtec.Identity.Saml2` that one Server can run several SAML providers for several applications, loaded from the database, with signature checks, replay protection and metadata. Compare `Sustainsys.Saml2` in one page of notes. Decide and record. The test IdP in the tests is built with the same library.
3. **LDAP library.** Prove the chosen library works **in a Linux container** against an OpenLDAP container with LDAPS and StartTLS (service-account bind, search, user re-bind, attributes, groups, timeouts). Compare `Novell.Directory.Ldap.NETStandard` and `System.DirectoryServices.Protocols`; pick the one that needs no native packages, or document the packages needed.
4. **Passkey store.** Prove that the Identity passkey store can be made per application (key and store overrides) and that a registration from App A cannot sign in App B. Record the exact table name and schema version.
5. Write the first docs pages (section 9, step 1).

## 7. Security rules (all mandatory, all tested)

**All providers**
1. Redirect flows: authorization code + PKCE (OIDC); `state`/`nonce` handled; replayed callback or assertion fails.
2. Unique callback/ACS URL per provider per application. A callback of provider A cannot complete a flow started for provider B.
3. `returnUrl` goes through `IReturnUrlValidator`. Challenge is a POST with anti-forgery.
4. **Never link or merge accounts using an unverified email.** Email from Generic OIDC, SAML, LDAP and Facebook is not trusted by default.
5. No automatic linking across applications.
6. Secrets (client secrets, Apple key, LDAP bind password, SAML private keys) are encrypted at rest, never returned, never logged, redacted from audit data. Secret rotation is an admin action with an audit event.
7. A disabled/deleted provider keeps existing link rows, but login through it is refused (`external_provider_disabled`).
8. Copy only mapped claims/attributes. Do not store raw assertions, raw claim sets or provider tokens (unless `StoreProviderTokens`).
9. Rate limit policies `external`, `ldap`, `saml-acs`, `passkey` (add to the main spec's list). Partition by IP + provider (+ user name for LDAP).
10. Any URL fetched by the Server (OIDC discovery, SAML metadata URL) must be HTTPS, size-limited, time-limited, and blocked for private/loopback/link-local addresses unless `Auth2:ExternalProviders:AllowPrivateNetworks=true` (default false; Development only).
11. Project admins manage only their own application's providers. These are `GlobalOnly`: `TrustProviderEmailVerification` for untrusted types, `AllowIdpInitiated`, `AllowPrivateNetworks`, `BlockedAaguids`, LDAP `Security=StartTls` downgrade options, `ValidateServerCertificate=false`.
12. Mappings (4.4) can never grant `GlobalAdmin` or system roles.

**SAML:** XXE-safe parsing; signature required; wrapping-safe processing; audience, destination, time, `InResponseTo` and replay checks (5.6); IdP-initiated off by default; certificates validated and rotation supported.

**LDAP:** TLS required; certificate validation on; no anonymous bind; no empty passwords; **filter-escape every user input** (LDAP injection tests with `*`, `)(`, `\00`); unique id attribute used as the subject (not DN); no referral following; timeouts; same error for unknown user and wrong password; password never stored or logged.

**Passkeys:** HTTPS only; explicit `ServerDomain`; state protected by `SignInManager`; limits per user; origin validation; backup-method rule; AAGUID block list; signature counter checked.

## 8. Admin API (JSON only, for the future UI)

Base `/api/auth2/admin/...`; same authorization rules as the main spec (5.12) and the Admin CRUD work package (application scope in the route).

| Route | Purpose |
|---|---|
| `GET external-provider-types` | All types with their `Fields`, defaults, setup help keys |
| `GET applications/{id}/external-providers` | List (secrets never returned, only `secretIsSet`) + all URLs to register (callback / ACS / SLO / metadata) |
| `POST applications/{id}/external-providers` | Create (validated) |
| `PUT applications/{id}/external-providers/{name}` | Update. Empty secret field = keep the old secret. Row version required |
| `POST .../{name}/rotate-secret` | Replace the secret / private key |
| `POST .../{name}/test` | OIDC: fetch discovery and report issuer, endpoints, `code` + PKCE. SAML: parse metadata, report entity id, endpoints, certificate expiry. LDAP: TLS connect + service-account bind + search count (never prints passwords) |
| `POST .../{name}/ldap/test-user` | LDAP: look up a user name (no password), return mapped attributes and groups |
| `POST .../{name}/saml/refresh-metadata` | Reload IdP metadata now |
| `POST .../{name}/saml/generate-sp-certificate` | Create a new SP signing certificate, return only the public part and expiry |
| `DELETE .../{name}` | Delete (login rows stay) |
| `GET\|POST\|PUT\|DELETE applications/{id}/external-group-mappings[/{mappingId}]` | Mappings (4.4) |
| `GET\|PUT applications/{id}/email-domain-routes` | Home-realm discovery (4.5) |
| `GET users/{id}/external-logins`, `DELETE users/{id}/external-logins/{provider}` | See and unlink (support case) |
| `GET users/{id}/passkeys`, `DELETE users/{id}/passkeys/{passkeyId}` | See and remove passkeys |
| `GET\|PUT passkeys/blocked-aaguids` | Global admin only |

Every write creates an audit event, bumps `SettingsVersion` and triggers the reload (5.1).

## 9. Build steps — each step = code → tests → green → docs (use + implement)

| # | Step | Tests that must pass first | Docs to write after green |
|---|---|---|---|
| 1 | **Spikes + ADRs** (section 6) | One spike test per decision | `decisions.md` (4 ADRs). Start `external-identity.md`: *How it works* and *Limits* |
| 2 | **Data model:** `ApplicationId` in `AspNetUserLogins`, passkey store per application, `external_group_mappings`, polymorphic settings for all types, validators, descriptors, secret encryption, Contracts enums | Store tests (4.1, 4.6), settings JSON round-trip for every type, validator tests, secret tests | `external-identity.md`: *Settings reference per type*, *Data model*; update `server-settings.md`, `credential-scope.md` |
| 3 | **Registry and reload** (OIDC + SAML), URLs per provider, change detection (Redis + polling) | Two applications, same type; reload; two-instance test with and without Redis | *URLs to register*, *Changing providers at runtime*; update `deployment-and-scaling.md` |
| 4 | **Social login + Generic OIDC** (Google, Microsoft, GitHub, Apple, Facebook, GenericOidc) with descriptors | One test class per provider type with `FakeOidcProvider` | One **setup guide per provider**; *How to add a new OIDC provider type* (worked example) |
| 5 | **Common login service + brokering:** `IExternalLoginService` decision table, claim mapping, group mapping, home-realm discovery, hint, `idp`/`amr` claims, unlink rules, audit, page models + empty pages | Every branch in 5.3; mapping add/remove-only-mapped; HRD; no-GlobalAdmin mapping; local logout when upstream logout fails | `authentication-flows.md`; *Brokering guide*; *Group mapping guide*; `ui-handover.md` (pages); `error-codes.md` |
| 6 | **SAML 2.0 SP:** challenge, ACS, metadata, SLO (optional), certificates, replay cache | All SAML tests in section 11 using a test IdP | *SAML setup guides* (Entra ID, Okta, ADFS, Keycloak, generic); *How the SAML flow is validated* (implement); certificate rotation guide |
| 7 | **LDAP/AD:** source, TLS, escaping, group reading, sync, modes, test endpoints | All LDAP tests in section 11 against an OpenLDAP container | *LDAP setup guides* (OpenLDAP, Active Directory incl. LDAPS and service account, `sAMAccountName` vs `userPrincipalName` filters); *Troubleshooting LDAP*; *How to add attributes* (implement) |
| 8 | **Passkeys:** options, registration, sign-in, per-application store, limits, backup rule, AAGUID block list, endpoints, page models | All passkey tests in section 11 with a software authenticator test helper | *Passkeys guide* (use + security + recovery); *JSON contract for the UI script* in `ui-handover.md`; *How the per-application store works* (implement) |
| 9 | **Admin API** (section 8) | Authorization tests (project vs global admin), secrets never in responses/logs, validation | `admin-api.md`, `api-reference.md` with request/response examples |
| 10 | **Client changes** (section 10) | Client unit + integration tests | `client-setup.md`, `external-identity.md`: *Self UI limit* |
| 11 | **Final pass** | Full suite green, coverage check, docs checks | `feature-status.md`, `security.md`, `troubleshooting.md`, `comparison-and-limits.md` (what is supported now; **no SAML IdP role**), `roadmap.md`, root README, `CHANGELOG.md` |

Never batch several steps and write the docs at the end.

## 10. Client package impact
- **Hosted UI** (`UseHostedUi`, `UseCookieAuthentication`, `UseBlazor`): no change. The user picks a provider (or types LDAP credentials, or uses a passkey) on the Server's login page (future UI); the Client sees a normal code flow. The token has `idp` and `amr` claims; add them to `ICurrentUser` (`IdentityProvider`, `AuthenticationMethods`).
- **Self UI** (`UseSelfUi`): redirect-based external login and passkeys are **not** supported (the user must go through the Server). LDAP password login works through the token endpoint if `AllowSelfUiLogin` is on. The app can call `IAuth2ApiClientService.GetExternalProvidersAsync()` to learn the list and then redirect to the Server's hosted login with a `provider` hint. Document the limits.
- API-only clients: nothing to do.
- Contracts: `LoginMethod.External|Ldap|Passkey`, `ExternalProviderType`, `ExternalProviderInfoDto(name, displayName, kind, challengeUrl, loginFormRequired)`.
- Tests: `GetExternalProvidersAsync`; hosted flow with external login ends on the original page B; `ICurrentUser` shows `idp` and `amr`.

## 11. Tests (minimum list)

Test helpers in the test project (no internet needed):
- `FakeOidcProvider`: in-memory OIDC provider (discovery, authorize, token, JWKS; claims like `email_verified=false`).
- `FakeSamlIdp`: builds signed/unsigned/encrypted SAML responses with the chosen library (wrong audience, wrong destination, expired, replayed, wrong signature, wrapped signature, XXE payload, missing `InResponseTo`).
- OpenLDAP container (Testcontainers generic container) with a seeded directory: users, groups, nested groups, disabled user, LDAPS and StartTLS with a test CA.
- `SoftwareAuthenticator`: creates WebAuthn registration and assertion JSON (ES256 key pair, "none" attestation, configurable counter, AAGUID, backup flags). If this helper proves too hard, record it in `decisions.md` and use the library's test vectors.

**Common (all kinds)**
- New user → one `AppUser`, one login row (with `ApplicationId`), one `user_applications` row, no local password, audit written. Returning user → no new rows.
- Existing user + verified email + `AllowLink` → linked, no duplicate. Unverified email → not linked, `external_email_exists_not_verified`. `AllowRegister=false` → nothing created.
- Same identity in App A and App B → one `AppUser`, two `user_applications`, two login rows.
- Provider enabled in A, disabled in B. Two providers of the same type in two applications work at once.
- Evil `returnUrl` ignored; page B preserved through the flow.
- Group mapping: adds matching targets, removes only mapped targets, never touches manual ones, refuses `GlobalAdmin`, same-application only, prefix/exact matching, no regex.
- Home-realm discovery by email domain; hint parameter; duplicate domain rejected.
- Brokering: app receives only Server tokens; `idp` and `amr` claims present; upstream tokens not stored by default.
- Unlink rules, `cannot_remove_last_login_method`.
- Secrets never appear in API responses, logs or audit data (log-capture test).
- Reload behavior as in the ADR; two server instances see changes (Redis and polling).
- SSRF guard for OIDC discovery and SAML metadata URLs.
- Descriptor test: every provider type has a descriptor, validator, docs anchor (the anchor must exist in the docs).

**OIDC specific:** callback replay fails; wrong-provider callback fails; Google `HostedDomain`; Microsoft `AllowedTenants`; GenericOidc `AllowedIssuers`; Apple secret JWT generation and refresh.

**SAML specific:** valid signed response logs in; unsigned assertion rejected; wrong certificate rejected; wrong audience / destination / issuer rejected; expired and not-yet-valid rejected (with clock skew limits); wrong or missing `InResponseTo` rejected; replay of the same response rejected (also across two instances); IdP-initiated rejected by default; XXE and DTD payloads rejected; signature-wrapping sample rejected; two IdP certificates (rotation) both accepted; metadata endpoint output is valid and contains the right URLs; metadata import (URL and XML); certificate expiry warning.

**LDAP specific:** correct login; wrong password; unknown user; empty password rejected; disabled account; injection strings (`*`, `)(uid=*`, `\00`) do not change the query (assert the escaped filter); two matching entries → `invalid_credentials`; TLS required (plain LDAP refused); certificate validation failure refused; timeout and server-down map to `ldap_unavailable`; subject is the unique id (rename the DN → same user); group sync with nested groups on/off; `Mode` ordering (`LdapOnly`, `LocalThenLdap`, `LdapThenLocal`); failed attempts count toward lockout and rate limit; password never in logs.

**Passkey specific:** register and sign in (with the software authenticator); unsupported origin rejected; wrong `ServerDomain` rejected; reused state rejected; tampered state rejected; registering for another user's account rejected; the passkey of App A does not sign in to App B; `MaxPasskeysPerUser` and name length limits; counter regression rejected; blocked AAGUID rejected; backup-method rule; `amr` claim; lockout and `user_applications` checks apply; endpoints refuse HTTP in Production configuration.

**Admin/Client:** authorization tests for every admin route (project admin cannot touch another application); `GlobalOnly` settings; Client unit + integration tests listed in section 10.

**Docs checks:** code fences marked `csharp` in the new docs compile (or are marked `// illustrative` with a reason); every documented error code and setting exists; every docs anchor referenced by a descriptor exists.

## 12. Documentation requirements (B1–B2 English)

Short sentences, active voice, acronyms explained once (OIDC, SAML, IdP, SP, ACS, LDAP, LDAPS, DN, AAGUID, WebAuthn, PKCE).

**Files**
- `docs/external-identity.md` (main page): what it is; the six kinds in plain words; when to use which; **How to use** (enable a provider step by step with admin API requests and responses); login vs register vs link decision table; brokering explained with a diagram; group mapping; home-realm discovery; settings reference; security rules in plain words; **How to implement / extend** (class map, `IExternalIdentitySource`, descriptors, stores, flow diagrams, how to add a new provider type, how to run tests, ADR links); limits.
- Setup guides: `docs/external-identity/` with one file per item: `google.md`, `microsoft.md`, `github.md`, `apple.md`, `facebook.md`, `generic-oidc.md`, `saml-entra-id.md`, `saml-okta.md`, `saml-adfs.md`, `saml-keycloak.md`, `saml-generic.md`, `ldap-openldap.md`, `ldap-active-directory.md`, `passkeys.md`, `brokering.md`, `group-mapping.md`. Each: where to click at the provider, which URLs to enter (exact format), which attributes/claims to send, common errors, how to test.
- `docs/external-identity/troubleshooting.md`: redirect URI mismatch, SAML clock skew, certificate expired, `InResponseTo` errors, LDAP TLS errors, LDAP filter returns two users, passkey `ServerDomain` problems.
- Update: `server-settings.md`, `security.md`, `authentication-flows.md`, `admin-api.md`, `api-reference.md`, `error-codes.md` (`external_email_exists_not_verified`, `external_registration_not_allowed`, `external_provider_disabled`, `external_claims_invalid`, `cannot_remove_last_login_method`, `saml_response_invalid`, `saml_replay_detected`, `ldap_unavailable`, `ldap_account_disabled`, `passkey_not_allowed`, `passkey_limit_reached`, `passkey_blocked_authenticator`), `credential-scope.md`, `client-setup.md`, `client-current-user.md` (`idp`, `amr`), `deployment-and-scaling.md` (shared replay cache, reload), `observability.md` (new metrics), `troubleshooting.md`, `feature-status.md`, `comparison-and-limits.md` (no SAML IdP role, no user federation/sync daemon for LDAP: sync happens at login), `ui-handover.md` (all pages, handlers, flags, passkey JSON contract), `roadmap.md` (SAML IdP role, scheduled LDAP sync, SCIM, more OIDC providers), `index.md`.

**Rules:** every page ends with *Related pages*; code samples come from tests or the sample project; every route, setting and error code in the docs exists in the code (add a check); do not describe untested behavior.

## 13. Definition of done
- Four spike ADRs written; each chosen approach proven by a test.
- For **every step in section 9**: tests pass, then docs (How to use **and** How to implement / extend), then changelog and feature status.
- All tests in section 11 pass locally and in CI, with **no internet access** (containers only).
- No secret, token, assertion or password appears in logs, audit data or API responses.
- Every provider type has descriptors, validators, defaults, tests and a setup guide.
- No HTML, CSS or JavaScript was written (page models and empty pages only).
- Docs checks pass.

## 14. Pitfalls
1. Default `IdentityUserLogin` and passkey keys do not include `ApplicationId` → change keys and stores, or applications collide.
2. Linking by an unverified email is an account-takeover bug → follow 5.3 step 3.
3. One redirect/ACS URL for all providers makes mix-up attacks easier → one per provider per application.
4. OpenIddict has no official dynamic registration → spike first; do not promise live reload before a test proves it.
5. SAML: trusting unsigned or wrapped XML, skipping `InResponseTo`/replay checks, or enabling IdP-initiated login are the classic holes → follow 5.6 exactly.
6. LDAP: string-built filters (injection), empty-password "success" binds, plain LDAP, using the DN as the user id → follow 5.5 exactly.
7. Passkeys: relying on the host header for the RP ID, storing registration state in a form field, or letting one application accept another's passkey → follow 5.7.
8. Apple's client secret is a signed JWT that expires → generate on demand and refresh.
9. Email can be missing (GitHub private email, Apple relay, Facebook, LDAP without `mail`) → confirm step; never create a user without an identity.
10. External ids are unique only inside their provider → store `(provider name, subject)` and check the issuer.
11. Group mappings that remove manual assignments surprise admins → remove only targets mentioned in mappings.
12. Several server instances must share the SAML replay cache and reload provider changes together.
13. Never log callback or ACS payloads (they contain `code`, `state`, assertions).
14. Docs written before tests pass, or at the end for many steps, are wrong → follow section 0.
