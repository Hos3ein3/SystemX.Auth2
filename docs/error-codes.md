# Error codes

## What this is

Every API error has a stable `code`. The `message` is localized text. Use the `code` in your program.
The same codes are keys in `Errors.resx` (localization).

## Example

```json
{ "code": "invalid_credentials", "message": "The user name or password is wrong." }
```

## Codes

The codes live in `Auth2ErrorCodes` (Contracts).

| Code | Meaning |
|---|---|
| `invalid_credentials` | Wrong user or wrong password. Same code on purpose (no user enumeration). |
| `locked_out` | The account is locked. |
| `two_factor_required` | A second factor is needed. |
| `not_registered_in_project` | The user does not belong to this project. |
| `project_not_found` | No project or application for this client. |
| `access_denied` | The **public** answer when the project or the application is disabled. It never tells which one. |
| `project_disabled`, `application_disabled` | The real reason. They appear only in the **audit log**, never in an answer to a caller. |
| `project_key_or_name_mismatch` | Handshake: the client does not belong to the project with that key, or the application name does not match. |
| `server_mode_mismatch` | Client and server use different modes (single vs multi). |
| `password_too_short`, `password_too_long`, `password_requires_digit`, `password_requires_lower`, `password_requires_upper`, `password_requires_non_alphanumeric`, `password_requires_unique_chars`, `password_banned` | Password rule failed. The rules come from the application settings. |
| `invalid_user_name`, `invalid_email`, `email_domain_not_allowed`, `invalid_phone_number` | User data rule failed. |
| `user_not_in_project` | The person has no membership row in this project. |
| `email_not_confirmed`, `phone_not_confirmed` | Confirmation is required. Shown only after the password was right. |
| `two_factor_setup_required` | The application requires two-factor and the user has not set it up. |
| `grant_not_allowed` | The grant type is not allowed for this application (Self UI is off). |
| `two_factor_not_enabled`, `two_factor_not_allowed` | Two-factor is off for the user, or the application does not allow authenticator apps. |
| `consent_required` | Consent is needed and the request used `prompt=none`. |
| `login_method_not_allowed` | This login method is off for the application. |
| `registration_disabled` | Registration is closed. |
| `invalid_token` | Token is missing or invalid. |
| `insufficient_scope` | The token lacks a scope. |
| `forbidden` | Not allowed. |
| `validation_failed` | Input is invalid. |
| `not_found` | Record not found. |
| `conflict` | Someone else changed the record. |
| `rate_limited` | Too many requests (HTTP 429). |
| `invalid_otp` | OTP code is wrong or expired. |
| `ownership_proof_required` | Joining a second project needs proof of ownership. |
| `sync_not_allowed` | Sync is off for this application. |
| `invalid_permission_key` | A permission key is invalid. |
| `too_many_items` | Too many items in one request. |
| `invalid_redirect_uri` | Redirect URI is not allowed. |
| `server_error` | Unexpected error. |

## Token endpoint errors

`/connect/token` returns the standard OAuth fields and our stable code:

```json
{ "error": "invalid_grant", "error_description": "invalid credentials", "error_code": "invalid_credentials" }
```

`error` is `invalid_grant` for failed logins, `unauthorized_client` when the grant is off for the client, `invalid_client` for a wrong secret, and `two_factor_required` for the first step of a two-factor login (with a `two_factor_token`). Use `error_code` in your program.

## Texts

Every code has an English text in `Errors.resx`. See [localization.md](localization.md).

## Related pages

- [api-reference.md](api-reference.md)

| `invalid_scope_name` | 400 | A scope name is invalid or reserved (`auth2.*` and the standard OpenID Connect scopes) |
| `external_email_exists_not_verified` | 400 | A user with this email exists, but the provider could not prove the email (or linking is off). Nothing was linked. The user signs in with the usual method and links the provider in *Manage external logins* |
| `external_registration_not_allowed` | 400 | A new account cannot be made from this provider (`AllowRegister` off, or the join mode is not `AutoJoin`) |
| `external_provider_disabled` | 400 | The provider is unknown, disabled, or the application is disabled |
| `external_claims_invalid` | 400 | The provider's answer is missing something or failed a check (state, nonce, signature, tenant, domain). The reason is only in the audit log |
| `cannot_remove_last_login_method` | 400 | The user tried to unlink the last way to sign in |
| `saml_response_invalid` | 400 | A SAML response did not pass validation |
| `saml_replay_detected` | 400 | A SAML response, assertion or request id was used before |
| `ldap_unavailable` | 503 | The directory cannot be reached |
| `ldap_account_disabled` | 400 | The directory account is disabled (shown only when the application reveals it) |
| `passkey_not_allowed` | 400 | Passkeys are off for this application |
| `passkey_limit_reached` | 400 | The user has the most passkeys that are allowed |
| `passkey_blocked_authenticator` | 400 | This authenticator model is on the block list |
- [Authentication flows](authentication-flows.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Credential scope](credential-scope.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [UI handover](ui-handover.md)
- [Server settings](server-settings.md)
- [Contracts reference](contracts-reference.md)
- [Client package reference](client-reference.md)
- [Machine to machine (client credentials)](machine-to-machine.md)
- [Troubleshooting](troubleshooting.md)
- [Getting started](getting-started.md)
- [Observability](observability.md)
- [Troubleshooting external sign-in](external-identity/troubleshooting.md)
- [Documentation index](index.md)
- [Two-factor authentication and `TwoFactorService`](two-factor.md)
- [Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)](identity-stores.md)
- [Admin JSON API](admin-api.md)
- [Client setup](client-setup.md)
- [Security](security.md)
- [Passkeys](external-identity/passkeys.md)
