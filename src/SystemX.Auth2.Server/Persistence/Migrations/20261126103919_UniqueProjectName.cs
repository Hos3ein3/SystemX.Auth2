using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemX.Auth2.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueProjectName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The name of a project is unique, compared without letter case and without spaces around. An old database may already have two projects with the same name:
            // the later ones get a number ("Shop (2)"), so the index can be created. (EF Core cannot describe an index on an expression, so it is plain SQL, and the model snapshot does not change.)
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT id, row_number() OVER (PARTITION BY lower(btrim(name)) ORDER BY created_at_utc, id) AS n
                    FROM projects
                )
                UPDATE projects p
                SET name = left(btrim(p.name), 190) || ' (' || numbered.n || ')'
                FROM numbered
                WHERE p.id = numbered.id AND numbered.n > 1;
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_projects_name_ci ON projects (lower(btrim(name)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_projects_name_ci;");
        }
    }
}
