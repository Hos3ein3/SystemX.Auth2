# SystemX.Auth2 documentation

## What this is

The map of all documentation pages. Every page is listed here and every page links back here from its "Related pages" section.

## When to use it

Start with **Start here** if you are new. Use the other groups to find the page for one job. The repository [README](../README.md) is the short version; the packages have their own READMEs: [Client](../src/SystemX.Auth2.Client/README.md) and [Server](../src/SystemX.Auth2.Server/README.md).

## Start here

| Page | Topic |
|---|---|
| [getting-started.md](getting-started.md) | Run the Server and the two samples |
| [concepts.md](concepts.md) | Project, application, membership, settings, tokens |
| [comparison-and-limits.md](comparison-and-limits.md) | What is and is not supported |
| [feature-status.md](feature-status.md) | What works today |
| [roadmap.md](roadmap.md) | What is not built yet and the known limits |

## Run and configure the Server

| Page | Topic |
|---|---|
| [server-setup.md](server-setup.md) | Run it, configuration keys, commands, production guard |
| [server-settings.md](server-settings.md) | Every project and application setting |
| [seeding.md](seeding.md) | The first admin and the sample data |
| [credential-scope.md](credential-scope.md) | Where passwords and two-factor live |
| [email-and-sms.md](email-and-sms.md) | Email and SMS providers per project and application (SMTP, Google) |
| [localization.md](localization.md) | Texts, culture order, add a language |
| [deployment-and-scaling.md](deployment-and-scaling.md) | Several instances, Redis, probes |
| [redis.md](redis.md) | Enable Redis: what it holds, why, failure behaviour, the Client access-data cache |
| [observability.md](observability.md) | Logs, traces, metrics, health |
| [security.md](security.md) | Threat model, rate limits, keys, checklist |
| [key-ring.md](key-ring.md) | The key ring and its in-memory snapshot: keys, rotation, use |
| [identity-stores.md](identity-stores.md) | The Identity stores and managers (`Persistence.Identity`): credentials, roles, lockout per project |
| [database-tables.md](database-tables.md) | Every database table: what it holds, who writes it, columns |

## How sign-in and access work

| Page | Topic |
|---|---|
| [authentication-flows.md](authentication-flows.md) | Login, tokens, grants, register, join, OTP, reset, two-factor, consent |
| [enums-reference.md](enums-reference.md) | Every enum: values, where it is used, which value to choose and why |
| [two-factor.md](two-factor.md) | Two-factor sign-in (authenticator app): enable, `TwoFactorService`, the sign-in step, recovery codes, admin reset |
| [password-history.md](password-history.md) | Old password hashes, no reuse, password expiry: settings, behaviour, examples |
| [authorization.md](authorization.md) | Permissions, roles, groups, effective access, `/me` |
| [permission-sync.md](permission-sync.md) | Push and pull of permissions, roles, scopes and groups |
| [handshake-and-startup.md](handshake-and-startup.md) | The startup check and the server mode |
| [machine-to-machine.md](machine-to-machine.md) | Client-credentials tokens |

## Use the Client package

| Page | Topic |
|---|---|
| [client-setup.md](client-setup.md) | Add the package, options, UI modes, handshake |
| [client-builder.md](client-builder.md) | `Auth2ClientServiceCollectionExtensions` in depth: `AddAuth2Client`, every `Enable...` and `Use...` method, every option, `UseAuth2` |
| [client-authorization.md](client-authorization.md) | Attributes, Minimal API extensions, API authentication |
| [client-current-user.md](client-current-user.md) | `ICurrentUser`, `ICurrentClient`, the cache and the stores |
| [client-reference.md](client-reference.md) | Every public type, member and overload of the Client package |

## External identity

| Page | Topic |
|---|---|
| [external-identity.md](external-identity.md) | Overview: social login, OIDC, SAML, LDAP, passkeys, brokering |
| [external-identity/social-webintegration.md](external-identity/social-webintegration.md) | Enable and use social login (OpenIddict.Client.WebIntegration) |
| [external-identity/saml.md](external-identity/saml.md) | Enable and use SAML 2.0 |
| [external-identity/ldap.md](external-identity/ldap.md) | Enable and use LDAP and Active Directory |
| [external-identity/passkeys.md](external-identity/passkeys.md) | Enable and use passkeys |
| [external-identity/brokering.md](external-identity/brokering.md) | Brokering: the Server between your app and a provider |
| [external-identity/group-mapping.md](external-identity/group-mapping.md) | Turn provider groups into roles, groups and permissions |
| [external-identity/troubleshooting.md](external-identity/troubleshooting.md) | Symptom, cause, fix for external sign-in |

## Setup guides per provider

| Page | Topic |
|---|---|
| [external-identity/google.md](external-identity/google.md) | Sign in with Google |
| [external-identity/microsoft.md](external-identity/microsoft.md) | Sign in with Microsoft |
| [external-identity/github.md](external-identity/github.md) | Sign in with GitHub |
| [external-identity/apple.md](external-identity/apple.md) | Sign in with Apple |
| [external-identity/facebook.md](external-identity/facebook.md) | Sign in with Facebook |
| [external-identity/generic-oidc.md](external-identity/generic-oidc.md) | Generic OpenID Connect (Keycloak, Okta, Auth0 and others) |
| [external-identity/saml-generic.md](external-identity/saml-generic.md) | SAML 2.0: generic identity provider |
| [external-identity/saml-entra-id.md](external-identity/saml-entra-id.md) | SAML with Microsoft Entra ID |
| [external-identity/saml-okta.md](external-identity/saml-okta.md) | SAML with Okta |
| [external-identity/saml-adfs.md](external-identity/saml-adfs.md) | SAML with Active Directory Federation Services (ADFS) |
| [external-identity/saml-keycloak.md](external-identity/saml-keycloak.md) | SAML with Keycloak |
| [external-identity/saml-certificate-rotation.md](external-identity/saml-certificate-rotation.md) | SAML certificate rotation |
| [external-identity/ldap-openldap.md](external-identity/ldap-openldap.md) | LDAP login with OpenLDAP (and other LDAP servers) |
| [external-identity/ldap-active-directory.md](external-identity/ldap-active-directory.md) | LDAP login with Active Directory |

## Admin and UI

| Page | Topic |
|---|---|
| [admin-api.md](admin-api.md) | Global and project admin JSON API |
| [ui-handover.md](ui-handover.md) | Pages and JSON contracts for a UI |
| [api-reference.md](api-reference.md) | Public routes |
| [error-codes.md](error-codes.md) | Stable error codes |
| [contracts-reference.md](contracts-reference.md) | Every type of the Contracts package |
| [server-reference.md](server-reference.md) | The public surface of the Server: registration, services, settings classes |

## About the project

| Page | Topic |
|---|---|
| [architecture.md](architecture.md) | Solution layout and layers |
| [decisions.md](decisions.md) | Design decisions (ADR list) |
| [contributing-workflow.md](contributing-workflow.md) | How we work: code, tests, docs |
| [testing.md](testing.md) | How to run the tests |
| [troubleshooting.md](troubleshooting.md) | Symptom, cause, fix |
| [gateway.md](gateway.md) | The gateway project (not built) |

## Related pages

- [Root README](../README.md)
- [Changelog](../CHANGELOG.md)
- [Reference generator](../tools/docs/README.md)
