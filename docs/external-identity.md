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

## Related pages

- [decisions.md](decisions.md) (ADR-085 to ADR-088: the four design spikes)
- [authentication-flows.md](authentication-flows.md)
- [security.md](security.md)
