using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationWebServerAndType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "applications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<string>(
                name: "web_server_url",
                table: "applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "web_server_url",
                table: "applications");
        }
    }
}
