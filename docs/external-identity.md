# External identity: social login, OIDC, brokering, SAML, LDAP and passkeys

## What this is

Ways for users to sign in or register that are **not** a local password:

| Kind | In plain words |
|---|---|
| **Social login** | "Sign in with Google / Microsoft / GitHub / Apple / Facebook". The browser goes to the provider and comes back |
| **Generic OIDC** | The same, for any provider that follows OpenID Connect (OIDC): Keycloak, Okta, Auth0, Entra ID |
| **Brokering** | The Server stands between your app and an upstream identity provider. Your app only trusts the Server and only gets **Server tokens**. The Server maps the provider's claims and groups to local roles and groups |
| **SAML 2.0** | The Server is the *service provider* (SP). Company users sign in with their company *identity provider* (IdP). Used with Entra ID, Okta, ADFS, Keycloak |
| **LDAP / Active Directory** | The user types a directory user name and password. The Server checks them in the directory |
| **Passkeys** | A passkey (WebAuthn) in the user's phone, laptop or security key. Used as a primary way to sign in |

Providers are set **per application** and stored in the database. A new provider needs no code change and no restart.

## When to use it

- You want "Sign in with Google" without writing OAuth code in every app.
- A company customer wants single sign-on with their own SAML identity provider.
- Users already exist in Active Directory.
- You want users to sign in without a password.

## How it works (overview)

1. A developer (global admin) or a project admin adds a provider to an application with the admin API.
2. The login page of the Server (built later by the UI team) asks `GET /connect/external/providers` and shows one button per enabled provider.
3. The user clicks a button. The Server starts the sign-in at the provider (a redirect) or, for LDAP, takes the user name and password.
4. The provider answers. The Server checks the answer, finds or creates the local user, applies mappings, and signs the user in with the **same pipeline as a password login** (lockout, application membership, two-factor rule, consent, single active session).
5. Your app receives normal Server tokens (code flow). They carry two extra claims: `idp` (which provider) and `amr` (how the user signed in).

One person is one `AppUser`. The same Google account in two applications is one user with two `user_applications` rows and two login rows.

## Limits

- The Server is **not** a SAML identity provider (it does not issue SAML assertions to other apps). Your apps use OIDC with this Server.
- There is no background directory sync. LDAP data and groups are updated **at login**.
- Self UI apps cannot do redirect-based or passkey sign-in: the user must go through the Server's login page. LDAP password login works through the token endpoint when `AllowSelfUiLogin` is on.
- Passkeys need HTTPS and an explicit `ServerDomain`.

## Settings reference

Providers live in `ExternalProviders` of the application settings. All fields are **GlobalOnly**. Names are lowercase letters, digits and `-` (1 to 40 characters), unique in the application, at most 20 providers per application.

### Common fields (all kinds)

| Field | Default | Meaning |
|---|---|---|
| `Name` | | Unique name. It is used in URLs and as the login provider name |
| `Type` | | `Google`, `Microsoft`, `GitHub`, `Apple`, `Facebook`, `GenericOidc`, `Saml`, `Ldap` |
| `DisplayName` | null | Text of the button |
| `Enabled` | false | On or off |
| `AllowRegister` | true | A new user may be created from this source |
| `AllowLink` | true | A login may join an existing user with the same **verified** email |
| `TrustProviderEmailVerification` | null | Null means the default of the type (true for Google, Microsoft, Apple; false for the rest) |
| `TrustProviderMfa` | false | The provider's multi-factor sign-in replaces the local two-factor step |
| `ClaimMappings` | empty | Source name to local field (`email`, `given_name`, `family_name`, `name`, `phone_number`, `picture`) |
| `GroupMappingMode` | `None` | `None` or `Sync` |
| `ProfileSyncMode` | `ImportOnce` | `ImportOnce` or `UpdateOnLogin` |
| `AutoRedirect` | false | Go straight to this provider when it is the only way to sign in |
| `EmailDomains` | empty | Domains that are sent to this provider. A domain belongs to one provider per application |

