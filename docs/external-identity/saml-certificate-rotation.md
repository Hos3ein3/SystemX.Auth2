# SAML certificate rotation

SAML trust is based on certificates. They expire and must be replaced **without** breaking sign-in.

## How to use (for operators)

### The identity provider's signing certificate (they sign, the Server checks)

- **With a metadata URL** (recommended): the Server reads the metadata, caches it for 6 hours and uses all certificates in it. When the identity provider publishes the new certificate next to the old one, the Server accepts both. If the metadata cannot be fetched, the **last good copy** stays in use.
- **With manual certificates**: `idpSigningCertificates` holds **up to two** certificates. Add the new one first, wait until the identity provider switches, then remove the old one.
- The admin API warns when a certificate expires within 30 days.

### The Server's own certificate (used to sign requests, decrypt assertions, sign metadata)

1. Make a new certificate with the admin route (`POST .../saml/generate-sp-certificate`, see [admin-api.md](../admin-api.md#external-identity-routes)). The Server stores it encrypted and shows only the public part and the expiry.
2. The metadata at `/saml/{slug}/{name}/metadata` now has the new public certificate. Give it to the identity provider (import the metadata again).
3. Check one sign-in. The old certificate is not kept: if the identity provider still encrypts with the old one, assertions fail until it has the new metadata. Plan the change for a quiet moment.

## How to implement or extend

`SamlProviderMaterial` (`Infrastructure/External/SamlMaterial.cs`) reads and caches the identity provider material; `SamlSpCertificates` makes and reads the SP certificate; `SamlIdpMaterial.ExpiringWithin` powers the warning. Tests: `SamlMetadataTests`, `SamlOptionsTests.TwoIdpCertificates_BothWork_ForRotation`.

## Related pages

- [SAML 2.0: enable and use](saml.md)
- [SAML 2.0: generic identity provider](saml-generic.md)
- [SAML with Microsoft Entra ID](saml-entra-id.md)
- [SAML with Okta](saml-okta.md)
- [SAML with Active Directory Federation Services (ADFS)](saml-adfs.md)
- [SAML with Keycloak](saml-keycloak.md)
- [Group mapping](group-mapping.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Documentation index](../index.md)
