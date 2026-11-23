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
- Phase 5: consent page and `IConsentService` (phase 6). Until then every client should keep `ConsentMode.Implicit`.
- Phase 5: OTP grant `urn:systemx:auth2:grant:otp` and `POST /api/auth2/otp/request` (phase 6).
- Phase 5: pages `Account/Register`, `ForgotPassword`, `ResetPassword`, `ConfirmEmail`, `ConfirmPhone`, `VerifyOtp`, `JoinApplication`, `External/Callback`, `Manage/*`, `Admin/*` come in phases 6, 9 and 14.
- Phase 5: rate limiting of `/connect/token` and the login page (phase 10). Do not expose the password grant on the internet before that.
- Phase 5: temporary keys in Development only; `rotate-keys` and `IKeyRing` in phase 10.
- Phase 5: the `locale` claim is now written to tokens. The culture provider reads it from the hosted session cookie only after the cookie also carries it (phase 6, with the user culture setting).
- Phase 5: login by phone looks the user up by exact `PhoneNumber`. If two users share a number the login is refused. Unique phone numbers are a phase 6 decision.
