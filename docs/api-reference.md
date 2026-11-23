# API reference

## What this is

The JSON endpoints of the Server for client applications. Route strings are in `Auth2Routes` (Contracts). The OpenID Connect endpoints under `/connect/*` are in [authentication-flows.md](authentication-flows.md).

## When to use it

When you call the server from your own backend (or you write a client). The `SystemX.Auth2.Client` package (phase 11) wraps these calls.

## Rules for every endpoint

- **Token.** `Authorization: Bearer <access token>`. Two kinds:
  - **Machine token**: `grant_type=client_credentials`. For the endpoints marked *client*.
  - **User token**: the token of a signed-in user. For the endpoints marked *user*. A machine token is refused on a user endpoint (`403 forbidden`) and the other way round.
- **The application is the audience of the token.** It is never read from the body.
- **Scope.** Each endpoint needs one scope. The application must allow it in `Clients.AllowedScopes`.
- **Errors** have the same shape. `code` is stable. `message` is localized (`Accept-Language`). `details` is optional.

```json
{ "code": "password_too_short", "message": "The password must be at least 10 characters long.",
  "details": { "errors": ["..."], "codes": ["password_too_short"] } }
```

| Status | When |
|---|---|
| 400 | Validation or a business rule (`validation_failed`, `password_*`, `invalid_otp`, `invalid_token` for a bad confirmation or reset token, `invalid_credentials`, `login_method_not_allowed`) |
| 401 | No token or a bad token (`invalid_token`) |
| 403 | Wrong kind of token (`forbidden`), missing scope (`insufficient_scope`), `registration_disabled`, `application_disabled`, `two_factor_not_allowed` |
| 404 | The application of the token does not exist (`application_not_found`) |
| 409 | `conflict` (for example two-factor is already on) |
| 423 | `locked_out` |
| 429 | `rate_limited` (rate limiting arrives in phase 10) |

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
| `POST /api/auth2/2fa/setup` | user | `auth2.me` | none | `200 TwoFactorSetupResponse` |
| `POST /api/auth2/2fa/enable` | user | `auth2.me` | `TwoFactorEnableRequest` | `200 TwoFactorEnableResponse` (recovery codes, once) |
| `POST /api/auth2/2fa/disable` | user | `auth2.me` | `TwoFactorDisableRequest` | `204` |
| `POST /api/auth2/2fa/recovery-codes` | user | `auth2.me` | `RecoveryCodesRequest` | `200 RecoveryCodesResponse` (once) |
| `GET /api/auth2/me` | user | `auth2.me` | none | `200 CurrentUserInfo` |

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
{ "userId": "0198...", "userWasCreated": true, "joinedApplication": true, "confirmationRequired": false }
```

For a person who exists in another application: `userWasCreated=false`, `joinedApplication=false`, `confirmationRequired=true`. A proof code went to their confirmed email or phone. Finish with `join/complete`. See [authentication-flows.md](authentication-flows.md).

`returnUrl` goes into the email links only when it is a local path or exactly one of the application's `AllowedRedirectUris`.

### Example: me

`GET /api/auth2/me` returns the roles, permissions, groups, scopes and access version of the token's user in the token's application. It never takes a user id. Details and errors: [authorization.md](authorization.md).

### Example: change password

```http
POST /api/auth2/password/change
Authorization: Bearer <user token with auth2.me>

{ "currentPassword": "old", "newPassword": "A-New-Strong-Pass-1" }
```

`204 No Content`. The other sessions of this user in this application end. The calling session stays.

## Not built yet

| Endpoint | Phase |
|---|---|
| `POST /api/auth2/client/handshake` | 8 |
| `POST /api/auth2/permissions/sync`, `/roles/sync`; `GET /permissions`, `/roles`, `/groups`; `GET /users/{id}/access` | 8 |
| `/api/auth2/admin/...` | 9 |
| `GET /health/live`, `/health/ready` | 10 |
