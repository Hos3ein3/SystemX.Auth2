# SystemX.Auth2 — Work Package: External Login Providers (OpenIddict.Client.WebIntegration)

> Give this file to the implementing LLM **after** `SystemX.Auth2-Build-Specification-v2.md`.
> It **replaces** the "external login / identity brokering" parts of the main spec: section 2 (feature table rows for social login and brokering), section 5.3 (`ExternalProviders` setting), section 5.10 (`Account/External/Callback`), phase 14 (external part), and section 13 ("Experimental").
> Social login and OIDC brokering are now **core features, not experimental**. Passkeys stay experimental.
> If this file and the main file disagree, this file wins for external login.
> All working rules from section 1 of the main spec still apply: no HTML/CSS, B1–B2 English docs, no secrets in logs. Section 0 below makes the **test-then-docs rule** explicit for this work package.

---

## 0. Working method for every step (MUST follow)

For **every** piece of work in this package, in this exact order:

1. **Write the code.**
2. **Write the tests** for it (unit tests; integration tests when it touches the database, Redis or HTTP). See section 11 for the minimum list.
3. **Run the tests until they all pass.** Do not write docs for code that has failing tests. Do not start the next step with a red test suite.
4. **Write the docs** for that piece, in the `/docs` folder, in simple English (B1–B2). Every docs page for this package has **two parts**:
   - **How to use** (for developers who run the Server): what it is, when to use it, step-by-step setup, settings, examples, expected results, common problems.
   - **How to implement / extend** (for contributors): which classes and interfaces are involved, how the flow works inside, how to add a new provider type or change behavior, which tests protect it, and the design reasons (link to the ADR).
5. **Check the docs against the code:** every code block in the docs must be copied from the sample, the tests or real code (it must compile), every setting name must exist, every route and error code must exist. Add or update the docs links in `docs/index.md`.
6. Update `CHANGELOG.md` and `docs/feature-status.md` (feature level + names of the tests that prove it). Then commit and go to the next step.

If a test cannot be written for a requirement, write the reason in `docs/decisions.md`. Never skip tests silently.

The mapping between steps, tests and docs is fixed in section 9. A step is **not finished** until its tests pass **and** its docs (both parts) exist.

---

## 1. Goal

Developers can let users **log in and register** with external providers (Google, Microsoft, GitHub, Apple, Facebook, and any generic OpenID Connect provider such as Keycloak, Okta, Auth0 or Entra ID).

- Each provider needs different settings. Handle that with typed settings per provider type.
- Providers are configured **per application** (project) by a developer (global admin) or a project admin, and stored in the database. A new provider instance needs no code change.
- The same person logging in through Google in App A and App B is one `AppUser` with two `user_applications` rows.
- Only the Server is involved. The Client package needs no provider code. External login works through the **Hosted UI** flow (the user is redirected to the Server, which redirects to the provider).

## 2. Technology decisions (locked)

- Use **`OpenIddict.Client`**, **`OpenIddict.Client.AspNetCore`** and **`OpenIddict.Client.WebIntegration`** inside the Server project (add to `Directory.Packages.props`).
- `WebIntegration` supports 100+ services. In v1 support these **provider types** (each one small, see 4.2 and 4.3): `Google`, `Microsoft` (Entra ID / personal accounts), `GitHub`, `Apple`, `Facebook`, `GenericOidc` (any OIDC provider through discovery).
  - Adding another provider later must need only: one settings class + one descriptor class + tests + docs. Document this in `docs/external-providers.md` ("How to implement / extend").
- Use the **ASP.NET Core Identity link table** for the link between a local user and a provider account: `AspNetUserLogins` (entity `IdentityUserLogin<Guid>`: `LoginProvider`, `ProviderKey`, `ProviderDisplayName`, `UserId`). Do not invent a second table for this.
- Use authorization code flow with PKCE for every provider. Never use implicit flow.

## 3. Facts the implementer must know (from the OpenIddict docs)

