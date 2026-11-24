using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectsAndApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_external_group_mappings_applications_application_id",
                table: "external_group_mappings");

            migrationBuilder.DropForeignKey(
                name: "fk_groups_applications_application_id",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "fk_openiddict_authorizations_openiddict_applications_applicati~",
                table: "openiddict_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_openiddict_tokens_openiddict_applications_application_id",
                table: "openiddict_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_permissions_applications_application_id",
                table: "permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_roles_applications_application_id",
                table: "roles");

            migrationBuilder.DropForeignKey(
                name: "fk_user_logins_applications_application_id",
                table: "user_logins");

            migrationBuilder.DropForeignKey(
                name: "fk_user_passkeys_applications_application_id",
                table: "user_passkeys");

            migrationBuilder.DropTable(
                name: "openiddict_applications");

            migrationBuilder.DropTable(
                name: "user_application_recovery_codes");

            migrationBuilder.DropTable(
                name: "user_applications");

            migrationBuilder.DropIndex(
                name: "ix_roles_application_id_normalized_name",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_permissions_application_id_key",
                table: "permissions");

            migrationBuilder.DropIndex(
                name: "ix_groups_application_id_name",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_application_id_occurred_at_utc",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "ix_applications_project_key",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "access_version",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "open_iddict_application_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "project_key",
                table: "applications");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "user_tokens",
                newName: "project_id");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "user_permissions",
                newName: "project_id");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "user_passkeys",
                newName: "project_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_passkeys_application_id_user_id",
                table: "user_passkeys",
                newName: "ix_user_passkeys_project_id_user_id");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "user_logins",
                newName: "project_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_logins_application_id_user_id",
                table: "user_logins",
                newName: "ix_user_logins_project_id_user_id");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "replay_entries",
                newName: "project_id");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "otp_codes",
                newName: "project_id");

            migrationBuilder.RenameIndex(
                name: "ix_otp_codes_user_id_application_id_purpose",
                table: "otp_codes",
                newName: "ix_otp_codes_user_id_project_id_purpose");

            migrationBuilder.RenameColumn(
                name: "application_id",
                table: "external_group_mappings",
                newName: "project_id");

            migrationBuilder.RenameIndex(
                name: "ix_external_group_mappings_application_id_provider_name",
                table: "external_group_mappings",
                newName: "ix_external_group_mappings_project_id_provider_name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "applications",
                newName: "settings");

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "application_id",
                table: "permissions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "permissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "application_id",
                table: "groups",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "groups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "application_type",
                table: "applications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_secret",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_type",
                table: "applications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "concurrency_token",
                table: "applications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consent_type",
                table: "applications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_names",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "json_web_key_set",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "permissions",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "post_logout_redirect_uris",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "applications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "properties",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "redirect_uris",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requirements",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "applications",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

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
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    settings_version = table.Column<int>(type: "integer", nullable: false),
                    access_version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "user_project_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_project_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_project_recovery_codes_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_project_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_projects",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    authenticator_key = table.Column<string>(type: "text", nullable: true),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    user_access_version = table.Column<int>(type: "integer", nullable: false),
                    last_login_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    active_session_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_projects", x => new { x.user_id, x.project_id });
                    table.ForeignKey(
                        name: "fk_user_projects_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_projects_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_roles_project_id_normalized_name",
                table: "roles",
                columns: new[] { "project_id", "normalized_name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_project_id_key",
                table: "permissions",
                columns: new[] { "project_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_groups_project_id_name",
                table: "groups",
                columns: new[] { "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_project_id_occurred_at_utc",
                table: "audit_events",
                columns: new[] { "project_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_project_id_name",
                table: "applications",
                columns: new[] { "project_id", "name" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_projects_project_key",
                table: "projects",
                column: "project_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_project_recovery_codes_project_id",
                table: "user_project_recovery_codes",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_project_recovery_codes_user_id_project_id",
                table: "user_project_recovery_codes",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_projects_project_id",
                table: "user_projects",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "fk_applications_projects_project_id",
                table: "applications",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_external_group_mappings_projects_project_id",
                table: "external_group_mappings",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_groups_projects_project_id",
                table: "groups",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_openiddict_authorizations_applications_application_id",
                table: "openiddict_authorizations",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_openiddict_tokens_applications_application_id",
                table: "openiddict_tokens",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_permissions_projects_project_id",
                table: "permissions",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_roles_projects_project_id",
                table: "roles",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_logins_projects_project_id",
                table: "user_logins",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_passkeys_projects_project_id",
                table: "user_passkeys",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_projects_project_id",
                table: "applications");

            migrationBuilder.DropForeignKey(
                name: "fk_external_group_mappings_projects_project_id",
                table: "external_group_mappings");

            migrationBuilder.DropForeignKey(
                name: "fk_groups_projects_project_id",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "fk_openiddict_authorizations_applications_application_id",
                table: "openiddict_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_openiddict_tokens_applications_application_id",
                table: "openiddict_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_permissions_projects_project_id",
                table: "permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_roles_projects_project_id",
                table: "roles");

            migrationBuilder.DropForeignKey(
                name: "fk_user_logins_projects_project_id",
                table: "user_logins");

            migrationBuilder.DropForeignKey(
                name: "fk_user_passkeys_projects_project_id",
                table: "user_passkeys");

            migrationBuilder.DropTable(
                name: "branding_assets");

            migrationBuilder.DropTable(
                name: "email_templates");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "user_project_recovery_codes");

            migrationBuilder.DropTable(
                name: "user_projects");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropIndex(
                name: "ix_roles_project_id_normalized_name",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_permissions_project_id_key",
                table: "permissions");

            migrationBuilder.DropIndex(
                name: "ix_groups_project_id_name",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_project_id_occurred_at_utc",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "ix_applications_project_id_name",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "application_type",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "client_secret",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "client_type",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "concurrency_token",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "consent_type",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "display_names",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "json_web_key_set",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "permissions",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "post_logout_redirect_uris",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "properties",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "redirect_uris",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "requirements",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "applications");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "user_tokens",
                newName: "application_id");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "user_permissions",
                newName: "application_id");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "user_passkeys",
                newName: "application_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_passkeys_project_id_user_id",
                table: "user_passkeys",
                newName: "ix_user_passkeys_application_id_user_id");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "user_logins",
                newName: "application_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_logins_project_id_user_id",
                table: "user_logins",
                newName: "ix_user_logins_application_id_user_id");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "replay_entries",
                newName: "application_id");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "otp_codes",
                newName: "application_id");

            migrationBuilder.RenameIndex(
                name: "ix_otp_codes_user_id_project_id_purpose",
                table: "otp_codes",
                newName: "ix_otp_codes_user_id_application_id_purpose");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "external_group_mappings",
                newName: "application_id");

            migrationBuilder.RenameIndex(
                name: "ix_external_group_mappings_project_id_provider_name",
                table: "external_group_mappings",
                newName: "ix_external_group_mappings_application_id_provider_name");

            migrationBuilder.RenameColumn(
                name: "settings",
                table: "applications",
                newName: "description");

            migrationBuilder.AlterColumn<Guid>(
                name: "application_id",
                table: "permissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "application_id",
                table: "groups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "access_version",
                table: "applications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "open_iddict_application_id",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project_key",
                table: "applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "openiddict_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_openiddict_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_application_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_application_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_application_recovery_codes_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_application_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_applications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    active_session_id = table.Column<string>(type: "text", nullable: true),
                    authenticator_key = table.Column<string>(type: "text", nullable: true),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_login_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    user_access_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_applications", x => new { x.user_id, x.application_id });
                    table.ForeignKey(
                        name: "fk_user_applications_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_applications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_roles_application_id_normalized_name",
                table: "roles",
                columns: new[] { "application_id", "normalized_name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_application_id_key",
                table: "permissions",
                columns: new[] { "application_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_groups_application_id_name",
                table: "groups",
                columns: new[] { "application_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_application_id_occurred_at_utc",
                table: "audit_events",
                columns: new[] { "application_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_project_key",
                table: "applications",
                column: "project_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_openiddict_applications_client_id",
                table: "openiddict_applications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_application_recovery_codes_application_id",
                table: "user_application_recovery_codes",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_application_recovery_codes_user_id_application_id",
                table: "user_application_recovery_codes",
                columns: new[] { "user_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_applications_application_id",
                table: "user_applications",
                column: "application_id");

            migrationBuilder.AddForeignKey(
                name: "fk_external_group_mappings_applications_application_id",
                table: "external_group_mappings",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_groups_applications_application_id",
                table: "groups",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_openiddict_authorizations_openiddict_applications_applicati~",
                table: "openiddict_authorizations",
                column: "application_id",
                principalTable: "openiddict_applications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_openiddict_tokens_openiddict_applications_application_id",
                table: "openiddict_tokens",
                column: "application_id",
                principalTable: "openiddict_applications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_permissions_applications_application_id",
                table: "permissions",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_roles_applications_application_id",
                table: "roles",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_logins_applications_application_id",
                table: "user_logins",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_passkeys_applications_application_id",
                table: "user_passkeys",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
