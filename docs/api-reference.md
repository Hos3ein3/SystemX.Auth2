# API reference

## What this is

The JSON endpoints of the Server for client applications. Route strings are in `Auth2Routes` (Contracts). The OpenID Connect endpoints under `/connect/*` are in [authentication-flows.md](authentication-flows.md).

## When to use it

When you call the server from your own backend (or you write a client). The `SystemX.Auth2.Client` package wraps these calls.

## Rules for every endpoint

- **Token.** `Authorization: Bearer <access token>`. Two kinds:
  - **Machine token**: `grant_type=client_credentials`. For the endpoints marked *client*.
  - **User token**: the token of a signed-in user. For the endpoints marked *user*. A machine token is refused on a user endpoint (`403 forbidden`) and the other way round.
- **The application is the audience of the token** (its client id). The project is the one of that application. Neither is ever read from the body.
- **Scope.** Each endpoint needs one scope. The application must allow it in `the allowed scopes of the application`.
- **Errors** have the same shape. `code` is stable. `message` is localized (`Accept-Language`). `details` is optional.

```json
{ "code": "password_too_short", "message": "The password must be at least 10 characters long.",
  "details": { "errors": ["..."], "codes": ["password_too_short"] } }
```

| Status | When |
|---|---|
| 400 | Validation or a business rule (`validation_failed`, `password_*`, `invalid_otp`, `invalid_token` for a bad confirmation or reset token, `invalid_credentials`, `login_method_not_allowed`) |
| 401 | No token or a bad token (`invalid_token`) |
| 403 | Wrong kind of token (`forbidden`), missing scope (`insufficient_scope`), `registration_disabled`, `project_disabled`, `two_factor_not_allowed` |
| 404 | The application of the token does not exist (`project_not_found`) |
| 403 | The project or the application is disabled (`access_denied`; the audit log has the real reason) |
| 409 | `conflict` (for example two-factor is already on), `server_mode_mismatch`, `project_key_or_name_mismatch` |
| 423 | `locked_out` |
| 429 | `rate_limited` ([security.md](security.md)) |

See [error-codes.md](error-codes.md).

## Account endpoints

| Method + route | Token | Scope | Body | Answer |
|---|---|---|---|---|
| `POST /api/auth2/register` | client | `auth2.register` | `RegisterRequest` | `200 RegisterResponse` |
| `POST /api/auth2/join/complete` | client | `auth2.register` | `JoinCompleteRequest` | `204` |
| `POST /api/auth2/otp/request` | client | `auth2.login` | `OtpRequest` | `202` (always the same) |
| `POST /api/auth2/password/forgot` | client | `auth2.login` | `ForgotPasswordRequest` | `202` (always the same) |
| `POST /api/auth2/password/reset` | client | `auth2.login` | `ResetPasswordRequest` | `204` |
| `POST /api/auth2/email/confirm` | client | `auth2.login` | `ConfirmEmailRequest` | `204` |
| `POST /api/auth2/email/confirm/resend` | client | `auth2.login` | `ResendEmailConfirmationRequest` | `202` (always the same) |
| `POST /api/auth2/phone/confirm` | client | `auth2.login` | `ConfirmPhoneRequest` | `204` |
| `POST /api/auth2/password/change` | user | `auth2.me` | `ChangePasswordRequest` | `204` |
| `POST /api/auth2/user-name/change` | user | `auth2.me` | `ChangeUserNameRequest` | `204` (needs the current password when the person has one) |
| `POST /api/auth2/email/change` | user | `auth2.me` | `ChangeEmailRequest` | `202`: a link goes to the **new** address, nothing changes yet |
| `POST /api/auth2/email/change/confirm` | user | `auth2.me` | `ConfirmEmailChangeRequest` | `204`: the address is replaced and confirmed (`invalid_token` for a wrong, used or foreign token) |
| `POST /api/auth2/phone/change` | user | `auth2.me` | `ChangePhoneNumberRequest` | `202`: an SMS code goes to the **new** number, nothing changes yet (`rate_limited` when asked again too soon) |
| `POST /api/auth2/phone/change/confirm` | user | `auth2.me` | `ConfirmPhoneChangeRequest` | `204`: the number is replaced and confirmed (`invalid_otp` for a wrong code) |
| `POST /api/auth2/2fa/setup` | user | `auth2.me` | none | `200 TwoFactorSetupResponse` |
| `POST /api/auth2/2fa/enable` | user | `auth2.me` | `TwoFactorEnableRequest` | `200 TwoFactorEnableResponse` (recovery codes, once) |
| `POST /api/auth2/2fa/disable` | user | `auth2.me` | `TwoFactorDisableRequest` | `204` |
| `POST /api/auth2/2fa/recovery-codes` | user | `auth2.me` | `RecoveryCodesRequest` | `200 RecoveryCodesResponse` (once) |
| `GET /api/auth2/me` | user | `auth2.me` | none | `200 CurrentUserInfo` |

## Client endpoints (handshake, sync, pull)

