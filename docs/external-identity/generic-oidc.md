# Generic OpenID Connect (Keycloak, Okta, Auth0 and others)

Any provider that follows OpenID Connect Discovery. The Server reads `{authority}/.well-known/openid-configuration`, then uses the code flow with PKCE. The authority must use **https** and must not point to a private network address.

## How to use (for operators)

1. At the provider create an OIDC client of type *confidential / web*, with the **authorization code** flow.
2. Add the redirect URL below.
3. Copy the client id and secret. Set `authority` to the issuer URL (for Keycloak: `https://host/realms/myrealm`; for Okta: `https://company.okta.com`; for Auth0: `https://tenant.auth0.com/`).
4. Optional: `nameClaim`, `emailClaim` (defaults `name`, `email`), `groupsClaim` (for example `groups`), `claimMappings` and group mappings (see [group-mapping.md](group-mapping.md)).
5. The issuer in the token must be the issuer of the discovery document. For multi-tenant providers list the other issuers in `allowedIssuers`.
6. A generic provider's email is **not** trusted as proof by default (`trustProviderEmailVerification` is off and only a global admin can turn it on).

### The URL to register

Enter this as the redirect (callback) URL at the provider. Replace `{slug}` with the project key of your application (or `default` in single-project mode) and `{name}` with the provider name you choose:

```
https://auth.example.com/callback/login/{slug}/{name}
```

### Provider settings

```json
{
  "kind": "oidc",
  "name": "corp-sso",
  "type": "GenericOidc",
  "displayName": "Company login",
  "enabled": true,
  "clientId": "auth2",
  "clientSecret": "...",
  "authority": "https://sso.corp.com/realms/main",
  "groupsClaim": "groups",
  "emailDomains": ["corp.com"]
}
```

The secret is sent in plain text **once** when you save. The Server encrypts it and never shows it again. See [external-identity.md](../external-identity.md) for every field and for the rules about linking by email.

## Common problems

- `discovery_failed` in the audit log: the authority is wrong, not https, unreachable from the Server, a private address (blocked on purpose), or its issuer does not match the authority.
- `issuer_invalid`: tokens come from another issuer than the discovery document. Add it to `allowedIssuers` only if you trust it.
- The user has no email claim: set `emailClaim` to the claim that holds it, or request the `email` scope.

## How to implement or extend

The engine is OpenIddict.Client: a generic registration with discovery from the `Authority` (`ExternalClientRegistrations`); `OpenIddictIdentitySource` runs the code flow (PKCE, `state`, `nonce`) and reads the claims named by `NameClaim`, `EmailClaim` and `GroupsClaim`. The type has its own descriptor in `ExternalProviderDescriptors.cs`. See [social-webintegration.md](social-webintegration.md) for the shared engine. Tests: `OidcSourceTests.cs`, `ExternalClientIntegrationTests.cs`, with `FakeOidcProvider`.

## Related pages

- [Sign in with Google](google.md)
- [Sign in with Microsoft](microsoft.md)
- [Sign in with GitHub](github.md)
- [Sign in with Apple](apple.md)
- [Sign in with Facebook](facebook.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Documentation index](../index.md)
