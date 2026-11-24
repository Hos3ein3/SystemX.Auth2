# Sign in with GitHub

Users sign in with GitHub. GitHub uses OAuth 2.0 (not OpenID Connect), so the Server reads the user and the email from the GitHub API. Only a **verified primary email** is accepted by default.

## How to use (for operators)

1. GitHub > **Settings > Developer settings > OAuth Apps > New OAuth App** (or an organization's settings).
2. Set **Authorization callback URL** to the URL below.
3. Copy the **client id**, generate a **client secret** and copy it.
4. To allow only members of some organizations, set `allowedOrganizations`. Members must grant access to the organization when they sign in (GitHub shows this).

### The URL to register

Enter this as the redirect (callback) URL at the provider. Replace `{slug}` with the project key of your application (or `default` in single-project mode) and `{name}` with the provider name you choose:

```
https://auth.example.com/callback/login/{slug}/{name}
```

### Provider settings

```json
{
  "kind": "oidc",
  "name": "github",
  "type": "GitHub",
  "enabled": true,
  "clientId": "Iv1.abc",
  "clientSecret": "...",
  "requireVerifiedPrimaryEmail": true,
  "allowedOrganizations": ["acme"]
}
```

The secret is sent in plain text **once** when you save. The Server encrypts it and never shows it again. See [external-identity.md](../external-identity.md) for every field and for the rules about linking by email.

## Common problems

- `email_not_verified`: the user has no verified primary email on GitHub. Ask them to verify it, or set `requireVerifiedPrimaryEmail` to false (then the email is never used to link accounts unless you also trust it).
- `organization_not_allowed`: the user is not a member, or has not granted the OAuth app access to the organization.
- The GitHub id (a number) is the stable user id. A changed GitHub login name does not break the link.

## How to implement or extend

The engine is OpenIddict.Client with its WebIntegration package: `ExternalClientRegistrations` builds the registration of this provider (`web.AddGitHub(...)`), and `OpenIddictIdentitySource` runs the code flow (PKCE, `state`, `nonce`) and applies the rules of this type. The type has its own descriptor in `ExternalProviderDescriptors.cs`. How to enable and use the engine: [social-webintegration.md](social-webintegration.md). Tests: `OidcSourceTests.cs`, `ExternalClientIntegrationTests.cs`, with `FakeOidcProvider`.

## Related pages

- [Sign in with Google](google.md)
- [Sign in with Microsoft](microsoft.md)
- [Sign in with Apple](apple.md)
- [Sign in with Facebook](facebook.md)
- [Generic OpenID Connect (Keycloak, Okta, Auth0 and others)](generic-oidc.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Documentation index](../index.md)
