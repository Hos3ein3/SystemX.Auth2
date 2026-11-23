# Brokering: your app trusts only the Server

**Brokering** means your application does not talk to Google, Okta, Entra ID or any other upstream provider. The application only trusts this Server. The Server talks to the upstream provider and gives your app **Server tokens**.

## How to use (for operators)

1. Add the upstream provider to the application (see the setup guide of its type).
2. Add `External` to `Methods.AllowedLoginMethods` of the application.
3. Your app keeps using the normal code flow with PKCE against the Server. Nothing changes in the app.

What your app sees:

- The tokens are signed by the Server (`iss` is the Server). The upstream issuer never appears.
- `idp` says which provider was used (for example `corp-sso`). `amr` says how (`fed`, and `mfa` if the provider reports several factors).
- Roles, groups and permissions come from the Server. They are filled from the upstream claims and groups **only** through mappings ([group-mapping.md](group-mapping.md)). Nothing is copied automatically.
- Upstream access and refresh tokens are **not** stored and **not** passed on, unless you turn on `StoreProviderTokens` for that provider.

### Home-realm discovery

Put the email domains of a company in `EmailDomains` of its provider (for example `contoso.com`). When the login page sends the user's email as `hint`, the Server suggests that provider. A domain can belong to one provider per application.

### Several providers

An application can have up to 20 providers. Two providers may point to the same upstream system only if their names are different; every provider has its own callback URL.

### Logout

Logout is local: the Server session and its tokens end. The upstream session is not touched (no single logout for OpenID Connect).

## How to implement or extend

The path is: source (`OidcIdentitySource`) -> `ExternalFlow` (cookie and redirects) -> `IExternalLoginService` (decision table) -> `ILoginService.ExternalLoginAsync` (the one login pipeline) -> `ITokenIssuer` (`idp` and `amr` claims). To add claims to tokens, change `TokenIssuer.CreateUserPrincipalAsync` and `ApplyDestinations`.