1. Providers are normally registered **at startup**: `options.UseWebProviders().AddGitHub(o => o.SetClientId(...).SetClientSecret(...).SetRedirectUri("callback/login/github"))`.
2. Several instances of the same provider are supported; each needs its own provider name (`SetProviderName`) when a challenge is started by name.
3. OpenIddict recommends a **different redirect URI per provider** to reduce mix-up attacks (unless all providers return the `iss` parameter).
4. A challenge is started with `Challenge(properties, providerName)`; the docs example uses a POST action with an anti-forgery token and a local-only return URL.
5. The callback authenticates with the OpenIddict client scheme, then your code builds the local sign-in. The official guide shows a callback route like `/callback/login/{provider}`.
6. **Dynamic registrations (adding providers at runtime from a database) are not officially supported.** The maintainer describes two workable approaches: (a) register through an `IConfigureOptions<OpenIddictClientOptions>` service and reload with an `IOptionsChangeTokenSource<OpenIddictClientOptions>` that fires when provider settings change (preferred), or (b) a derived `OpenIddictClientService` that returns `OpenIddictClientRegistration` objects on the fly.

Because of fact 6, **the first task is a spike** (section 6). Do not build the rest before the spike result is written in `docs/decisions.md`.

## 4. Data model changes

### 4.1 `AspNetUserLogins` becomes per application

This changes the main spec (section 5.2 said "nullable `ApplicationId`"). New rule:
- Add `ApplicationId` (**non-null**) to `IdentityUserLogin`. In single-application mode use the fixed application row, so the column is always filled.
- Primary key becomes `(ApplicationId, LoginProvider, ProviderKey)`. Add an index on `(ApplicationId, UserId)`.
- `LoginProvider` stores the **provider instance name** (for example `google`, `github-corp`), unique inside one application. `ProviderDisplayName` stores the text for the UI.
- Override the login store methods in `Auth2UserStore` so every call uses the current application: `AddLoginAsync`, `RemoveLoginAsync`, `GetLoginsAsync`, `FindByLoginAsync` (and `FindUserLoginAsync` / `FindUserAsync` if used).
- Tests: the same Google account can be linked in App A and App B; `FindByLoginAsync` in App A never returns an App B link.
- Do not merge users automatically across applications (see 5.3).

### 4.2 Provider settings in the application jsonb

`ApplicationSettings.ExternalProviders` is a list of polymorphic entries (`System.Text.Json` polymorphism with a `type` discriminator and a `schemaVersion`).

Common fields (base class `ExternalProviderSettings`):

| Field | Meaning |
|---|---|
| `Name` | Unique inside the application. Lowercase letters, digits, `-`. Used in URLs and as `LoginProvider` |
| `Type` | `Google`, `Microsoft`, `GitHub`, `Apple`, `Facebook`, `GenericOidc` |
| `DisplayName` | Text for the future UI |
| `Enabled` | Turn the provider on or off for this application |
| `ClientId` | Provider client id |
| `ClientSecretProtected` | Secret, encrypted with Data Protection. **Never returned by any API.** The API only says `secretIsSet: true` |
| `Scopes` | Extra scopes (defaults per type) |
| `AllowRegister` | A new local user may be created from this provider |
| `AllowLink` | A login may be linked to an existing user with the same **verified** email |
| `TrustProviderEmailVerification` | The provider's "email verified" claim counts as proof of email ownership (default true for Google/Microsoft/Apple; false for Generic OIDC and Facebook) |
| `TrustProviderMfa` | Skip the local 2FA step for this provider (default false) |
| `ClaimMappings` | Map provider claims to `email`, `given_name`, `family_name`, `name`, `phone_number`, `picture` |
| `StoreProviderTokens` | Default false. If true, store provider tokens (encrypted) in `IdentityUserToken` with `ApplicationId` |

Type-specific fields:

| Type | Extra fields |
|---|---|
| `Google` | `HostedDomain` (optional, restrict to a Workspace domain) |
| `Microsoft` | `TenantId` (`common`, `organizations`, `consumers`, or a tenant GUID), `AllowedTenants` (optional) |
| `GitHub` | `RequireVerifiedPrimaryEmail` (default true), `AllowedOrganizations` (optional) |
| `Apple` | `TeamId`, `KeyId`, `PrivateKeyProtected` (the client secret is a signed JWT; generate it on demand and refresh before it expires) |
| `Facebook` | `ApiVersion` |
| `GenericOidc` | `Authority` (HTTPS issuer URL), fixed `code` response type and PKCE, `NameClaim`, `EmailClaim`, `AllowedIssuers` (optional) |

Validation (`ExternalProviderSettingsValidator`): name pattern and uniqueness; HTTPS for `Authority`; required fields per type; scopes are safe tokens; claim mappings only to allowed targets; max 20 providers per application; reject saving an enabled provider with an empty client id or missing secret.

