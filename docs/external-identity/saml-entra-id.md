# SAML with Microsoft Entra ID

Single sign-on with a Microsoft Entra ID (Azure AD) enterprise application.

## How to use (for operators)

Three things you give the identity provider (from the Server), and three you give the Server (from the identity provider).

**From the Server** (replace `{slug}` with the project key or `default`, and `{name}` with the provider name; the admin API shows the full URLs):

| Name at most providers | Value |
|---|---|
| Entity ID / Identifier / Audience | `https://auth.example.com/saml/{slug}/{name}` (or the `SpEntityId` you set) |
| ACS URL / Reply URL / Single sign-on URL | `https://auth.example.com/saml/{slug}/{name}/acs` (binding **POST**) |
| Metadata | `https://auth.example.com/saml/{slug}/{name}/metadata` |

**From the identity provider**: its **metadata URL** (best, it also updates itself) or the metadata XML, or by hand: entity id, single sign-on URL and the **signing certificate**.

1. Entra admin center > **Enterprise applications > New application > Create your own application > Integrate any other application (Non-gallery)**.
2. **Single sign-on > SAML**. In *Basic SAML configuration* enter the **Identifier (Entity ID)** and the **Reply URL (ACS)** from the Server.
3. *Attributes and claims*: the default claims work. The email is `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress`, the names are `givenname` and `surname`. For groups add a **group claim** (it sends group object ids; use them in mappings, or enable *cloud-only group display names*).
4. *SAML certificates*: copy the **App Federation Metadata URL** into `idpMetadataUrl`. Set *Signing option* to **Sign SAML assertion**.
5. *Users and groups*: assign the users that may sign in.

### Settings

```json
{
  "kind": "saml",
  "name": "entra",
  "type": "Saml",
  "displayName": "Microsoft work account",
  "enabled": true,
  "idpMetadataUrl": "https://login.microsoftonline.com/<tenant-id>/federationmetadata/2007-06/federationmetadata.xml?appid=<app-id>",
  "groupsAttribute": "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups"
}
```

Also add `External` to `Methods.AllowedLoginMethods` of the application. See [external-identity.md](../external-identity.md) for every field.

## Common problems

- **AADSTS700016 / AADSTS50105**: the user is not assigned to the application, or the Identifier does not match `SpEntityId`.
- Entra rolls its signing certificate: use the metadata URL so the Server picks up the new one (see [saml-certificate-rotation.md](saml-certificate-rotation.md)).
- `saml_response_invalid` in the browser: the reason is in the audit log (`saml.assertion.rejected`), for example `audience mismatch`, `destination_mismatch`, `assertion_expired`, `assertion_not_signed`, `in_response_to_mismatch`.
- Clocks: the Server allows `ClockSkewSeconds` (default 60). Fix the clock of the server that is wrong; do not raise the number above a few minutes.
- The user lands on an error page after the identity provider: the identity provider sent the answer to another ACS URL. The URL must match exactly.

## How to implement or extend

The flow is described in [how the SAML flow is validated](../external-identity.md#how-the-saml-flow-is-validated). Tests: `SamlSourceTests.cs` with `FakeSamlIdp`.