| Method + route | Token | Scope | Body | Answer |
|---|---|---|---|---|
| `POST /api/auth2/client/handshake` | client | `auth2.client.handshake` | `ClientHandshakeRequest` (the optional `healthUrl`, or `healthPort` + `healthScheme` + `healthPath`, registers the address for the two-way health check, [client-health-check.md](client-health-check.md#how-the-server-finds-the-client); none of them removes it) | `200 ClientHandshakeResponse`, `409` on mismatch |
| `POST /api/auth2/permissions/sync` | client | `auth2.permissions.sync` | `PermissionSyncRequest` | `200 SyncResponse` |
| `POST /api/auth2/roles/sync` | client | `auth2.roles.sync` | `RoleSyncRequest` | `200 SyncResponse` |
| `GET /api/auth2/permissions` | client | `auth2.permissions.sync` | none | `200 PermissionDto[]` |
| `GET /api/auth2/roles` | client | `auth2.roles.sync` | none | `200 RoleDto[]` |
| `GET /api/auth2/groups` | client | `auth2.groups.read` | none | `200 GroupDto[]` |
| `POST /api/auth2/groups/sync` | client | `auth2.groups.sync` | `GroupSyncRequest` | `200 SyncResponse` (needs `Clients.GroupSyncAllowed`) |
| `POST /api/auth2/scopes/sync` | client | `auth2.scopes.sync` | `ScopeSyncRequest` | `200 SyncResponse` |
| `GET /api/auth2/scopes` | client | `auth2.scopes.sync` | none | `200 ScopeDto[]` |
| `GET /api/auth2/users/{id}/access` | client | `auth2.admin` | none | `200 CurrentUserInfo` (no scopes) |

Details: [handshake-and-startup.md](handshake-and-startup.md), [permission-sync.md](permission-sync.md). `users/{id}/access` answers `404` when the user does not exist or is not a member of the token's application. The scope `auth2.admin` is not in the default `AllowedScopes`: add it only for trusted back-end programs.

The DTOs are in `SystemX.Auth2.Contracts.Auth`. JSON uses camelCase.

### Example: register

```http
POST /api/auth2/register
Authorization: Bearer <client token with auth2.register>
Content-Type: application/json

{ "userName": null, "email": "sara@example.com", "phoneNumber": null, "password": "A-Strong-Pass-1",
  "firstName": "Sara", "lastName": "K", "returnUrl": "https://app.example.com/signin-oidc" }
```

```json
{ "userId": "0198...", "userWasCreated": true, "joinedProject": true, "confirmationRequired": false }
```

For a person who exists in another application: `userWasCreated=false`, `joinedProject=false`, `confirmationRequired=true`. A proof code went to their confirmed email or phone. Finish with `join/complete`. See [authentication-flows.md](authentication-flows.md).

`returnUrl` goes into the email links only when it is a local path or exactly one of the application's the redirect URIs of the application.

### Example: me

`GET /api/auth2/me` returns the roles, permissions, groups, scopes and access version of the token's user in the token's application. It never takes a user id. Details and errors: [authorization.md](authorization.md).

### Example: change password

```http
POST /api/auth2/password/change
Authorization: Bearer <user token with auth2.me>

{ "currentPassword": "old", "newPassword": "A-New-Strong-Pass-1" }
```

`204 No Content`. The other sessions of this user in this application end. The calling session stays.

## External sign-in endpoints

| Method and route | Auth | What |
|---|---|---|
| `GET /connect/external/providers?client_id=&hint=` | none | Enabled providers (`providers`, `suggestedProvider`). No secrets |
| `POST /connect/external/challenge` | anti-forgery | Form `provider`, `returnUrl`, `hint`, `client_id`. Redirects to the provider (or JSON with `postAction`/`postFields` for the SAML POST binding) |
| `GET/POST /callback/login/{slug}/{name}` | state cookie | OIDC callback (page) |
| `POST /saml/{slug}/{name}/acs`, `GET .../metadata`, `GET .../slo` | cookie / none / signed request | SAML (pages) |
| `POST /account/passkey/creation-options`, `/account/passkey/register` | session + anti-forgery header | Register a passkey |
| `POST /account/passkey/request-options`, `/account/passkey/signin` | none, rate limited | Sign in with a passkey |
| `GET /account/passkeys`, `PATCH`/`DELETE /account/passkeys/{id}` | session | Manage passkeys |

Details and JSON examples: [ui-handover.md](ui-handover.md). The admin routes for providers are in [admin-api.md](admin-api.md#external-identity-routes).

## Admin endpoints

All routes under `/api/auth2/admin` need the token of a signed-in admin with scope `auth2.admin`. See [admin-api.md](admin-api.md).

## Health

| Endpoint | Meaning |
|---|---|
| `GET /health/live` | The process is up |
| `GET /health/ready` | The process can serve (database reachable). See [observability.md](observability.md) and [deployment-and-scaling.md](deployment-and-scaling.md) |

## Related pages

- [Credential scope](credential-scope.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Server settings](server-settings.md)
- [Email and SMS](email-and-sms.md)
- [The key ring and its snapshot](key-ring.md)
- [Contracts reference](contracts-reference.md)
- [Client package reference](client-reference.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
- [Server reference](server-reference.md)
- [Documentation index](index.md)
- [Calling the Server: `IAuth2ApiClientService`](client-api-service.md)
- [Client setup](client-setup.md)
- [Registering the client: `Auth2ClientServiceCollectionExtensions`](client-builder.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Localization](localization.md)