### 4.3 Descriptors (one per provider type)

```csharp
public interface IExternalProviderDescriptor
{
    ExternalProviderType Type { get; }
    string DisplayName { get; }                       // "Google"
    Type SettingsType { get; }                        // typeof(GoogleProviderSettings)
    IReadOnlyList<ProviderField> Fields { get; }      // name, label key, kind (text/secret/select/list/bool), required, default, help key
    IReadOnlyList<string> DefaultScopes { get; }
    string SetupDocsAnchor { get; }                   // anchor in docs/external-providers.md
    OpenIddictClientRegistration BuildRegistration(ExternalProviderSettings settings, string redirectUri, string registrationName);
}
```

- `Fields` lets a future admin UI draw the form without code per provider. It is returned by an admin endpoint (section 8).
- Labels and help texts are **resx keys**, not text.

## 5. Runtime design

### 5.1 Registry and reload
- `IExternalProviderRegistry` loads enabled providers of **all applications**: `Get(applicationId, name)`, `ListForApplication(applicationId)`.
- Registration name in OpenIddict (globally unique): `ext-{applicationId:N}-{name}`.
- Redirect URI per provider and per application (mix-up protection): `/callback/login/{projectSlug}/{name}`, where `projectSlug` is the `ProjectKey` (multi mode) or `default` (single mode). The admin API and docs show the full redirect URL, because the developer must enter it in the provider's console.
- Build registrations from the database with the approach chosen in the spike (section 6). When settings change, bump `SettingsVersion` and refresh on **all instances**: Redis pub/sub when Redis is on; otherwise poll `applications.SettingsVersion` every 30 seconds (configurable).
- Decrypt secrets only when building a registration; keep them in memory only; never log them.

### 5.2 Endpoints and pages
- `GET /connect/external/providers?returnUrl=...` (page handler for the future UI): enabled providers for the current application (name, display name, challenge URL). Also `GET /api/auth2/external-providers` (M2M token) for apps with their own pages (see section 10).
- `POST /connect/external/challenge` (handler of `Account/External/Challenge`): inputs `provider`, `returnUrl`. Anti-forgery required. Checks: application enabled, provider exists and enabled, `returnUrl` passes `IReturnUrlValidator`. Starts the challenge; `returnUrl` is kept in `AuthenticationProperties`.
- `GET|POST /callback/login/{projectSlug}/{name}` (handler of `Account/External/Callback`): authenticates with the OpenIddict client scheme, builds an `ExternalLoginResult`, calls `IExternalLoginService.HandleAsync`.
- `Account/External/Confirm` (page model only): used when the provider gave no email and the settings require one. The user enters an email and the server runs an **email ownership check** (confirmation mail) before linking.
- `Manage/ExternalLogins` (page model only): list, link another provider, unlink.
- Empty `.cshtml` for all pages (main spec rule: only `@page`, `@model`, one comment).

### 5.3 `IExternalLoginService.HandleAsync` — decision table

Input: application, provider settings, external principal (subject, issuer, email, email-verified flag, names, phone), `returnUrl`.

1. **Validate the external result:** subject present; issuer matches the registration; `GenericOidc` `AllowedIssuers`; Google `HostedDomain`; Microsoft `AllowedTenants`; GitHub `AllowedOrganizations` (extra API call behind an interface).
2. **Find an existing link:** `FindByLoginAsync(provider name, subject)` inside the current application. Found → step 6.
3. **No link, a user with the same email exists:**
   - Provider email **verified** (`TrustProviderEmailVerification` and the claim is true) **and** `AllowLink` → add the link, ensure the `user_applications` row exists (this counts as proof of ownership for joining; document it) → step 6.
   - Otherwise do **not** link. Return `external_email_exists_not_verified`; tell the user to log in with the existing method and link from `Manage/ExternalLogins`.
4. **No link, no user:**
   - `AllowRegister` false, or `JoinMode` is `Closed`/`InviteOnly` without an invite → `external_registration_not_allowed`.
   - Email missing and required → `Account/External/Confirm`.
   - Otherwise create `AppUser` (no password; `EmailConfirmed` only if verified; names from claim mapping), add the login row, add the `user_applications` row (credential columns empty). In single mode no `user_applications` row. Generate a unique user name from the email or `provider-subject`.
