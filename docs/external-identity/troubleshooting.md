# Troubleshooting external sign-in

Find the **reason** in the audit log. The user only sees a stable error code ([error-codes.md](../error-codes.md)); the audit event has `code` and `reason`.

| Audit action | Where |
|---|---|
| `external.login.failed` | OIDC, social, brokering, and refusals of the decision table |
| `saml.assertion.rejected` | SAML (every failed check) |
| `ldap.login.failed` | LDAP |
| `external.register`, `external.login.linked`, `external.login.success`, `external.mapping.applied` | Good events |

## General

| Symptom | Cause and fix |
|---|---|
| The login page has no button | The provider is not `Enabled`, or the project is disabled. Check `GET /connect/external/providers?client_id=...` |
| `login_method_not_allowed` after a good sign-in | Add `External` (or `Ldap`) to `Methods.AllowedLoginMethods` |
| `external_email_exists_not_verified` | A user with this email exists and the provider cannot prove the email. The user signs in the usual way and links the provider in *Manage external logins*. Or (global admin) turn on `TrustProviderEmailVerification` if you trust that provider's emails |
| `external_registration_not_allowed` | `AllowRegister` is off, or `Registration.JoinMode` is not `AutoJoin` |
| Change not visible on another instance | Wait for the poll (`Auth2:ExternalProviders:PollSeconds`, default 30) or use Redis |
| `redirect_uri_mismatch` at the provider | The URL at the provider must be exactly `.../callback/login/{slug}/{name}`. Slug is the project key, or `default` in single mode |
| `state_replayed`, `state_expired`, `state_missing` | The user used the back button, waited more than 10 minutes, or the state cookie was blocked (third-party cookie rules, a different host name). Start again |
| Cookie not set behind a proxy | Set `Auth2:Issuer` and forward headers; the state cookie is `SameSite=None; Secure` on HTTPS |
| Provider cannot be reached | The Server refuses private network addresses (SSRF guard). Use a public address or, for tests only, `Auth2:ExternalProviders:AllowPrivateNetworks` |

## SAML

| Reason | Meaning |
|---|---|
| `saml_assertion_count`, `saml_duplicate_id` | The XML has two assertions or repeated ids (an attack or a broken identity provider) |
| `assertion_not_signed`, `saml_response_not_signed` | The identity provider must sign the assertion (or the response when `WantResponseSigned`) |
| `saml_validation_failed` | Signature wrong, certificate not trusted, or wrong audience. Check the certificate in the metadata |
| `destination_mismatch`, `recipient_mismatch` | The identity provider uses another ACS URL |
| `in_response_to_mismatch`, `saml_in_response_to_missing`, `saml_state_missing` | The answer is not for a request of this browser. IdP-initiated needs `AllowIdpInitiated` |
| `assertion_expired`, `assertion_not_yet_valid` | Clocks differ more than `ClockSkewSeconds` |
| `saml_idp_material_invalid` | The identity provider metadata or certificate cannot be read |

## LDAP

| Symptom | Cause and fix |
|---|---|
| `ldap_unavailable` | The host or port is wrong, TLS failed (unknown CA, wrong name, expired certificate), the service account is wrong, or a timeout. Run the admin "test connection". The reason is `connect_failed`, `service_bind_failed`, `search_failed` or `timeout` |
| `invalid_credentials` for everyone | The filter matches nobody (check `userSearchBase`, the object class and the attribute), or `{0}` is wrong |
| `invalid_credentials` for one user | Wrong password, or two entries match the filter (`ambiguous_user`) |
| Groups are empty | Check `groupSearchBase`, `groupFilter` (use the admin "test user lookup"), and `nestedGroups` |
| User signs in but has no roles | `groupMappingMode` is `None`, or no mapping matches ([group-mapping.md](group-mapping.md)) |

## Related pages

- [Troubleshooting](../troubleshooting.md)
- [Getting started](../getting-started.md)
- [Observability](../observability.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](../external-identity.md)
- [Social login with OpenIddict.Client.WebIntegration](social-webintegration.md)
- [SAML 2.0: enable and use](saml.md)
- [LDAP and Active Directory: enable and use](ldap.md)
- [Passkeys](passkeys.md)
- [Brokering: your app trusts only the Server](brokering.md)
- [UI handover](../ui-handover.md)
- [Admin JSON API](../admin-api.md)
- [Sign in with Google](google.md)
- [Sign in with Microsoft](microsoft.md)
- [Sign in with GitHub](github.md)
- [Sign in with Apple](apple.md)
- [Sign in with Facebook](facebook.md)
- [Generic OpenID Connect (Keycloak, Okta, Auth0 and others)](generic-oidc.md)
- [SAML 2.0: generic identity provider](saml-generic.md)
- [SAML with Microsoft Entra ID](saml-entra-id.md)
- [SAML with Okta](saml-okta.md)
- [SAML with Active Directory Federation Services (ADFS)](saml-adfs.md)
- [SAML with Keycloak](saml-keycloak.md)
- [SAML certificate rotation](saml-certificate-rotation.md)
- [LDAP login with OpenLDAP (and other LDAP servers)](ldap-openldap.md)
- [LDAP login with Active Directory](ldap-active-directory.md)
- [Authentication flows](../authentication-flows.md)
- [Server settings](../server-settings.md)
- [Documentation index](../index.md)
