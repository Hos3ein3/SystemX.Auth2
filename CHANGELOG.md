# Changelog

All notable changes are listed here. The format follows "Keep a Changelog".

## [Unreleased]

### Added
- Phase 1: solution skeleton, central package management, CI workflow, docs skeleton.
- Phase 4: Identity customization: `Auth2UserStore` / `Auth2RoleStore`, credential scope strategies (`PerApplication`, `Shared`), per-application password / user / lockout rules,
  `Auth2IdentityErrorDescriber`, role rules per application, resx localization (`en` + `fa`, `nl`, `it` skeletons), culture order. Docs: `credential-scope`, `localization`.
- Phase 3: Server data model (Domain, `Auth2DbContext`, `InitialCreate` migration, query filters, `ApplicationSettings` jsonb + validator),
  `IApplicationContext` (single and multi), `ServerWithSingleApplication()`, `Auth2Seeder`, commands `migrate`, `seed`, `create-application`.
  Docs: `server-setup`, `seeding`, `server-settings`, `concepts`.
- Phase 2: `SystemX.Auth2.Contracts` with DTOs, enums and constants, plus tests.
