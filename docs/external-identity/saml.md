# SAML 2.0: enable and use

## What this is

The Server is a SAML 2.0 **service provider** (SP). People of a company sign in with their company **identity provider** (IdP: Entra ID, Okta, ADFS, Keycloak ...). The Server checks the assertion, finds or creates the local user, and then signs the user in with the same pipeline as a password login. Your applications keep using OpenID Connect with the Server; the Server is **not** a SAML IdP.

Library: ITfoxtec.Identity.Saml2 ([ADR-086](../decisions.md)). How every check works: [external-identity.md](../external-identity.md#how-the-saml-flow-is-validated).

## When to use it

A customer says "our users must sign in with our company directory" and their IdP speaks SAML. One SAML provider per customer company, inside the project of that customer.

## How to enable it

1. **Give the IdP three values** (replace `{slug}` with the project key, `default` in single-project mode, and `{name}` with the provider name you will use in step 4):

   | At the IdP it is called | Value |
   |---|---|
   | Entity ID / Identifier / Audience | `https://auth.example.com/saml/{slug}/{name}` (or your `SpEntityId`) |
   | ACS / Reply URL / Single sign-on URL (binding **POST**) | `https://auth.example.com/saml/{slug}/{name}/acs` |
   | Metadata (many IdPs can import it) | `https://auth.example.com/saml/{slug}/{name}/metadata` |

   Use `https` and the real issuer. The admin API shows these URLs in `urls` after step 4.
2. **At the IdP**, send these attributes and sign the assertion: email, first name, last name and (optional) a groups attribute. NameID *persistent* is best. Guides: [Entra ID](saml-entra-id.md), [Okta](saml-okta.md), [ADFS](saml-adfs.md), [Keycloak](saml-keycloak.md), [any other](saml-generic.md).
3. **Allow it in the project**: `External` in `Methods.AllowedLoginMethods` (read the settings, add it, write them back with `If-Match`; see step 2 of [social-webintegration.md](social-webintegration.md#how-to-enable-it)).
4. **Add the provider**:

   ```http
   POST /api/auth2/admin/projects/{projectId}/external-providers
   { "kind": "saml", "name": "corp-saml", "type": "Saml", "displayName": "Company SSO", "enabled": true,
     "idpMetadataUrl": "https://idp.corp.com/metadata", "nameIdFormat": "Persistent",
     "groupsAttribute": "groups", "attributeMappings": { "mailaddress": "email" }, "emailDomains": ["corp.com"] }
   ```

   Without a metadata URL send `idpMetadataXml`, or `idpEntityId` + `idpSsoUrl` + `idpSigningCertificates` by hand. The Server refuses a provider whose assertion **and** response are both unsigned.
5. **Check it**: `POST .../external-providers/corp-saml/test` reads the metadata and reports the entity id, the endpoints and the days until the certificate expires. `warnings` in the list also tell you about a certificate that ends in 30 days.

## How to use it

- **The user** goes to the Server's sign-in, picks the company button, signs in at the IdP, and comes back to `/saml/{slug}/{name}/acs`. Then the usual result: Server tokens with `idp=corp-saml`.
- **Home-realm discovery**: with `emailDomains: ["corp.com"]`, `GET /connect/external/providers?hint=sara@corp.com` suggests this provider (`suggestedProvider`), so your page can skip the choice.
- **Your page** gets the buttons from `GET /connect/external/providers?client_id=...` and starts the sign-in with `POST /connect/external/challenge`. With the SAML POST binding the answer is `{ postAction, postFields }`: post that form to the IdP. Details and field names: [ui-handover.md](../ui-handover.md#external-sign-in-pages).
- **Groups to roles**: map the groups attribute with [group-mapping.md](group-mapping.md).
- **Single logout** is off by default (`allowSingleLogout`). When on, the IdP sends a signed LogoutRequest to `/saml/{slug}/{name}/slo`.
- **IdP-initiated sign-in** is off by default (`allowIdpInitiated`). Only a global admin may turn it on; the response must then be signed as a whole.
- **Certificates** change about once a year: [saml-certificate-rotation.md](saml-certificate-rotation.md). `POST .../saml/refresh-metadata` reads the IdP metadata again, `POST .../saml/generate-sp-certificate` makes a new SP certificate.

## Common problems

`saml_response_invalid` in the browser: the **reason** is in the audit log (`saml.assertion.rejected`): `audience mismatch`, `destination_mismatch`, `assertion_expired`, `assertion_not_signed`, `in_response_to_mismatch`. See [saml-generic.md](saml-generic.md#common-problems) and [troubleshooting.md](troubleshooting.md).

## How to implement or extend

`SamlIdentitySource` (challenge, ACS checks, SLO), `SamlProviderMaterial` (metadata and certificates), `SamlMetadataBuilder`, pages `Pages/Saml/{Acs,Metadata,Slo}`. Tests: `SamlSourceTests.cs` with `FakeSamlIdp`. To read one more attribute, extend `BuildIdentity`.
