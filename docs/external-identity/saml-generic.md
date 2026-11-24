# SAML 2.0: generic identity provider

Use this page for any identity provider that speaks SAML 2.0 and is not covered by a special guide.

## How to use (for operators)

Three things you give the identity provider (from the Server), and three you give the Server (from the identity provider).

**From the Server** (replace `{slug}` with the project key or `default`, and `{name}` with the provider name; the admin API shows the full URLs):

| Name at most providers | Value |
|---|---|
| Entity ID / Identifier / Audience | `https://auth.example.com/saml/{slug}/{name}` (or the `SpEntityId` you set) |
| ACS URL / Reply URL / Single sign-on URL | `https://auth.example.com/saml/{slug}/{name}/acs` (binding **POST**) |
| Metadata | `https://auth.example.com/saml/{slug}/{name}/metadata` |

**From the identity provider**: its **metadata URL** (best, it also updates itself) or the metadata XML, or by hand: entity id, single sign-on URL and the **signing certificate**.

1. Create a SAML application (a "relying party" or "service provider") at the identity provider. Enter the three values from the Server.
2. Choose the **NameID**: *persistent* is best (a stable id). *Email* works if emails never change. *Transient* cannot link users and should not be used.
3. Send these attributes: email, first name, last name and, if you want group mapping, a groups attribute. Their names are free: tell the Server with `AttributeMappings` and `GroupsAttribute`.
4. Turn on **signing of the assertion** at the identity provider (the Server requires it by default).

### Settings

```json
{
  "kind": "saml",
  "name": "corp-saml",
  "type": "Saml",
  "displayName": "Company SSO",
  "enabled": true,
  "idpMetadataUrl": "https://idp.corp.com/metadata",
  "nameIdFormat": "Persistent",
  "groupsAttribute": "groups",
  "attributeMappings": { "mailaddress": "email" },
  "emailDomains": ["corp.com"]
}
```

Also add `External` to `Methods.AllowedLoginMethods` of the application. See [external-identity.md](../external-identity.md) for every field.

## Common problems

- The identity provider cannot reach our metadata: use the XML or the manual fields.
- Users get duplicate accounts: the NameID changes (transient). Use *persistent* or set `SubjectAttribute` to a stable attribute such as an employee id.
- `saml_response_invalid` in the browser: the reason is in the audit log (`saml.assertion.rejected`), for example `audience mismatch`, `destination_mismatch`, `assertion_expired`, `assertion_not_signed`, `in_response_to_mismatch`.
- Clocks: the Server allows `ClockSkewSeconds` (default 60). Fix the clock of the server that is wrong; do not raise the number above a few minutes.
- The user lands on an error page after the identity provider: the identity provider sent the answer to another ACS URL. The URL must match exactly.

## How to implement or extend

The flow is described in [how the SAML flow is validated](../external-identity.md#how-the-saml-flow-is-validated). Tests: `SamlSourceTests.cs` with `FakeSamlIdp`.
