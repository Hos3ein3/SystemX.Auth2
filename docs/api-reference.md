# API reference

## What this is

All public endpoints of the Server. This page is a skeleton. Each phase fills in its endpoints.
Route strings live in `Auth2Routes` (Contracts).

## Endpoints

| Method + route | Auth | Status |
|---|---|---|
| `POST /api/auth2/register` | M2M, `auth2.register` | Planned (phase 6) |
| `POST /api/auth2/otp/request` | M2M, `auth2.login` | Planned (phase 6) |
| `POST /api/auth2/password/forgot`, `/reset` | M2M | Planned (phase 6) |
| `POST /api/auth2/password/change` | User | Planned (phase 6) |
| `POST /api/auth2/email/confirm`, `/phone/confirm` | M2M | Planned (phase 6) |
| `POST /api/auth2/2fa/setup`, `/enable`, `/disable`, `/recovery-codes` | User | Planned (phase 6) |
| `GET /api/auth2/me` | User, `auth2.me` | Planned (phase 7) |
| `POST /api/auth2/client/handshake` | M2M, `auth2.client.handshake` | Planned (phase 8) |
| `POST /api/auth2/permissions/sync`, `/roles/sync` | M2M | Planned (phase 8) |
| `GET /api/auth2/permissions`, `/roles`, `/groups` | M2M | Planned (phase 8) |
| `GET /api/auth2/users/{id}/access` | M2M, `auth2.admin` | Planned (phase 8) |
| `GET /health/live`, `/health/ready` | Anonymous | Planned (phase 10) |

## Errors

See [error-codes.md](error-codes.md).
