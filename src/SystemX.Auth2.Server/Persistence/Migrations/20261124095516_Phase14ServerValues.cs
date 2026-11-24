using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14ServerValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "server_values",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_server_values", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "server_values");
        }
    }
}
