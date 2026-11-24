# Social login with OpenIddict.Client.WebIntegration

## What this is

Google, Microsoft, GitHub, Apple and Facebook sign-in is done by **OpenIddict.Client** with its **WebIntegration** package (`OpenIddict.Client.WebIntegration`, version 7.9.0). The package knows the addresses, scopes and special rules of each service, so the Server only needs your client id and secret. Every other OpenID Connect provider (Keycloak, Okta, Auth0 ...) uses the same engine as a **generic** registration with an `Authority`: see [generic-oidc.md](generic-oidc.md).

You do **not** write code to turn it on. The Server already references the three packages and sets the client up once at start:

```csharp
// Infrastructure/OpenIddict/OpenIddictSetup.cs
.AddClient(o =>
{
    o.AllowAuthorizationCodeFlow();
    o.DisableTokenStorage();                                  // upstream tokens are not stored
    o.SetRedirectionEndpointUris("/callback/login/_unused");  // OpenIddict needs one endpoint to start
    o.UseAspNetCore().EnableRedirectionEndpointPassthrough();
    o.UseSystemNetHttp();
    o.UseWebProviders();                                       // <- WebIntegration
})
```

The providers themselves are **data**: one entry per provider in the settings of a project. `ExternalClientRegistrations` turns every enabled entry into an OpenIddict registration and builds them again when the list changes. No restart, no deployment.

## When to use it

- You want "Sign in with Google / Microsoft / GitHub / Apple / Facebook" for a project.
- Two projects need different Google accounts or different client ids: each project has its own entry and its own callback URL.

## How to enable it

1. **Register the Server at the provider.** Use the guide of the provider ([Google](google.md), [Microsoft](microsoft.md), [GitHub](github.md), [Apple](apple.md), [Facebook](facebook.md)). Everywhere you enter the **callback URL** of the provider entry:

   ```
   https://auth.example.com/callback/login/{slug}/{name}
   ```

   `{slug}` is the project key (`default` in single-project mode) and `{name}` is the name you give the entry in step 3. The URL must match exactly (scheme, host, slug, name).

2. **Allow external sign-in in the project.** `External` must be in `Methods.AllowedLoginMethods`. Read the settings, add the value and write them back with the version you read:

   ```http
   GET /api/auth2/admin/projects/{projectId}/settings          -> ETag: "v7"
   PUT /api/auth2/admin/projects/{projectId}/settings          If-Match: "v7"
   { ... "methods": { "allowedLoginMethods": ["EmailPassword", "External"] } ... }
   ```

   `Methods` is a global-admin setting. The generic settings route never changes the provider list, so this step and the next one are separate.

3. **Add the provider** (global admin or project admin of that project). The secret is sent in plain text once, encrypted by the Server and never returned:

   ```http
   POST /api/auth2/admin/projects/{projectId}/external-providers
   { "kind": "oidc", "name": "google", "type": "Google", "displayName": "Sign in with Google", "enabled": true,
     "clientId": "1234-abc.apps.googleusercontent.com", "clientSecret": "GOCSPX-..." }
   ```

   The answer lists `urls.callback` (the exact URL for step 1), `secretIsSet` and `warnings`. `type` is one of `Google`, `Microsoft`, `GitHub`, `Apple`, `Facebook`. Extra fields per type (`HostedDomain`, `TenantId`, `AllowedOrganizations`, `TeamId`/`KeyId`/`privateKey`, `ApiVersion`): [external-identity.md](../external-identity.md#oidc-kinds-kind-oidc).

4. **Test it** without a browser: `POST /api/auth2/admin/projects/{projectId}/external-providers/{name}/test`. For these providers it reads the discovery or the fixed endpoints of the service and answers `{ ok, errorCode, reason, details }`.

## How to use it

**The user.** The button must send the browser to the Server. The hosted sign-in pages of the Server do the rest: provider, back to `/callback/login/{slug}/{name}`, then the same login pipeline as a password (lockout, project membership, two-factor rule, consent), then your application gets normal Server tokens with the extra claims `idp` (the provider name) and `amr`.

**Your page.** The login page of the Server does not draw the provider buttons yet (see the note in [ui-handover.md](../ui-handover.md#external-sign-in-pages)). Until it does, get the list and post the start form yourself:

```http
GET  /connect/external/providers?client_id=mvc-sample          -> [ { "name": "google", "displayName": "Sign in with Google", ... } ]
POST /connect/external/challenge   (form: provider=google, client_id=mvc-sample, returnUrl=/, anti-forgery token)   -> 302 to the provider
```

**The sample application.** `samples/SystemX.Auth2.Samples.Mvc` uses the hosted UI (`.UseHostedUi()`). Its Sign In link goes to `/auth2/login`, which starts the authorization code flow on the Server; after the provider has answered, the sample receives its normal tokens. Which provider signed the user in is `ICurrentClient.Idp` ([client-current-user.md](../client-current-user.md)).

**Linking rules.** A returning user is found by the provider's stable id (`sub`). A new user is matched to an existing account only by a **verified** email. Google, Microsoft and Apple are trusted by default; for the others a global admin must turn on `TrustProviderEmailVerification`. See [external-identity.md](../external-identity.md#signing-in-linking-and-unlinking).

## Changing or removing a provider

`PUT .../external-providers/{name}` needs `If-Match: "v{settingsVersion}"`. An empty or missing secret keeps the old one; `POST .../{name}/rotate-secret` replaces it. `DELETE` removes the entry. The change reaches every Server instance within seconds ([external-identity.md](../external-identity.md#changing-providers-at-runtime)).

## Common problems

See [troubleshooting.md](troubleshooting.md). The most common: the callback URL differs by one character (`redirect_uri_mismatch`), the project does not have `External` in `AllowedLoginMethods`, or the clock of the Server is wrong (identity token lifetime).

## How to implement or extend

- Registrations: `ExternalClientRegistrations.Build` has one `case` per type (`web.AddGoogle(...)`, `AddMicrosoft`, `AddGitHub`, `AddFacebook`, `AddApple`). To add a service that WebIntegration already has, add the type and a `case`: the worked example "GitLab" is in [external-identity.md](../external-identity.md#how-to-add-a-new-oidc-provider-type-worked-example).
- The flow runs in `OpenIddictIdentitySource` (challenge, callback, rules of each type). Why the library and what was learned: [ADR-096](../decisions.md).
- Tests: `OidcSourceTests`, `ExternalClientIntegrationTests`, `OpenIddictClientSpikeTests` (a WebIntegration provider from a row, two projects, mixed-up and replayed callbacks refused), with `FakeOidcProvider`.
