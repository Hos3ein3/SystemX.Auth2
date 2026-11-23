using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14ExternalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Links never had an application before and nothing created them, so there is nothing to keep.
            migrationBuilder.Sql("DELETE FROM user_logins;");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_logins",
                table: "user_logins");

            migrationBuilder.AlterColumn<Guid>(
                name: "application_id",
                table: "user_logins",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_key",
                table: "user_logins",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "login_provider",
                table: "user_logins",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_logins",
                table: "user_logins",
                columns: new[] { "application_id", "login_provider", "provider_key" });

            migrationBuilder.CreateTable(
                name: "external_group_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    match_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    target_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_group_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_group_mappings_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "replay_entries",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_replay_entries", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "user_passkeys",
                columns: table => new
                {
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aaguid = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkeys", x => new { x.application_id, x.credential_id });
                    table.ForeignKey(
                        name: "fk_user_passkeys_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_passkeys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_application_id_user_id",
                table: "user_logins",
                columns: new[] { "application_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_group_mappings_application_id_provider_name",
                table: "external_group_mappings",
                columns: new[] { "application_id", "provider_name" });

            migrationBuilder.CreateIndex(
                name: "ix_replay_entries_expires_at_utc",
                table: "replay_entries",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_application_id_user_id",
                table: "user_passkeys",
                columns: new[] { "application_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_user_id",
                table: "user_passkeys",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_logins_applications_application_id",
                table: "user_logins",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_logins_applications_application_id",
                table: "user_logins");

            migrationBuilder.DropTable(
                name: "external_group_mappings");

            migrationBuilder.DropTable(
                name: "replay_entries");

            migrationBuilder.DropTable(
                name: "user_passkeys");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_logins",
                table: "user_logins");

            migrationBuilder.DropIndex(
                name: "ix_user_logins_application_id_user_id",
                table: "user_logins");

            migrationBuilder.AlterColumn<string>(
                name: "provider_key",
                table: "user_logins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "login_provider",
                table: "user_logins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "application_id",
                table: "user_logins",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_logins",
                table: "user_logins",
                columns: new[] { "login_provider", "provider_key" });
        }
    }
}
