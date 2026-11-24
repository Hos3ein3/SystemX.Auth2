using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <summary>
    /// Splits the old "application = project + client" model into <c>projects</c> and <c>applications</c> (the OpenIddict application table, extended).
    /// All data is kept: the old <c>applications</c> table becomes <c>projects</c>, the OpenIddict client of each project becomes its first application
    /// (<c>_global</c> for the global project, <c>default</c> otherwise), <c>user_applications</c> becomes <c>user_projects</c>, and every
    /// <c>application_id</c> of a tenant table becomes <c>project_id</c>. Written by hand because EF cannot see that the old table is the new one.
    /// Not reversible: restore a backup to go back.
    /// </summary>
    public partial class ProjectsAndApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. the old "applications" table is the new "projects" table
            migrationBuilder.Sql("""
                ALTER TABLE applications RENAME TO projects;
                ALTER TABLE projects RENAME CONSTRAINT pk_applications TO pk_projects;
                ALTER INDEX ix_applications_project_key RENAME TO ix_projects_project_key;
                DROP INDEX ix_applications_client_id;
                ALTER TABLE projects ADD COLUMN updated_at_utc timestamptz NOT NULL DEFAULT now();
                ALTER TABLE projects ALTER COLUMN updated_at_utc DROP DEFAULT;
                """);

            // ---- 2. user_applications -> user_projects, user_application_recovery_codes -> user_project_recovery_codes
            migrationBuilder.Sql("""
                ALTER TABLE user_applications RENAME TO user_projects;
                ALTER TABLE user_projects RENAME COLUMN application_id TO project_id;
                ALTER TABLE user_projects RENAME CONSTRAINT pk_user_applications TO pk_user_projects;
                ALTER TABLE user_projects RENAME CONSTRAINT fk_user_applications_applications_application_id TO fk_user_projects_projects_project_id;
                ALTER TABLE user_projects RENAME CONSTRAINT fk_user_applications_users_user_id TO fk_user_projects_users_user_id;
                ALTER INDEX ix_user_applications_application_id RENAME TO ix_user_projects_project_id;

                ALTER TABLE user_application_recovery_codes RENAME TO user_project_recovery_codes;
                ALTER TABLE user_project_recovery_codes RENAME COLUMN application_id TO project_id;
                ALTER TABLE user_project_recovery_codes RENAME CONSTRAINT pk_user_application_recovery_codes TO pk_user_project_recovery_codes;
                ALTER TABLE user_project_recovery_codes RENAME CONSTRAINT fk_user_application_recovery_codes_applications_application_id TO fk_user_project_recovery_codes_projects_project_id;
                ALTER TABLE user_project_recovery_codes RENAME CONSTRAINT fk_user_application_recovery_codes_users_user_id TO fk_user_project_recovery_codes_users_user_id;
                ALTER INDEX ix_user_application_recovery_codes_application_id RENAME TO ix_user_project_recovery_codes_project_id;
                ALTER INDEX ix_user_application_recovery_codes_user_id_application_id RENAME TO ix_user_project_recovery_codes_user_id_project_id;
                """);

            // ---- 3. tenant tables: application_id -> project_id (with their foreign keys and indexes)
            migrationBuilder.Sql("""
                ALTER TABLE user_tokens RENAME COLUMN application_id TO project_id;
                ALTER TABLE user_permissions RENAME COLUMN application_id TO project_id;
                ALTER TABLE replay_entries RENAME COLUMN application_id TO project_id;

                ALTER TABLE user_passkeys RENAME COLUMN application_id TO project_id;
                ALTER TABLE user_passkeys RENAME CONSTRAINT fk_user_passkeys_applications_application_id TO fk_user_passkeys_projects_project_id;
                ALTER INDEX ix_user_passkeys_application_id_user_id RENAME TO ix_user_passkeys_project_id_user_id;

                ALTER TABLE user_logins RENAME COLUMN application_id TO project_id;
                ALTER TABLE user_logins RENAME CONSTRAINT fk_user_logins_applications_application_id TO fk_user_logins_projects_project_id;
                ALTER INDEX ix_user_logins_application_id_user_id RENAME TO ix_user_logins_project_id_user_id;

                ALTER TABLE otp_codes RENAME COLUMN application_id TO project_id;
                ALTER INDEX ix_otp_codes_user_id_application_id_purpose RENAME TO ix_otp_codes_user_id_project_id_purpose;

                ALTER TABLE external_group_mappings RENAME COLUMN application_id TO project_id;
                ALTER TABLE external_group_mappings RENAME CONSTRAINT fk_external_group_mappings_applications_application_id TO fk_external_group_mappings_projects_project_id;
                ALTER INDEX ix_external_group_mappings_application_id_provider_name RENAME TO ix_external_group_mappings_project_id_provider_name;

                -- roles, permissions, groups and audit events keep an OPTIONAL application_id: the application that pushed the item or did the action.
                -- It is unknown for old rows, so the old value (a project id) moves to project_id and application_id starts empty.
                ALTER TABLE roles RENAME COLUMN application_id TO project_id;
                ALTER TABLE roles ADD COLUMN application_id uuid NULL;
                ALTER TABLE roles RENAME CONSTRAINT fk_roles_applications_application_id TO fk_roles_projects_project_id;
                ALTER INDEX ix_roles_application_id_normalized_name RENAME TO ix_roles_project_id_normalized_name;

                ALTER TABLE permissions RENAME COLUMN application_id TO project_id;
                ALTER TABLE permissions ADD COLUMN application_id uuid NULL;
                ALTER TABLE permissions RENAME CONSTRAINT fk_permissions_applications_application_id TO fk_permissions_projects_project_id;
                ALTER INDEX ix_permissions_application_id_key RENAME TO ix_permissions_project_id_key;

                ALTER TABLE groups RENAME COLUMN application_id TO project_id;
                ALTER TABLE groups ADD COLUMN application_id uuid NULL;
                ALTER TABLE groups RENAME CONSTRAINT fk_groups_applications_application_id TO fk_groups_projects_project_id;
                ALTER INDEX ix_groups_application_id_name RENAME TO ix_groups_project_id_name;

                ALTER TABLE audit_events RENAME COLUMN application_id TO project_id;
                ALTER TABLE audit_events ADD COLUMN application_id uuid NULL;
                ALTER INDEX ix_audit_events_application_id_occurred_at_utc RENAME TO ix_audit_events_project_id_occurred_at_utc;
                """);

            // ---- 4. the OpenIddict application table IS the application table now
            migrationBuilder.Sql("""
                ALTER TABLE openiddict_applications RENAME TO applications;
                ALTER TABLE applications RENAME CONSTRAINT pk_openiddict_applications TO pk_applications;
                ALTER INDEX ix_openiddict_applications_client_id RENAME TO ix_applications_client_id;
                ALTER TABLE openiddict_authorizations RENAME CONSTRAINT "fk_openiddict_authorizations_openiddict_applications_applicati~" TO fk_openiddict_authorizations_applications_application_id;
                ALTER TABLE openiddict_tokens RENAME CONSTRAINT fk_openiddict_tokens_openiddict_applications_application_id TO fk_openiddict_tokens_applications_application_id;

                ALTER TABLE applications ADD COLUMN project_id uuid NULL;
                ALTER TABLE applications ADD COLUMN is_enabled boolean NOT NULL DEFAULT true;
                ALTER TABLE applications ADD COLUMN name character varying(200) NULL;
                ALTER TABLE applications ADD COLUMN settings_json jsonb NOT NULL DEFAULT '{}'::jsonb;
                ALTER TABLE applications ADD COLUMN settings_version integer NOT NULL DEFAULT 1;
                ALTER TABLE applications ADD COLUMN created_at_utc timestamptz NOT NULL DEFAULT now();
                ALTER TABLE applications ADD COLUMN updated_at_utc timestamptz NOT NULL DEFAULT now();

                -- each project's OpenIddict client becomes its first application ('_global' for the global project, 'default' for the others)
                UPDATE applications a SET
                    project_id = p.id,
                    name = CASE WHEN p.project_key = '_global' THEN '_global' ELSE 'default' END,
                    created_at_utc = p.created_at_utc,
                    settings_json = jsonb_build_object(
                        'schemaVersion', 1,
                        'allowSelfUiLogin', COALESCE((p.settings_json -> 'clients' ->> 'allowSelfUiLogin')::boolean, false),
                        'allowedCorsOrigins', COALESCE(p.settings_json -> 'clients' -> 'allowedCorsOrigins', '[]'::jsonb))
                FROM projects p
                WHERE p.open_iddict_application_id = a.id;

                -- a client that belonged to no project cannot exist any more
                DELETE FROM applications WHERE project_id IS NULL;
                ALTER TABLE applications ALTER COLUMN project_id SET NOT NULL;
                ALTER TABLE applications ALTER COLUMN name SET NOT NULL;
                ALTER TABLE applications ALTER COLUMN is_enabled DROP DEFAULT;
                ALTER TABLE applications ALTER COLUMN settings_json DROP DEFAULT;
                ALTER TABLE applications ALTER COLUMN settings_version DROP DEFAULT;
                ALTER TABLE applications ALTER COLUMN created_at_utc DROP DEFAULT;
                ALTER TABLE applications ALTER COLUMN updated_at_utc DROP DEFAULT;
                CREATE UNIQUE INDEX ix_applications_project_id_name ON applications (project_id, name);
                ALTER TABLE applications ADD CONSTRAINT fk_applications_projects_project_id FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE;

                -- the settings that moved to the application (redirect URIs and scopes are OpenIddict fields and were mirrored there already)
                UPDATE projects SET settings_json = settings_json
                    #- '{redirects,allowedRedirectUris}' #- '{redirects,allowedPostLogoutRedirectUris}'
                    #- '{clients,allowedScopes}' #- '{clients,allowSelfUiLogin}' #- '{clients,allowedCorsOrigins}';

                ALTER TABLE projects DROP COLUMN client_id;
                ALTER TABLE projects DROP COLUMN open_iddict_application_id;
                """);

            // ---- 5. new tables (admin screens: branding, e-mail templates, job history)
            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rows_affected = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branding_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branding_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_branding_assets_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    culture = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_templates_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_branding_assets_project_id_kind",
                table: "branding_assets",
                columns: new[] { "project_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_project_id_name_culture",
                table: "email_templates",
                columns: new[] { "project_id", "name", "culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_job_name_started_at_utc",
                table: "job_runs",
                columns: new[] { "job_name", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_started_at_utc",
                table: "job_runs",
                column: "started_at_utc");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("This migration cannot be reversed: the project/application split merges data. Restore a backup taken before the upgrade.");
    }
}