### OIDC kinds (`kind: "oidc"`)

`ClientId`, `ClientSecretProtected`, `Scopes`, `StoreProviderTokens` (default false) apply to all. Then per type:

| Type | Extra fields |
|---|---|
| Google | `HostedDomain` |
| Microsoft | `TenantId` (`common`, `organizations`, `consumers` or a GUID), `AllowedTenants` |
| GitHub | `RequireVerifiedPrimaryEmail` (default true), `AllowedOrganizations` |
| Apple | `TeamId`, `KeyId`, `PrivateKeyProtected` (no client secret: a short-lived signed token is made from the key) |
| Facebook | `ApiVersion` (for example `v19.0`) |
| GenericOidc | `Authority` (https), `NameClaim`, `EmailClaim`, `GroupsClaim`, `AllowedIssuers` |

### SAML (`kind: "saml"`)

Trust material: `IdpMetadataUrl` or `IdpMetadataXml`, or manual `IdpEntityId` + `IdpSsoUrl` + `IdpSigningCertificates` (at most two, for rotation).
Request: `SpEntityId`, `NameIdFormat` (default `Persistent`), `AuthnRequestBinding`, `SignAuthnRequests`, `ForceAuthn`, `RequiredAuthnContext`.
Checks: `WantAssertionsSigned` (default true), `WantResponseSigned`, `WantAssertionsEncrypted`, `AllowIdpInitiated` (default false), `ClockSkewSeconds` (default 60, most 300).
Data: `AttributeMappings`, `GroupsAttribute`, `EmailAttribute`, `SubjectAttribute`. Logout: `AllowSingleLogout` (default false). Private data: `SpSigningCertificateProtected`.
Either the assertion or the response must be signed; a provider with neither is refused.

### LDAP (`kind: "ldap"`)

`Host`, `Port`, `Security` (`Ldaps` or `StartTls`; plain LDAP is not offered), `ValidateServerCertificate` (default true), `CustomCaCertificate`, `TlsServerName`,
`BindDn`, `BindPasswordProtected` (a service account; no anonymous bind, no empty password), `UserSearchBase`, `UserSearchFilter` (`{0}` exactly once),
`UserNameAttribute`, `UniqueIdAttribute`, `EmailAttribute`, `FirstNameAttribute`, `LastNameAttribute`, `PhoneAttribute`,
`GroupSearchBase`, `GroupFilter`, `GroupMemberAttribute`, `MemberOfAttribute`, `NestedGroups` (default false, depth 5), `TimeoutSeconds` (default 5), `FollowReferrals` (default false),
`Mode` (`LdapOnly`, `LocalThenLdap`, `LdapThenLocal`), `SyncOnLogin` (default true), `RevealDirectoryAccountState` (default false).

### Passkeys

See `Passkeys` in [server-settings.md](server-settings.md).

### Rules the Server checks when you save

Name pattern and uniqueness, at most 20 providers, one provider per email domain, HTTPS for `Authority`, metadata and SSO URLs, `{0}` once in LDAP filters,
SAML certificates can be read and are not expired, assertion or response signed, an enabled provider has its client id and secret (or trust material, or bind account).
An invalid list is refused with the list of paths that are wrong.

## URLs to register at the provider

Every provider of every application has its own URLs. They contain the **project slug** (the project key in multi-application mode, `default` in single mode) and the provider name,
so a response meant for one provider can never be accepted by another (this blocks "mix-up" attacks). Replace `https://auth.example.com` with your issuer.

| Kind | URL | Enter it as |
|---|---|---|
| OIDC (Google, Microsoft, GitHub, Apple, Facebook, generic) | `https://auth.example.com/callback/login/{slug}/{name}` | Redirect URI / callback URL |
| SAML | `https://auth.example.com/saml/{slug}/{name}/acs` | Assertion Consumer Service (ACS) URL, binding POST |
| SAML | `https://auth.example.com/saml/{slug}/{name}/metadata` | SP metadata (many identity providers can import it) |
| SAML | `https://auth.example.com/saml/{slug}/{name}/slo` | Single logout URL (only if you turn SLO on) |
| LDAP | none | Allow the Server to reach the host and port |

