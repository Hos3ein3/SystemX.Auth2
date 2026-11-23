# Roadmap

## Future work

- UI pages for the server and the admin area (written by a separate step, see `ui-handover.md` later).
- MVC sample and `docker-compose` file.
- Gateway package (YARP).
- SAML and LDAP / Active Directory.
- Passkeys: from experimental to stable.
- Source generator for permission constants.
- Helm chart.
- Webhook cache invalidation.
- Database translation overrides.

DocFX is not planned.

## Open notes

Every `TODO` in code must be listed here.

- Client.Tests and Client.IntegrationTests have no tests yet. They get tests in phase 11.
- Phase 3: `create-admin`, `rotate-keys`, `convert-to-single`, `convert-to-multi` commands are not built yet (phases 4, 6, 10).
- Phase 3: row version / `If-Match` for application edits comes with the admin API (phase 9).
- Phase 3: the settings cache is per instance (memory). Redis and pub/sub invalidation come in phase 10.
- Phase 4: the claim `locale` (saved user culture) is read by the culture provider, but nothing writes it yet. Phase 5 adds it to tokens and cookies.
- Phase 4: `convert-to-single` / `convert-to-multi` must move credentials between the two places described in `credential-scope.md`.
- Pages still to come: `Admin/*` (phase 9), `External/Callback` and `Manage/Passkeys` (phase 14).
- Phase 5: rate limiting of `/connect/token` and the login page (phase 10). Do not expose the password grant on the internet before that.
- Phase 5: temporary keys in Development only; `rotate-keys` and `IKeyRing` in phase 10.
- Phase 5: the `locale` claim is now written to tokens. The culture provider reads it from the hosted session cookie only after the cookie also carries it (phase 6, with the user culture setting).
- Phone numbers are not unique in the database. If two users share one, phone login, OTP and join treat the number as unknown. A unique-phone rule is a possible later setting.
- Phase 6: the OTP table is the only OTP store. Redis storage comes in phase 10.
- Phase 6: `InviteOnly` join mode has no invitation mechanism. Admins add people in the admin API (phase 9).
- Phase 6: no rate limiting on `otp/request`, `password/forgot` or `register` yet (phase 10). Until then, one code per `OtpResendSeconds` and the lockout are the only brakes.
- Phase 6: the access version is raised by password and two-factor changes. Role, permission and group changes raise it in phase 7.
- Phase 6: there is no "remember this device" for two-factor, and no way to list or delete single recovery codes.
- Phase 6: the `locale` claim is in the tokens, but not in the hosted-UI session cookie yet. So a user's saved culture is not used for the hosted pages until the cookie carries it.
- Phase 6: `Account/Login` has no link target for `JoinApplication`/`Register` yet: the UI decides (flags `ShowRegisterLink`, `ShowJoinLink`).
