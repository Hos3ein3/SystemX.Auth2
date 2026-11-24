# Sign in with Apple

Users sign in with an Apple ID. Apple has no client secret: the Server makes a short-lived signed token (valid 5 minutes) from your private key for every request. Apple sends the name only the first time, and may hide the email behind a relay address.

## How to use (for operators)

1. In the Apple Developer account create an **App ID** with *Sign in with Apple*, and a **Services ID** (this is the `clientId`, for example `com.example.web`).
2. Configure the Services ID: add your domain and the redirect URL below.
3. Create a **Key** with *Sign in with Apple* and download the `.p8` file. Note the **Key ID** and your **Team ID**.
4. Put the content of the `.p8` file (PEM text) into `privateKey`.
5. Apple sends the answer with a **POST** to the redirect URL (form post). The Server accepts this.

### The URL to register

Enter this as the redirect (callback) URL at the provider. Replace `{slug}` with the project key of your application (or `default` in single-project mode) and `{name}` with the provider name you choose:

```
https://auth.example.com/callback/login/{slug}/{name}
```

### Provider settings

```json
{
  "kind": "oidc",
  "name": "apple",
  "type": "Apple",
  "enabled": true,
  "clientId": "com.example.web",
  "teamId": "ABCDE12345",
  "keyId": "ABC123DEFG",
  "privateKey": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----"
}
```

The secret is sent in plain text **once** when you save. The Server encrypts it and never shows it again. See [external-identity.md](../external-identity.md) for every field and for the rules about linking by email.

## Common problems

- `invalid_client` at Apple: Team ID, Key ID or Services ID is wrong, or the key was revoked.
- A user who hides the email gets an address like `xyz@privaterelay.appleid.com`. It is verified, but you cannot mail it unless you register your sending domain at Apple.
- The `.p8` file can be downloaded only once. Keep it safe.

## How to implement or extend

The engine is `OidcIdentitySource` (`Infrastructure/External/OidcIdentitySource.cs`). It uses the code flow with PKCE, `state` and `nonce`. This provider type has its own descriptor in `ExternalProviderDescriptors.cs` and its own rules in the same engine. Tests: `OidcSourceTests.cs`, with `FakeOidcProvider`.