The admin API shows the full URLs of every provider (step 9). The registration name of a provider inside the Server is `ext-{applicationId}-{name}` (unique in the whole server).

## Changing providers at runtime

You do **not** restart the Server when you add, change or remove a provider.

1. The admin API saves the settings and raises `SettingsVersion` of the application.
2. The instance that saved the change reloads that application at once.
3. With **Redis**, it also sends a message and every other instance reloads the application within a moment.
4. Without Redis (or if a message is lost), every instance asks the database for changed `SettingsVersion` values every `Auth2:ExternalProviders:PollSeconds` seconds (default 30) and reloads what changed.

A user who is in the middle of a sign-in during a change may need to start again. If a reload fails (for example the database is down), the old list stays in use and a warning is logged.
Secrets are decrypted only when a provider is used, never at load time, and are never written to logs.

## Data model

| Table | What it holds |
|---|---|
| `user_logins` | The link between a user and an external identity. Key: `(application_id, login_provider, provider_key)`. `login_provider` is the provider **name**, `provider_key` is the stable id at the provider (OIDC `sub`, SAML NameID, LDAP `entryUUID`/`objectGUID`), never a user name or DN. One person using Google in two applications has two rows |
| `user_passkeys` | Passkeys. Key: `(application_id, credential_id)`. The public key and counters are in a jsonb column. A passkey of one application is never found in another |
| `external_group_mappings` | Rules that turn a group, claim or attribute into a role, group or permission **of the same application**. Exact or prefix match only, no regex |
| `replay_entries` | One-time values (SAML ids, login nonces) shared by all instances, removed after they expire |

Secrets (client secrets, Apple key, SAML SP key, bind password) are encrypted with Data Protection (purpose `SystemX.Auth2.ExternalProviderSecrets.v1`). The keys are stored in the database, so every instance can read them.

## How to implement or extend

- **Settings classes**: `Domain/Settings/ExternalProviders.cs`. A new kind needs a `[JsonDerivedType]` and a validator branch in `Application/ExternalProviderSettingsValidator.cs`.
- **Descriptors**: `Infrastructure/External/ExternalProviderDescriptors.cs`. One descriptor per type lists its fields, default scopes, setup guide and the URLs the admin must enter at the provider. Add a class and register it in `ExternalIdentityRegistration`.
- **Secrets**: use `IExternalSecretProtector` (`Protect`, `Unprotect`, `Redact`). Never log or return a `*Protected` field; always call `Redact` before showing a provider.
- **Stores**: `Auth2UserStore` overrides the login and passkey methods so every call needs a current application; the query filters of `user_logins` and `user_passkeys` do the isolation. Do not use `DbSet<AppUserLogin>` without the filter (`IGlobalScope.Begin()` turns it off).
- **JSON**: providers are stored in jsonb, which does not keep key order. `SettingsJson` sets `AllowOutOfOrderMetadataProperties` so the `kind` field can be anywhere.
- **Registry**: `IExternalProviderRegistry` (`Infrastructure/External/ExternalProviderRegistry.cs`) answers from memory: `Get`, `ListForApplication`, `FindByEmailDomain`, `FindByUrl`. Use it in endpoints; never read provider settings from the database per request. `ExternalProviderUrls` builds all names and paths in one place. `ExternalProviderRefreshService` loads at start, listens to Redis and polls.
- **Tests**: `ExternalRegistryTests.cs` (two applications, reload, two instances with and without Redis) and `ExternalIdentityDataTests.cs` (stores, passkeys, mappings, secrets, descriptors) and `SettingsTests.cs` (validator, JSON round trip).

## Related pages

- [decisions.md](decisions.md) (ADR-085 to ADR-088: the four design spikes)
- [authentication-flows.md](authentication-flows.md)
- [security.md](security.md)
