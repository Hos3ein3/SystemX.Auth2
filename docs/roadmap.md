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
