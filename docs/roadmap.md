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
- Phase 6: the access version is raised by password and two-factor changes. Role, permission and group changes raise it too (phase 7 done).
- Phase 6: there is no "remember this device" for two-factor, and no way to list or delete single recovery codes.
- Phase 6: the `locale` claim is in the tokens, but not in the hosted-UI session cookie yet. So a user's saved culture is not used for the hosted pages until the cookie carries it.
- Phase 6: `Account/Login` has no link target for `JoinApplication`/`Register` yet: the UI decides (flags `ShowRegisterLink`, `ShowJoinLink`).
- Phase 7: access services have no HTTP admin routes yet (phase 9) and no sync (phase 8). Until then they are used from code and tests.
- Phase 7: the `role` claim in an already issued token is not updated when roles change. `/me` is always current; the `pv` claim tells a client when to ask again.
- Phase 7: changing roles of a global role from a project, and global permissions, are not supported. Permissions are always per application.
- Phase 7: the access cache is per instance (memory); correct across instances because the version is read from the database each time.
- Phase 8: sync has no rate limit yet (phase 10). The client side (scanners, hosted sync services, `HandshakeHostedService`) is built in phases 11–13.
- Phase 8: role `displayName` is accepted but not stored. Add a column in a later migration if the admin UI needs it.
- Phase 8: an unexpected unique-violation inside the sync transaction (for example an admin creating the same role at the same moment) ends that sync with a server error. Calling again fixes it.
- Phase 9: no rate limit on the admin API yet (phase 10, policy `admin`). User lists compute lock state per user (one query each), so keep page sizes small for big user bases.
- Phase 9: `external-providers` admin routes come with the experimental features (phase 14). Email and phone changes of a user are not in `PUT /users/{id}` (they need confirmation again).
- Phase 9: the admin Razor pages (`Admin/*`) are not created yet; the UI hand-over (phase 15) lists them. Permission and role changes by admin do not yet notify other instances (the version is read from the database, so it is correct, only slower to push).

