# SAML with Active Directory Federation Services (ADFS)

Single sign-on with ADFS as the identity provider.

## How to use (for operators)

Three things you give the identity provider (from the Server), and three you give the Server (from the identity provider).

**From the Server** (replace `{slug}` with the project key or `default`, and `{name}` with the provider name; the admin API shows the full URLs):

| Name at most providers | Value |
|---|---|
| Entity ID / Identifier / Audience | `https://auth.example.com/saml/{slug}/{name}` (or the `SpEntityId` you set) |
| ACS URL / Reply URL / Single sign-on URL | `https://auth.example.com/saml/{slug}/{name}/acs` (binding **POST**) |
| Metadata | `https://auth.example.com/saml/{slug}/{name}/metadata` |

**From the identity provider**: its **metadata URL** (best, it also updates itself) or the metadata XML, or by hand: entity id, single sign-on URL and the **signing certificate**.

1. ADFS Management > **Relying Party Trusts > Add Relying Party Trust > Claims aware**. Import the **metadata of the Server** (the metadata URL) or enter the values by hand.
2. *Claim issuance policy*: send LDAP attributes as claims (email address, given name, surname, and *Token-Groups as names* for groups). Add a rule that transforms an incoming claim into **Name ID** with format *Persistent Identifier* (for example from `objectGUID`).
3. In the properties of the trust, *Advanced*: **SHA-256**. *Endpoints*: the ACS must be **POST**.
4. The ADFS metadata is at `https://adfs.corp.com/FederationMetadata/2007-06/FederationMetadata.xml`: put it in `idpMetadataUrl`.

### Settings

```json
{
  "kind": "saml",
  "name": "adfs",
  "type": "Saml",
  "enabled": true,
  "idpMetadataUrl": "https://adfs.corp.com/FederationMetadata/2007-06/FederationMetadata.xml",
  "groupsAttribute": "http://schemas.xmlsoap.org/claims/Group"
}
```

Also add `External` to `Methods.AllowedLoginMethods` of the application. See [external-identity.md](../external-identity.md) for every field.

## Common problems

- ADFS event 364 (*MSIS7065*): the relying party identifier is wrong. It must equal the entity id of the Server.
- ADFS signs only the response by default: set `WantResponseSigned` true and `WantAssertionsSigned` false, or set ADFS to sign both (preferred).
- `saml_response_invalid` in the browser: the reason is in the audit log (`saml.assertion.rejected`), for example `audience mismatch`, `destination_mismatch`, `assertion_expired`, `assertion_not_signed`, `in_response_to_mismatch`.
- Clocks: the Server allows `ClockSkewSeconds` (default 60). Fix the clock of the server that is wrong; do not raise the number above a few minutes.
- The user lands on an error page after the identity provider: the identity provider sent the answer to another ACS URL. The URL must match exactly.

## How to implement or extend

The flow is described in [how the SAML flow is validated](../external-identity.md#how-the-saml-flow-is-validated). Tests: `SamlSourceTests.cs` with `FakeSamlIdp`.