5. **Audit events:** `external.login.success`, `external.login.linked`, `external.register`, `external.login.failed` (reason code only; no tokens, no raw claims).
6. **Sign in** through the same `ILoginService` pipeline as password login, with method `External`: application enabled → user belongs to application → not locked out → 2FA rule (skip if `TrustProviderMfa`; else require the local second factor when `TwoFactorMode` is `Required`) → consent → `SingleActiveSession` → local cookie → redirect to a valid `returnUrl`, else `RedirectAfterLogin`.
7. Add `LoginMethod.External` to Contracts. If `External` is not in `AllowedLoginMethods`, no provider works for that application.

### 5.4 Unlink rule
A user cannot unlink the **last** way to log in (no password in this application, one external login, no other method). Return `cannot_remove_last_login_method`.

## 6. Spike and decision (first task)

1. Write a spike (in `spikes/`, not shipped) that proves: **a provider added or changed in the database becomes usable without restarting the app**, and two providers of the same type for two applications work at the same time with different redirect URIs.
2. Try approach (a): `IConfigureOptions<OpenIddictClientOptions>` + `IOptionsChangeTokenSource<OpenIddictClientOptions>`.
3. If (a) does not work for web-provider registrations, try approach (b): a derived `OpenIddictClientService` (or an equivalent extension point).
4. If neither is reliable, use the **fallback**: build all registrations at startup from the database and apply changes after a controlled refresh (restart or recycle). The docs must then say clearly: "A change to a provider may need a restart."
5. Write the result, the chosen code, its limits and the rejected options in `docs/decisions.md` (ADR "External provider registration"). Add a test that proves the chosen behavior. Then write the first docs (section 9, step 1).

## 7. Security rules (all mandatory, all tested)

1. Authorization code + PKCE; OpenIddict handles `state` and `nonce`. Test that a replayed callback fails.
2. A **unique redirect URI per provider per application**. Test that the callback of provider A cannot complete a flow started for provider B.
3. `returnUrl` goes through `IReturnUrlValidator`. Challenge is a POST with anti-forgery.
4. **Never link or merge accounts using an unverified email.** Never trust an email from `GenericOidc` unless `TrustProviderEmailVerification` is on and the claim is `true`. Facebook email is not trusted by default.
5. No automatic linking across applications. A second application needs its own link row; joining follows 5.3 step 3.
6. Secrets (client secrets, Apple private key) are encrypted at rest with Data Protection, never returned, never logged, and redacted from audit data. Rotating a secret is an admin action with an audit event.
7. A disabled or deleted provider keeps existing link rows but login through it is refused (`external_provider_disabled`). Document recovery ("forgot password" only if the application allows password login).
8. Copy only mapped claims. Do not store the raw external principal.
9. Rate limit challenge and callback endpoints (policy `external`; add it to the rate limit list of the main spec).
10. `GenericOidc`: `Authority` must be HTTPS, discovery is cached, the token issuer must equal the discovered issuer, no wildcard issuers. Block private IP ranges for discovery (basic SSRF protection) unless `Auth2:ExternalProviders:AllowPrivateNetworks=true` (default false; for Development).
11. Provider tokens are **not stored** unless `StoreProviderTokens` is true.
12. Project admins manage only their own application's providers. Only the global admin can set `TrustProviderEmailVerification` for `GenericOidc` and can enable private-network discovery. Mark these settings `GlobalOnly`.

## 8. Admin API (JSON only, for the future UI)

Base `/api/auth2/admin/...`, same authorization rules as the main spec (5.12).

| Route | Purpose |
|---|---|
| `GET external-provider-types` | All types with their `Fields` (descriptor), defaults, setup help key |
| `GET applications/{id}/external-providers` | List (secrets never returned, only `secretIsSet`) + the exact **redirect URL** to register at the provider |
| `POST applications/{id}/external-providers` | Create (validated) |
| `PUT applications/{id}/external-providers/{name}` | Update. Empty secret field = keep the old secret |
| `POST applications/{id}/external-providers/{name}/rotate-secret` | Replace the secret |
| `POST applications/{id}/external-providers/{name}/test` | `GenericOidc`: fetch discovery, report issuer, endpoints, `code` and PKCE support. Others: validate settings only |
| `DELETE applications/{id}/external-providers/{name}` | Delete (login rows stay) |
| `GET users/{id}/external-logins`, `DELETE users/{id}/external-logins/{provider}` | See and unlink for support cases |

