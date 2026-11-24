# Sign in with Google

Users sign in with a Google account. Google tells the Server the user's email and whether it is verified. A verified Google email counts as proof of ownership by default.

## How to use (for operators)

1. Open the Google Cloud console, create or choose a project.
2. Go to **APIs & Services > OAuth consent screen** and set it up (user type *External* for the public, *Internal* for one Google Workspace).
3. Go to **Credentials > Create credentials > OAuth client ID**, type **Web project**.
4. Add the redirect URL below under **Authorized redirect URIs**.
5. Copy the **client id** and **client secret** into the settings below.
6. To allow only one company, set `HostedDomain` to the Workspace domain (for example `corp.com`). The Server also checks the `hd` claim of the token, so a user cannot skip it.

### The URL to register

Enter this as the redirect (callback) URL at the provider. Replace `{slug}` with the project key of your application (or `default` in single-project mode) and `{name}` with the provider name you choose:

```
https://auth.example.com/callback/login/{slug}/{name}
```

### Provider settings

```json
{
  "kind": "oidc",
  "name": "google",
  "type": "Google",
  "displayName": "Sign in with Google",
  "enabled": true,
  "clientId": "1234-abc.apps.googleusercontent.com",
  "clientSecret": "GOCSPX-...",
  "hostedDomain": "corp.com"
}
```

The secret is sent in plain text **once** when you save. The Server encrypts it and never shows it again. See [external-identity.md](../external-identity.md) for every field and for the rules about linking by email.

## Common problems

- **redirect_uri_mismatch** at Google: the URL in the console is not exactly the one above (scheme, host, slug and name must match).
- **`external_claims_invalid` with reason `hosted_domain_mismatch`** in the audit log: the user's account is not in the `HostedDomain`.
- A user with a personal Gmail address cannot sign in when `HostedDomain` is set. This is intended.

## How to implement or extend

The engine is OpenIddict.Client with its WebIntegration package: `ExternalClientRegistrations` builds the registration of this provider (`web.AddGoogle(...)`), and `OpenIddictIdentitySource` runs the code flow (PKCE, `state`, `nonce`) and applies the rules of this type. The type has its own descriptor in `ExternalProviderDescriptors.cs`. How to enable and use the engine: [social-webintegration.md](social-webintegration.md). Tests: `OidcSourceTests.cs`, `ExternalClientIntegrationTests.cs`, with `FakeOidcProvider`.

## Related pages

- [Sign in with Microsoft](microsoft.md)
- [Sign in with GitHub](github.md)
- [Sign in with Apple](apple.md)
- [Sign in with Facebook](facebook.md)
- [Generic OpenID Connect (Keycloak, Okta, Auth0 and others)](generic-oidc.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Documentation index](../index.md)
