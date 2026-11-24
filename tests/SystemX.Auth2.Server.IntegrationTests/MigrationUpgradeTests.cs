using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// A database made by the last migration of the OLD model (application = project + client) is upgraded by the new migration.
/// All data stays, and the schema ends exactly like the schema of a fresh database (same columns, indexes and constraints).
/// </summary>
public class MigrationUpgradeTests
{
    private const string LastOldMigration = "20261124095516_Phase14ServerValues";

    private static Auth2DbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<Auth2DbContext>();
        options.UseNpgsql(connectionString);
        options.UseOpenIddict<Auth2Application, Auth2Authorization, Auth2Scope, Auth2Token, Guid>();
        return new Auth2DbContext(options.Options, new ProjectScope());
    }

    /// <summary>Inserts a row: the given values, and a harmless value for every other NOT NULL column that has no default.</summary>
    private static async Task InsertAsync(NpgsqlConnection conn, string table, Dictionary<string, object?> values)
    {
        var required = new List<(string Name, string Type)>();
        await using (var cmd = new NpgsqlCommand("SELECT column_name, data_type FROM information_schema.columns WHERE table_schema='public' AND table_name=@t AND is_nullable='NO' AND column_default IS NULL AND is_identity='NO'", conn))
        {
            cmd.Parameters.AddWithValue("t", table);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) required.Add((r.GetString(0), r.GetString(1)));
        }
        foreach (var (name, type) in required.Where(c => !values.ContainsKey(c.Name)))
            values[name] = type switch
            {
                "uuid" => Guid.NewGuid(),
                "boolean" => false,
                "integer" or "bigint" => 0,
                "timestamp with time zone" => DateTimeOffset.UtcNow,
                "jsonb" => "{}",
                _ => "x",
            };
        var columns = string.Join(", ", values.Keys.Select(k => $"\"{k}\""));
        var parameters = string.Join(", ", values.Keys.Select((k, i) => k.EndsWith("settings_json") || (table == "audit_events" && k == "data_json") ? $"@p{i}::jsonb" : $"@p{i}"));
        await using var insert = new NpgsqlCommand($"INSERT INTO \"{table}\" ({columns}) VALUES ({parameters})", conn);
        var n = 0;
        foreach (var v in values.Values) insert.Parameters.AddWithValue($"p{n++}", v ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ScalarsAsync(NpgsqlConnection conn, string sql)
    {
        var list = new List<string>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : r.GetValue(i).ToString())));
        return list;
    }

    private static async Task<(List<string> Columns, List<string> Indexes, List<string> Constraints)> SchemaAsync(NpgsqlConnection conn) =>
    (
        await ScalarsAsync(conn, "SELECT table_name, column_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema='public' AND table_name <> '__EFMigrationsHistory' ORDER BY 1, 2"),
        await ScalarsAsync(conn, "SELECT tablename, indexname, indexdef FROM pg_indexes WHERE schemaname='public' ORDER BY 1, 2"),
        await ScalarsAsync(conn, "SELECT conrelid::regclass::text, conname, contype, pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace = 'public'::regnamespace ORDER BY 1, 2")
    );

    [Fact]
    public async Task OldDatabase_IsUpgraded_WithAllItsData_AndEndsWithTheSchemaOfAFreshDatabase()
    {
        var oldCs = await SharedPostgres.CreateDatabaseAsync();
        var project = Guid.NewGuid(); var global = Guid.NewGuid(); var client = Guid.NewGuid(); var globalClient = Guid.NewGuid();
        var user = Guid.NewGuid(); var role = Guid.NewGuid(); var permission = Guid.NewGuid(); var group = Guid.NewGuid();

        await using (var old = Context(oldCs))
        {
            await old.GetService<IMigrator>().MigrateAsync(LastOldMigration);
            await using var conn = new NpgsqlConnection(oldCs);
            await conn.OpenAsync();
            var settings = """
                {"schemaVersion":1,"password":{"minLength":12},"redirects":{"redirectAfterLogin":"/home","allowedRedirectUris":["https://shop.test/cb"],"allowedPostLogoutRedirectUris":[]},
                 "clients":{"allowedScopes":["openid","orders.read"],"allowSelfUiLogin":true,"allowedCorsOrigins":["https://shop.test"],"permissionSyncAllowed":true}}
                """;
            await InsertAsync(conn, "openiddict_applications", new() { ["id"] = client, ["client_id"] = "papertrade", ["client_type"] = "confidential", ["display_name"] = "PaperTrade", ["redirect_uris"] = "[\"https://shop.test/cb\"]", ["permissions"] = "[\"ept:token\",\"scp:orders.read\"]" });
            await InsertAsync(conn, "openiddict_applications", new() { ["id"] = globalClient, ["client_id"] = "_global", ["client_type"] = "confidential" });
            await InsertAsync(conn, "applications", new() { ["id"] = project, ["project_key"] = "papertrade", ["name"] = "PaperTrade", ["client_id"] = "papertrade", ["open_iddict_application_id"] = client, ["is_enabled"] = true, ["settings_json"] = settings, ["settings_version"] = 4, ["access_version"] = 7 });
            await InsertAsync(conn, "applications", new() { ["id"] = global, ["project_key"] = "_global", ["name"] = "Global", ["client_id"] = "_global", ["open_iddict_application_id"] = globalClient, ["is_enabled"] = true, ["settings_json"] = "{}" });
            await InsertAsync(conn, "users", new() { ["id"] = user, ["user_name"] = "sara", ["normalized_user_name"] = "SARA", ["email"] = "sara@example.com", ["normalized_email"] = "SARA@EXAMPLE.COM" });
            await InsertAsync(conn, "user_applications", new() { ["user_id"] = user, ["application_id"] = project, ["password_hash"] = "hash", ["user_access_version"] = 3 });
            await InsertAsync(conn, "user_application_recovery_codes", new() { ["id"] = Guid.NewGuid(), ["user_id"] = user, ["application_id"] = project, ["code_hash"] = "code" });
            await InsertAsync(conn, "roles", new() { ["id"] = role, ["name"] = "Editor", ["normalized_name"] = "EDITOR", ["application_id"] = project });
            await InsertAsync(conn, "permissions", new() { ["id"] = permission, ["application_id"] = project, ["key"] = "orders.read", ["source"] = "Sync" });
            await InsertAsync(conn, "groups", new() { ["id"] = group, ["application_id"] = project, ["name"] = "finance" });
            await InsertAsync(conn, "audit_events", new() { ["application_id"] = project, ["action"] = "login.success" });
            await InsertAsync(conn, "user_logins", new() { ["application_id"] = project, ["login_provider"] = "google", ["provider_key"] = "123", ["user_id"] = user });
            await InsertAsync(conn, "external_group_mappings", new() { ["application_id"] = project, ["source_name"] = "groups", ["source_value"] = "x", ["source_type"] = "Group", ["match_type"] = "Exact", ["target_type"] = "Role" });
        }

        // upgrade
        await using (var upgrade = Context(oldCs)) await upgrade.Database.MigrateAsync();
        var freshCs = await SharedPostgres.CreateDatabaseAsync();
        await using (var fresh = Context(freshCs)) await fresh.Database.MigrateAsync();

        await using var a = new NpgsqlConnection(oldCs);
        await using var b = new NpgsqlConnection(freshCs);
        await a.OpenAsync(); await b.OpenAsync();

        // ---- the data
        (await ScalarsAsync(a, $"SELECT project_key, name, is_enabled, settings_version, access_version FROM projects WHERE id = '{project}'")).ShouldBe(["papertrade|PaperTrade|True|4|7"]);
        (await ScalarsAsync(a, "SELECT project_key FROM projects ORDER BY 1")).ShouldBe(["_global", "papertrade"]);
        (await ScalarsAsync(a, $"SELECT name, client_id, is_enabled, redirect_uris, settings_json ->> 'allowSelfUiLogin', settings_json -> 'allowedCorsOrigins' ->> 0 FROM applications WHERE project_id = '{project}'"))
            .ShouldBe(["default|papertrade|True|[\"https://shop.test/cb\"]|true|https://shop.test"]);   // the OpenIddict row IS the application, with its fields intact
        (await ScalarsAsync(a, $"SELECT name FROM applications WHERE project_id = '{global}'")).ShouldBe(["_global"]);
        (await ScalarsAsync(a, $"SELECT settings_json #> '{{password,minLength}}', settings_json #> '{{redirects,redirectAfterLogin}}', settings_json #> '{{clients,allowedScopes}}', settings_json #> '{{clients,permissionSyncAllowed}}' FROM projects WHERE id = '{project}'"))
            .ShouldBe(["12|\"/home\"|NULL|true"]);   // what moved to the application is gone from the project, the rest stays
        (await ScalarsAsync(a, $"SELECT password_hash, user_access_version FROM user_projects WHERE project_id = '{project}' AND user_id = '{user}'")).ShouldBe(["hash|3"]);
        (await ScalarsAsync(a, $"SELECT code_hash FROM user_project_recovery_codes WHERE project_id = '{project}'")).ShouldBe(["code"]);
        (await ScalarsAsync(a, $"SELECT project_id, application_id FROM roles WHERE id = '{role}'")).ShouldBe([$"{project}|NULL"]);
        (await ScalarsAsync(a, $"SELECT project_id, application_id FROM permissions WHERE id = '{permission}'")).ShouldBe([$"{project}|NULL"]);
        (await ScalarsAsync(a, $"SELECT project_id FROM groups WHERE id = '{group}'")).ShouldBe([$"{project}"]);
        (await ScalarsAsync(a, "SELECT project_id, application_id FROM audit_events")).ShouldBe([$"{project}|NULL"]);
        (await ScalarsAsync(a, $"SELECT login_provider FROM user_logins WHERE project_id = '{project}'")).ShouldBe(["google"]);
        (await ScalarsAsync(a, $"SELECT count(*) FROM external_group_mappings WHERE project_id = '{project}'")).ShouldBe(["1"]);

        // ---- the schema: exactly the schema of a fresh database
        var upgraded = await SchemaAsync(a);
        var expected = await SchemaAsync(b);
        upgraded.Columns.ShouldBe(expected.Columns);
        upgraded.Indexes.ShouldBe(expected.Indexes);
        upgraded.Constraints.ShouldBe(expected.Constraints);

        // ---- and the new model can read it
        await using var model = Context(oldCs);
        using (new ProjectScope().Begin()) { }
        (await model.Projects.CountAsync()).ShouldBe(2);
        (await model.Set<Auth2Application>().CountAsync()).ShouldBe(2);
        (await model.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