Every write creates an audit event, bumps `SettingsVersion` and triggers the reload (5.1).

## 9. Build steps — each step = code → tests → green → docs (use + implement)

| # | Step | Tests that must pass first | Docs to write after green |
|---|---|---|---|
| 1 | **Spike + ADR** (section 6) | Spike test proving the chosen reload behavior (or proving the fallback) | `decisions.md` (ADR). Start `external-providers.md`: *How it works* + *Limits (reload behavior)* |
| 2 | **Data model:** `ApplicationId` in `AspNetUserLogins` (key, store overrides, migration), `LoginMethod.External`, polymorphic settings, validators, descriptors, Data Protection for secrets | Store tests (4.1), settings JSON round-trip, validator tests, secret encryption test | `external-providers.md`: *Settings reference per provider type* (use) and *Data model and stores* (implement). Update `server-settings.md`, `credential-scope.md` |
| 3 | **Registry and reload:** registry, registration names, redirect URIs, change detection (Redis pub/sub + polling) | Two applications with the same provider type; reload test; two-instance test with and without Redis | `external-providers.md`: *Redirect URLs* and *Changing providers at runtime* (use); *Registry internals* (implement). Update `deployment-and-scaling.md` |
| 4 | **Provider types:** Google, Microsoft, GitHub, Apple (secret JWT), Facebook, GenericOidc, each with descriptor, defaults, tests (use `FakeOidcProvider`) | One test class per provider type + the descriptor test | **One setup guide per provider** (use): where to create the app, exact redirect URL, scopes, common errors. *How to add a new provider type* step by step (implement) with a worked example |
| 5 | **Login flow:** challenge and callback handlers, `IExternalLoginService` decision table, unlink rule, audit events, rate limit policy, page models + empty pages | Every branch in 5.3, unlink rule, return URL tests, replay and wrong-provider tests | `authentication-flows.md`, `external-providers.md`: *Login vs register vs link* in plain words (use); *Decision table and code map* (implement). `ui-handover.md` for the new pages. `error-codes.md` |
| 6 | **Admin API** (section 8) | Authorization tests (project admin vs global admin), secrets never in responses or logs, validation tests | `admin-api.md`, `api-reference.md` with request and response examples |
| 7 | **Client changes** (section 10) | Client unit + integration tests | `client-setup.md`, `external-providers.md`: *Self UI limit* |
| 8 | **Final pass** | Full suite green, coverage check | `feature-status.md`, `security.md` (section 7 rules), `troubleshooting.md` (external login problems), `roadmap.md`, root README feature list, `CHANGELOG.md` |

Never batch several steps and write the docs at the end. Docs are written **per step**, after that step's tests are green.

## 10. Client package impact

- **Hosted UI** (`UseHostedUi`, `UseCookieAuthentication`, `UseBlazor`): no change. The user sees the provider buttons on the Server's login page (future UI); the Client sees a normal code flow.
- **Self UI** (`UseSelfUi`): external login is **not supported** in v1 because the user must go through the Server. The app can call `IAuth2ApiClientService.GetExternalProvidersAsync()` (`GET /api/auth2/external-providers`) to learn the list, then redirect the browser to the Server's hosted login with a `provider` hint. Document this limit.
- API-only clients: nothing to do.
- Contracts: add `LoginMethod.External`, `ExternalProviderType`, `ExternalProviderInfoDto(name, displayName, challengeUrl)`.
- Tests: a Client unit test for `GetExternalProvidersAsync`; an integration test that a hosted flow with an external login ends on the original page B.

## 11. Tests (minimum list)

Create `FakeOidcProvider` in the test project: a tiny in-memory OIDC provider (discovery, authorize, token, JWKS; configurable claims such as `email_verified=false`). Tests must need no internet.

