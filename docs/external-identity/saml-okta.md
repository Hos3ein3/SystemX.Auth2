# SAML with Okta

Single sign-on with an Okta SAML 2.0 project.

## How to use (for operators)

Three things you give the identity provider (from the Server), and three you give the Server (from the identity provider).

**From the Server** (replace `{slug}` with the project key or `default`, and `{name}` with the provider name; the admin API shows the full URLs):

| Name at most providers | Value |
|---|---|
| Entity ID / Identifier / Audience | `https://auth.example.com/saml/{slug}/{name}` (or the `SpEntityId` you set) |
| ACS URL / Reply URL / Single sign-on URL | `https://auth.example.com/saml/{slug}/{name}/acs` (binding **POST**) |
| Metadata | `https://auth.example.com/saml/{slug}/{name}/metadata` |

**From the identity provider**: its **metadata URL** (best, it also updates itself) or the metadata XML, or by hand: entity id, single sign-on URL and the **signing certificate**.

1. Okta admin > **Applications > Create App Integration > SAML 2.0**.
2. *Single sign-on URL*: the ACS URL. *Audience URI (SP Entity ID)*: the entity id. *Name ID format*: **Persistent** (or EmailAddress). *Project username*: Okta username or email.
3. *Attribute statements*: `email` = `user.email`, `givenname` = `user.firstName`, `surname` = `user.lastName`. *Group attribute statements*: name `groups`, filter as you need.
4. After saving, *Sign On > Metadata URL*: copy it into `idpMetadataUrl`.
5. Assign people or groups to the app.

### Settings

```json
{
  "kind": "saml",
  "name": "okta",
  "type": "Saml",
  "enabled": true,
  "idpMetadataUrl": "https://company.okta.com/app/<id>/sso/saml/metadata",
  "groupsAttribute": "groups"
}
```

Also add `External` to `Methods.AllowedLoginMethods` of the project. See [external-identity.md](../external-identity.md) for every field.

## Common problems

- Okta shows *Invalid Audience*: the *Audience URI* is not exactly the entity id of the Server.
- Okta response not signed: set *Assertion Signature* to **Signed** in the advanced settings.
- `saml_response_invalid` in the browser: the reason is in the audit log (`saml.assertion.rejected`), for example `audience mismatch`, `destination_mismatch`, `assertion_expired`, `assertion_not_signed`, `in_response_to_mismatch`.
- Clocks: the Server allows `ClockSkewSeconds` (default 60). Fix the clock of the server that is wrong; do not raise the number above a few minutes.
- The user lands on an error page after the identity provider: the identity provider sent the answer to another ACS URL. The URL must match exactly.

## How to implement or extend

The flow is described in [how the SAML flow is validated](../external-identity.md#how-the-saml-flow-is-validated). Tests: `SamlSourceTests.cs` with `FakeSamlIdp`.

## Related pages

- [SAML 2.0: enable and use](saml.md)
- [SAML 2.0: generic identity provider](saml-generic.md)
- [SAML with Microsoft Entra ID](saml-entra-id.md)
- [SAML with Active Directory Federation Services (ADFS)](saml-adfs.md)
- [SAML with Keycloak](saml-keycloak.md)
- [SAML certificate rotation](saml-certificate-rotation.md)
- [Group mapping](group-mapping.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Documentation index](../index.md)
