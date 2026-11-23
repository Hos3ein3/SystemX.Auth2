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
| `not_registered_in_application` | The user does not belong to this application. |
| `application_not_found` | No application for this client. |
| `application_disabled` | The application is disabled. |
| `project_key_or_name_mismatch` | Handshake: ProjectKey or application name does not match. |
| `server_mode_mismatch` | Client and server use different modes (single vs multi). |
| `password_too_short`, `password_too_long`, `password_requires_digit`, `password_requires_lower`, `password_requires_upper`, `password_requires_non_alphanumeric`, `password_requires_unique_chars`, `password_banned` | Password rule failed. The rules come from the application settings. |
| `invalid_user_name`, `invalid_email`, `email_domain_not_allowed`, `invalid_phone_number` | User data rule failed. |
| `user_not_in_application` | The person has no membership row in this application. |
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
| `ownership_proof_required` | Joining a second application needs proof of ownership. |
| `sync_not_allowed` | Sync is off for this application. |
| `invalid_permission_key` | A permission key is invalid. |
| `too_many_items` | Too many items in one request. |
| `invalid_redirect_uri` | Redirect URI is not allowed. |
| `server_error` | Unexpected error. |

## Texts

Every code has an English text in `Errors.resx`. See [localization.md](localization.md).

## Related pages

- [api-reference.md](api-reference.md)
