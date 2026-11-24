# Sign in with Microsoft

Users sign in with a work, school or personal Microsoft account (Microsoft Entra ID, v2.0 endpoint).

## How to use (for operators)

1. Open the Microsoft Entra admin center > **App registrations > New registration**.
2. Choose who can sign in: *this directory only* (one tenant), *any directory* (`organizations`), or *any directory and personal accounts* (`common`).
3. Add a **Web** redirect URI (below).
4. Under **Certificates & secrets**, create a **client secret** and copy its **value** (not the id).
5. Copy the **Project (client) ID**. For one tenant, copy the **Directory (tenant) ID** too and set `tenantId` to it.
6. To allow only some tenants when you use `organizations` or `common`, list their ids in `allowedTenants`. The Server checks the `tid` claim and that the token issuer belongs to that tenant.

### The URL to register

Enter this as the redirect (callback) URL at the provider. Replace `{slug}` with the project key of your application (or `default` in single-project mode) and `{name}` with the provider name you choose:

```
https://auth.example.com/callback/login/{slug}/{name}
```

### Provider settings

```json
{
  "kind": "oidc",
  "name": "microsoft",
  "type": "Microsoft",
  "enabled": true,
  "clientId": "00000000-0000-0000-0000-000000000000",
  "clientSecret": "...",
  "tenantId": "organizations",
  "allowedTenants": ["11111111-1111-1111-1111-111111111111"]
}
```

The secret is sent in plain text **once** when you save. The Server encrypts it and never shows it again. See [external-identity.md](../external-identity.md) for every field and for the rules about linking by email.

## Common problems

- **AADSTS50011** (reply URL does not match): fix the redirect URI.
- `tenant_not_allowed` in the audit log: the user's tenant is not in `allowedTenants` (or not the single `tenantId`).
- Secret expired: Microsoft secrets expire (often after 6 or 24 months). Create a new one and save the provider again.

## How to implement or extend

The engine is `OidcIdentitySource` (`Infrastructure/External/OidcIdentitySource.cs`). It uses the code flow with PKCE, `state` and `nonce`. This provider type has its own descriptor in `ExternalProviderDescriptors.cs` and its own rules in the same engine. Tests: `OidcSourceTests.cs`, with `FakeOidcProvider`.
