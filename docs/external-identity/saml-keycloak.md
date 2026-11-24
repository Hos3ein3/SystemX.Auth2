# SAML with Keycloak

Single sign-on with a Keycloak SAML client.

## How to use (for operators)

Three things you give the identity provider (from the Server), and three you give the Server (from the identity provider).

**From the Server** (replace `{slug}` with the project key or `default`, and `{name}` with the provider name; the admin API shows the full URLs):

| Name at most providers | Value |
|---|---|
| Entity ID / Identifier / Audience | `https://auth.example.com/saml/{slug}/{name}` (or the `SpEntityId` you set) |
| ACS URL / Reply URL / Single sign-on URL | `https://auth.example.com/saml/{slug}/{name}/acs` (binding **POST**) |
| Metadata | `https://auth.example.com/saml/{slug}/{name}/metadata` |

**From the identity provider**: its **metadata URL** (best, it also updates itself) or the metadata XML, or by hand: entity id, single sign-on URL and the **signing certificate**.

1. Keycloak admin > your realm > **Clients > Create client**, type **SAML**. *Client ID* = the entity id of the Server.
2. *Valid redirect URIs* = the ACS URL. *Master SAML Processing URL* = the ACS URL. Turn on **Sign assertions**, set *Name ID format* to **persistent**.
3. *Client scopes > ... > Mappers*: add user property mappers for email, firstName and lastName, and a **Group list** mapper with the attribute name `groups`.
4. The realm metadata is at `https://keycloak.corp.com/realms/<realm>/protocol/saml/descriptor`: put it in `idpMetadataUrl`.

### Settings

```json
{
  "kind": "saml",
  "name": "keycloak",
  "type": "Saml",
  "enabled": true,
  "idpMetadataUrl": "https://keycloak.corp.com/realms/main/protocol/saml/descriptor",
  "groupsAttribute": "groups"
}
```

Also add `External` to `Methods.AllowedLoginMethods` of the project. See [external-identity.md](../external-identity.md) for every field.

## Common problems

- *Invalid requester*: the *Client ID* does not equal the entity id.
- Keycloak sends a full group path such as `/finance/emea`: use a **Prefix** mapping or turn off *Full group path* in the mapper.
- `saml_response_invalid` in the browser: the reason is in the audit log (`saml.assertion.rejected`), for example `audience mismatch`, `destination_mismatch`, `assertion_expired`, `assertion_not_signed`, `in_response_to_mismatch`.
- Clocks: the Server allows `ClockSkewSeconds` (default 60). Fix the clock of the server that is wrong; do not raise the number above a few minutes.
- The user lands on an error page after the identity provider: the identity provider sent the answer to another ACS URL. The URL must match exactly.

## How to implement or extend

The flow is described in [how the SAML flow is validated](../external-identity.md#how-the-saml-flow-is-validated). Tests: `SamlSourceTests.cs` with `FakeSamlIdp`.
