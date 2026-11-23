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
- Phase 3: `convert-to-single` and `convert-to-multi` commands are not built yet. (`create-admin` and `rotate-keys` exist since phase 10.)
- Phase 3: row version / `If-Match` for application edits comes with the admin API (phase 9).
- Phase 3: the settings cache is per instance; with Redis, pub/sub clears it on all instances (phase 10).
- Phase 4: the claim `locale` (saved user culture) is read by the culture provider, but nothing writes it yet. Phase 5 adds it to tokens and cookies.
- Phase 4: `convert-to-single` / `convert-to-multi` must move credentials between the two places described in `credential-scope.md`.
- Pages still to come: `Admin/*` (phase 9), `External/Callback` and `Manage/Passkeys` (phase 14).
- Phase 5: rate limiting of `/connect/token` and the login page: done in phase 10.
- Phase 5: temporary keys are replaced by the key ring; `rotate-keys` and `IKeyRing` exist (phase 10).
- Phase 5: the `locale` claim is now written to tokens. The culture provider reads it from the hosted session cookie only after the cookie also carries it (phase 6, with the user culture setting).
- Phone numbers are not unique in the database. If two users share one, phone login, OTP and join treat the number as unknown. A unique-phone rule is a possible later setting.
- Phase 6: OTP can be stored in Redis (phase 10).
- Phase 6: `InviteOnly` join mode has no invitation mechanism. Admins add people in the admin API (phase 9).
- Phase 6: rate limiting of `otp/request`, `password/forgot`, `register`: done in phase 10.
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
- Phase 10: no distributed lock or cache through Redis (the database lock is enough). Rate limiting uses fixed windows (a burst at the window edge is possible). The key ring is protected by Data Protection keys that sit unencrypted in the database unless the operator adds a certificate.
- Phase 10: `Auth2:Security:UseForwardedHeaders` is off by default; behind a proxy the operator must turn it on. The admin API still has no Razor pages.
- Phase 10: Serilog sinks other than console and OTLP are configured by the operator through the `Serilog` section (extra sink packages are not included).
- Phase 11: `UseApiAuthentication`, `UseCookieAuthentication`, `UseBlazor`, `UseHostedUi`, `UseSelfUi`, `EnableTokenForwarding`, `UseDistributedCurrentUserStore`, `IAuth2WebService` and `IAuth2BlazorService` are built in phases 12 and 13. `EnablePermissions`, `EnableRoles`, `EnableGroups` and `EnableCurrentUser` only store their options for now (sync and current user come in phases 12-13).
- Phase 11: the Client does not cache the OIDC discovery document and JWKS yet (needed with API authentication, phase 12).
- Phase 12: a revoked access token still works on a client until it expires (no introspection). Cookie / OIDC login, Blazor helpers, the redirect-or-401 choice for browser apps, and cookie storage of access data come in phase 13.
- Phase 12: a role given to a user shows in a **new** token only; permissions and groups follow the cache rules in `client-current-user.md`.

