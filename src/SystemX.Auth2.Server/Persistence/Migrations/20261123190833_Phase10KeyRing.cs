using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10KeyRing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "key_ring",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    protected_pfx = table.Column<string>(type: "text", nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    not_after_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    retired_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    remove_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_ring", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_key_ring_purpose_retired_at_utc",
                table: "key_ring",
                columns: new[] { "purpose", "retired_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "key_ring");
        }
    }
}
