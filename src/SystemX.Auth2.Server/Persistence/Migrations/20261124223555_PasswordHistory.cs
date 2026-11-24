using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasswordHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_changed_at_utc",
                table: "users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_changed_at_utc",
                table: "user_projects",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "password_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    saved_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_history_projects_credential_project_id",
                        column: x => x.credential_project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_password_history_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_password_history_credential_project_id",
                table: "password_history",
                column: "credential_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_history_user_id_credential_project_id_saved_at_utc",
                table: "password_history",
                columns: new[] { "user_id", "credential_project_id", "saved_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_history");

            migrationBuilder.DropColumn(
                name: "password_changed_at_utc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_changed_at_utc",
                table: "user_projects");
        }
    }
}