- New user through a provider → one `AppUser`, one login row (with `ApplicationId`), one `user_applications` row, no password, audit written.
- Returning user → no new rows, signed in.
- Existing user + verified email + `AllowLink` → linked, no duplicate user.
- Existing user + **unverified** email → not linked, `external_email_exists_not_verified`.
- `AllowRegister=false` → error, nothing created.
- Same Google account in App A and App B → one `AppUser`, two `user_applications`, two login rows.
- Provider enabled in App A, disabled in App B → works in A, refused in B.
- Two applications with the same provider type, different client ids and redirect URIs → both work at once.
- Callback replay and wrong-provider callback fail.
- Evil `returnUrl` values ignored; page B preserved through the whole flow.
- Provider restrictions: Google `HostedDomain`, Microsoft `AllowedTenants`, GenericOidc `AllowedIssuers`.
- Unlink: cannot remove the last login method.
- Secrets: not in API responses, logs or audit data (log-capture test).
- Reload behavior as defined in the ADR; two server instances see the change (Redis and polling variants).
- SSRF guard for `GenericOidc` private addresses.
- Descriptor test: every provider type has a descriptor, validator, default scopes and a docs anchor (the docs anchor must exist in `external-providers.md`).
- Docs test (simple script or test): all code fences marked `csharp` in `external-providers.md` compile, or are marked `// illustrative` with a reason.

## 12. Documentation requirements (B1–B2 English)

Write for developers who are not security experts. Short sentences, active voice, every acronym explained once.

**Files**
- `docs/external-providers.md` (main page). It has these parts:
  1. *What this is* and *when to use it*.
  2. **How to use** — enable the feature step by step: create the provider app, copy the redirect URL, add the provider with the admin API (example request and response), test login, common problems.
  3. Login vs register vs link — the decision table in plain words with examples.
  4. Settings reference — every field, default, who can edit, risks.
  5. Security rules in plain words (why unverified emails are not linked).
  6. **How to implement / extend** — class map (descriptors, settings, registry, `IExternalLoginService`, stores), the flow diagram (text or mermaid), how to add a new provider type step by step with a worked example, how to run the tests, how the reload works (link to the ADR).
  7. Limits (Self UI, reload behavior, Passkeys are separate and experimental).
- One setup guide per provider: Google, Microsoft, GitHub, Apple, Facebook, Generic OIDC (either sections in `external-providers.md` or `docs/external-providers/{name}.md`).
- Update: `server-settings.md`, `security.md`, `authentication-flows.md`, `admin-api.md`, `api-reference.md`, `error-codes.md` (`external_email_exists_not_verified`, `external_registration_not_allowed`, `external_provider_disabled`, `cannot_remove_last_login_method`, `external_claims_invalid`), `credential-scope.md`, `client-setup.md`, `deployment-and-scaling.md`, `troubleshooting.md`, `feature-status.md`, `ui-handover.md`, `roadmap.md` (more provider types, SAML through brokering, Self-UI external login), `index.md`.

**Rules for the docs**
- Every page ends with *Related pages*.
- Code samples come from tests or the sample project and must compile.
- Every route, setting and error code in the docs must exist in the code. Add a check that fails the build if a documented error code is missing from `error-codes.md` or from the code.
- Do not describe features that are not tested.

## 13. Definition of done

- Spike result and ADR written; the chosen approach is proven by a test.
- For **every step in section 9**: tests pass, then docs (How to use **and** How to implement / extend) are written, then the changelog and feature status are updated.
- All tests in section 11 pass locally and in CI, with no internet access.
- No secret or token appears in logs, audit data or API responses.
- All provider types have descriptors, validators, defaults, tests and setup guides.
- No HTML or CSS was written (page models and empty pages only).
- Docs checks pass (compiling code blocks, documented error codes exist).

## 14. Pitfalls

1. The default `IdentityUserLogin` primary key does not include `ApplicationId` → change the key and the store, or two applications will collide.
2. Auto-linking by an unverified email is an account-takeover bug → follow 5.3 step 3 exactly.
3. One redirect URI for all providers makes mix-up attacks easier → one URI per provider per application.
4. OpenIddict has no official dynamic registration → do the spike first; do not promise live reload in the docs before a test proves it.
5. Apple's client secret is a signed JWT that expires → generate on demand and refresh before expiry.
6. The provider email can be missing (GitHub private email, Apple relay, Facebook) → use the confirm step; never create a user without an identity.
7. A provider's `sub` is unique only inside that provider → store `(provider name, subject)` together and check the issuer.
8. Do not copy all claims into the user profile or tokens.
9. Several server instances must reload provider changes together.
10. Never log the callback query string (it contains `code` and `state`).
11. Writing docs before tests pass, or at the end for many steps, makes docs wrong → follow section 0.
