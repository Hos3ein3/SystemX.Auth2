# Sign in with Facebook

Users sign in with Facebook. Facebook does not say whether an email is verified, so the Server never uses the Facebook email to link to an existing account (unless a global admin turns on `trustProviderEmailVerification`). A new account is created when registration is allowed.

## How to use (for operators)

1. Open Meta for Developers, create an app of type *Consumer* or *Business* and add the **Facebook Login** product.
2. Under **Facebook Login > Settings** add the redirect URL below to **Valid OAuth Redirect URIs**.
3. Copy the **App ID** (the `clientId`) and the **App secret**.
4. Set `apiVersion` to a current Graph API version, for example `v19.0`. Facebook retires old versions, so check it once a year.

### The URL to register

Enter this as the redirect (callback) URL at the provider. Replace `{slug}` with the project key of your application (or `default` in single-project mode) and `{name}` with the provider name you choose:

```
https://auth.example.com/callback/login/{slug}/{name}
```

### Provider settings

```json
{
  "kind": "oidc",
  "name": "facebook",
  "type": "Facebook",
  "enabled": true,
  "clientId": "123456789",
  "clientSecret": "...",
  "apiVersion": "v19.0"
}
```

The secret is sent in plain text **once** when you save. The Server encrypts it and never shows it again. See [external-identity.md](../external-identity.md) for every field and for the rules about linking by email.

## Common problems

- No email returned: the user has no email on the account or did not grant it. The Server then asks the user for an email (confirmation page) when email is required.
- `external_email_exists_not_verified`: a user with this email already exists and Facebook cannot prove ownership. The user signs in with the usual method and links Facebook from *Manage external logins*.
- App in *development mode*: only app roles (admins, testers) can sign in until you publish the app.

## How to implement or extend

The engine is OpenIddict.Client with its WebIntegration package: `ExternalClientRegistrations` builds the registration of this provider (`web.AddFacebook(...)`), and `OpenIddictIdentitySource` runs the code flow (PKCE, `state`, `nonce`) and applies the rules of this type. The type has its own descriptor in `ExternalProviderDescriptors.cs`. How to enable and use the engine: [social-webintegration.md](social-webintegration.md). Tests: `OidcSourceTests.cs`, `ExternalClientIntegrationTests.cs`, with `FakeOidcProvider`.

## Related pages

- [Sign in with Google](google.md)
- [Sign in with Microsoft](microsoft.md)
- [Sign in with GitHub](github.md)
- [Sign in with Apple](apple.md)
- [Generic OpenID Connect (Keycloak, Okta, Auth0 and others)](generic-oidc.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Documentation index](../index.md)
